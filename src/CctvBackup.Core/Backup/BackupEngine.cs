using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Backup;

/// <summary>
/// Two steps: <see cref="FindAsync"/> lists the recordings in the chosen period (nothing is downloaded), the user ticks
/// the ones to keep, then <see cref="DownloadAsync"/> fetches the ticked files one branch after another and one file at a time (so only
/// one branch line is in use at any moment). Finished files are skipped on re-runs, so an interrupted backup can simply be
/// started again. <see cref="RunAsync"/> does both steps with everything ticked.
/// </summary>
public sealed class BackupEngine
{
    private readonly IDvrClient _client;
    private readonly Func<string, Credential?> _credentialFor;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    public BackupEngine(IDvrClient client, Func<string, Credential?> credentialFor,
        int maxAttempts = 3, TimeSpan? retryDelay = null)
    {
        _client = client;
        _credentialFor = credentialFor;
        _maxAttempts = Math.Max(1, maxAttempts);
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(5);
    }

    /// <summary>Raised once per branch with the full list of files found (may be raised from a worker thread).</summary>
    public event Action<IReadOnlyList<DownloadJob>>? JobsPlanned;
    /// <summary>Raised when a job's status or progress changes (may be raised from a worker thread).</summary>
    public event Action<DownloadJob>? JobUpdated;
    public event Action<string>? Log;

    // ------------------------------------------------------------------ step 1: find

    /// <summary>Lists the recordings for every request. Files already on the disk are marked "Already saved" and left unticked.</summary>
    public async Task<IReadOnlyList<BranchPlan>> FindAsync(IReadOnlyList<BackupRequest> requests, string destinationRoot,
        CancellationToken ct)
    {
        // One branch after another: only one DVR line is in use at a time.
        var plans = new List<BranchPlan>();
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            plans.Add(await FindBranchAsync(request, destinationRoot, ct));
        }
        return plans;
    }

    private async Task<BranchPlan> FindBranchAsync(BackupRequest request, string destinationRoot, CancellationToken ct)
    {
        var branch = request.Branch;
        var none = Array.Empty<DownloadJob>();
        Info($"=== {branch.Name} ===");

        var credential = _credentialFor(branch.Name);
        if (credential is null)
            return PlanError(branch, Loc.T("No password saved for this branch. Select it, press Edit branch... and enter it."));

        IDvrConnection conn;
        try
        {
            Info($"{branch.Name}: {branch.ConnectingText}");
            conn = await _client.ConnectAsync(branch, credential, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is DvrException or SdkException)
        {
            return PlanError(branch, Loc.F("Could not connect: {0}", ex.Message));
        }

        try
        {
            var jobs = new List<DownloadJob>();
            var usedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var camera in request.Cameras.OrderBy(c => c.Channel))
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<RemoteFile> files;
                try
                {
                    try
                    {
                        files = await conn.FindFilesAsync(camera.Channel, request.From, request.To, ct);
                    }
                    catch (Exception timedOut) when (IsTimeoutError(timedOut) && !ct.IsCancellationRequested)
                    {
                        // The session may be stale (the driver sometimes keeps timing out until it is restarted):
                        // close it, reload the driver, log in again and search this camera once more.
                        Info(Loc.F("{0} {1}: the search timed out. Resetting the DVR driver and trying again.", branch.Name, camera.FolderName));
                        conn.Dispose();
                        _client.ResetDriver();
                        conn = await _client.ConnectAsync(branch, credential, ct);
                        files = await conn.FindFilesAsync(camera.Channel, request.From, request.To, ct);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is DvrException or SdkException)
                {
                    return PlanError(branch, Loc.F("Could not list recordings for {0}: {1}", camera.FolderName, ex.Message));
                }

                Info(Loc.F("{0} {1}: {2} recording(s) found", branch.Name, camera.FolderName, files.Count));
                foreach (var file in files)
                {
                    string path = PathBuilder.Build(destinationRoot, branch.Name, camera.Channel, file);
                    path = PathBuilder.MakeUnique(path, p => usedPaths.ContainsKey(p));
                    usedPaths[path] = file.RemoteName;

                    var job = new DownloadJob(branch.Name, camera, file, path);
                    if (IsAlreadyDone(job))
                    {
                        job.Status = JobStatus.Skipped;
                        job.Percent = 100;
                        job.Selected = false;
                    }
                    jobs.Add(job);
                }
            }

            if (jobs.Count == 0) Info(Loc.F("{0}: no recordings in that period.", branch.Name));
            else JobsPlanned?.Invoke(jobs);
            return new BranchPlan(branch, jobs, null);
        }
        finally
        {
            conn.Dispose();
        }
    }

    // ------------------------------------------------------------------ step 2: download

    /// <summary>Downloads the ticked files of every plan. Returns what happened per branch.</summary>
    public async Task<BackupSummary> DownloadAsync(IReadOnlyList<BranchPlan> plans, string destinationRoot, CancellationToken ct)
    {
        // One space check for everything that is about to be fetched: branches run side by side, so checking
        // them one by one could let each pass while together they would fill the disk.
        var allTodo = plans.Where(p => p.Error is null).SelectMany(p => p.Jobs).Where(j => j.IsPendingDownload).ToList();
        string? diskProblem = null;
        if (allTodo.Count > 0)
        {
            var check = DiskCheck.Check(destinationRoot, allTodo.Sum(j => j.SizeKb), allTodo.Max(j => j.SizeKb));
            Info(check.Message);
            if (!check.Ok) diskProblem = check.Message;
        }

        // One branch after another, one file at a time. A branch interrupted by Cancel is left out of the summary.
        var results = new List<BranchResult>();
        bool cancelled = false;
        foreach (var plan in plans)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            try
            {
                if (diskProblem is not null && plan.Error is null)
                {
                    var todo = plan.Jobs.Where(j => j.IsPendingDownload).ToList();
                    int saved = plan.Jobs.Count(j => j.Status == JobStatus.Skipped);
                    results.Add(todo.Count == 0
                        ? new BranchResult(plan.Branch.Name, saved, 0, saved, 0, null)
                        : FailAll(plan.Branch, todo, saved, diskProblem));
                }
                else
                {
                    results.Add(await DownloadBranchAsync(plan, destinationRoot, ct));
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                break;
            }
        }

        return new BackupSummary(results, cancelled);
    }

    /// <summary>Find + download with every file ticked.</summary>
    public async Task<BackupSummary> RunAsync(IReadOnlyList<BackupRequest> requests, string destinationRoot,
        CancellationToken ct)
    {
        IReadOnlyList<BranchPlan> plans;
        try
        {
            plans = await FindAsync(requests, destinationRoot, ct);
        }
        catch (OperationCanceledException)
        {
            return new BackupSummary(Array.Empty<BranchResult>(), true);
        }
        return await DownloadAsync(plans, destinationRoot, ct);
    }

    private async Task<BranchResult> DownloadBranchAsync(BranchPlan plan, string destinationRoot, CancellationToken ct)
    {
        var branch = plan.Branch;
        if (plan.Error is not null)
            return new BranchResult(branch.Name, 0, 0, 0, 0, plan.Error);

        int alreadySaved = plan.Jobs.Count(j => j.Status == JobStatus.Skipped);
        var todo = plan.Jobs.Where(j => j.IsPendingDownload).ToList();
        if (todo.Count == 0)
            return new BranchResult(branch.Name, alreadySaved, 0, alreadySaved, 0, null);

        Info(Loc.F("=== {0}: downloading {1} file(s) ===", branch.Name, todo.Count));

        var credential = _credentialFor(branch.Name);
        if (credential is null)
            return FailAll(branch, todo, alreadySaved, Loc.T("No password saved for this branch. Select it, press Edit branch... and enter it."));

        IDvrConnection? conn;
        try
        {
            Info($"{branch.Name}: {branch.ConnectingText}");
            conn = await _client.ConnectAsync(branch, credential, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is DvrException or SdkException)
        {
            return FailAll(branch, todo, alreadySaved, Loc.F("Could not connect: {0}", ex.Message));
        }

        try
        {
            int done = 0, failed = 0;
            string branchFolder = PathBuilder.BranchFolder(destinationRoot, branch.Name);
            foreach (var job in todo)
            {
                ct.ThrowIfCancellationRequested();

                if (IsAlreadyDone(job))
                {
                    job.Status = JobStatus.Skipped; job.Percent = 100; job.Selected = false; JobUpdated?.Invoke(job);
                    alreadySaved++;
                    continue;
                }

                bool ok;
                (ok, conn) = await DownloadWithRetriesAsync(conn, branch, credential, job, branchFolder, ct);
                if (ok) done++; else failed++;

                if (conn is null)
                {
                    // Lost the connection and could not get it back: remaining files cannot be fetched.
                    foreach (var rest in todo.SkipWhile(j => j != job).Skip(1).Where(j => j.Status == JobStatus.Pending))
                    {
                        rest.Status = JobStatus.Failed; rest.Error = Loc.T("Connection to the branch was lost.");
                        JobUpdated?.Invoke(rest); failed++;
                    }
                    return new BranchResult(branch.Name, done + failed + alreadySaved, done, alreadySaved, failed, Loc.T("Connection to the branch was lost."));
                }
            }

            Info(Loc.F("{0}: done {1}, already saved {2}, failed {3}", branch.Name, done, alreadySaved, failed));
            return new BranchResult(branch.Name, done + failed + alreadySaved, done, alreadySaved, failed, null);
        }
        finally
        {
            conn?.Dispose();
        }
    }

    private async Task<(bool Ok, IDvrConnection? Conn)> DownloadWithRetriesAsync(
        IDvrConnection conn, Branch branch, Credential credential, DownloadJob job, string branchFolder, CancellationToken ct)
    {
        IDvrConnection? current = conn;
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(job.FinalPath)!);
                job.MarkStarted(); job.Status = JobStatus.Downloading; job.Percent = 0; job.Error = null; JobUpdated?.Invoke(job);
                Info(Loc.F("{0} {1} {2} ({3})", branch.Name, job.CameraName, job.Period, job.SizeText) + (attempt > 1 ? Loc.F(" - attempt {0}", attempt) : ""));

                var progress = new Progress<int>(p => { job.Percent = p; JobUpdated?.Invoke(job); });
                await current!.DownloadAsync(job.File, job.PartPath, progress, ct);

                File.Move(job.PartPath, job.FinalPath, overwrite: false);
                long bytes = new FileInfo(job.FinalPath).Length;
                BackupManifest.Append(branchFolder, new ManifestEntry(
                    job.File.RemoteName, Path.GetRelativePath(branchFolder, job.FinalPath), bytes, DateTime.UtcNow));

                job.MarkFinished(); job.Percent = 100; job.Status = JobStatus.Done; JobUpdated?.Invoke(job);
                Info(Loc.F("{0} {1} {2}: saved", branch.Name, job.CameraName, job.Period) + (job.SpeedText.Length > 0 ? Loc.F(" at {0}", job.SpeedText) : string.Empty));
                return (true, current);
            }
            catch (OperationCanceledException)
            {
                TryDelete(job.PartPath);
                job.Status = JobStatus.Cancelled; JobUpdated?.Invoke(job);
                throw;
            }
            catch (Exception ex) when (ex is DvrException or SdkException or IOException or UnauthorizedAccessException)
            {
                TryDelete(job.PartPath);
                string message = ex is IOException io && IsDiskFull(io) ? Loc.T("The disk is full.") : ex.Message;
                job.Error = message;
                Info($"{branch.Name} {job.CameraName}: {message}");

                if (attempt == _maxAttempts)
                {
                    job.Status = JobStatus.Failed; JobUpdated?.Invoke(job);
                    return (false, current);
                }

                // The session may have died: wait, then open a fresh one for the next attempt.
                if (_retryDelay > TimeSpan.Zero) await Task.Delay(_retryDelay, ct);
                current?.Dispose();
                if (IsTimeoutError(ex))
                {
                    Info(Loc.F("{0}: the connection timed out. Resetting the DVR driver before reconnecting.", branch.Name));
                    _client.ResetDriver();
                }
                try
                {
                    current = await _client.ConnectAsync(branch, credential, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception reconnect) when (reconnect is DvrException or SdkException)
                {
                    job.Status = JobStatus.Failed;
                    job.Error = Loc.F("Reconnect failed: {0}", reconnect.Message);
                    JobUpdated?.Invoke(job);
                    return (false, null);
                }
            }
        }

        return (false, current);
    }

    private BranchResult FailAll(Branch branch, List<DownloadJob> jobs, int alreadySaved, string message)
    {
        Info($"{branch.Name}: {message}");
        foreach (var j in jobs)
        {
            j.Status = JobStatus.Failed; j.Error = message; JobUpdated?.Invoke(j);
        }
        return new BranchResult(branch.Name, jobs.Count + alreadySaved, 0, alreadySaved, jobs.Count, message);
    }

    private BranchPlan PlanError(Branch branch, string message)
    {
        Info($"{branch.Name}: {message}");
        return new BranchPlan(branch, Array.Empty<DownloadJob>(), message);
    }

    private static bool IsAlreadyDone(DownloadJob job) =>
        File.Exists(job.FinalPath) && new FileInfo(job.FinalPath).Length > 0;

    private static bool IsDiskFull(IOException ex) =>
        (ex.HResult & 0xFFFF) is 112 or 39;   // ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>True when the failure is the driver's "request timed out" (SDK_TIMEOUT, -10005), possibly wrapped in a DvrException.</summary>
    internal static bool IsTimeoutError(Exception ex) =>
        (ex as SdkException ?? ex.InnerException as SdkException) is { Code: -10005 };

    private void Info(string message) => Log?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");
}
