using CctvBackup.Core.Branches;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Backup;

/// <summary>What the user asked for on one branch. Times are the DVR's own clock.</summary>
public sealed record BackupRequest(Branch Branch, IReadOnlyList<Camera> Cameras, DateTime From, DateTime To);

public enum JobStatus { Pending, Downloading, Done, Skipped, Failed, Cancelled }

/// <summary>
/// One recorded file to fetch. Status/Percent/Error are written by the download thread and
/// read by the UI on a timer, so they are volatile-style plain properties (no change events).
/// </summary>
public sealed class DownloadJob
{
    private volatile JobStatus _status = JobStatus.Pending;
    private volatile int _percent;
    private volatile string? _error;

    public DownloadJob(string branchName, Camera camera, RemoteFile file, string finalPath)
    {
        BranchName = branchName; Camera = camera; File = file; FinalPath = finalPath;
    }

    public string BranchName { get; }
    public Camera Camera { get; }
    public RemoteFile File { get; }
    public string FinalPath { get; }
    public string PartPath => FinalPath + ".part";
    public long SizeKb => File.SizeKb;

    public string CameraName => Camera.FolderName;
    public string Period => $"{File.Begin:yyyy-MM-dd HH:mm:ss} - {File.End:HH:mm:ss}";
    public string SizeText => DiskCheck.Format(File.SizeKb * 1024);

    public JobStatus Status { get => _status; set => _status = value; }
    public int Percent { get => _percent; set => _percent = value; }
    public string? Error { get => _error; set => _error = value; }

    // ---- timing (UTC ticks; 0 = not set). Plain longs read with Interlocked because this runs on 32-bit. ----
    private long _startedTicks, _finishedTicks;

    public void MarkStarted() => MarkStarted(DateTime.UtcNow);
    public void MarkStarted(DateTime utcNow)
    {
        Interlocked.Exchange(ref _finishedTicks, 0);
        Interlocked.Exchange(ref _startedTicks, utcNow.Ticks);
    }

    public void MarkFinished() => MarkFinished(DateTime.UtcNow);
    public void MarkFinished(DateTime utcNow) => Interlocked.Exchange(ref _finishedTicks, utcNow.Ticks);

    private TimeSpan? ElapsedAt(DateTime utcNow)
    {
        long started = Interlocked.Read(ref _startedTicks);
        if (started == 0) return null;
        long finished = Interlocked.Read(ref _finishedTicks);
        long end = finished != 0 ? finished : utcNow.Ticks;
        return end >= started ? TimeSpan.FromTicks(end - started) : TimeSpan.Zero;
    }

    /// <summary>Speed for the grid: live while downloading, the average once done.</summary>
    public string SpeedTextAt(DateTime utcNow)
    {
        var elapsed = ElapsedAt(utcNow);
        if (elapsed is null) return string.Empty;
        return Status switch
        {
            JobStatus.Downloading => TransferStats.FormatSpeed(TransferStats.SpeedMbPerSec(SizeKb, Percent / 100.0, elapsed.Value)),
            JobStatus.Done => TransferStats.FormatSpeed(TransferStats.SpeedMbPerSec(SizeKb, 1, elapsed.Value)),
            _ => string.Empty,
        };
    }

    /// <summary>Time left for the grid (only while downloading).</summary>
    public string LeftTextAt(DateTime utcNow)
    {
        var elapsed = ElapsedAt(utcNow);
        if (elapsed is null || Status != JobStatus.Downloading) return string.Empty;
        var speed = TransferStats.SpeedMbPerSec(SizeKb, Percent / 100.0, elapsed.Value);
        double remainingMb = SizeKb / 1024.0 * (1 - Percent / 100.0);
        return TransferStats.FormatDuration(TransferStats.TimeLeft(remainingMb, speed));
    }

    public string SpeedText => SpeedTextAt(DateTime.UtcNow);
    public string LeftText => LeftTextAt(DateTime.UtcNow);

    /// <summary>The user's tick box: only ticked files are downloaded.</summary>
    public bool Selected { get => _selected; set => _selected = value; }
    private volatile bool _selected = true;

    /// <summary>True while the file can still be ticked/unticked or downloaded (not finished, not running).</summary>
    public bool CanChangeSelection => Status is JobStatus.Pending or JobStatus.Failed or JobStatus.Cancelled;

    /// <summary>True when ticking it would start a download the next time the user presses Download.</summary>
    public bool IsPendingDownload => Selected && CanChangeSelection;

    /// <summary>Text for the grid's Status column.</summary>
    public string StatusText => Status switch
    {
        JobStatus.Pending => Selected ? Loc.T("Waiting") : Loc.T("Not selected"),
        JobStatus.Downloading => Loc.T("Downloading"),
        JobStatus.Done => Loc.T("Done"),
        JobStatus.Skipped => Loc.T("Already saved"),
        JobStatus.Failed => Loc.T("Failed"),
        JobStatus.Cancelled => Loc.T("Cancelled"),
        _ => Status.ToString(),
    };
}

public sealed record BranchResult(string Branch, int Total, int Done, int Skipped, int Failed, string? Error)
{
    public bool Succeeded => Error is null && Failed == 0;
}

public sealed record BackupSummary(IReadOnlyList<BranchResult> Branches, bool Cancelled);

/// <summary>The result of searching one branch: the recordings found (or why that was not possible).</summary>
public sealed record BranchPlan(Branch Branch, IReadOnlyList<DownloadJob> Jobs, string? Error);
