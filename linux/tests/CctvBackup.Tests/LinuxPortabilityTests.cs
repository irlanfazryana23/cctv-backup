using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Tests;

public class LinuxPortabilityTests
{
    [Fact]
    public void Linux_aes_gcm_protector_round_trips_credentials_and_rejects_tampering()
    {
        using var dir = new TempDir();
        string keyPath = Path.Combine(dir.Path, ".key");
        var protector = new LinuxFileProtector(keyPath);

        byte[] plain = Encoding.UTF8.GetBytes("{\"BranchA\":{\"Username\":\"admin\",\"Password\":\"Secret123!\"}}");
        byte[] cipher = protector.Protect(plain);

        Assert.True(File.Exists(keyPath));
        Assert.NotEqual(plain, cipher);
        Assert.Equal(plain, new LinuxFileProtector(keyPath).Unprotect(cipher));

        // Flip a ciphertext byte and verify AES-GCM authentication tag rejects it
        cipher[^1] ^= 0xFF;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(cipher));
    }

    [Fact]
    public unsafe void XmStructs_have_exact_32bit_sizes_even_on_64bit_linux()
    {
        Assert.Equal(24, sizeof(XmTime));
        Assert.Equal(32, sizeof(XmSystemTime));
        Assert.Equal(96, sizeof(XmFindInfo));
        Assert.Equal(188, sizeof(XmFileData));
    }

    [Fact]
    public void Sofia_md5_hash_produces_8_char_alphanumeric_digest()
    {
        string hash = DvripDvrClient.SofiaHash("");
        Assert.Equal(8, hash.Length);
        Assert.All(hash, c => Assert.True(char.IsLetterOrDigit(c)));
        // Empty password in Xiongmai Sofia MD5 is well-known "tlJwpbo6"
        Assert.Equal("tlJwpbo6", hash);
    }

    [Fact]
    public void SdkLocator_finds_linux_libnetsdk_so_as_well_as_windows_netsdk_dll()
    {
        using var t = new TempDir();
        string linuxSdkDir = Path.Combine(t.Path, "linux-sdk");
        Directory.CreateDirectory(linuxSdkDir);
        File.WriteAllText(Path.Combine(linuxSdkDir, SdkLocator.LinuxSoName), "elf");

        string missing = Path.Combine(t.Path, "missing");
        Assert.Equal(linuxSdkDir, SdkLocator.Find(linuxSdkDir, missing, missing));
        Assert.True(SdkLocator.HasNativeLinuxSdk(linuxSdkDir));
        Assert.False(SdkLocator.HasWindowsSdk(linuxSdkDir));
    }

    [Fact]
    public void XmCloudNatClient_serializes_and_parses_libeznat_packets_accurately()
    {
        // 1. Master discovery request & response
        byte[] discReq = XmCloudNatClient.BuildMasterServerDiscoveryPacket();
        Assert.Equal(104, discReq.Length);
        Assert.Equal(XmCloudNatClient.CmdMasterServerReq, BinaryPrimitives.ReadUInt32LittleEndian(discReq.AsSpan(0, 4)));

        byte[] discRsp = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(discRsp.AsSpan(0, 4), XmCloudNatClient.CmdMasterServerRsp);
        Encoding.ASCII.GetBytes("112.124.0.188").CopyTo(discRsp.AsSpan(4));
        Assert.True(XmCloudNatClient.TryParseMasterServerDiscoveryResponse(discRsp, out string? masterIp));
        Assert.Equal("112.124.0.188", masterIp);

        // 2. QueryUUIDRegisterSvrAddr request & response
        const string serial = "1234567890abcdef";
        byte[] uuidReq = XmCloudNatClient.BuildQueryDeviceNatServerPacket(serial);
        Assert.Equal(4 + serial.Length, uuidReq.Length);
        Assert.Equal(XmCloudNatClient.CmdQueryUuidSvrReq, BinaryPrimitives.ReadUInt32LittleEndian(uuidReq.AsSpan(0, 4)));

        byte[] uuidRsp = new byte[140];
        BinaryPrimitives.WriteUInt32LittleEndian(uuidRsp.AsSpan(0, 4), XmCloudNatClient.CmdQueryUuidSvrRsp);
        BinaryPrimitives.WriteInt32LittleEndian(uuidRsp.AsSpan(4, 4), 1); // online
        Encoding.ASCII.GetBytes(serial).CopyTo(uuidRsp.AsSpan(8));
        Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(uuidRsp.AsSpan(108));
        BinaryPrimitives.WriteUInt32LittleEndian(uuidRsp.AsSpan(128, 4), 8765);
        BinaryPrimitives.WriteUInt32LittleEndian(uuidRsp.AsSpan(132, 4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(uuidRsp.AsSpan(136, 4), 1);

        Assert.True(XmCloudNatClient.TryParseQueryDeviceNatServerResponse(uuidRsp, out var devInfo));
        Assert.NotNull(devInfo);
        Assert.Equal(1, devInfo!.Status);
        Assert.Equal(serial, devInfo.Uuid);
        Assert.Equal("127.0.0.1", devInfo.ServerIp);
        Assert.Equal(8765, devInfo.ServerPort);

        // 3. XMIP Data & ACK packets
        byte[] payload = [0xFF, 0x01, 0x00, 0x00, 0x11, 0x22];
        byte[] xmipData = XmCloudNatClient.BuildXmipDataPacket(42, payload, isFirstSlice: true, isLastSlice: true);
        Assert.Equal(24 + payload.Length, xmipData.Length);
        Assert.Equal(XmCloudNatClient.CmdDataPacket, BinaryPrimitives.ReadUInt32LittleEndian(xmipData.AsSpan(0, 4)));
        Assert.Equal(12 + payload.Length, BinaryPrimitives.ReadUInt16LittleEndian(xmipData.AsSpan(6, 2)));
        Assert.Equal("XMIP", Encoding.ASCII.GetString(xmipData, 12, 4));
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(xmipData.AsSpan(16, 4)));
        Assert.Equal(0x0E, xmipData[21]); // DATA (0x02) | sflag (0x04) | eflag (0x08)

        byte[] xmipAck = XmCloudNatClient.BuildXmipAckPacket(42, 1);
        Assert.Equal(24, xmipAck.Length);
        Assert.Equal("XMIP", Encoding.ASCII.GetString(xmipAck, 12, 4));
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32LittleEndian(xmipAck.AsSpan(16, 4)));
        Assert.Equal(1, xmipAck[20]);
        Assert.Equal(0x01, xmipAck[21]);
    }

    [Fact]
    public async Task Pure_csharp_cloud_serial_id_login_search_and_download_succeeds_without_wine()
    {
        using var tempDir = new TempDir();
        const string serialId = "0123456789abcdef";

        var network = new InMemoryUdpNetwork();
        var discoveryEp = new IPEndPoint(IPAddress.Loopback, 7999);
        var uuidQueryEp = new IPEndPoint(IPAddress.Loopback, 8777);
        var natServerEp = new IPEndPoint(IPAddress.Loopback, 8765);

        using var discoveryUdp = network.Bind(discoveryEp);
        using var uuidQueryUdp = network.Bind(uuidQueryEp);
        using var natAndDvrUdp = network.Bind(natServerEp);

        using var serverCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunSimulatedCloudNatAndDvrAsync(
            discoveryUdp, uuidQueryUdp, natAndDvrUdp, natServerEp, serialId, serverCts.Token);

        var options = new XmCloudNatClient.NatClientOptions(
            MasterDiscoveryEndpoint: discoveryEp,
            UuidQueryEndpoint: uuidQueryEp,
            StepTimeout: TimeSpan.FromSeconds(2),
            TransportFactory: () => network.BindEphemeral());

        var client = new LinuxDvrClient(Path.Combine(tempDir.Path, "no-sdk"), options);
        Assert.True(client.IsDriverReadyForCloud);

        var branch = new Branch(
            Name: "Bandung Cloud Branch",
            Area: "West Java",
            CloudId: serialId,
            Port: 34567,
            Username: "admin",
            Cameras: Camera.Range("Bandung Cloud Branch", 4),
            Mode: LoginMode.Cloud,
            Address: string.Empty);

        using var conn = await client.ConnectAsync(branch, new Credential("admin", "12345"), serverCts.Token);
        Assert.Equal(8, conn.Info.ChannelCount);
        Assert.Equal(serialId, conn.Info.SerialNumber);
        Assert.Equal("MBD6808T-EL", conn.Info.Model);

        var from = new DateTime(2025, 2, 10, 8, 0, 0);
        var to = new DateTime(2025, 2, 10, 12, 0, 0);
        var files = await conn.FindFilesAsync(0, from, to, serverCts.Token);
        Assert.Single(files);
        Assert.Equal("/idea0/2025-02-10/001/08.00.00-09.00.00[R][@1].h264", files[0].RemoteName);
        Assert.Equal(4, files[0].SizeKb);

        string destFile = Path.Combine(tempDir.Path, "downloaded.h264");
        await conn.DownloadAsync(files[0], destFile, null, serverCts.Token);
        Assert.True(File.Exists(destFile));
        byte[] savedBytes = await File.ReadAllBytesAsync(destFile, serverCts.Token);
        Assert.Equal(4096, savedBytes.Length);

        serverCts.Cancel();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    private sealed class InMemoryUdpNetwork
    {
        private readonly ConcurrentDictionary<IPEndPoint, InMemoryEndpoint> _endpoints = new();
        private int _nextPort = 40000;

        public IUdpDatagramTransport Bind(IPEndPoint ep)
        {
            var node = new InMemoryEndpoint(this, ep);
            _endpoints[ep] = node;
            return node;
        }

        public IUdpDatagramTransport BindEphemeral()
        {
            int port = Interlocked.Increment(ref _nextPort);
            return Bind(new IPEndPoint(IPAddress.Loopback, port));
        }

        private void Deliver(IPEndPoint from, IPEndPoint to, byte[] payload)
        {
            if (_endpoints.TryGetValue(to, out var target))
                target.Enqueue(new UdpReceiveResult(payload, from));
        }

        private void Unregister(IPEndPoint ep) => _endpoints.TryRemove(ep, out _);

        private sealed class InMemoryEndpoint : IUdpDatagramTransport
        {
            private readonly InMemoryUdpNetwork _net;
            private readonly Channel<UdpReceiveResult> _inbox = Channel.CreateUnbounded<UdpReceiveResult>();

            public InMemoryEndpoint(InMemoryUdpNetwork net, IPEndPoint localEp)
            {
                _net = net;
                LocalEndPoint = localEp;
            }

            public IPEndPoint LocalEndPoint { get; }

            public int Available => _inbox.Reader.CanCount && _inbox.Reader.Count > 0 ? _inbox.Reader.Count * 64 : 0;

            public void Enqueue(UdpReceiveResult pkt) => _inbox.Writer.TryWrite(pkt);

            public ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint, CancellationToken ct = default)
            {
                ct.ThrowIfCancellationRequested();
                _net.Deliver(LocalEndPoint, remoteEndPoint, datagram.ToArray());
                return ValueTask.CompletedTask;
            }

            public ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken ct = default) =>
                _inbox.Reader.ReadAsync(ct);

            public void Dispose()
            {
                _net.Unregister(LocalEndPoint);
                _inbox.Writer.TryComplete();
            }
        }
    }

    private static async Task RunSimulatedCloudNatAndDvrAsync(
        IUdpDatagramTransport discoveryUdp,
        IUdpDatagramTransport uuidQueryUdp,
        IUdpDatagramTransport natAndDvrUdp,
        IPEndPoint natServerEp,
        string expectedSerial,
        CancellationToken ct)
    {
        // Task 1: Port 7999 Master discovery responder
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await discoveryUdp.ReceiveAsync(ct);
                byte[] rsp = new byte[24];
                BinaryPrimitives.WriteUInt32LittleEndian(rsp.AsSpan(0, 4), XmCloudNatClient.CmdMasterServerRsp);
                Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(rsp.AsSpan(4));
                await discoveryUdp.SendAsync(rsp, req.RemoteEndPoint, ct);
            }
        }, ct);

        // Task 2: Port 8777 UUID -> NAT Server responder
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var req = await uuidQueryUdp.ReceiveAsync(ct);
                byte[] rsp = new byte[140];
                BinaryPrimitives.WriteUInt32LittleEndian(rsp.AsSpan(0, 4), XmCloudNatClient.CmdQueryUuidSvrRsp);
                BinaryPrimitives.WriteInt32LittleEndian(rsp.AsSpan(4, 4), 1); // status = 1 (online)
                Encoding.ASCII.GetBytes(expectedSerial).CopyTo(rsp.AsSpan(8));
                Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(rsp.AsSpan(108));
                BinaryPrimitives.WriteUInt32LittleEndian(rsp.AsSpan(128, 4), (uint)natServerEp.Port);
                await uuidQueryUdp.SendAsync(rsp, req.RemoteEndPoint, ct);
            }
        }, ct);

        // Task 3: NAT Server + Simulated DVR Peer on natAndDvrUdp
        var dvrSendSeqByEp = new Dictionary<IPEndPoint, uint>();
        var recvBufferByEp = new Dictionary<IPEndPoint, List<byte>>();
        IPEndPoint? claimedDataEndpoint = null;

        uint NextSeqFor(IPEndPoint ep)
        {
            dvrSendSeqByEp.TryGetValue(ep, out uint seq);
            dvrSendSeqByEp[ep] = seq + 1;
            return seq;
        }

        List<byte> BufferFor(IPEndPoint ep)
        {
            if (!recvBufferByEp.TryGetValue(ep, out var list))
            {
                list = new List<byte>();
                recvBufferByEp[ep] = list;
            }
            return list;
        }

        while (!ct.IsCancellationRequested)
        {
            var pkt = await natAndDvrUdp.ReceiveAsync(ct);
            if (pkt.Buffer.Length < 4) continue;
            uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(pkt.Buffer.AsSpan(0, 4));

            if (cmd == XmCloudNatClient.CmdWanAddrReq)
            {
                byte[] rsp = new byte[24];
                BinaryPrimitives.WriteUInt32LittleEndian(rsp.AsSpan(0, 4), XmCloudNatClient.CmdWanAddrRsp);
                Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(rsp.AsSpan(4));
                BinaryPrimitives.WriteUInt16LittleEndian(rsp.AsSpan(20, 2), (ushort)pkt.RemoteEndPoint.Port);
                await natAndDvrUdp.SendAsync(rsp, pkt.RemoteEndPoint, ct);
            }
            else if (cmd == XmCloudNatClient.CmdPeerConnectReq && pkt.Buffer.Length >= 360)
            {
                uint clientSession = BinaryPrimitives.ReadUInt32LittleEndian(pkt.Buffer.AsSpan(4, 4));
                dvrSendSeqByEp[pkt.RemoteEndPoint] = 0;
                BufferFor(pkt.RemoteEndPoint).Clear();

                // Send 0x03ED2012 ACK (status = 1)
                byte[] ack = new byte[12];
                BinaryPrimitives.WriteUInt32LittleEndian(ack.AsSpan(0, 4), XmCloudNatClient.CmdPeerConnectAck);
                BinaryPrimitives.WriteUInt32LittleEndian(ack.AsSpan(4, 4), clientSession);
                BinaryPrimitives.WriteInt32LittleEndian(ack.AsSpan(8, 4), 1);
                await natAndDvrUdp.SendAsync(ack, pkt.RemoteEndPoint, ct);

                // Send 0x03EC2012 reverse peer info pointing P2P directly at natServerEp
                byte[] rev = new byte[360];
                BinaryPrimitives.WriteUInt32LittleEndian(rev.AsSpan(0, 4), XmCloudNatClient.CmdPeerConnectReq);
                BinaryPrimitives.WriteUInt32LittleEndian(rev.AsSpan(4, 4), 9001u); // dstSessionId
                Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(rev.AsSpan(208));
                BinaryPrimitives.WriteUInt16LittleEndian(rev.AsSpan(224, 2), (ushort)natServerEp.Port);
                Encoding.ASCII.GetBytes("127.0.0.1").CopyTo(rev.AsSpan(226));
                BinaryPrimitives.WriteUInt16LittleEndian(rev.AsSpan(326, 2), (ushort)natServerEp.Port);
                BinaryPrimitives.WriteUInt32LittleEndian(rev.AsSpan(328, 4), clientSession);
                await natAndDvrUdp.SendAsync(rev, pkt.RemoteEndPoint, ct);
            }
            else if (cmd == XmCloudNatClient.CmdHelloReq && pkt.Buffer.Length >= 112)
            {
                ushort attempt = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Buffer.AsSpan(108, 2));
                byte[] helloRsp = XmCloudNatClient.BuildHelloRspPacket(9001u, expectedSerial, attempt);
                await natAndDvrUdp.SendAsync(helloRsp, pkt.RemoteEndPoint, ct);
            }
            else if (cmd == XmCloudNatClient.CmdDataPacket && pkt.Buffer.Length >= 24)
            {
                ushort innerLen = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Buffer.AsSpan(6, 2));
                byte[] inner = pkt.Buffer.AsSpan(12, innerLen).ToArray();
                if (inner[0] == (byte)'X' && inner[1] == (byte)'M' && inner[2] == (byte)'I' && inner[3] == (byte)'P')
                {
                    uint seq = BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(4, 4));
                    byte flags = inner[9];
                    if ((flags & 0x02) != 0)
                    {
                        // Acknowledge client's XMIP data slice
                        byte[] xmipAck = XmCloudNatClient.BuildXmipAckPacket(seq, 1);
                        await natAndDvrUdp.SendAsync(xmipAck, pkt.RemoteEndPoint, ct);

                        var recvBuffer = BufferFor(pkt.RemoteEndPoint);
                        recvBuffer.AddRange(inner[12..]);
                        if (recvBuffer.Count >= 20)
                        {
                            byte[] raw = recvBuffer.ToArray();
                            ushort msgId = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(14, 2));
                            int bodyLen = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(16, 4));
                            if (raw.Length >= 20 + bodyLen)
                            {
                                recvBuffer.Clear();
                                if (msgId == 1424)
                                    claimedDataEndpoint = pkt.RemoteEndPoint;

                                await ReplyDvripOverXmipAsync(
                                    natAndDvrUdp,
                                    pkt.RemoteEndPoint,
                                    claimedDataEndpoint ?? pkt.RemoteEndPoint,
                                    msgId,
                                    expectedSerial,
                                    NextSeqFor,
                                    ct);
                            }
                        }
                    }
                }
            }
        }
    }

    private static async Task ReplyDvripOverXmipAsync(
        IUdpDatagramTransport udp,
        IPEndPoint controlEp,
        IPEndPoint claimEp,
        ushort reqMsgId,
        string serialId,
        Func<IPEndPoint, uint> nextSeqFor,
        CancellationToken ct)
    {
        if (reqMsgId == 1000) // LoginReq -> LoginRsp (1001)
        {
            var rsp = new JsonObject
            {
                ["Ret"] = 100,
                ["SessionID"] = "0x0000002a",
                ["ChannelNum"] = 8,
                ["ExtraChannel"] = 0,
                ["DeviceType"] = "MBD6808T-EL",
            };
            await SendDvripPacketOverXmipAsync(udp, controlEp, 1001, 0x2A, 0, Encoding.UTF8.GetBytes(rsp.ToJsonString() + "\n\0"), () => nextSeqFor(controlEp), ct);
        }
        else if (reqMsgId == 1020) // SysInfoReq -> SysInfoRsp (1021)
        {
            var rsp = new JsonObject
            {
                ["Ret"] = 100,
                ["Name"] = "SystemInfo",
                ["SystemInfo"] = new JsonObject
                {
                    ["VideoInChannel"] = 8,
                    ["DigChannel"] = 0,
                    ["SerialNo"] = serialId,
                    ["Hardware"] = "MBD6808T-EL",
                },
            };
            await SendDvripPacketOverXmipAsync(udp, controlEp, 1021, 0x2A, 0, Encoding.UTF8.GetBytes(rsp.ToJsonString() + "\n\0"), () => nextSeqFor(controlEp), ct);
        }
        else if (reqMsgId == 1440) // FileSearchReq -> FileSearchRsp (1441)
        {
            var rsp = new JsonObject
            {
                ["Ret"] = 100,
                ["Name"] = "OPFileQuery",
                ["OPFileQuery"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["FileName"] = "/idea0/2025-02-10/001/08.00.00-09.00.00[R][@1].h264",
                        ["BeginTime"] = "2025-02-10 08:00:00",
                        ["EndTime"] = "2025-02-10 09:00:00",
                        ["FileLength"] = "0x00000004", // 4 KB
                    }
                },
            };
            await SendDvripPacketOverXmipAsync(udp, controlEp, 1441, 0x2A, 0, Encoding.UTF8.GetBytes(rsp.ToJsonString() + "\n\0"), () => nextSeqFor(controlEp), ct);
        }
        else if (reqMsgId == 1424) // ClaimReq (0x0590) -> ClaimRsp (1425 = 0x0591) on claim connection
        {
            var claimRsp = new JsonObject { ["Ret"] = 100, ["Name"] = "OPPlayBack", ["SessionID"] = "0x2a" };
            await SendDvripPacketOverXmipAsync(udp, controlEp, 1425, 0x2A, 0, Encoding.UTF8.GetBytes(claimRsp.ToJsonString() + "\n\0"), () => nextSeqFor(controlEp), ct);
        }
        else if (reqMsgId == 1420) // PlayBackReq (DownloadStart, 0x058C) -> PlayBackRsp (1421) on control + PlayDownloadData (1426) on claim connection
        {
            var startRsp = new JsonObject { ["Ret"] = 100, ["Name"] = "OPPlayBack", ["SessionID"] = "0x2a" };
            await SendDvripPacketOverXmipAsync(udp, controlEp, 1421, 0x2A, 0, Encoding.UTF8.GetBytes(startRsp.ToJsonString() + "\n\0"), () => nextSeqFor(controlEp), ct);

            byte[] h264Chunk = new byte[4096];
            Random.Shared.NextBytes(h264Chunk);
            await SendDvripPacketOverXmipAsync(udp, claimEp, 1426, 0x2A, endFlag: 0, h264Chunk, () => nextSeqFor(claimEp), ct);
            await SendDvripPacketOverXmipAsync(udp, claimEp, 1426, 0x2A, endFlag: 1, Array.Empty<byte>(), () => nextSeqFor(claimEp), ct);
        }
    }

    private static async Task SendDvripPacketOverXmipAsync(
        IUdpDatagramTransport udp,
        IPEndPoint clientEp,
        ushort msgId,
        uint sessionId,
        byte endFlag,
        byte[] body,
        Func<uint> nextSeq,
        CancellationToken ct)
    {
        byte[] dvrip = new byte[20 + body.Length];
        dvrip[0] = 0xFF;
        dvrip[1] = 0x01;
        BinaryPrimitives.WriteUInt32LittleEndian(dvrip.AsSpan(4, 4), sessionId);
        dvrip[13] = endFlag;
        BinaryPrimitives.WriteUInt16LittleEndian(dvrip.AsSpan(14, 2), msgId);
        BinaryPrimitives.WriteInt32LittleEndian(dvrip.AsSpan(16, 4), body.Length);
        body.CopyTo(dvrip.AsSpan(20));

        int offset = 0;
        while (offset < dvrip.Length)
        {
            int len = Math.Min(XmCloudNatStream.MaxSlicePayloadBytes, dvrip.Length - offset);
            bool isFirst = offset == 0;
            bool isLast = (offset + len) >= dvrip.Length;
            byte[] xmip = XmCloudNatClient.BuildXmipDataPacket(nextSeq(), dvrip.AsSpan(offset, len), isFirst, isLast);
            await udp.SendAsync(xmip, clientEp, ct);
            offset += len;
        }
    }
}
