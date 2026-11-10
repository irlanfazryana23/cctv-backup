using CctvBackup.Core.Backup;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Tests;

public class PathBuilderTests
{
    [Fact]
    public void Builds_branch_camera_date_and_time_layout()
    {
        var file = Make.File(2, "2025-01-15 08:30:00", "2025-01-15 09:15:30");

        var path = PathBuilder.Build(@"E:\Backup", "BPR-Cisaat", 2, file);

        Assert.Equal(@"E:\Backup\BPR-Cisaat\Cam03\2025-01-15\083000-091530.h264", path);
    }

    [Fact]
    public void File_that_crosses_midnight_is_filed_under_its_start_day()
    {
        var file = Make.File(0, "2025-01-15 23:50:00", "2025-01-16 00:10:00");

        var path = PathBuilder.Build(@"E:\B", "X", 0, file);

        Assert.EndsWith(@"Cam01\2025-01-15\235000-001000.h264", path);
    }

    [Theory]
    [InlineData("Branch/One", "Branch_One")]
    [InlineData("a:b*c?d", "a_b_c_d")]
    [InlineData("trailing. ", "trailing")]
    [InlineData("", "_")]
    public void Sanitize_makes_names_safe(string input, string expected) =>
        Assert.Equal(expected, PathBuilder.Sanitize(input));

    [Fact]
    public void Unreadable_time_falls_back_to_the_dvr_file_name()
    {
        var file = new RemoteFile(0, "/x/weird name.264", DateTime.MinValue, DateTime.MinValue, 1, default);

        var path = PathBuilder.Build(@"E:\B", "X", 0, file);

        Assert.EndsWith(@"unknown-date\weird name.264", path);
    }

    [Fact]
    public void Odd_extension_defaults_to_h264()
    {
        var file = Make.File(0, "2025-01-15 08:00:00", "2025-01-15 08:10:00", name: "/x/noext");

        Assert.EndsWith(".h264", PathBuilder.Build(@"E:\B", "X", 0, file));
    }

    [Fact]
    public void MakeUnique_adds_a_counter_only_when_needed()
    {
        var taken = new HashSet<string> { @"E:\a\f.h264", @"E:\a\f_2.h264" };

        Assert.Equal(@"E:\a\g.h264", PathBuilder.MakeUnique(@"E:\a\g.h264", taken.Contains));
        Assert.Equal(@"E:\a\f_3.h264", PathBuilder.MakeUnique(@"E:\a\f.h264", taken.Contains));
    }
}

public class FilePagerTests
{
    private static List<RemoteFile> Day(int count) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var start = new DateTime(2025, 1, 15).AddMinutes(10 * i);
            return Make.File(0, start.ToString("s"), start.AddMinutes(10).ToString("s"));
        }).ToList();

    private static Func<DateTime, DateTime, IReadOnlyList<RemoteFile>> Server(List<RemoteFile> all, int pageSize, List<int>? calls = null) =>
        (from, to) =>
        {
            calls?.Add(1);
            return all.Where(f => f.End > from && f.Begin < to).OrderBy(f => f.Begin).Take(pageSize).ToList();
        };

    [Fact]
    public void Single_short_page_is_returned_as_is()
    {
        var all = Day(5);
        var result = FilePager.FindAll(Server(all, 64), new DateTime(2025, 1, 15), new DateTime(2025, 1, 16), 64);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void Pages_through_a_range_larger_than_one_page_without_duplicates()
    {
        var all = Day(140);   // more than two pages of 64
        var calls = new List<int>();

        var result = FilePager.FindAll(Server(all, 64, calls), new DateTime(2025, 1, 15), new DateTime(2025, 1, 17), 64);

        Assert.Equal(140, result.Count);
        Assert.Equal(140, result.Select(f => f.Begin).Distinct().Count());
        Assert.True(calls.Count >= 3);
        Assert.Equal(result.OrderBy(f => f.Begin), result);
    }

    [Fact]
    public void Exactly_one_full_page_still_terminates()
    {
        var all = Day(64);

        var result = FilePager.FindAll(Server(all, 64), new DateTime(2025, 1, 15), new DateTime(2025, 1, 16), 64);

        Assert.Equal(64, result.Count);
    }

    [Fact]
    public void A_server_that_keeps_returning_the_same_page_does_not_loop_forever()
    {
        var stuck = Day(64);

        var result = FilePager.FindAll((_, _) => stuck, new DateTime(2025, 1, 15), new DateTime(2025, 1, 16), 64);

        Assert.Equal(64, result.Count);
    }

    [Fact]
    public void Empty_range_gives_empty_list()
    {
        Assert.Empty(FilePager.FindAll((_, _) => new List<RemoteFile>(), DateTime.Today, DateTime.Today.AddDays(1), 64));
    }

    [Fact]
    public void Cancellation_is_honoured()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            FilePager.FindAll((_, _) => Day(64), DateTime.Today, DateTime.Today.AddDays(1), 64, cts.Token));
    }
}
