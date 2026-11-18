namespace CctvBackup.Core.Settings;

/// <summary>Where the tool keeps its own files (per user, XDG ~/.config/CctvBackup on Linux, %APPDATA%\CctvBackup on Windows).</summary>
public static class AppPaths
{
    public static string DataDirectory { get; set; } = GetDefaultDataDirectory();

    private static string GetDefaultDataDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            appData = Path.Combine(home, ".config");
        }
        return Path.Combine(appData, "CctvBackup");
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string BranchesFile => Path.Combine(DataDirectory, "branches.json");
    public static string CredentialsFile => Path.Combine(DataDirectory, "credentials.dat");
    public static string AuthFile => Path.Combine(DataDirectory, "auth.json");
    public static string KeyFile => Path.Combine(DataDirectory, ".key");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>ASCII-only scratch folder (per-user cache on Linux so root permissions are never needed).</summary>
    public static string StagingDirectory =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CctvBackup", "staging")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } local
                    ? local
                    : Path.GetTempPath(),
                "CctvBackup", "staging");
}
