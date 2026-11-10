using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;

namespace CctvBackup.Tests;

public class BackupEngineTests
{
    private static readonly DateTime From = new(2025, 1, 15, 0, 0, 0);
    private static readonly DateTime To = new(2025, 1, 16, 0, 0, 0);

    private static BackupEngine NewEngine(FakeDvrClient fake, Func<string, Credential?>? creds = null, int attempts = 3) =>
        new(fake, creds ?? (_ => new Credential("u", "p")), attempts, TimeSpan.Zero);

    private static BackupRequest Request(Branch branch, params int[] channels) =>
        new(branch, branch.Cameras.Where(c => channels.Contains(c.Channel)).ToList(), From, To);

    private static FakeDvrClient FakeWith(Branch branch)
    {
        var fake = new FakeDvrClient();
        fake.Add(branch.Name, Make.File(0, "2025-01-15 08:00:00", "2025-01-15 08:10:00"));
        fake.Add(branch.Name, Make.File(0, "2025-01-15 08:10:00", "2025-01-15 08:20:00"));
        fake.Add(branch.Name, Make.File(1, "2025-01-15 08:00:00", "2025-01-15 08:10:00"));
        return fake;
    }

    [Fact]
    public async Task Downloads_every_file_into_the_expected_layout()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);

        var summary = await NewEngine(fake).RunAsync(new[] { Request(branch, 0, 1) }, dest.Path, default);

        var r = Assert.Single(summary.Branches);
        Assert.Equal(3, r.Done);
        Assert.True(r.Succeeded);
        Assert.True(File.Exists(Path.Combine(dest.Path, "Alpha", "Cam01", "2025-01-15", "080000-081000.h264")));
        Assert.True(File.Exists(Path.Combine(dest.Path, "Alpha", "Cam02", "2025-01-15", "080000-081000.h264")));
        Assert.Empty(Directory.GetFiles(dest.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Only_selected_cameras_are_downloaded()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);

        var summary = await NewEngine(fake).RunAsync(new[] { Request(branch, 1) }, dest.Path, default);

        Assert.Equal(1, summary.Branches[0].Done);
        Assert.False(Directory.Exists(Path.Combine(dest.Path, "Alpha", "Cam01")));
    }

    [Fact]
    public async Task Second_run_skips_files_already_on_disk()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        var engine = NewEngine(fake);

        await engine.RunAsync(new[] { Request(branch, 0, 1) }, dest.Path, default);
        fake.ClearDownloaded();
        var second = await engine.RunAsync(new[] { Request(branch, 0, 1) }, dest.Path, default);

        Assert.Equal(0, second.Branches[0].Done);
        Assert.Equal(3, second.Branches[0].Skipped);
        Assert.Empty(fake.Downloaded);
    }

    [Fact]
    public async Task Interrupted_partial_file_is_not_treated_as_finished()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        var partDir = Path.Combine(dest.Path, "Alpha", "Cam01", "2025-01-15");
        Directory.CreateDirectory(partDir);
        File.WriteAllText(Path.Combine(partDir, "080000-081000.h264.part"), "half");

        var summary = await NewEngine(fake).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.Equal(2, summary.Branches[0].Done);
        Assert.Equal(1024, new FileInfo(Path.Combine(partDir, "080000-081000.h264")).Length);
    }

    [Fact]
    public async Task Failed_download_is_retried_then_succeeds()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        fake.FailFirstDownloads = 2;

        var summary = await NewEngine(fake, attempts: 3).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.Equal(2, summary.Branches[0].Done);
        Assert.Equal(0, summary.Branches[0].Failed);
        Assert.True(fake.Connects >= 3);   // reconnected after each failure
    }

    [Fact]
    public async Task Search_timeout_resets_the_driver_reconnects_and_finds_the_files()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        fake.FindTimeouts = 1;
        var log = new List<string>();
        var engine = NewEngine(fake);
        engine.Log += log.Add;

        var summary = await engine.RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.Equal(2, summary.Branches[0].Done);
        Assert.Equal(1, fake.Resets);
        Assert.Contains(log, l => l.Contains("search timed out"));
    }

    [Fact]
    public async Task Search_timeout_is_retried_only_once()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        fake.FindTimeouts = 5;

        var summary = await NewEngine(fake).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.False(summary.Branches[0].Succeeded);
        Assert.Equal(1, fake.Resets);
    }

    [Fact]
    public async Task Ordinary_download_failure_does_not_reset_the_driver()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        fake.FailFirstDownloads = 1;

        await NewEngine(fake).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.Equal(0, fake.Resets);
    }

    [Fact]
    public async Task Download_timeout_resets_the_driver_before_reconnecting()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        int calls = 0;
        fake.OnDownload = (_, _) => Interlocked.Increment(ref calls) == 1
            ? Task.FromException(new DvrException("The request timed out.", new CctvBackup.Core.Sdk.SdkException("Start download", -10005)))
            : Task.CompletedTask;

        var summary = await NewEngine(fake).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.Equal(2, summary.Branches[0].Done);
        Assert.Equal(1, fake.Resets);
    }

    [Fact]
    public async Task Gives_up_after_the_retry_limit_and_reports_failure()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        fake.AlwaysFailDownloads = true;

        var summary = await NewEngine(fake, attempts: 2).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        var r = summary.Branches[0];
        Assert.Equal(0, r.Done);
        Assert.True(r.Failed > 0);
        Assert.False(r.Succeeded);
        Assert.Empty(Directory.GetFiles(dest.Path, "*.h264", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(dest.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Cancel_stops_cleanly_and_leaves_no_half_written_files()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        using var cts = new CancellationTokenSource();
        int started = 0;
        fake.OnDownload = (_, ct) =>
        {
            if (++started == 2) cts.Cancel();   // cancel while the second file is starting
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };

        var summary = await NewEngine(fake).RunAsync(new[] { Request(branch, 0, 1) }, dest.Path, cts.Token);

        Assert.True(summary.Cancelled);
        Assert.Single(Directory.GetFiles(dest.Path, "*.h264", SearchOption.AllDirectories));   // only the first finished
        Assert.Empty(Directory.GetFiles(dest.Path, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Offline_branch_is_reported_and_the_next_branch_still_runs()
    {
        using var dest = new TempDir();
        var down = Make.Branch("Down");
        var up = Make.Branch("Up");
        var fake = FakeWith(up);
        fake.OfflineBranches.Add("Down");

        var summary = await NewEngine(fake).RunAsync(
            new[] { Request(down, 0), Request(up, 0) }, dest.Path, default);

        Assert.Equal(2, summary.Branches.Count);
        Assert.NotNull(summary.Branches[0].Error);
        Assert.Contains("offline", summary.Branches[0].Error);
        Assert.Equal(2, summary.Branches[1].Done);
        Assert.False(summary.Cancelled);
    }

    [Fact]
    public async Task Missing_login_is_reported_without_connecting()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);

        var summary = await NewEngine(fake, creds: _ => null).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.Contains("No password saved", summary.Branches[0].Error);
        Assert.Equal(0, fake.Connects);
    }

    // ---------------------------------------------------------------- find, tick, then download

    [Fact]
    public async Task Find_lists_recordings_without_downloading_anything()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);

        var plans = await NewEngine(fake).FindAsync(new[] { Request(branch, 0, 1) }, dest.Path, default);

        var plan = Assert.Single(plans);
        Assert.Null(plan.Error);
        Assert.Equal(3, plan.Jobs.Count);
        Assert.All(plan.Jobs, j => { Assert.True(j.Selected); Assert.Equal(JobStatus.Pending, j.Status); });
        Assert.Empty(fake.Downloaded);
        Assert.Empty(Directory.GetFiles(dest.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Only_ticked_files_are_downloaded()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        var engine = NewEngine(fake);
        var plans = await engine.FindAsync(new[] { Request(branch, 0, 1) }, dest.Path, default);
        var skipped = plans[0].Jobs[1];
        skipped.Selected = false;

        var summary = await engine.DownloadAsync(plans, dest.Path, default);

        Assert.Equal(2, summary.Branches[0].Done);
        Assert.Equal(2, fake.Downloaded.Count);
        Assert.DoesNotContain(skipped.File.RemoteName, fake.Downloaded);
        Assert.False(File.Exists(skipped.FinalPath));
        Assert.Equal(JobStatus.Pending, skipped.Status);
        Assert.Equal("Not selected", skipped.StatusText);
    }

    [Fact]
    public async Task Unticking_everything_downloads_nothing_and_does_not_connect_again()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        var engine = NewEngine(fake);
        var plans = await engine.FindAsync(new[] { Request(branch, 0, 1) }, dest.Path, default);
        int connectsAfterFind = fake.Connects;
        foreach (var j in plans[0].Jobs) j.Selected = false;

        var summary = await engine.DownloadAsync(plans, dest.Path, default);

        Assert.Equal(0, summary.Branches[0].Done);
        Assert.Empty(fake.Downloaded);
        Assert.Equal(connectsAfterFind, fake.Connects);
    }

    [Fact]
    public async Task Files_already_on_disk_are_flagged_and_left_unticked_by_find()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        var engine = NewEngine(fake);
        await engine.RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        var plans = await engine.FindAsync(new[] { Request(branch, 0, 1) }, dest.Path, default);

        var saved = plans[0].Jobs.Where(j => j.Camera.Channel == 0).ToList();
        Assert.All(saved, j => { Assert.Equal(JobStatus.Skipped, j.Status); Assert.False(j.Selected); Assert.False(j.CanChangeSelection); });
        var fresh = plans[0].Jobs.Single(j => j.Camera.Channel == 1);
        Assert.Equal(JobStatus.Pending, fresh.Status);
        Assert.True(fresh.Selected);
    }

    [Fact]
    public async Task Failed_files_are_retried_by_the_next_download_and_finished_ones_are_not_repeated()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = FakeWith(branch);
        fake.AlwaysFailDownloads = true;
        var engine = NewEngine(fake, attempts: 1);
        var plans = await engine.FindAsync(new[] { Request(branch, 0) }, dest.Path, default);

        var first = await engine.DownloadAsync(plans, dest.Path, default);
        Assert.Equal(2, first.Branches[0].Failed);
        Assert.All(plans[0].Jobs, j => Assert.Equal(JobStatus.Failed, j.Status));

        fake.AlwaysFailDownloads = false;
        var second = await engine.DownloadAsync(plans, dest.Path, default);
        Assert.Equal(2, second.Branches[0].Done);
        Assert.All(plans[0].Jobs, j => Assert.Equal(JobStatus.Done, j.Status));

        fake.ClearDownloaded();
        var third = await engine.DownloadAsync(plans, dest.Path, default);
        Assert.Empty(fake.Downloaded);
        Assert.Equal(0, third.Branches[0].Done);
    }

    [Fact]
    public async Task A_branch_that_could_not_be_searched_downloads_nothing_and_reports_why()
    {
        using var dest = new TempDir();
        var down = Make.Branch("Down");
        var fake = new FakeDvrClient();
        fake.OfflineBranches.Add("Down");
        var engine = NewEngine(fake);

        var plans = await engine.FindAsync(new[] { Request(down, 0) }, dest.Path, default);
        var summary = await engine.DownloadAsync(plans, dest.Path, default);

        Assert.NotNull(plans[0].Error);
        Assert.Empty(plans[0].Jobs);
        Assert.Equal(plans[0].Error, summary.Branches[0].Error);
    }

    [Fact]
    public async Task Cancelling_the_search_throws_so_the_caller_can_stop()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewEngine(FakeWith(branch)).FindAsync(new[] { Request(branch, 0) }, dest.Path, cts.Token));
    }

    [Fact]
    public async Task No_recordings_in_range_is_a_clean_empty_result()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");

        var summary = await NewEngine(new FakeDvrClient()).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        var r = summary.Branches[0];
        Assert.Equal(0, r.Total);
        Assert.True(r.Succeeded);
    }

    [Fact]
    public async Task Two_files_with_the_same_time_get_distinct_paths()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var fake = new FakeDvrClient();
        fake.Add("Alpha", Make.File(0, "2025-01-15 08:00:00", "2025-01-15 08:10:00", name: "/a/one[R].h264"));
        fake.Add("Alpha", Make.File(0, "2025-01-15 08:00:00", "2025-01-15 08:10:00", name: "/a/two[A].h264"));

        var summary = await NewEngine(fake).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.Equal(2, summary.Branches[0].Done);
        Assert.Equal(2, Directory.GetFiles(dest.Path, "*.h264", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Manifest_records_each_completed_file()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");

        await NewEngine(FakeWith(branch)).RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        var manifest = File.ReadAllText(Path.Combine(dest.Path, "Alpha", BackupManifest.FileName));
        Assert.Contains("080000-081000.h264", manifest);
        Assert.Contains("081000-082000.h264", manifest);
    }

    [Fact]
    public async Task Progress_events_report_planned_jobs_and_final_status()
    {
        using var dest = new TempDir();
        var branch = Make.Branch("Alpha");
        var engine = NewEngine(FakeWith(branch));
        IReadOnlyList<DownloadJob>? planned = null;
        engine.JobsPlanned += j => planned = j;

        await engine.RunAsync(new[] { Request(branch, 0) }, dest.Path, default);

        Assert.NotNull(planned);
        Assert.Equal(2, planned!.Count);
        Assert.All(planned, j => Assert.Equal(JobStatus.Done, j.Status));
        Assert.All(planned, j => Assert.Equal(100, j.Percent));
    }
}

public class DiskCheckTests
{
    [Fact]
    public void Plenty_of_space_is_ok()
    {
        using var dir = new TempDir();
        Assert.True(DiskCheck.Check(dir.Path, requiredKb: 1, largestFileKb: 1).Ok);
    }

    [Fact]
    public void Impossible_size_is_rejected_with_a_clear_message()
    {
        using var dir = new TempDir();
        var result = DiskCheck.Check(dir.Path, requiredKb: long.MaxValue / 4096, largestFileKb: 1);

        Assert.False(result.Ok);
        Assert.Contains("Not enough free space", result.Message);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5 GB")]
    public void Format_is_human_readable(long bytes, string expected)
    {
        // The app shows sizes in the user's own number format (e.g. "1,5 KB" on an Indonesian PC),
        // so compare against the same culture rather than assuming a decimal point.
        string decimalSeparator = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal(expected.Replace(".", decimalSeparator), DiskCheck.Format(bytes));
    }
}
