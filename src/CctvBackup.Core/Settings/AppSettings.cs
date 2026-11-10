using System.Text.Json;

namespace CctvBackup.Core.Settings;

public sealed class AppSettings
{
    /// <summary>
    /// Optional folder with the vendor DLLs. Normally empty: the program uses the "sdk" folder next to it
    /// and falls back to the CMS install folder.
    /// </summary>
    public string SdkDirectory { get; set; } = string.Empty;

    /// <summary>"en" or "id". Empty = follow the Windows display language the first time.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>The CMS file (Data.xml) the branches were last imported from. Empty = the CMS default location.</summary>
    public string CmsDataFile { get; set; } = string.Empty;

    public string LastDestination { get; set; } = string.Empty;
    public string LastBranch { get; set; } = string.Empty;

    /// <summary>Attempts per file before it is marked failed.</summary>
    public int MaxRetries { get; set; } = 3;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // fall through to defaults: a broken settings file must never stop the app from starting
        }
        return new();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
