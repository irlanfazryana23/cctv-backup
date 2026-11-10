using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;
using CctvBackup.Core.Settings;

namespace CctvBackup.Core.Dvr;

/// <summary>Real DVR access through the CMS's Xiongmai NetSdk.dll (cloud ID login).</summary>
public sealed class XmDvrClient : IDvrClient, IDisposable
{
    private const int PageSize = 64;
    private readonly string _sdkDir;
    private readonly object _gate = new();
    private XmSdk? _sdk;
    private int _busy;   // logins in progress + open connections; the driver is only reset when this is just the caller

    // How long one login may wait for the DVR (or the cloud relay) before it counts as timed out.
    // The driver's own wait is short (it gave up after ~5 s in practice). Meaning of the two numbers is from the
    // standard Xiongmai header (wait in ms, number of tries); one try, because a timeout is retried below after a driver reset.
    private const int LoginWaitMs = 15000;
    private const int LoginTries = 1;

    /// <summary>Told when the client does something the user may want to see in the log (e.g. resetting the driver).</summary>
    public event Action<string>? Notice;

    public XmDvrClient(string sdkDirectory) => _sdkDir = sdkDirectory;

    private XmSdk Sdk
    {
        get
        {
            lock (_gate)
            {
                try
                {
                    if (_sdk is null)
                    {
                        _sdk = XmSdk.Load(_sdkDir);
                        try { _sdk.SetConnectTime(LoginWaitMs, LoginTries); } catch { /* keep the driver default */ }
                    }
                    return _sdk;
                }
                catch (Exception ex) when (ex is SdkException or DllNotFoundException or BadImageFormatException)
                {
                    throw new DvrException(Loc.F("Could not load the DVR SDK from '{0}': {1}", _sdkDir, ex.Message), ex);
                }
            }
        }
    }

    public Task<IDvrConnection> ConnectAsync(Branch branch, Credential credential, CancellationToken ct) =>
        Task.Run<IDvrConnection>(() =>
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _busy);
            bool handedOver = false;
            try
            {
                for (int attempt = 1; ; attempt++)
                {
                    var sdk = Sdk;
                    try
                    {
                        var (handle, device) = branch.Mode == LoginMode.Ip
                            ? sdk.LoginIpDetailed(branch.Address.Trim(), branch.Port, credential.Username, credential.Password)
                            : sdk.LoginCloudDetailed(branch.CloudId, credential.Username, credential.Password);
                        var conn = new Connection(this, sdk, handle, new DvrInfo(device.TotalChannels, device.SerialNumber, device.Model));
                        handedOver = true;   // the connection now owns the _busy count
                        return conn;
                    }
                    catch (SdkException ex) when (attempt == 1 && IsTimeout(ex.Code) && !ct.IsCancellationRequested)
                    {
                        // After a timeout the driver often keeps timing out until the program is restarted
                        // (seen on every branch, not only the one that failed). Reloading it is the same as a restart.
                        bool reset = ResetDriver(1);
                        Notice?.Invoke($"{DateTime.Now:HH:mm:ss}  " + (reset
                            ? Loc.F("{0}: the login timed out. Resetting the DVR driver and trying again.", branch.Name)
                            : Loc.F("{0}: the login timed out. Trying again.", branch.Name)));
                    }
                }
            }
            catch (SdkException ex)
            {
                string hint = branch.Mode == LoginMode.Ip && IsReachabilityError(ex.Code)
                    ? Loc.F("\nCheck that this PC can reach {0} (same network, VPN or leased line).", branch.ConnectionText)
                    : string.Empty;
                throw new DvrException($"{branch.Name}: {ex.Message}{hint}", ex);
            }
            finally
            {
                if (!handedOver) Interlocked.Decrement(ref _busy);
            }
        }, ct);

    /// <summary>The login did not get an answer in time (SDK_TIMEOUT).</summary>
    internal static bool IsTimeout(int code) => code == -10005;

    /// <summary>
    /// Unloads and reloads the driver. Skipped (returns false) while another login or connection is using it,
    /// because unloading it under them would break them. The caller counts as one user.
    /// </summary>
    private bool ResetDriver(int allowedUsers)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _busy) > allowedUsers) return false;
            _sdk?.Dispose();
            _sdk = null;
            return true;
        }
    }

    /// <summary>Reloads the driver when nothing is using it (used between sessions, e.g. after a search timed out).</summary>
    public void ResetDriver() => ResetDriver(0);

    /// <summary>Errors that mean "could not get to the address" rather than "wrong login".</summary>
    internal static bool IsReachabilityError(int code) =>
        code is -10005 or -10007 or -11307 or -11600 or -11601 or -11602 or -11603 or -11611 or -11608;

    public void Dispose()
    {
        lock (_gate) { _sdk?.Dispose(); _sdk = null; }
    }

    private sealed class Connection : IDvrConnection
    {
        private readonly XmDvrClient _owner;
        private readonly XmSdk _sdk;
        private readonly int _login;
        private bool _closed;

        public Connection(XmDvrClient owner, XmSdk sdk, int login, DvrInfo info)
        {
            _owner = owner; _sdk = sdk; _login = login; Info = info;
        }

        public DvrInfo Info { get; }

        public Task<IReadOnlyList<RemoteFile>> FindFilesAsync(int channel, DateTime from, DateTime to, CancellationToken ct) =>
            Task.Run<IReadOnlyList<RemoteFile>>(() =>
            {
                try
                {
                    return FilePager.FindAll(
                        (a, b) => _sdk.FindFiles(_login, channel, a, b, PageSize),
                        from, to, PageSize, ct);
                }
                catch (SdkException ex) { throw new DvrException(ex.Message, ex); }
            }, ct);

        public Task DownloadAsync(RemoteFile file, string destPath, IProgress<int>? progress, CancellationToken ct) =>
            Task.Run(() => DownloadBlocking(file, destPath, progress, ct), ct);

        private void DownloadBlocking(RemoteFile file, string destPath, IProgress<int>? progress, CancellationToken ct)
        {
            // The SDK only understands ANSI paths. If the destination has other characters
            // (e.g. a drive label or folder with accents), download to an ASCII staging file and move it.
            bool ascii = destPath.All(c => c < 128);
            string workPath = destPath;
            if (!ascii)
            {
                Directory.CreateDirectory(AppPaths.StagingDirectory);
                workPath = Path.Combine(AppPaths.StagingDirectory, Guid.NewGuid().ToString("N") + ".part");
            }

            try
            {
                if (File.Exists(workPath)) File.Delete(workPath);
                using (var dl = StartDownload(file, workPath))
                {
                    WaitForCompletion(dl, workPath, progress, ct);
                }

                if (!File.Exists(workPath) || new FileInfo(workPath).Length == 0)
                    throw new DvrException(Loc.T("The DVR reported success but no data was written."));

                if (!ascii) File.Move(workPath, destPath, overwrite: true);
            }
            catch
            {
                TryDelete(workPath);
                throw;
            }
        }

        private XmSdk.Download StartDownload(RemoteFile file, string path)
        {
            try { return _sdk.StartDownload(_login, file, path); }
            catch (SdkException ex) { throw new DvrException(ex.Message, ex); }
        }

        private static void WaitForCompletion(XmSdk.Download dl, string path, IProgress<int>? progress, CancellationToken ct)
        {
            // Give up if neither the reported position nor the file size has moved for this long.
            TimeSpan stallLimit = TimeSpan.FromSeconds(90);
            var lastMove = DateTime.UtcNow;
            int lastPos = -1;
            long lastLen = -1;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int pos = dl.Position;

                if (pos < 0 || pos > 100)
                    throw new DvrException(Loc.F("The DVR download failed (position {0}).", pos));

                long len = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (pos != lastPos || len != lastLen) lastMove = DateTime.UtcNow;
                lastPos = pos; lastLen = len;

                progress?.Report(pos);
                if (pos == 100) return;

                if (DateTime.UtcNow - lastMove > stallLimit)
                    throw new DvrException(Loc.T("The download stalled (no data for 90 seconds)."));

                if (ct.WaitHandle.WaitOne(500)) ct.ThrowIfCancellationRequested();
            }
        }

        private static void TryDelete(string p)
        {
            try { if (File.Exists(p)) File.Delete(p); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            try { _sdk.Logout(_login); } catch { /* best effort */ }
            Interlocked.Decrement(ref _owner._busy);
        }
    }
}
