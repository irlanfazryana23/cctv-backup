using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Tests;

internal static class Make
{
    public static Branch Branch(string name, int cameras = 2, string id = "0000000000000001") =>
        new(name, "TEST AREA", id, 34567, "tester",
            Enumerable.Range(0, cameras).Select(i => new Camera(i, $"{name}_{i + 1}")).ToList());

    public static RemoteFile File(int channel, string begin, string end, long sizeKb = 100, string? name = null)
    {
        var b = DateTime.Parse(begin, System.Globalization.CultureInfo.InvariantCulture);
        var e = DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture);
        return new RemoteFile(channel, name ?? $"/idea0/{b:yyyy-MM-dd}/{channel:000}/{b:HH.mm.ss}-{e:HH.mm.ss}[R][0].h264",
            b, e, sizeKb, default);
    }
}

/// <summary>A reversible stand-in for DPAPI so tests do not depend on the machine.</summary>
internal sealed class XorProtector : ISecretProtector
{
    public byte[] Protect(byte[] data) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
    public byte[] Unprotect(byte[] data) => Protect(data);
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cctvtests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

/// <summary>An in-memory DVR for engine tests.</summary>
internal sealed class FakeDvrClient : IDvrClient
{
    public Dictionary<string, List<RemoteFile>> FilesByBranchChannel { get; } = new();
    public HashSet<string> OfflineBranches { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int FailFirstDownloads { get; set; }                  // fail this many download attempts, then succeed
    public bool AlwaysFailDownloads { get; set; }
    public Func<RemoteFile, CancellationToken, Task>? OnDownload { get; set; }
    public int ChannelCount { get; set; } = 8;
    public int Connects => _connects;
    public Dictionary<string, TimeSpan> FindDelays { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int FindTimeouts { get; set; }                        // make this many searches time out (SDK_TIMEOUT), then succeed
    public int Resets => _resets;
    public void ResetDriver() => Interlocked.Increment(ref _resets);
    private int _resets;
    private int _findTimeoutsDone;
    private int _connects;
    private readonly object _lock = new();
    private readonly List<string> _downloaded = new();
    /// <summary>A snapshot of what was downloaded (safe to read while other branches are still running).</summary>
    public IReadOnlyList<string> DownloadedNames { get { lock (_lock) return _downloaded.ToList(); } }
    public List<string> Downloaded => new(DownloadedNames);
    public void ClearDownloaded() { lock (_lock) _downloaded.Clear(); }
    private int _failuresDone;

    public static string Key(string branch, int channel) => $"{branch}#{channel}";

    public void Add(string branch, RemoteFile file)
    {
        var key = Key(branch, file.Channel);
        if (!FilesByBranchChannel.TryGetValue(key, out var list)) FilesByBranchChannel[key] = list = new();
        list.Add(file);
    }

    public Task<IDvrConnection> ConnectAsync(Branch branch, Credential credential, CancellationToken ct)
    {
        if (OfflineBranches.Contains(branch.Name))
            return Task.FromException<IDvrConnection>(new DvrException($"{branch.Name}: device offline"));
        Interlocked.Increment(ref _connects);
        return Task.FromResult<IDvrConnection>(new Conn(this, branch));
    }

    private sealed class Conn : IDvrConnection
    {
        private readonly FakeDvrClient _owner;
        private readonly Branch _branch;
        public Conn(FakeDvrClient owner, Branch branch) { _owner = owner; _branch = branch; }

        public DvrInfo Info => new(_owner.ChannelCount, "FAKE", "FAKE-DVR");

        public async Task<IReadOnlyList<RemoteFile>> FindFilesAsync(int channel, DateTime from, DateTime to, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _owner._findTimeoutsDone) <= _owner.FindTimeouts)
                throw new DvrException("The request timed out.", new SdkException("Find recorded files", -10005));
            if (_owner.FindDelays.TryGetValue(_branch.Name, out var delay)) await Task.Delay(delay, ct);
            _owner.FilesByBranchChannel.TryGetValue(Key(_branch.Name, channel), out var list);
            IReadOnlyList<RemoteFile> result = (list ?? new()).Where(f => f.End >= from && f.Begin <= to)
                .OrderBy(f => f.Begin).ToList();
            return result;
        }

        public async Task DownloadAsync(RemoteFile file, string destPath, IProgress<int>? progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_owner.OnDownload is not null) await _owner.OnDownload(file, ct);
            bool fail;
            lock (_owner._lock)
            {
                fail = _owner.AlwaysFailDownloads || _owner._failuresDone < _owner.FailFirstDownloads;
                if (fail) _owner._failuresDone++;
            }
            if (fail) throw new DvrException("simulated download failure");
            progress?.Report(50);
            await File.WriteAllBytesAsync(destPath, new byte[1024], ct);
            progress?.Report(100);
            lock (_owner._lock) _owner._downloaded.Add(file.RemoteName);
        }

        public void Dispose() { }
    }
}
