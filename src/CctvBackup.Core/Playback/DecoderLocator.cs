namespace CctvBackup.Core.Playback;

/// <summary>
/// Finds the folder with the video decoder (H264Play.dll and its helper DLLs, taken from the DVR player).
/// The installer puts a copy in the "decoder" folder next to the program; a separately installed DVR player is the fallback.
/// </summary>
public static class DecoderLocator
{
    public const string DllName = "H264Play.dll";

    public static string BundledDirectory => Path.Combine(AppContext.BaseDirectory, "decoder");

    public static string InstalledDirectory => @"C:\Program Files (x86)\Player";

    /// <summary>The folder that holds the decoder, or null when there is none. Folders can be given for tests.</summary>
    public static string? Find(string? bundledDirectory = null, string? installedDirectory = null)
    {
        foreach (var dir in new[] { bundledDirectory ?? BundledDirectory, installedDirectory ?? InstalledDirectory })
            if (File.Exists(Path.Combine(dir, DllName))) return dir;
        return null;
    }
}
