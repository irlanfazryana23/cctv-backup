using System.Globalization;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;

namespace CctvBackup.Tests;

public class OneAtATimeTests
{
    private static readonly DateTime From = new(2025, 1, 15);
    private static readonly DateTime To = new(2025, 1, 16);

    private static BackupEngine Engine(FakeDvrClient fake) =>
        new(fake, _ => new Credential("u", "p"), 2, TimeSpan.Zero);

    private static BackupRequest Req(Branch b) => new(b, b.Cameras.Take(1).ToList(), From, To);

    private static (List<Branch> Branches, FakeDvrClient Fake) Setup(int branches)
    {
        var fake = new FakeDvrClient();
        var list = Enumerable.Range(1, branches).Select(i => Make.Branch($"Branch{i}", 2)).ToList();
        foreach (var b in list)
        {
            fake.Add(b.Name, Make.File(0, "2025-01-15 08:00:00", "2025-01-15 08:10:00", name: $"/{b.Name}/1.h264"));
            fake.Add(b.Name, Make.File(0, "2025-01-15 08:10:00", "2025-01-15 08:20:00", name: $"/{b.Name}/2.h264"));
        }
        return (list, fake);
    }

    [Fact]
    public async Task Only_one_file_from_one_branch_is_downloaded_at_any_moment()
    {
        using var dest = new TempDir();
        var (branches, fake) = Setup(4);
        int current = 0, max = 0;
        fake.OnDownload = async (_, ct) =>
        {
            int now = Interlocked.Increment(ref current);
            int seen;
            while (now > (seen = Volatile.Read(ref max)) && Interlocked.CompareExchange(ref max, now, seen) != seen) { }
            try { await Task.Delay(60, ct); }
            finally { Interlocked.Decrement(ref current); }
        };

        var summary = await Engine(fake).RunAsync(branches.Select(Req).ToList(), dest.Path, default);

        Assert.All(summary.Branches, r => Assert.Equal(2, r.Done));
        Assert.Equal(1, Volatile.Read(ref max));
    }

    [Fact]
    public async Task Branches_are_handled_in_the_order_they_were_requested()
    {
        using var dest = new TempDir();
        var (branches, fake) = Setup(3);
        var engine = Engine(fake);

        var plans = await engine.FindAsync(branches.Select(Req).ToList(), dest.Path, default);
        var summary = await engine.DownloadAsync(plans, dest.Path, default);

        Assert.Equal(new[] { "Branch1", "Branch2", "Branch3" }, plans.Select(p => p.Branch.Name));
        Assert.Equal(new[] { "Branch1", "Branch2", "Branch3" }, summary.Branches.Select(r => r.Branch));
        // all of Branch1's files, then all of Branch2's, then Branch3's
        Assert.Equal(
            new[] { "/Branch1/1.h264", "/Branch1/2.h264", "/Branch2/1.h264", "/Branch2/2.h264", "/Branch3/1.h264", "/Branch3/2.h264" },
            fake.DownloadedNames);
    }

    [Fact]
    public async Task Cancel_during_the_first_branch_means_later_branches_never_start()
    {
        using var dest = new TempDir();
        var (branches, fake) = Setup(3);
        using var cts = new CancellationTokenSource();
        fake.OnDownload = (_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        var summary = await Engine(fake).RunAsync(branches.Select(Req).ToList(), dest.Path, cts.Token);

        Assert.True(summary.Cancelled);
        Assert.Empty(fake.DownloadedNames);
        Assert.Empty(Directory.GetFiles(dest.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Disk_space_is_checked_once_for_everything()
    {
        using var dest = new TempDir();
        var (branches, fake) = Setup(2);
        var engine = Engine(fake);
        var plans = await engine.FindAsync(branches.Select(Req).ToList(), dest.Path, default);
        // Pretend the files are far bigger than any disk.
        var bigPlans = plans.Select(p => new BranchPlan(p.Branch, p.Jobs.Select(j =>
            new DownloadJob(j.BranchName, j.Camera,
                Make.File(0, j.File.Begin.ToString("s"), j.File.End.ToString("s"), long.MaxValue / 8192, j.File.RemoteName),
                j.FinalPath)).ToList(), null)).ToList();

        var summary = await engine.DownloadAsync(bigPlans, dest.Path, default);

        Assert.All(summary.Branches, r => Assert.Contains("Not enough free space", r.Error));
        Assert.Empty(fake.DownloadedNames);
    }
}

public class TransferStatsTests
{
    [Fact]
    public void Speed_is_unknown_until_a_few_seconds_have_passed()
    {
        Assert.Null(TransferStats.SpeedMbPerSec(100 * 1024, 0.5, TimeSpan.FromSeconds(1)));
        Assert.Null(TransferStats.SpeedMbPerSec(100 * 1024, 0, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Speed_is_data_done_divided_by_time()
    {
        // 100 MB file, half done after 50 s = 50 MB / 50 s
        Assert.Equal(1.0, TransferStats.SpeedMbPerSec(100 * 1024, 0.5, TimeSpan.FromSeconds(50))!.Value, 3);
    }

    [Fact]
    public void Time_left_is_what_remains_divided_by_the_speed()
    {
        Assert.Equal(TimeSpan.FromSeconds(50), TransferStats.TimeLeft(50, 1.0));
        Assert.Null(TransferStats.TimeLeft(50, null));
        Assert.Null(TransferStats.TimeLeft(50, 0));
    }

    [Fact]
    public void Overall_adds_up_all_files_and_estimates_what_is_left()
    {
        var work = new[]
        {
            (SizeKb: 100L * 1024, FractionDone: 1.0),   // finished: 100 MB
            (SizeKb: 100L * 1024, FractionDone: 0.5),   // half: 50 MB
            (SizeKb: 100L * 1024, FractionDone: 0.0),   // not started: 100 MB left
        };

        var (speed, left) = TransferStats.Overall(work, TimeSpan.FromSeconds(75));

        Assert.Equal(2.0, speed!.Value, 3);                       // 150 MB in 75 s
        Assert.Equal(TimeSpan.FromSeconds(75), left);             // 150 MB left at 2 MB/s
    }

    [Fact]
    public void Overall_is_unknown_when_nothing_has_been_done_yet()
    {
        var (speed, left) = TransferStats.Overall(new[] { (SizeKb: 1024L, FractionDone: 0.0) }, TimeSpan.FromMinutes(5));

        Assert.Null(speed);
        Assert.Null(left);
    }

    [Theory]
    [InlineData(20, "under 1 min")]
    [InlineData(12 * 60, "12 min")]
    [InlineData(65 * 60, "1 h 05 min")]
    [InlineData(3 * 24 * 3600, "3 days")]
    public void Durations_are_written_roughly(int seconds, string expected) =>
        Assert.Equal(expected, TransferStats.FormatDuration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Missing_values_give_empty_text()
    {
        Assert.Equal(string.Empty, TransferStats.FormatDuration(null));
        Assert.Equal(string.Empty, TransferStats.FormatSpeed(null));
    }

    [Fact]
    public void Speed_is_written_in_the_users_number_format()
    {
        string expected = 1.5.ToString("0.00", CultureInfo.CurrentCulture) + " MB/s";
        Assert.Equal(expected, TransferStats.FormatSpeed(1.5));
    }
}

public class DownloadJobTimingTests
{
    private static readonly DateTime T0 = new(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static DownloadJob Job(long sizeKb) =>
        new("B", new Camera(0, "c"), Make.File(0, "2025-01-15 08:00:00", "2025-01-15 09:00:00", sizeKb), @"E:\x.h264");

    private static string Mb(double v) => v.ToString("0.00", CultureInfo.CurrentCulture) + " MB/s";

    [Fact]
    public void Nothing_is_shown_before_the_download_starts()
    {
        var job = Job(1_024_000);
        Assert.Equal(string.Empty, job.SpeedTextAt(T0));
        Assert.Equal(string.Empty, job.LeftTextAt(T0));
    }

    [Fact]
    public void Running_download_shows_live_speed_and_time_left()
    {
        var job = Job(1_024_000);                // 1000 MB
        job.MarkStarted(T0);
        job.Status = JobStatus.Downloading;
        job.Percent = 20;                        // 200 MB in 200 s = 1 MB/s, 800 MB left = 13 min

        Assert.Equal(Mb(1.0), job.SpeedTextAt(T0.AddSeconds(200)));
        Assert.Equal("13 min", job.LeftTextAt(T0.AddSeconds(200)));
    }

    [Fact]
    public void Finished_download_shows_the_average_speed_and_no_time_left()
    {
        var job = Job(102_400);                  // 100 MB
        job.MarkStarted(T0);
        job.Status = JobStatus.Downloading;
        job.MarkFinished(T0.AddSeconds(100));
        job.Status = JobStatus.Done;
        job.Percent = 100;

        // "now" is long after it finished: the speed must not keep dropping as time passes
        Assert.Equal(Mb(1.0), job.SpeedTextAt(T0.AddHours(5)));
        Assert.Equal(string.Empty, job.LeftTextAt(T0.AddHours(5)));
    }

    [Fact]
    public void Retrying_restarts_the_clock()
    {
        var job = Job(102_400);
        job.MarkStarted(T0);
        job.MarkFinished(T0.AddSeconds(10));
        job.MarkStarted(T0.AddSeconds(60));      // second attempt
        job.Status = JobStatus.Downloading;
        job.Percent = 50;

        Assert.Equal(Mb(1.0), job.SpeedTextAt(T0.AddSeconds(110)));   // 50 MB in 50 s
    }

    [Fact]
    public void Failed_or_waiting_files_show_no_speed()
    {
        var job = Job(102_400);
        job.MarkStarted(T0);
        job.Status = JobStatus.Failed;
        Assert.Equal(string.Empty, job.SpeedTextAt(T0.AddSeconds(30)));
    }
}
