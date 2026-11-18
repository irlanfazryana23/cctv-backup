using CctvBackup.Core.Localization;

namespace CctvBackup.App;

/// <summary>
/// Exposes translated UI strings (English &amp; Bahasa Indonesia) to the Linux Desktop UI.
/// </summary>
public static class UiStrings
{
    public static Dictionary<string, string> GetCurrentTable()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in IndonesianStrings.Table.Keys)
        {
            dict[key] = Loc.T(key).Replace("&", "");
        }
        return dict;
    }
}
