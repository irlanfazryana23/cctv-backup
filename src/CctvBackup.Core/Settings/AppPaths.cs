namespace CctvBackup.Core.Settings;

/// <summary>Where the tool keeps its own files (per Windows user).</summary>
public static class AppPaths
{
    public static string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CctvBackup");

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string BranchesFile => Path.Combine(DataDirectory, "branches.json");
    public static string CredentialsFile => Path.Combine(DataDirectory, "credentials.dat");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>ASCII-only scratch folder (the vendor SDK only accepts ANSI paths).</summary>
    public static string StagingDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CctvBackup", "staging");
}
