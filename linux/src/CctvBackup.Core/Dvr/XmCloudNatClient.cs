using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace CctvBackup.Core.Dvr;

/// <summary>
/// Abstraction over a bound UDP socket so the entire libeznat + "XMIP" state machine can run
/// over real OS UDP sockets in production and deterministic in-memory datagram networks in tests.
/// </summary>
public interface IUdpDatagramTransport : IDisposable
{
    IPEndPoint LocalEndPoint { get; }
    int Available { get; }
    ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint, CancellationToken ct = default);
    ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken ct = default);

    /// <summary>Synchronous UDP send — zero async overhead, safe for hot-path ACK sending.</summary>
    void Send(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint)
    {
        // Default: fallback to async. Overridden for real sockets.
        SendAsync(datagram, remoteEndPoint).AsTask().GetAwaiter().GetResult();
    }
}

public sealed class SocketUdpTransport : IUdpDatagramTransport
{
    private readonly UdpClient _udp;

    public SocketUdpTransport()
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        try
        {
            _udp.Client.ReceiveBufferSize = 4 * 1024 * 1024;
            _udp.Client.SendBufferSize = 1024 * 1024;
        }
        catch { }
        XmCloudNatClient.DisableUdpConnReset(_udp);
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)_udp.Client.LocalEndPoint!;

    public int Available
    {
        get
        {
            try { return _udp.Available; }
            catch { return 0; }
        }
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint, CancellationToken ct = default) =>
        await _udp.SendAsync(datagram, remoteEndPoint, ct);

    /// <summary>Synchronous UDP send — copies 24-byte ACK to kernel buffer in ~1µs, no async overhead.</summary>
    public void Send(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint) =>
        _udp.Send(datagram.Span, remoteEndPoint);

    public ValueTask<UdpReceiveResult> ReceiveAsync(CancellationToken ct = default) =>
        _udp.ReceiveAsync(ct);

    public void Dispose() => _udp.Dispose();
}

/// <summary>
/// Pure native C# implementation of the Xiongmai (XMeye / secu100.net / libeznat) Cloud NAT traversal,
/// proxy relay, and "XMIP" reliable UDP transport protocol for connecting to DVRs by Serial ID on Linux
/// without Wine or 32-bit DLLs.
/// </summary>
public static class XmCloudNatClient
{
    public const uint CmdMasterServerReq = 0x05F42014;
    public const uint CmdMasterServerRsp = 0x05F52014;
    public const uint CmdServerListReq = 0x05F02012;
    public const uint CmdServerListRsp = 0x05F12012;
    public const uint CmdQueryUuidSvrReq = 0xB0002015;
    public const uint CmdQueryUuidSvrRsp = 0xB0012015;
    public const uint CmdWanAddrReq = 0x03E82012;
    public const uint CmdWanAddrRsp = 0x03E92012;
    public const uint CmdPeerConnectReq = 0x03EC2012;
    public const uint CmdPeerConnectAck = 0x03ED2012;
    public const uint CmdHelloReq = 0x07D02012;
    public const uint CmdHelloRsp = 0x07D12012;
    public const uint CmdHeartbeat = 0x07D22012;
    public const uint CmdDataPacket = 0x07D32012;
    public const uint CmdCloseCon = 0x07D42012;
    public const uint CmdCloseConEx = 0x07D62014;
    public const uint CmdProxyReq = 0x0BB92012;
    public const uint CmdProxyRsp = 0x0BBA2012;
    public const uint CmdProxyFullRsp = 0x0BBA2013;

    public const int MasterDiscoveryPort = 7999;
    public const int DefaultNatServerPort = 8765;
    public const int UuidQueryPort = 8777;

    private static readonly string[] DefaultMasterHosts =
    [
        "secu100.net",
        "112.124.0.188",
        "54.84.132.236"
    ];

    private static uint _sessionCounter = (uint)(Environment.TickCount & 0x7FFF);
    private static readonly ConcurrentDictionary<string, (IPEndPoint Endpoint, DateTime ExpiresUtc)> _natServerCache =
        new(StringComparer.OrdinalIgnoreCase);

    public sealed record DeviceNatServerInfo(
        int Status,
        string Uuid,
        string ServerIp,
        int ServerPort,
        uint RemoteNatType,
        uint FamilySeries);

    public sealed record PeerReverseInfo(
        uint DstSessionId,
        string DstWanIp,
        int DstWanPort,
        string[] DstLanIps,
        int DstLanPort,
        string ProxyIp,
        int ProxyPort);

    /// <summary>
    /// Optional override endpoints and transport factory for unit/integration testing.
    /// </summary>
    public sealed record NatClientOptions(
        IPEndPoint? MasterDiscoveryEndpoint = null,
        IPEndPoint? UuidQueryEndpoint = null,
        TimeSpan? StepTimeout = null,
        Func<IUdpDatagramTransport>? TransportFactory = null);

    private static IUdpDatagramTransport CreateTransport(NatClientOptions? options) =>
        options?.TransportFactory is not null ? options.TransportFactory() : new SocketUdpTransport();

    public static async Task<XmCloudNatStream> ConnectAsync(
        string serialId,
        CancellationToken ct,
        NatClientOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(serialId))
            throw new ArgumentException("Serial ID is required for Cloud connection.", nameof(serialId));

        serialId = serialId.Trim();
        TimeSpan stepTimeout = options?.StepTimeout ?? TimeSpan.FromSeconds(4);

        bool canUseCache = options?.MasterDiscoveryEndpoint is null && options?.UuidQueryEndpoint is null;
        if (canUseCache &&
            _natServerCache.TryGetValue(serialId, out var cached) &&
            cached.ExpiresUtc > DateTime.UtcNow)
        {
            try
            {
                return await ConnectPeerAsync(serialId, cached.Endpoint, stepTimeout, ct, options);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                _natServerCache.TryRemove(serialId, out _);
            }
        }

        // Step 1: Discover active NAT master server IP
        string masterIp = await DiscoverMasterServerIpAsync(options, stepTimeout, ct);

        // Step 2: Query which NAT server the target DVR (Serial ID) is registered on
        int queryPort = options?.UuidQueryEndpoint?.Port ?? UuidQueryPort;
        string queryHost = options?.UuidQueryEndpoint?.Address.ToString() ?? masterIp;
        var devNatInfo = await QueryDeviceNatServerAsync(queryHost, queryPort, serialId, stepTimeout, ct, options);

        if (devNatInfo.Status != 1 || string.IsNullOrWhiteSpace(devNatInfo.ServerIp))
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

        // Step 3: Perform CPeerConnect NAT handshake (P2P + Proxy fallback) and return reliable XMIP stream
        var devServerEp = new IPEndPoint(
            IPAddress.Parse(devNatInfo.ServerIp),
            devNatInfo.ServerPort > 0 ? devNatInfo.ServerPort : DefaultNatServerPort);

        if (canUseCache)
        {
            _natServerCache[serialId] = (devServerEp, DateTime.UtcNow.AddMinutes(3));
        }

        return await ConnectPeerAsync(serialId, devServerEp, stepTimeout, ct, options);
    }

    public static async Task<string> DiscoverMasterServerIpAsync(
        NatClientOptions? options,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (options?.MasterDiscoveryEndpoint is { } customEp)
        {
            string? discovered = await TryQueryMasterPort7999Async(customEp, timeout, ct, options);
            return !string.IsNullOrWhiteSpace(discovered) ? discovered : customEp.Address.ToString();
        }

        var candidates = new List<IPAddress>();
        foreach (string host in DefaultMasterHosts)
        {
            try
            {
                if (IPAddress.TryParse(host, out var ip))
                {
                    candidates.Add(ip);
                }
                else
                {
                    var resolved = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
                    candidates.AddRange(resolved);
                }
            }
            catch
            {
                // Ignore DNS failures for individual candidate hosts
            }
        }

        foreach (var ip in candidates.Distinct())
        {
            string? activeIp = await TryQueryMasterPort7999Async(new IPEndPoint(ip, MasterDiscoveryPort), timeout, ct, options);
            if (!string.IsNullOrWhiteSpace(activeIp) && IPAddress.TryParse(activeIp, out _))
                return activeIp;
        }

        if (candidates.Count > 0)
            return candidates[0].ToString();

        throw new SocketException((int)SocketError.HostNotFound);
    }

    private static async Task<string?> TryQueryMasterPort7999Async(
        IPEndPoint endpoint,
        TimeSpan timeout,
        CancellationToken ct,
        NatClientOptions? options)
    {
        using var udp = CreateTransport(options);
        byte[] req = BuildMasterServerDiscoveryPacket();

        try
        {
            var recvTask = udp.ReceiveAsync(ct).AsTask();
            await udp.SendAsync(req, endpoint, ct);
            var rsp = await recvTask.WaitAsync(timeout, ct);
            return TryParseMasterServerDiscoveryResponse(rsp.Buffer, out string? masterIp) ? masterIp : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public static byte[] BuildMasterServerDiscoveryPacket(string macHex = "1234567890ab")
    {
        byte[] pkt = new byte[104];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdMasterServerReq);
        WriteFixedAscii(pkt.AsSpan(4, 100), macHex);
        return pkt;
    }

    public static bool TryParseMasterServerDiscoveryResponse(ReadOnlySpan<byte> buffer, out string? serverIp)
    {
        serverIp = null;
        if (buffer.Length < 24) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]) != CmdMasterServerRsp) return false;
        serverIp = ReadNullTerminatedAscii(buffer.Slice(4, 20));
        return !string.IsNullOrWhiteSpace(serverIp);
    }

    public static byte[] BuildQueryDeviceNatServerPacket(string serialId)
    {
        byte[] serialBytes = Encoding.ASCII.GetBytes(serialId);
        byte[] pkt = new byte[4 + serialBytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdQueryUuidSvrReq);
        serialBytes.CopyTo(pkt.AsSpan(4));
        return pkt;
    }

    public static bool TryParseQueryDeviceNatServerResponse(ReadOnlySpan<byte> buffer, out DeviceNatServerInfo? info)
    {
        info = null;
        if (buffer.Length < 140) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]) != CmdQueryUuidSvrRsp) return false;

        int status = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(4, 4));
        string uuid = ReadNullTerminatedAscii(buffer.Slice(8, 100));
        string serverIp = ReadNullTerminatedAscii(buffer.Slice(108, 20));
        int serverPort = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(128, 4));
        uint natType = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(132, 4));
        uint family = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(136, 4));

        info = new DeviceNatServerInfo(status, uuid, serverIp, serverPort, natType, family);
        return true;
    }

    public static async Task<DeviceNatServerInfo> QueryDeviceNatServerAsync(
        string masterIp,
        int port,
        string serialId,
        TimeSpan timeout,
        CancellationToken ct,
        NatClientOptions? options = null)
    {
        using var udp = CreateTransport(options);
        var endpoint = new IPEndPoint(IPAddress.Parse(masterIp), port);
        byte[] req = BuildQueryDeviceNatServerPacket(serialId);

        Task<UdpReceiveResult>? recvTask = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                recvTask ??= udp.ReceiveAsync(ct).AsTask();
                await udp.SendAsync(req, endpoint, ct);
                var rsp = await recvTask.WaitAsync(timeout, ct);
                recvTask = null;
                if (TryParseQueryDeviceNatServerResponse(rsp.Buffer, out var info) && info is not null)
                    return info;
            }
            catch (TimeoutException) when (attempt == 0)
            {
                // Retry once while keeping recvTask active
            }
        }

        throw new TimeoutException($"Timed out querying NAT server for Serial ID '{serialId}'.");
    }

    public static string GenerateClientUuid()
    {
        int tickPart = Math.Abs(Environment.TickCount) % 10000;
        int r1 = Random.Shared.Next(0, 10000);
        int r2 = Random.Shared.Next(0, 10000);
        return $"!xmnatuuid-IEClient-{tickPart:D4}{r1:D4}{r2:D4}";
    }

    public static byte[] BuildWanAddrRequestPacket()
    {
        byte[] pkt = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt, CmdWanAddrReq);
        return pkt;
    }

    public static bool TryParseWanAddrResponse(ReadOnlySpan<byte> buffer, out string wanIp, out ushort wanPort)
    {
        wanIp = string.Empty;
        wanPort = 0;
        if (buffer.Length < 24) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]) != CmdWanAddrRsp) return false;
        wanIp = ReadNullTerminatedAscii(buffer.Slice(4, 16));
        wanPort = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(20, 2));
        return !string.IsNullOrWhiteSpace(wanIp);
    }

    public static byte[] BuildPeerConnectRequestPacket(
        uint mySessionId,
        string myUuid,
        string destSerialId,
        string myWanIp,
        ushort myWanPort,
        string myLanIpList,
        ushort myLanPort,
        bool skipP2P = false)
    {
        byte[] pkt = new byte[360];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdPeerConnectReq);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(4, 4), mySessionId);
        WriteFixedAscii(pkt.AsSpan(8, 100), myUuid);
        WriteFixedAscii(pkt.AsSpan(108, 100), destSerialId);
        WriteFixedAscii(pkt.AsSpan(208, 16), myWanIp);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(224, 2), myWanPort);
        WriteFixedAscii(pkt.AsSpan(226, 100), myLanIpList);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(326, 2), myLanPort);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(328, 4), 0); // dstSessionId = 0 in request
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(352, 4), (uint)(Environment.TickCount64 / 1000));
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(356, 4), skipP2P ? 1u : 0u);
        return pkt;
    }

    public static bool TryParsePeerReversePacket(
        ReadOnlySpan<byte> buffer,
        uint expectedMySessionId,
        out PeerReverseInfo? info)
    {
        info = null;
        if (buffer.Length < 360) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]) != CmdPeerConnectReq) return false;

        uint echoedMySession = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(328, 4));
        if (expectedMySessionId != 0 && echoedMySession != expectedMySessionId)
            return false;

        uint dstSessionId = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(4, 4));
        string dstWanIp = ReadNullTerminatedAscii(buffer.Slice(208, 16));
        ushort dstWanPort = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(224, 2));
        string dstLanRaw = ReadNullTerminatedAscii(buffer.Slice(226, 100));
        ushort dstLanPort = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(326, 2));
        string proxyIp = ReadNullTerminatedAscii(buffer.Slice(332, 16));
        int proxyPort = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(348, 4));

        string[] lanIps = dstLanRaw
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        info = new PeerReverseInfo(dstSessionId, dstWanIp, dstWanPort, lanIps, dstLanPort, proxyIp, proxyPort);
        return true;
    }

    public static byte[] BuildHelloReqPacket(uint mySessionId, string myUuid, uint attempt)
    {
        byte[] pkt = new byte[112];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdHelloReq);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(4, 4), mySessionId);
        WriteFixedAscii(pkt.AsSpan(8, 100), myUuid);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(108, 4), attempt);
        return pkt;
    }

    public static byte[] BuildHelloRspPacket(uint mySessionId, string myUuid, ushort peerAttempt)
    {
        byte[] pkt = new byte[112];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdHelloRsp);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(4, 4), mySessionId);
        WriteFixedAscii(pkt.AsSpan(8, 100), myUuid);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(108, 2), peerAttempt);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(110, 2), 2);
        return pkt;
    }

    public static byte[] BuildProxyReqPacket(string myUuid, uint mySessionId, string destSerialId, uint dstSessionId)
    {
        byte[] pkt = new byte[212];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdProxyReq);
        WriteFixedAscii(pkt.AsSpan(4, 100), myUuid);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(104, 4), mySessionId);
        WriteFixedAscii(pkt.AsSpan(108, 100), destSerialId);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(208, 4), dstSessionId);
        return pkt;
    }

    public static byte[] BuildHeartbeatPacket(ushort seq)
    {
        byte[] pkt = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdHeartbeat);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(4, 2), seq);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(6, 2), 2);
        return pkt;
    }

    public static byte[] BuildXmipDataPacket(uint seq, ReadOnlySpan<byte> payload, bool isFirstSlice, bool isLastSlice)
    {
        int innerLen = 12 + payload.Length;
        byte[] pkt = new byte[12 + innerLen];

        // Outer 12-byte CPeerConnect header (0x07D32012)
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdDataPacket);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(4, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(6, 2), (ushort)innerLen);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(8, 4), 0);

        // Inner 12-byte "XMIP" slice header
        pkt[12] = (byte)'X';
        pkt[13] = (byte)'M';
        pkt[14] = (byte)'I';
        pkt[15] = (byte)'P';
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(16, 4), seq);
        pkt[20] = 1; // count = 1
        byte flags = 0x02; // DATA bit
        if (isFirstSlice) flags |= 0x04; // sflag
        if (isLastSlice) flags |= 0x08;  // eflag
        pkt[21] = flags;
        pkt[22] = 0;
        pkt[23] = 0;

        payload.CopyTo(pkt.AsSpan(24));
        return pkt;
    }

    public static byte[] BuildXmipAckPacket(uint seq, byte count = 1)
    {
        byte[] pkt = new byte[24]; // 12-byte outer + 12-byte inner
        FillXmipAckPacket(pkt, seq, count);
        return pkt;
    }

    /// <summary>Fill a pre-allocated 24-byte buffer with an XMIP ACK. Zero allocations.</summary>
    public static void FillXmipAckPacket(byte[] pkt, uint seq, byte count)
    {
        // Outer 12-byte CPeerConnect header (0x07D32012)
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(0, 4), CmdDataPacket);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(4, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(pkt.AsSpan(6, 2), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(8, 4), 0);

        // Inner 12-byte "XMIP" ACK header
        pkt[12] = (byte)'X';
        pkt[13] = (byte)'M';
        pkt[14] = (byte)'I';
        pkt[15] = (byte)'P';
        BinaryPrimitives.WriteUInt32LittleEndian(pkt.AsSpan(16, 4), seq);
        pkt[20] = count;
        pkt[21] = 0x01; // ACK bit
        pkt[22] = 0;
        pkt[23] = 0;
    }

    public static async Task<XmCloudNatStream> ConnectPeerAsync(
        string serialId,
        IPEndPoint devNatServer,
        TimeSpan stepTimeout,
        CancellationToken ct,
        NatClientOptions? options = null)
    {
        var udp = CreateTransport(options);

        try
        {
            ushort myLanPort = (ushort)udp.LocalEndPoint.Port;
            string myLanIps = GetLocalIPv4ListString();
            string myUuid = GenerateClientUuid();
            uint mySessionId = Interlocked.Increment(ref _sessionCounter);

            Task<UdpReceiveResult>? pendingRecv = null;
            async Task<UdpReceiveResult> ReceiveWithTimeoutAsync(TimeSpan timeout)
            {
                pendingRecv ??= udp.ReceiveAsync(ct).AsTask();
                var res = await pendingRecv.WaitAsync(timeout, ct);
                pendingRecv = null;
                return res;
            }

            // State 0: Query our WAN IP and Port from device's NAT server
            string myWanIp = "0.0.0.0";
            ushort myWanPort = myLanPort;
            byte[] wanReq = BuildWanAddrRequestPacket();

            for (int i = 0; i < 3; i++)
            {
                await udp.SendAsync(wanReq, devNatServer, ct);
                try
                {
                    var rsp = await ReceiveWithTimeoutAsync(stepTimeout);
                    if (TryParseWanAddrResponse(rsp.Buffer, out string wip, out ushort wport))
                    {
                        myWanIp = wip;
                        myWanPort = wport;
                        break;
                    }
                }
                catch (TimeoutException) when (i < 2)
                {
                }
            }

            // State 1 & 2: Send 360-byte PeerConnectReq and wait for 0x03ED2012 ACK + 0x03EC2012 PeerReverseInfo
            byte[] connReq = BuildPeerConnectRequestPacket(
                mySessionId, myUuid, serialId, myWanIp, myWanPort, myLanIps, myLanPort, skipP2P: false);

            PeerReverseInfo? reverseInfo = null;
            IPEndPoint? connectedPeer = null;
            DateTime hsDeadline = DateTime.UtcNow.AddMilliseconds(Math.Max(3000, stepTimeout.TotalMilliseconds * 2));
            await udp.SendAsync(connReq, devNatServer, ct);

            while (reverseInfo is null && connectedPeer is null)
            {
                if (DateTime.UtcNow >= hsDeadline)
                    throw new TimeoutException($"Timed out waiting for peer reverse info for Serial ID '{serialId}'.");

                try
                {
                    var pkt = await ReceiveWithTimeoutAsync(TimeSpan.FromMilliseconds(600));
                    if (pkt.Buffer.Length < 4) continue;
                    uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(pkt.Buffer.AsSpan(0, 4));
                    if (cmd == CmdPeerConnectAck && pkt.Buffer.Length >= 12)
                    {
                        int status = BinaryPrimitives.ReadInt32LittleEndian(pkt.Buffer.AsSpan(8, 4));
                        if (status != 1)
                            throw new SocketException((int)SocketError.HostUnreachable);
                    }
                    else if (cmd == CmdPeerConnectReq &&
                             TryParsePeerReversePacket(pkt.Buffer, mySessionId, out var parsed) &&
                             parsed is not null)
                    {
                        reverseInfo = parsed;
                    }
                    else if (cmd == CmdHelloReq && pkt.Buffer.Length >= 112)
                    {
                        // Peer already punched through to us while we were waiting for reverse info
                        ushort peerAttempt = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Buffer.AsSpan(108, 2));
                        byte[] rsp = BuildHelloRspPacket(mySessionId, myUuid, peerAttempt);
                        await udp.SendAsync(rsp, pkt.RemoteEndPoint, ct);
                        connectedPeer = pkt.RemoteEndPoint;
                    }
                }
                catch (TimeoutException)
                {
                    await udp.SendAsync(connReq, devNatServer, ct);
                }
            }

            // State 3: P2P direct punch (HELLO_REQ / HELLO_RSP)
            var p2pTargets = new List<IPEndPoint>();
            if (reverseInfo is not null)
            {
                if (reverseInfo.DstLanPort > 0)
                {
                    foreach (string lanIp in reverseInfo.DstLanIps)
                    {
                        if (IPAddress.TryParse(lanIp, out var ip))
                            p2pTargets.Add(new IPEndPoint(ip, reverseInfo.DstLanPort));
                    }
                }
                if (reverseInfo.DstWanPort > 0 && IPAddress.TryParse(reverseInfo.DstWanIp, out var wanAddr))
                {
                    var wanEp = new IPEndPoint(wanAddr, reverseInfo.DstWanPort);
                    if (!p2pTargets.Contains(wanEp))
                        p2pTargets.Add(wanEp);
                }
            }

            if (connectedPeer is null && p2pTargets.Count > 0)
            {
                connectedPeer = await TryHandshakeWithTargetsAsync(
                    udp, ReceiveWithTimeoutAsync, p2pTargets, mySessionId, myUuid, stepTimeout, ct);
            }

            // State 4 & 5: Proxy fallback if P2P did not connect and proxy is provided
            if (connectedPeer is null &&
                reverseInfo is not null &&
                reverseInfo.ProxyPort > 0 &&
                IPAddress.TryParse(reverseInfo.ProxyIp, out var proxyAddr))
            {
                var proxyEp = new IPEndPoint(proxyAddr, reverseInfo.ProxyPort);
                byte[] proxyReq = BuildProxyReqPacket(myUuid, mySessionId, serialId, reverseInfo.DstSessionId);

                bool proxyRegistered = false;
                for (int attempt = 0; attempt < 3 && !proxyRegistered; attempt++)
                {
                    await udp.SendAsync(proxyReq, proxyEp, ct);
                    try
                    {
                        var pkt = await ReceiveWithTimeoutAsync(TimeSpan.FromMilliseconds(Math.Max(500, stepTimeout.TotalMilliseconds / 2)));
                        if (pkt.Buffer.Length < 4) continue;
                        uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(pkt.Buffer.AsSpan(0, 4));
                        if (cmd == CmdProxyRsp)
                        {
                            proxyRegistered = true;
                        }
                        else if (cmd == CmdHelloReq && pkt.Buffer.Length >= 112)
                        {
                            ushort peerAttempt = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Buffer.AsSpan(108, 2));
                            byte[] rsp = BuildHelloRspPacket(mySessionId, myUuid, peerAttempt);
                            await udp.SendAsync(rsp, pkt.RemoteEndPoint, ct);
                            connectedPeer = pkt.RemoteEndPoint;
                            proxyRegistered = true;
                        }
                        else if (cmd is CmdHelloRsp or CmdHeartbeat)
                        {
                            connectedPeer = pkt.RemoteEndPoint;
                            proxyRegistered = true;
                        }
                    }
                    catch (TimeoutException)
                    {
                    }
                }

                if (connectedPeer is null && proxyRegistered)
                {
                    connectedPeer = await TryHandshakeWithTargetsAsync(
                        udp, ReceiveWithTimeoutAsync, [proxyEp], mySessionId, myUuid, stepTimeout, ct);
                }
            }

            if (connectedPeer is null)
                throw new TimeoutException($"Unable to establish P2P or Proxy NAT session with Serial ID '{serialId}'.");

            // Determine connection type for diagnostics
            bool isProxy = false;
            if (reverseInfo is not null && reverseInfo.ProxyPort > 0 &&
                IPAddress.TryParse(reverseInfo.ProxyIp, out var diagProxyAddr))
            {
                var proxyEpDiag = new IPEndPoint(diagProxyAddr, reverseInfo.ProxyPort);
                isProxy = connectedPeer.Equals(proxyEpDiag);
            }
            string connType = isProxy ? "PROXY (relay)" : "P2P (direct)";
            Console.Error.WriteLine($"[XMIP] Connected to {connectedPeer} via {connType}");

            return new XmCloudNatStream(udp, connectedPeer, mySessionId, myUuid, pendingRecv);
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }

    private static async Task<IPEndPoint?> TryHandshakeWithTargetsAsync(
        IUdpDatagramTransport udp,
        Func<TimeSpan, Task<UdpReceiveResult>> receiveWithTimeoutAsync,
        IReadOnlyList<IPEndPoint> targets,
        uint mySessionId,
        string myUuid,
        TimeSpan timeout,
        CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        const int sendIntervalMs = 150;

        for (uint attempt = 0; attempt < 24 && DateTime.UtcNow < deadline; attempt++)
        {
            byte[] helloReq = BuildHelloReqPacket(mySessionId, myUuid, attempt);
            foreach (var target in targets)
            {
                await udp.SendAsync(helloReq, target, ct);
            }

            DateTime nextSendAt = DateTime.UtcNow.AddMilliseconds(sendIntervalMs);
            if (nextSendAt > deadline) nextSendAt = deadline;

            while (DateTime.UtcNow < nextSendAt)
            {
                TimeSpan wait = nextSendAt - DateTime.UtcNow;
                if (wait <= TimeSpan.Zero) break;

                try
                {
                    var pkt = await receiveWithTimeoutAsync(wait);
                    if (pkt.Buffer.Length < 4) continue;
                    uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(pkt.Buffer.AsSpan(0, 4));
                    if (cmd == CmdHelloReq && pkt.Buffer.Length >= 112)
                    {
                        ushort peerAttempt = BinaryPrimitives.ReadUInt16LittleEndian(pkt.Buffer.AsSpan(108, 2));
                        byte[] rsp = BuildHelloRspPacket(mySessionId, myUuid, peerAttempt);
                        await udp.SendAsync(rsp, pkt.RemoteEndPoint, ct);
                        return pkt.RemoteEndPoint;
                    }
                    if (cmd is CmdHelloRsp or CmdHeartbeat)
                    {
                        return pkt.RemoteEndPoint;
                    }
                }
                catch (TimeoutException)
                {
                    break;
                }
            }
        }

        return null;
    }

    internal static void DisableUdpConnReset(UdpClient udp)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, [0, 0, 0, 0], null);
            }
            catch
            {
            }
        }
    }

    private static string GetLocalIPv4ListString()
    {
        try
        {
            var ips = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(ua => ua.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(ua => ua.Address.ToString())
                .Distinct()
                .ToList();
            if (ips.Count > 0)
                return string.Join(",", ips);
        }
        catch
        {
        }
        return "127.0.0.1";
    }

    internal static void WriteFixedAscii(Span<byte> dest, string value)
    {
        dest.Clear();
        if (string.IsNullOrEmpty(value)) return;
        int maxBytes = dest.Length - 1;
        int count = Encoding.ASCII.GetBytes(value.AsSpan(0, Math.Min(value.Length, maxBytes)), dest);
        dest[count] = 0;
    }

    internal static string ReadNullTerminatedAscii(ReadOnlySpan<byte> src)
    {
        int nullIdx = src.IndexOf((byte)0);
        if (nullIdx >= 0) src = src[..nullIdx];
        return Encoding.ASCII.GetString(src).Trim();
    }
}

/// <summary>
/// Reliable ordered stream over UDP implementing libeznat's CPeerConnect + "XMIP" slicing & ACK protocol.
/// Plugs directly into <see cref="DvripDvrClient"/> as a <see cref="Stream"/>.
/// </summary>
public sealed class XmCloudNatStream : Stream
{
    public const int MaxSlicePayloadBytes = 1388; // 1400 MTU - 12 byte XMIP header

    private readonly IUdpDatagramTransport _udp;
    private readonly IPEndPoint _peerEndpoint;
    private readonly uint _mySessionId;
    private readonly string _myUuid;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Task _recvLoopTask;
    private readonly Task _heartbeatLoopTask;

    private readonly ConcurrentDictionary<uint, TaskCompletionSource<bool>> _pendingAcks = new();
    private readonly SortedDictionary<uint, byte[]> _reorderBuffer = new();
    private readonly Queue<byte[]> _readyChunks = new();
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private readonly object _recvLock = new();

    private Task<UdpReceiveResult>? _initialPendingRecv;
    private byte[]? _currentChunk;
    private int _currentChunkOffset;
    private uint _nextSendSeq;
    private uint _nextRecvSeq;
    // 16-bit sliding bitmap for UdpSafeRecvHelper (0x0C4560), matching NetSdk.dll exactly:
    private bool _hasBitmap;
    private uint _maxRecvSeq;
    private ushort _recvBitmask;
    // Pre-allocated ACK buffer — reused across all ACK sends to eliminate per-packet GC allocation.
    // Safe because we use synchronous Send() which copies to kernel buffer before returning.
    private readonly byte[] _ackBuf = new byte[24];
    // Diagnostic stats for speed degradation investigation
    private long _totalPacketsReceived;
    private long _totalInOrderPackets;
    private long _totalOooPackets;    // out-of-order (gap detected)
    private long _totalDuplicates;    // seq < _nextRecvSeq
    private DateTime _lastStatsTime = DateTime.UtcNow;
    private long _lastStatsPackets;
    private ushort _heartbeatSeq;
    private bool _remoteClosed;
    private bool _disposed;

    public XmCloudNatStream(
        IUdpDatagramTransport udp,
        IPEndPoint peerEndpoint,
        uint mySessionId,
        string myUuid,
        Task<UdpReceiveResult>? initialPendingRecv = null)
    {
        _udp = udp;
        _peerEndpoint = peerEndpoint;
        _mySessionId = mySessionId;
        _myUuid = myUuid;
        _initialPendingRecv = initialPendingRecv;
        _recvLoopTask = Task.Run(ReceiveLoopAsync);
        _heartbeatLoopTask = Task.Run(HeartbeatLoopAsync);
    }

    public IPEndPoint PeerEndPoint => _peerEndpoint;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return;

        int total = buffer.Length;
        int offset = 0;

        while (offset < total)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int chunkLen = Math.Min(MaxSlicePayloadBytes, total - offset);
            bool isFirst = offset == 0;
            bool isLast = (offset + chunkLen) >= total;

            uint seq = Interlocked.Increment(ref _nextSendSeq) - 1;
            byte[] pkt = XmCloudNatClient.BuildXmipDataPacket(
                seq, buffer.Span.Slice(offset, chunkLen), isFirst, isLast);

            var ackTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingAcks[seq] = ackTcs;

            try
            {
                bool acked = false;
                for (int attempt = 0; attempt < 12 && !acked; attempt++)
                {
                    await _udp.SendAsync(pkt, _peerEndpoint, cancellationToken);
                    try
                    {
                        await ackTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(350), cancellationToken);
                        acked = true;
                    }
                    catch (TimeoutException)
                    {
                        // Retransmit unacknowledged XMIP slice
                    }
                }

                if (!acked)
                    throw new IOException($"XMIP UDP slice seq={seq} was not acknowledged by peer {_peerEndpoint}.");
            }
            finally
            {
                _pendingAcks.TryRemove(seq, out _);
            }

            offset += chunkLen;
        }
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0) return 0;

        while (true)
        {
            lock (_recvLock)
            {
                int totalCopied = 0;
                while (totalCopied < buffer.Length)
                {
                    if (_currentChunk is null || _currentChunkOffset >= _currentChunk.Length)
                    {
                        if (_readyChunks.Count > 0)
                        {
                            _currentChunk = _readyChunks.Dequeue();
                            _currentChunkOffset = 0;
                        }
                        else
                        {
                            break;
                        }
                    }

                    int toCopy = Math.Min(buffer.Length - totalCopied, _currentChunk.Length - _currentChunkOffset);
                    _currentChunk.AsSpan(_currentChunkOffset, toCopy).CopyTo(buffer.Span.Slice(totalCopied, toCopy));
                    _currentChunkOffset += toCopy;
                    totalCopied += toCopy;
                }

                if (totalCopied > 0)
                    return totalCopied;

                if (_remoteClosed)
                    return 0;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
            await _dataAvailable.WaitAsync(linked.Token);
        }
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    private void SignalDataAvailable()
    {
        try { _dataAvailable.Release(); } catch { }
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_disposeCts.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result;
                if (_initialPendingRecv is not null)
                {
                    var pending = _initialPendingRecv;
                    _initialPendingRecv = null;
                    result = await pending;
                }
                else
                {
                    result = await _udp.ReceiveAsync(_disposeCts.Token);
                }

                ProcessIncomingDatagram(result.Buffer, result.RemoteEndPoint);

                // Drain all packets currently queued in the socket buffer.
                // When Available > 0, ReceiveAsync completes synchronously (no async overhead).
                // This prevents socket buffer overflow under high packet rates, which is the
                // primary cause of packet loss → DVR congestion window reduction → speed degradation.
                while (_udp.Available > 0 && !_disposeCts.IsCancellationRequested)
                {
                    result = await _udp.ReceiveAsync(_disposeCts.Token);
                    ProcessIncomingDatagram(result.Buffer, result.RemoteEndPoint);
                }
            }
            catch
            {
                if (_disposeCts.IsCancellationRequested) break;
            }
        }
    }

    private async Task HeartbeatLoopAsync()
    {
        while (!_disposeCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4), _disposeCts.Token);
                byte[] hb = XmCloudNatClient.BuildHeartbeatPacket(++_heartbeatSeq);
                await _udp.SendAsync(hb, _peerEndpoint, _disposeCts.Token);

                // Track packet statistics
                _lastStatsTime = DateTime.UtcNow;
                _lastStatsPackets = _totalPacketsReceived;
            }
            catch
            {
                if (_disposeCts.IsCancellationRequested) break;
            }
        }
    }

    private void ProcessIncomingDatagram(byte[] buf, IPEndPoint sender)
    {
        if (buf.Length < 4) return;
        uint cmd = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0, 4));

        switch (cmd)
        {
            case XmCloudNatClient.CmdHelloReq when buf.Length >= 112:
            {
                ushort peerAttempt = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(108, 2));
                byte[] rsp = XmCloudNatClient.BuildHelloRspPacket(_mySessionId, _myUuid, peerAttempt);
                // Fire-and-forget — handshake responses are rare and non-critical if delayed
                _ = _udp.SendAsync(rsp, sender, _disposeCts.Token);
                break;
            }

            case XmCloudNatClient.CmdCloseCon:
            case XmCloudNatClient.CmdCloseConEx:
            {
                lock (_recvLock)
                {
                    _remoteClosed = true;
                }
                SignalDataAvailable();
                break;
            }

            case XmCloudNatClient.CmdDataPacket when buf.Length >= 12:
            {
                ushort innerLen = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(6, 2));
                if (innerLen == 0 || 12 + innerLen > buf.Length) return;

                if (innerLen >= 12 &&
                    buf[12] == (byte)'X' &&
                    buf[13] == (byte)'M' &&
                    buf[14] == (byte)'I' &&
                    buf[15] == (byte)'P')
                {
                    uint seq = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(16, 4));
                    byte count = buf[20];
                    byte flags = buf[21];

                    if (innerLen == 12 && (flags & 0x01) != 0)
                    {
                        // XMIP ACK packet from peer
                        uint ackCount = Math.Max((byte)1, count);
                        for (uint i = 0; i < ackCount; i++)
                        {
                            if (_pendingAcks.TryGetValue(seq + i, out var tcs))
                                tcs.TrySetResult(true);
                        }
                    }
                    else if ((flags & 0x02) != 0)
                    {
                        byte[] slicePayload = buf.AsSpan(24, innerLen - 12).ToArray();

                        bool released = false;
                        uint ackSeq;
                        byte ackCount;
                        int reorderSize;
                        _totalPacketsReceived++;

                        lock (_recvLock)
                        {
                            if (seq < _nextRecvSeq)
                            {
                                // Old / duplicate packet already consumed: ACK this packet specifically so sender stops retransmitting
                                _totalDuplicates++;
                                ackSeq = seq;
                                ackCount = 1;
                            }
                            else
                            {
                                bool wasInOrder = seq == _nextRecvSeq;
                                if (wasInOrder) _totalInOrderPackets++;
                                else _totalOooPackets++;

                                // Insert into reorder buffer and advance contiguous ready chunks
                                if (!_reorderBuffer.ContainsKey(seq))
                                {
                                    _reorderBuffer[seq] = slicePayload;
                                    while (_reorderBuffer.Remove(_nextRecvSeq, out byte[]? nextSlice))
                                    {
                                        _readyChunks.Enqueue(nextSlice);
                                        _nextRecvSeq++;
                                        released = true;
                                    }
                                }

                                // Exact NetSdk.dll UdpSafeRecvHelper (0x0C4560) algorithm:
                                if (!_hasBitmap)
                                {
                                    _hasBitmap = true;
                                    _maxRecvSeq = seq;
                                    _recvBitmask = 1;
                                    ackSeq = seq;
                                    ackCount = 1;
                                }
                                else if (seq > _maxRecvSeq)
                                {
                                    uint diff = seq - _maxRecvSeq;
                                    _recvBitmask = diff >= 32 ? (ushort)0 : (ushort)((_recvBitmask << (int)diff) & 0xFFFF);
                                    _recvBitmask |= 1;
                                    _maxRecvSeq = seq;

                                    // Find longest contiguous run of 1-bits from bit 0 (0x0C45D0..0x0C45E8)
                                    int cl = 0;
                                    for (; cl < 16; cl++)
                                    {
                                        uint pattern = 0xFFFFu >> cl;
                                        if ((_recvBitmask & pattern) == pattern)
                                            break;
                                    }
                                    ackCount = (byte)(16 - cl);
                                    ackSeq = seq - (uint)(ackCount - 1);
                                }
                                else // seq <= _maxRecvSeq (in-window packet that arrived out of order, or retransmit filling a gap)
                                {
                                    uint diff = _maxRecvSeq - seq;
                                    if (diff < 16)
                                        _recvBitmask |= (ushort)(1 << (int)diff);
                                    ackSeq = seq;
                                    ackCount = 1;
                                }
                            }
                            reorderSize = _reorderBuffer.Count;
                        }

                        // Send ACK immediately — synchronous UDP send. Zero async overhead.
                        XmCloudNatClient.FillXmipAckPacket(_ackBuf, ackSeq, ackCount);
                        _udp.Send(_ackBuf, _peerEndpoint);

                        if (released)
                            SignalDataAvailable();
                    }
                }
                else
                {
                    // Raw non-XMIP data frame
                    byte[] rawPayload = buf.AsSpan(12, innerLen).ToArray();
                    lock (_recvLock)
                    {
                        _readyChunks.Enqueue(rawPayload);
                    }
                    SignalDataAvailable();
                }
                break;
            }
        }
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            try
            {
                byte[] closePkt = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(closePkt, XmCloudNatClient.CmdCloseCon);
                _ = _udp.SendAsync(closePkt, _peerEndpoint);
            }
            catch { }

            _disposeCts.Cancel();
            try { _udp.Dispose(); } catch { }
            try { _dataAvailable.Release(); } catch { }
        }

        base.Dispose(disposing);
    }
}
