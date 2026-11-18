using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Dvr;

/// <summary>
/// Pure native C# Xiongmai (Sofia / DVRIP) client supporting both:
/// - Cloud Serial ID connections via <see cref="XmCloudNatClient"/> (libeznat P2P / Proxy + "XMIP" reliable UDP), and
/// - Direct IP / Domain connections over TCP (port 34567).
/// Runs 100% natively on Linux without Wine or 32-bit DLLs.
/// </summary>
public sealed class DvripDvrClient : IDvrClient
{
    private const int HeaderSize = 20;
    private const ushort MsgLoginReq = 1000;
    private const ushort MsgLoginRsp = 1001;
    private const ushort MsgLogoutReq = 1002;
    private const ushort MsgKeepAliveReq = 1006;
    private const ushort MsgKeepAliveRsp = 1007;
    private const ushort MsgSysInfoReq = 1020;
    private const ushort MsgSysInfoRsp = 1021;
    private const ushort MsgPlayBackReq = 1420;      // 0x058C (OPPlayBack DownloadStart / DownloadStop on control connection)
    private const ushort MsgPlayBackRsp = 1421;      // 0x058D (OPPlayBack JSON response on control connection)
    private const ushort MsgPlayBackData = 1422;     // 0x058E (OPPlayBack stream chunk)
    private const ushort MsgPlayClaimReq = 1424;     // 0x0590 (OPPlayBack Claim on dedicated data connection)
    private const ushort MsgPlayClaimRsp = 1425;     // 0x0591 (OPPlayBack Claim response on dedicated data connection)
    private const ushort MsgPlayDownloadData = 1426; // 0x0592 (OPPlayBack download H.264 stream chunk on dedicated data connection)
    private const ushort MsgFileSearchReq = 1440;
    private const ushort MsgFileSearchRsp = 1441;

    private readonly XmCloudNatClient.NatClientOptions? _natOptions;

    public DvripDvrClient(XmCloudNatClient.NatClientOptions? natOptions = null)
    {
        _natOptions = natOptions;
    }

    public async Task<IDvrConnection> ConnectAsync(Branch branch, Credential credential, CancellationToken ct)
    {
        Stream? stream = null;
        IDisposable? owner = null;
        try
        {
            (stream, owner) = await OpenTransportStreamAsync(branch, _natOptions, ct);
            var conn = new Connection(branch, credential, stream, owner, _natOptions);
            await conn.LoginAsync(ct);
            return conn;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stream?.Dispose();
            owner?.Dispose();
            string hint = branch.Mode == LoginMode.Ip
                ? Loc.F("\nCheck that this PC can reach {0} (same network, VPN or leased line).", branch.ConnectionText)
                : string.Empty;
            throw new DvrException($"{branch.Name}: {new SdkException("Login", -10005).Message}{hint}", new SdkException("Login", -10005));
        }
        catch (TimeoutException tex)
        {
            stream?.Dispose();
            owner?.Dispose();
            string hint = branch.Mode == LoginMode.Ip
                ? Loc.F("\nCheck that this PC can reach {0} (same network, VPN or leased line).", branch.ConnectionText)
                : string.Empty;
            throw new DvrException($"{branch.Name}: {new SdkException("Login", -10005).Message}{hint}", tex);
        }
        catch (SocketException sockEx)
        {
            stream?.Dispose();
            owner?.Dispose();
            string hint = branch.Mode == LoginMode.Ip
                ? Loc.F("\nCheck that this PC can reach {0} (same network, VPN or leased line).", branch.ConnectionText)
                : string.Empty;
            throw new DvrException($"{branch.Name}: {new SdkException("Login", -11307).Message}{hint}", sockEx);
        }
        catch
        {
            stream?.Dispose();
            owner?.Dispose();
            throw;
        }
    }

    private static async Task<(Stream Stream, IDisposable Owner)> OpenTransportStreamAsync(
        Branch branch,
        XmCloudNatClient.NatClientOptions? natOptions,
        CancellationToken ct)
    {
        if (branch.Mode == LoginMode.Cloud)
        {
            var natStream = await XmCloudNatClient.ConnectAsync(branch.CloudId.Trim(), ct, natOptions);
            return (natStream, natStream);
        }

        var tcp = new TcpClient();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            await tcp.ConnectAsync(branch.Address.Trim(), branch.Port, timeoutCts.Token);
            return (tcp.GetStream(), tcp);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>Xiongmai Sofia MD5 password hash (8-char base-62 digest).</summary>
    public static string SofiaHash(string password)
    {
        byte[] md5 = MD5.HashData(Encoding.UTF8.GetBytes(password));
        Span<char> chars = stackalloc char[8];
        for (int i = 0; i < 8; i++)
        {
            int n = (md5[2 * i] + md5[2 * i + 1]) % 62;
            chars[i] = n switch
            {
                < 10 => (char)('0' + n),
                < 36 => (char)('A' + n - 10),
                _ => (char)('a' + n - 36),
            };
        }
        return new string(chars);
    }

    private sealed class Connection : IDvrConnection
    {
        private readonly Branch _branch;
        private readonly Credential _credential;
        private readonly Stream _stream;
        private readonly IDisposable? _owner;
        private readonly XmCloudNatClient.NatClientOptions? _natOptions;
        private uint _sessionId;
        private uint _seq;
        private bool _disposed;

        public Connection(
            Branch branch,
            Credential credential,
            Stream stream,
            IDisposable? owner,
            XmCloudNatClient.NatClientOptions? natOptions)
        {
            _branch = branch;
            _credential = credential;
            _stream = stream;
            _owner = owner;
            _natOptions = natOptions;
            Info = new DvrInfo(0, string.Empty, string.Empty);
        }

        public DvrInfo Info { get; private set; }

        public async Task LoginAsync(CancellationToken ct)
        {
            var loginObj = new JsonObject
            {
                ["EncryptType"] = "MD5",
                ["LoginType"] = "DVRIP-Web",
                ["PassWord"] = SofiaHash(_credential.Password),
                ["UserName"] = _credential.Username,
            };

            var (_, sessionId, body) = await RequestJsonAsync(MsgLoginReq, loginObj, ct);
            _sessionId = sessionId;
            int ret = body?["Ret"]?.GetValue<int>() ?? 0;

            if (ret is not (100 or 0))
            {
                int sdkCode = MapRetToSdkCode(ret);
                throw new DvrException($"{_branch.Name}: {new SdkException("Login", sdkCode).Message}", new SdkException("Login", sdkCode));
            }

            int channels = 0;
            string serial = _branch.Mode == LoginMode.Cloud ? _branch.CloudId : string.Empty;
            string model = string.Empty;

            if (body?["ChannelNum"] is JsonNode chNode)
                channels += Math.Max(0, chNode.GetValue<int>());
            if (body?["ExtraChannel"] is JsonNode exNode)
                channels += Math.Max(0, exNode.GetValue<int>());
            if (body?["DeviceType"] is JsonNode dtNode)
                model = dtNode.ToString();

            try
            {
                var sysReq = new JsonObject
                {
                    ["Name"] = "SystemInfo",
                    ["SessionID"] = $"0x{_sessionId:x8}",
                };
                var (_, _, sysRsp) = await RequestJsonAsync(MsgSysInfoReq, sysReq, ct);
                if (sysRsp?["SystemInfo"] is JsonObject sysInfo)
                {
                    int analog = sysInfo["VideoInChannel"]?.GetValue<int>() ?? 0;
                    int digital = sysInfo["DigChannel"]?.GetValue<int>() ?? 0;
                    if (analog + digital > 0) channels = analog + digital;
                    serial = sysInfo["SerialNo"]?.ToString() ?? serial;
                    model = sysInfo["Hardware"]?.ToString() ?? model;
                }
            }
            catch { /* keep channels reported in LoginRsp */ }

            if (channels is < 1 or > DeviceDetails.MaxSaneChannels) channels = 0;
            Info = new DvrInfo(channels, serial, model);
        }

        public async Task<IReadOnlyList<RemoteFile>> FindFilesAsync(int channel, DateTime from, DateTime to, CancellationToken ct)
        {
            const int pageSize = 64;
            var all = new List<RemoteFile>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            DateTime cursor = from;

            for (int guard = 0; guard < 10_000; guard++)
            {
                ct.ThrowIfCancellationRequested();
                var page = await FindPageAsync(channel, cursor, to, ct);
                int added = 0;
                foreach (var f in page)
                {
                    if (seen.Add(f.RemoteName + "|" + f.Begin.Ticks)) { all.Add(f); added++; }
                }

                if (page.Count < pageSize || added == 0) break;
                DateTime next = page.Max(f => f.End);
                cursor = next > cursor ? next : cursor.AddSeconds(1);
                if (cursor >= to) break;
            }

            return all.OrderBy(f => f.Begin).ThenBy(f => f.RemoteName, StringComparer.Ordinal).ToList();
        }

        private async Task<List<RemoteFile>> FindPageAsync(int channel, DateTime from, DateTime to, CancellationToken ct)
        {
            var req = new JsonObject
            {
                ["Name"] = "OPFileQuery",
                ["SessionID"] = $"0x{_sessionId:x8}",
                ["OPFileQuery"] = new JsonObject
                {
                    ["BeginTime"] = from.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["EndTime"] = to.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["Channel"] = channel,
                    ["DriverTypeMask"] = "0x0000FFFF",
                    ["Event"] = "*",
                    ["StreamType"] = "0x00000000",
                    ["Type"] = "h264",
                },
            };

            var (_, _, rsp) = await RequestJsonAsync(MsgFileSearchReq, req, ct);
            var list = new List<RemoteFile>();
            if (rsp?["OPFileQuery"] is not JsonArray arr) return list;

            foreach (var item in arr.OfType<JsonObject>())
            {
                string fileName = item["FileName"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(fileName)) continue;
                DateTime.TryParseExact(item["BeginTime"]?.ToString(), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime begin);
                DateTime.TryParseExact(item["EndTime"]?.ToString(), "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end);

                long sizeKb = ParseFileSizeKb(item["FileLength"]?.ToString());
                list.Add(new RemoteFile(channel, fileName, begin, end, sizeKb, default));
            }
            return list;
        }

        public async Task DownloadAsync(RemoteFile file, string destPath, IProgress<int>? progress, CancellationToken ct)
        {
            // Exact NetSdk.dll H264_DVR_GetFileByName (0x019D0B) two-connection OPPlayBack protocol:
            // 1. Open a second transport stream (claimStream) to the DVR (Cloud NAT or Direct TCP) WITHOUT re-login;
            //    it shares the existing control connection's _sessionId (0x027365..0x027496).
            // 2. Send MsgPlayClaimReq (1424 = 0x0590) with "Action": "Claim" on claimStream and read MsgPlayClaimRsp (1425 = 0x0591).
            // 3. Send MsgPlayBackReq (1420 = 0x058C) with "Action": "DownloadStart" and header[12] = channel on the main
            //    control connection (_stream) and read MsgPlayBackRsp (1421 = 0x058D).
            // 4. Read MsgPlayDownloadData (1426 = 0x0592) raw H.264 stream chunks from claimStream until header[13] (endFlag) != 0.
            var (claimStream, claimOwner) = await OpenTransportStreamAsync(_branch, _natOptions, ct);
            try
            {
                byte[] claimPayload = BuildPlayBackRequestPayload(_sessionId, "Claim", file);
                await SendPacketOnStreamAsync(claimStream, MsgPlayClaimReq, _sessionId, 0, channel: 0, claimPayload, ct);

                var (claimRspMsgId, _, _, claimRspBytes) = await ReadPacketFromStreamAsync(claimStream, ct);
                if (claimRspBytes.Length > 0 && claimRspBytes[0] == (byte)'{')
                {
                    var claimNode = ParseJsonPayload(claimRspBytes);
                    int claimRet = claimNode?["Ret"]?.GetValue<int>() ?? 100;
                    if (claimRet is not (100 or 0))
                        throw new DvrException(Loc.F("The DVR download failed (position {0}).", claimRet));
                }
                else if (claimRspMsgId != MsgPlayClaimRsp)
                {
                    throw new DvrException(Loc.F("The DVR download failed (position {0}).", claimRspMsgId));
                }

                byte channelByte = (byte)Math.Clamp(file.Channel, 0, 255);
                byte[] startPayload = BuildPlayBackRequestPayload(_sessionId, "DownloadStart", file);
                await SendPacketOnStreamAsync(_stream, MsgPlayBackReq, _sessionId, 0, channel: channelByte, startPayload, ct);

                try
                {
                    using var startAckCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    startAckCts.CancelAfter(TimeSpan.FromSeconds(5));
                    while (true)
                    {
                        var (startRspMsgId, _, _, startRspBytes) = await ReadPacketFromStreamAsync(_stream, startAckCts.Token);
                        if (startRspBytes.Length > 0 && startRspBytes[0] == (byte)'{')
                        {
                            var startNode = ParseJsonPayload(startRspBytes);
                            int startRet = startNode?["Ret"]?.GetValue<int>() ?? 100;
                            if (startRet is not (100 or 0))
                                throw new DvrException(Loc.F("The DVR download failed (position {0}).", startRet));
                        }
                        if (startRspMsgId == MsgPlayBackRsp)
                            break;
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Proceed to read media chunks from claimStream if the DVR started streaming directly
                }

                long expectedBytes = Math.Max(1024, file.SizeKb * 1024);
                long writtenBytes = 0;
                int lastReportedPct = -1;
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                // Sofia DVR firmware drops sessions that have no control-connection activity for 60 seconds.
                // Run a background KeepAlive task on _stream every 15 seconds to prevent session timeout during download.
                using var keepAliveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var keepAliveTask = Task.Run(async () =>
                {
                    byte[] keepAlivePayload = Encoding.UTF8.GetBytes(new JsonObject
                    {
                        ["Name"] = "KeepAlive",
                        ["SessionID"] = $"0x{_sessionId:x8}"
                    }.ToJsonString() + "\n\0");

                    while (!keepAliveCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(15), keepAliveCts.Token);
                            await SendPacketOnStreamAsync(_stream, MsgKeepAliveReq, _sessionId, _seq++, channel: 0, keepAlivePayload, keepAliveCts.Token);

                            // Drain the MsgKeepAliveRsp response with a 3-second timeout
                            using var rspCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                            using var linked = CancellationTokenSource.CreateLinkedTokenSource(keepAliveCts.Token, rspCts.Token);
                            await ReadPacketFromStreamAsync(_stream, linked.Token);
                        }
                        catch (OperationCanceledException) when (keepAliveCts.IsCancellationRequested)
                        {
                            break;
                        }
                        catch
                        {
                            // KeepAlive failure shouldn't abort the download
                        }
                    }
                });

                try
                {
                    await using (var fs = new FileStream(
                        destPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 1024 * 1024,
                        options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        using var stallCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        stallCts.CancelAfter(TimeSpan.FromSeconds(45));

                        while (true)
                        {
                            ct.ThrowIfCancellationRequested();
                            ushort msgId;
                            byte endFlag;
                            byte[] payload;
                            try
                            {
                                (msgId, _, endFlag, payload) = await ReadPacketFromStreamAsync(claimStream, stallCts.Token);
                                stallCts.CancelAfter(TimeSpan.FromSeconds(45));
                            }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested && stallCts.IsCancellationRequested)
                            {
                                throw new DvrException(Loc.T("The DVR download stalled: no data received for 45 seconds."));
                            }
                            catch (EndOfStreamException) when (writtenBytes > 0)
                            {
                                break;
                            }

                            if (msgId is MsgPlayDownloadData or MsgPlayBackData or MsgPlayClaimReq)
                            {
                                if (endFlag != 0)
                                {
                                    if (writtenBytes == 0 && payload.Length > 0 && payload[0] != (byte)'{')
                                    {
                                        await fs.WriteAsync(payload, ct);
                                        writtenBytes += payload.Length;
                                    }
                                    break;
                                }

                                if (payload.Length > 0)
                                {
                                    await fs.WriteAsync(payload, ct);
                                    writtenBytes += payload.Length;
                                    int pct = (int)Math.Clamp(writtenBytes * 99 / expectedBytes, 0, 99);
                                    if (pct != lastReportedPct)
                                    {
                                        lastReportedPct = pct;
                                        progress?.Report(pct);
                                    }
                                }
                            }
                            else if (payload.Length > 0 && payload[0] == (byte)'{')
                            {
                                var node = ParseJsonPayload(payload);
                                int ret = node?["Ret"]?.GetValue<int>() ?? 100;
                                if (ret is not (100 or 0))
                                    throw new DvrException(Loc.F("The DVR download failed (position {0}).", ret));
                                if (writtenBytes > 0) break;
                            }
                            else if (endFlag != 0 && writtenBytes > 0)
                            {
                                break;
                            }
                        }
                    }

                    if (writtenBytes == 0)
                        throw new DvrException(Loc.T("The DVR reported success but no data was written."));

                    progress?.Report(100);
                }
                finally
                {
                    keepAliveCts.Cancel();
                    try { await keepAliveTask; } catch { }
                }
            }
            finally
            {
                try { claimStream.Dispose(); } catch { }
                try { claimOwner?.Dispose(); } catch { }
            }
        }

        private static byte[] BuildPlayBackRequestPayload(uint sessionId, string action, RemoteFile file)
        {
            var req = new JsonObject
            {
                ["Name"] = "OPPlayBack",
                ["SessionID"] = $"0x{sessionId:x}",
                ["OPPlayBack"] = new JsonObject
                {
                    ["Action"] = action,
                    ["EndTime"] = file.End.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    ["Parameter"] = new JsonObject
                    {
                        ["FileName"] = file.RemoteName,
                        ["IntelligentPlayBackEvent"] = string.Empty,
                        ["IntelligentPlayBackSpeed"] = 0,
                        ["PlayMode"] = "ByName",
                        ["StreamType"] = 0,
                        ["TransMode"] = "TCP",
                        ["Value"] = 0,
                    },
                    ["StartTime"] = file.Begin.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                },
            };
            return Encoding.UTF8.GetBytes(req.ToJsonString() + "\n\0");
        }

        private static long ParseFileSizeKb(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            raw = raw.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hexVal))
            {
                return hexVal;
            }
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long decVal) ? decVal : 0;
        }

        private static int MapRetToSdkCode(int ret) => ret switch
        {
            203 or 513 => -11301, // PASSWORD_NOT_VALID
            204 or 512 => -11302, // LOGIN_USER_NOEXIST
            205 or 515 => -11303, // USER_LOCKED
            206 => -11305,        // USER_HAS_USED
            _ => -11307,          // CONNECT_DEVICE_ERROR
        };

        private async Task<(ushort MsgId, uint SessionId, JsonObject? Body)> RequestJsonAsync(
            ushort msgId, JsonObject payload, CancellationToken ct)
        {
            string json = payload.ToJsonString() + "\n\0";
            await SendPacketAsync(msgId, Encoding.UTF8.GetBytes(json), ct);
            var (rspMsgId, sessionId, rspBytes) = await ReadPacketAsync(ct);
            return (rspMsgId, sessionId, ParseJsonPayload(rspBytes));
        }

        private static JsonObject? ParseJsonPayload(byte[] bytes)
        {
            int len = Array.IndexOf(bytes, (byte)0);
            if (len < 0) len = bytes.Length;
            string text = Encoding.UTF8.GetString(bytes, 0, len).Trim();
            if (string.IsNullOrEmpty(text)) return null;
            try { return JsonNode.Parse(text) as JsonObject; }
            catch (JsonException) { return null; }
        }

        private Task SendPacketAsync(ushort msgId, byte[] body, CancellationToken ct) =>
            SendPacketOnStreamAsync(_stream, msgId, _sessionId, _seq++, channel: 0, body, ct);

        private static async Task SendPacketOnStreamAsync(
            Stream stream,
            ushort msgId,
            uint sessionId,
            uint sequenceNumber,
            byte channel,
            byte[] body,
            CancellationToken ct)
        {
            byte[] packet = new byte[HeaderSize + body.Length];
            packet[0] = 0xFF;
            packet[1] = 0x01;
            packet[2] = 0x00;
            packet[3] = 0x00;
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4, 4), sessionId);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), sequenceNumber);
            packet[12] = channel;
            packet[13] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(14, 2), msgId);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(16, 4), body.Length);

            if (body.Length > 0)
                body.CopyTo(packet.AsSpan(HeaderSize));

            await stream.WriteAsync(packet, ct);
            await stream.FlushAsync(ct);
        }

        private async Task<(ushort MsgId, uint SessionId, byte[] Payload)> ReadPacketAsync(CancellationToken ct)
        {
            var (msgId, sessionId, _, payload) = await ReadPacketFromStreamAsync(_stream, ct);
            return (msgId, sessionId, payload);
        }

        private static async Task<(ushort MsgId, uint SessionId, byte EndFlag, byte[] Payload)> ReadPacketFromStreamAsync(
            Stream stream,
            CancellationToken ct)
        {
            byte[] header = new byte[HeaderSize];
            await ReadExactAsync(stream, header, ct, throwEndOfStreamOnInitialZeroRead: true);
            uint sessionId = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
            byte endFlag = header[13]; // NetSdk.dll 0x054CFB: [esp+0x2D] - [esp+0x20] = offset 13 (header[8..11] is 32-bit sequence number)
            ushort msgId = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14, 2));
            int len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(16, 4));
            if (len is < 0 or > 32 * 1024 * 1024)
                throw new DvrException("Invalid DVRIP packet length from device.");

            byte[] payload = len > 0 ? new byte[len] : Array.Empty<byte>();
            if (len > 0)
                await ReadExactAsync(stream, payload, ct, throwEndOfStreamOnInitialZeroRead: false);
            return (msgId, sessionId, endFlag, payload);
        }

        private static async Task ReadExactAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken ct,
            bool throwEndOfStreamOnInitialZeroRead = false)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
                if (read == 0)
                {
                    if (offset == 0 && throwEndOfStreamOnInitialZeroRead)
                        throw new EndOfStreamException("The DVR stream closed at packet boundary.");
                    throw new DvrException("The DVR closed the connection unexpectedly.");
                }
                offset += read;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Dispose(); } catch { }
            try { _owner?.Dispose(); } catch { }
        }
    }
}
