namespace CctvBackup.Core.Sdk;

/// <summary>
/// Finds the folder that holds the vendor DLLs. The copy bundled next to the program (the "sdk" folder)
/// wins, so the program works on a PC without the CMS. An explicit setting and the CMS install folder
/// are fallbacks.
/// </summary>
public static class SdkLocator
{
    public const string DllName = "NetSdk.dll";

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "sdk");

    public static string? Find(string? configured, string? bundled = null, string? cmsFolder = null)
    {
        var candidates = new[]
        {
            bundled ?? BundledDirectory,
            configured,
            cmsFolder ?? XmSdk.DefaultSdkDirectory,
        };

        return candidates.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d) && File.Exists(Path.Combine(d, DllName)));
    }
}
