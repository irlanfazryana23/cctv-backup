using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Backup;

public sealed record DiskCheckResult(bool Ok, string Message);

/// <summary>Checks that the destination can hold the download before it starts (supports Linux mount points and Windows drives).</summary>
public static class DiskCheck
{
    private const long FourGb = 4L * 1024 * 1024 * 1024;

    /// <param name="requiredKb">Total size of files still to download, in KB (as reported by the DVR).</param>
    /// <param name="largestFileKb">Largest single file, in KB.</param>
    public static DiskCheckResult Check(string destinationRoot, long requiredKb, long largestFileKb)
    {
        try
        {
            string full = Path.GetFullPath(destinationRoot);
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\"))
                return new(true, Loc.T("Network destination: free space not checked."));

            var drive = ResolveDrive(full, root);
            if (drive is null || !drive.IsReady)
                return new(false, Loc.F("Drive {0} is not ready. Is the external disk connected?", drive?.Name ?? root));

            string label = drive.Name;
            if (IsFat32(drive.DriveFormat) && largestFileKb * 1024 >= FourGb)
                return new(false, Loc.F("{0} is formatted FAT32, which cannot store files of 4 GB or more. Use an NTFS or exFAT disk.", label));

            long needed = requiredKb * 1024;
            long headroom = 256L * 1024 * 1024;
            if (drive.AvailableFreeSpace < needed + headroom)
                return new(false,
                    Loc.F("Not enough free space on {0}: need about {1}, only {2} free.", label, Format(needed), Format(drive.AvailableFreeSpace)));

            return new(true, Loc.F("{0} free on {1}, need about {2}.", Format(drive.AvailableFreeSpace), label, Format(needed)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new(false, Loc.F("Cannot use destination '{0}': {1}", destinationRoot, ex.Message));
        }
    }

    /// <summary>
    /// On Linux, Path.GetPathRoot("/media/usb/backup") returns "/", which would inspect the root partition instead of /media/usb.
    /// Find the mounted filesystem with the longest matching mount point prefix.
    /// </summary>
    private static DriveInfo? ResolveDrive(string fullPath, string fallbackRoot)
    {
        if (OperatingSystem.IsWindows())
            return new DriveInfo(fallbackRoot);

        try
        {
            string normalized = fullPath.TrimEnd('/') + "/";
            var match = DriveInfo.GetDrives()
                .Where(d =>
                {
                    string mount = d.Name.TrimEnd('/') + "/";
                    return normalized.StartsWith(mount, StringComparison.Ordinal);
                })
                .OrderByDescending(d => d.Name.Length)
                .FirstOrDefault();
            if (match is not null) return match;
        }
        catch (Exception) { }

        return new DriveInfo(fallbackRoot);
    }

    private static bool IsFat32(string format) =>
        format.Equals("FAT32", StringComparison.OrdinalIgnoreCase)
        || format.Equals("vfat", StringComparison.OrdinalIgnoreCase)
        || format.Equals("msdos", StringComparison.OrdinalIgnoreCase);

    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }
}
