using CctvBackup.Core.Sdk;

namespace CctvBackup.Core.Backup;

/// <summary>Builds the on-disk layout: Dest/Branch/CamNN/YYYY-MM-DD/HHmmss-HHmmss.h264</summary>
public static class PathBuilder
{
    // Include Windows invalid chars even on Linux so external NTFS/exFAT drives remain 100% compatible across OSes.
    private static readonly HashSet<char> InvalidChars = Path.GetInvalidFileNameChars()
        .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
        .ToHashSet();

    /// <summary>Makes a string safe to use as a single folder or file name on both Linux and Windows (NTFS/exFAT).</summary>
    public static string Sanitize(string name)
    {
        var chars = name.Select(c => InvalidChars.Contains(c) || c < 32 ? '_' : c).ToArray();
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
            day = "unknown-date";
            string normalized = file.RemoteName.Replace('\\', '/');
            name = Sanitize(Path.GetFileNameWithoutExtension(normalized));
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
        string ext = Path.GetExtension(remoteName.Replace('\\', '/'));
        bool sane = ext.Length is >= 2 and <= 6 && ext.Skip(1).All(char.IsLetterOrDigit);
        return sane ? ext.ToLowerInvariant() : ".h264";
    }
}
