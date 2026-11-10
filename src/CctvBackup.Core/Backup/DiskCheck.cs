using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Backup;

public sealed record DiskCheckResult(bool Ok, string Message);

/// <summary>Checks that the destination can hold the download before it starts.</summary>
public static class DiskCheck
{
    private const long FourGb = 4L * 1024 * 1024 * 1024;

    /// <param name="requiredKb">Total size of files still to download, in KB (as reported by the DVR).</param>
    /// <param name="largestFileKb">Largest single file, in KB.</param>
    public static DiskCheckResult Check(string destinationRoot, long requiredKb, long largestFileKb)
    {
        try
        {
            string? root = Path.GetPathRoot(Path.GetFullPath(destinationRoot));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\"))
                return new(true, Loc.T("Network destination: free space not checked."));

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
                return new(false, Loc.F("Drive {0} is not ready. Is the external disk connected?", root));

            if (drive.DriveFormat.Equals("FAT32", StringComparison.OrdinalIgnoreCase) && largestFileKb * 1024 >= FourGb)
                return new(false, Loc.F("{0} is formatted FAT32, which cannot store files of 4 GB or more. Use an NTFS or exFAT disk.", root));

            long needed = requiredKb * 1024;
            // Keep a little headroom so Windows and the disk are not driven to 0 bytes free.
            long headroom = 256L * 1024 * 1024;
            if (drive.AvailableFreeSpace < needed + headroom)
                return new(false,
                    Loc.F("Not enough free space on {0}: need about {1}, only {2} free.", root, Format(needed), Format(drive.AvailableFreeSpace)));

            return new(true, Loc.F("{0} free on {1}, need about {2}.", Format(drive.AvailableFreeSpace), root, Format(needed)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new(false, Loc.F("Cannot use destination '{0}': {1}", destinationRoot, ex.Message));
        }
    }

    public static string Format(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {units[i]}";
    }
}
