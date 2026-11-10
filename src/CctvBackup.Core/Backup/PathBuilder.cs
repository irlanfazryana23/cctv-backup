using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Backup;

/// <summary>Builds the on-disk layout: Dest\Branch\CamNN\YYYY-MM-DD\HHmmss-HHmmss.h264</summary>
public static class PathBuilder
{
    /// <summary>Makes a string safe to use as a single folder or file name on Windows.</summary>
    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || c < 32 ? '_' : c).ToArray();
        string s = new string(chars).Trim().TrimEnd('.', ' ');
        return s.Length == 0 ? "_" : s;
    }

    public static string BranchFolder(string root, string branchName) =>
        Path.Combine(root, Sanitize(branchName));

    public static string Build(string root, string branchName, int channel, RemoteFile file)
    {
        string camera = $"Cam{channel + 1:00}";
        string ext = ExtensionOf(file.RemoteName);

        string day, name;
        if (file.Begin == DateTime.MinValue)
        {
            // Could not read the time from the DVR: fall back to its own file name.
            day = "unknown-date";
            name = Sanitize(Path.GetFileNameWithoutExtension(file.RemoteName));
        }
        else
        {
            day = file.Begin.ToString("yyyy-MM-dd");
            name = $"{file.Begin:HHmmss}-{file.End:HHmmss}";
        }

        return Path.Combine(BranchFolder(root, branchName), camera, day, name + ext);
    }

    /// <summary>Adds _2, _3 ... before the extension until the path is not already taken.</summary>
    public static string MakeUnique(string path, Func<string, bool> isTaken)
    {
        if (!isTaken(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!isTaken(candidate)) return candidate;
        }
    }

    private static string ExtensionOf(string remoteName)
    {
        string ext = Path.GetExtension(remoteName);
        bool sane = ext.Length is >= 2 and <= 6 && ext.Skip(1).All(char.IsLetterOrDigit);
        return sane ? ext.ToLowerInvariant() : ".h264";
    }
}
