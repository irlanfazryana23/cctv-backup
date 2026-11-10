using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;

namespace CctvBackup.Tests;

public class RequestValidatorTests
{
    private static readonly DateTime From = new(2025, 1, 15);
    private static readonly DateTime To = new(2025, 1, 16);

    private static BackupRequest Req(int cameras = 1, DateTime? from = null, DateTime? to = null)
    {
        var b = Make.Branch("Alpha", 4);
        return new BackupRequest(b, b.Cameras.Take(cameras).ToList(), from ?? From, to ?? To);
    }

    [Fact]
    public void Valid_request_has_no_problems()
    {
        using var dir = new TempDir();
        Assert.Empty(RequestValidator.Validate(new[] { Req() }, dir.Path));
    }

    [Fact]
    public void No_branch_selected_is_reported()
    {
        using var dir = new TempDir();
        Assert.Contains(RequestValidator.Validate(Array.Empty<BackupRequest>(), dir.Path), p => p.Contains("at least one branch"));
    }

    [Fact]
    public void Branch_without_cameras_is_reported_by_name()
    {
        using var dir = new TempDir();
        Assert.Contains(RequestValidator.Validate(new[] { Req(cameras: 0) }, dir.Path), p => p.StartsWith("Alpha") && p.Contains("choose at least one camera"));
    }

    [Fact]
    public void Branch_whose_cameras_were_never_detected_asks_for_a_connection_test()
    {
        using var dir = new TempDir();
        var undetected = Make.Branch("Alpha", 0);
        var request = new BackupRequest(undetected, Array.Empty<Camera>(), From, To);

        var problems = RequestValidator.Validate(new[] { request }, dir.Path);

        Assert.Contains(problems, p => p.StartsWith("Alpha") && p.Contains("Test connection"));
    }

    [Fact]
    public void Start_after_end_is_reported()
    {
        using var dir = new TempDir();
        Assert.Contains(RequestValidator.Validate(new[] { Req(from: To, to: From) }, dir.Path), p => p.Contains("before the end"));
        Assert.Contains(RequestValidator.Validate(new[] { Req(from: From, to: From) }, dir.Path), p => p.Contains("before the end"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_destination_is_reported(string dest) =>
        Assert.Contains(RequestValidator.Validate(new[] { Req() }, dest), p => p.Contains("save the backup"));

    [Fact]
    public void Unplugged_drive_is_reported()
    {
        // Pick a drive letter that is not present on this machine.
        char free = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c)
            .First(c => !Directory.Exists($"{c}:\\"));

        Assert.Contains(RequestValidator.Validate(new[] { Req() }, $@"{free}:\Backup"), p => p.Contains("external disk"));
    }
}
