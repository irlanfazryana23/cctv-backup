namespace CctvBackup.Core.Sdk;

/// <summary>
/// Finds the folder that holds the vendor SDK (either native Linux libnetsdk.so or Windows NetSdk.dll).
/// The copy bundled next to the program (the "sdk" folder) wins, followed by configured and system/CMS paths.
/// </summary>
public static class SdkLocator
{
    public const string DllName = "NetSdk.dll";
    public const string LinuxSoName = "libnetsdk.so";

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "sdk");

    public static string? Find(string? configured, string? bundled = null, string? cmsFolder = null)
    {
        var candidates = new[]
        {
            bundled ?? BundledDirectory,
            configured,
            cmsFolder ?? XmSdk.DefaultSdkDirectory,
        };

        return candidates.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d) &&
            (File.Exists(Path.Combine(d, DllName)) || File.Exists(Path.Combine(d, LinuxSoName))));
    }

    public static bool HasNativeLinuxSdk(string? sdkDir) =>
        !string.IsNullOrWhiteSpace(sdkDir) && File.Exists(Path.Combine(sdkDir, LinuxSoName));

    public static bool HasWindowsSdk(string? sdkDir) =>
        !string.IsNullOrWhiteSpace(sdkDir) && File.Exists(Path.Combine(sdkDir, DllName));
}
