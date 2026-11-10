using System.Globalization;

namespace CctvBackup.Core.Localization;

/// <summary>
/// Text in the language the user chose (English or Indonesian).
/// <para>
/// The English text itself is the key: <c>Loc.T("Cancel")</c> returns "Cancel" in English and the Indonesian
/// translation (see <see cref="IndonesianStrings"/>) when Indonesian is selected. Text that is missing from the
/// table simply stays English, so a forgotten translation never breaks the program (a unit test catches it).
/// Texts with changing parts use numbered placeholders: <c>Loc.F("{0} of {1} files", done, total)</c>.
/// </para>
/// </summary>
public static class Loc
{
    public const string English = "en";
    public const string Indonesian = "id";

    private static string _global = English;
    private static readonly AsyncLocal<string?> Override_ = new();

    /// <summary>The language in use: "en" or "id".</summary>
    public static string Code => Override_.Value ?? _global;

    public static bool IsIndonesian => Code == Indonesian;

    /// <summary>Raised after the language was changed with <see cref="Set"/>.</summary>
    public static event Action? Changed;

    public static void Set(string code)
    {
        code = Normalize(code);
        if (code == _global) return;
        _global = code;
        Changed?.Invoke();
    }

    /// <summary>Uses a language only for the current flow of execution (for tests); disposing restores the previous one.</summary>
    public static IDisposable Use(string code)
    {
        var previous = Override_.Value;
        Override_.Value = Normalize(code);
        return new Restore(previous);
    }

    /// <summary>"id" when the Windows display language is Indonesian, otherwise "en".</summary>
    public static string DetectFromWindows() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("id", StringComparison.OrdinalIgnoreCase) ? Indonesian : English;

    /// <summary>The saved choice ("en" / "id"), or the Windows language when nothing was chosen yet.</summary>
    public static string Resolve(string? saved) =>
        saved is not null && (saved.Equals(English, StringComparison.OrdinalIgnoreCase) || saved.Equals(Indonesian, StringComparison.OrdinalIgnoreCase))
            ? Normalize(saved)
            : DetectFromWindows();

    public static string Normalize(string? code) =>
        string.Equals(code, Indonesian, StringComparison.OrdinalIgnoreCase) ? Indonesian : English;

    /// <summary>The text in the current language (English when no translation exists).</summary>
    public static string T(string english) =>
        Code == Indonesian && IndonesianStrings.Table.TryGetValue(english, out var id) ? id : english;

    /// <summary>Like <see cref="T"/> for text with placeholders {0}, {1}...</summary>
    public static string F(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    private sealed class Restore : IDisposable
    {
        private readonly string? _previous;
        public Restore(string? previous) => _previous = previous;
        public void Dispose() => Override_.Value = _previous;
    }
}
