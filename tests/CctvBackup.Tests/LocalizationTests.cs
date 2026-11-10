using System.Text.RegularExpressions;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Tests;

public class LocalizationTests
{
    // ---------------------------------------------------------------- how the text lookup behaves

    [Fact]
    public void English_is_the_default_and_text_is_returned_as_written()
    {
        using var _ = Loc.Use("en");
        Assert.Equal("Cancel", Loc.T("Cancel"));
        Assert.Equal("3 of 5 files", Loc.F("{0} of {1} files", 3, 5));
    }

    [Fact]
    public void Indonesian_text_is_returned_when_that_language_is_in_use()
    {
        using var _ = Loc.Use("id");
        Assert.Equal("Batal", Loc.T("Cancel"));
        Assert.Equal("3 dari 5 file", Loc.F("{0} of {1} files", 3, 5));
    }

    [Fact]
    public void Text_without_a_translation_stays_english_instead_of_failing()
    {
        using var _ = Loc.Use("id");
        Assert.Equal("Some brand new message", Loc.T("Some brand new message"));
        Assert.Equal("Some brand new 7", Loc.F("Some brand new {0}", 7));
    }

    [Fact]
    public void Using_a_language_only_lasts_for_the_using_block()
    {
        using (Loc.Use("id")) Assert.Equal("Batal", Loc.T("Cancel"));
        Assert.Equal("Cancel", Loc.T("Cancel"));
    }

    [Theory]
    [InlineData("id", "id")]
    [InlineData("ID", "id")]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    public void A_saved_language_choice_is_respected(string saved, string expected) =>
        Assert.Equal(expected, Loc.Resolve(saved));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("garbage")]
    public void No_or_unknown_choice_falls_back_to_the_windows_language(string? saved) =>
        Assert.Equal(Loc.DetectFromWindows(), Loc.Resolve(saved));

    // ---------------------------------------------------------------- messages from the program in Indonesian

    [Fact]
    public void Job_status_names_are_translated()
    {
        var job = new DownloadJob("B", new Camera(0, "c"), Make.File(0, "2025-01-15 08:00:00", "2025-01-15 09:00:00"), @"E:\x.h264");
        using (Loc.Use("en")) Assert.Equal("Waiting", job.StatusText);
        using (Loc.Use("id")) Assert.Equal("Menunggu", job.StatusText);
    }

    [Fact]
    public void Durations_and_error_codes_are_translated()
    {
        using var _ = Loc.Use("id");
        Assert.Equal("kurang dari 1 mnt", TransferStats.FormatDuration(TimeSpan.FromSeconds(20)));
        Assert.Equal("12 mnt", TransferStats.FormatDuration(TimeSpan.FromMinutes(12)));
        Assert.Contains("Kata sandi salah", SdkErrors.Describe(-11301));
        Assert.Contains("PASSWORD_NOT_VALID", SdkErrors.Describe(-11301));    // the technical name stays, for support
        Assert.Contains("-11301", SdkErrors.Describe(-11301));
    }

    [Fact]
    public void Exceptions_carry_the_message_in_the_language_in_use()
    {
        using var _ = Loc.Use("id");
        var ex = new SdkException("Login", -11301);
        Assert.StartsWith("Login gagal:", ex.Message);
    }

    [Fact]
    public void Validation_messages_are_translated()
    {
        using var _ = Loc.Use("id");
        var problems = BranchValidator.Validate("", LoginMode.Cloud, "", "", 34567, "", "", Array.Empty<string>(), null, true);

        Assert.Contains("Isi nama cabang.", problems);
        Assert.Contains("Isi Serial ID (ID cloud) DVR.", problems);
        Assert.Contains("Isi nama pengguna DVR.", problems);
        Assert.Contains("Isi kata sandi DVR.", problems);
    }

    [Fact]
    public void Branch_connection_texts_are_translated()
    {
        var ip = new Branch("C", "", "", 34567, "u", Array.Empty<Camera>(), LoginMode.Ip, "192.168.1.20");
        using var _ = Loc.Use("id");
        Assert.Equal("menghubungkan lewat cloud...", Make.Branch("A").ConnectingText);
        Assert.Equal("menghubungkan ke 192.168.1.20:34567...", ip.ConnectingText);
    }

    // ---------------------------------------------------------------- the translation table itself

    private static readonly Regex[] Patterns =
    {
        new(@"Loc\.[TF]\(\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),                         // Loc.T("...") / Loc.F("...", ...)
        new(@"Bind(?:Accessible)?\([^;]*?,\s*""((?:[^""\\]|\\.)*)""\)", RegexOptions.Compiled),        // Bind(control, "...")
        new(@"\bCol\(""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),                                 // grid column headers
        new(@"\bGroup\(""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled),                               // group box titles
        new(@"\bTip\([^;]*?,\s*""((?:[^""\\]|\\.)*)""\)", RegexOptions.Compiled),                       // tooltips: Tip(control, "...")
        new(@"\(""[A-Z0-9_]+"",\s*""((?:[^""\\]|\\.)*)""\)", RegexOptions.Compiled),                  // DVR error table: ("NAME", "hint")
        new(@"new SdkException\(""((?:[^""\\]|\\.)*)"",\s*", RegexOptions.Compiled),                  // operation names, translated when shown
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CctvBackup.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("CctvBackup.sln not found above the test folder.");
    }

    private static List<(string File, string Text)> SourceFiles() =>
        Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.EndsWith("IndonesianStrings.cs"))
            .Select(f => (f, File.ReadAllText(f)))
            .ToList();

    private static string Unescape(string s) =>
        s.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\\", "\\");

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static SortedSet<string> TextsUsedInCode()
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (_, text) in SourceFiles())
            foreach (var p in Patterns)
                foreach (Match m in p.Matches(text))
                    used.Add(Unescape(m.Groups[1].Value));
        return used;
    }

    [Fact]
    public void The_scan_really_finds_the_texts_of_the_program()
    {
        // guards the next test: if the patterns stopped matching, "nothing is missing" would be a false comfort
        var used = TextsUsedInCode();
        Assert.True(used.Count > 150, $"only {used.Count} texts found");
        Assert.Contains("Cancel", used);
        Assert.Contains("Wrong password.", used);
        Assert.Contains("{0} of {1} files", used);
    }

    [Fact]
    public void Every_text_in_the_program_has_an_Indonesian_translation()
    {
        var missing = TextsUsedInCode().Where(t => !IndonesianStrings.Table.ContainsKey(t)).ToList();

        Assert.True(missing.Count == 0, "Missing Indonesian translation for:\n" + string.Join("\n", missing.Select(m => "  " + Escape(m))));
    }

    [Fact]
    public void No_translation_is_left_over_after_its_text_was_removed_from_the_code()
    {
        string all = string.Join("\n", SourceFiles().Select(f => f.Text));
        var unused = IndonesianStrings.Table.Keys.Where(k => !all.Contains("\"" + Escape(k) + "\"")).ToList();

        Assert.True(unused.Count == 0, "Translations that no code uses:\n" + string.Join("\n", unused.Select(u => "  " + Escape(u))));
    }

    [Fact]
    public void Translations_use_the_same_placeholders_as_the_english_text()
    {
        static List<string> Holes(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).OrderBy(x => x).ToList();

        var wrong = IndonesianStrings.Table.Where(kv => !Holes(kv.Key).SequenceEqual(Holes(kv.Value))).Select(kv => kv.Key).ToList();

        Assert.True(wrong.Count == 0, "Placeholders differ for:\n" + string.Join("\n", wrong.Select(w => "  " + Escape(w))));
    }

    [Fact]
    public void Shortcut_keys_are_kept_in_translations()
    {
        // "&Cancel" must not become a plain "Batal": the Alt+letter shortcut would be lost.
        var wrong = IndonesianStrings.Table
            .Where(kv => Regex.IsMatch(kv.Key, @"&\w") && !Regex.IsMatch(kv.Value, @"&\w"))
            .Select(kv => kv.Key).ToList();

        Assert.True(wrong.Count == 0, "Shortcut key (&) lost in:\n" + string.Join("\n", wrong.Select(w => "  " + Escape(w))));
    }

    [Fact]
    public void Line_breaks_are_kept_in_translations()
    {
        var wrong = IndonesianStrings.Table
            .Where(kv => kv.Key.Count(c => c == '\n') != kv.Value.Count(c => c == '\n'))
            .Select(kv => kv.Key).ToList();

        Assert.True(wrong.Count == 0, "Different number of line breaks in:\n" + string.Join("\n", wrong.Select(w => "  " + Escape(w))));
    }
}
