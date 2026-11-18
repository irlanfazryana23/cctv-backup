using CctvBackup.Core.Localization;

namespace CctvBackup.Core.Backup;

/// <summary>Checks what the user entered before a backup starts. Returns plain-language problems.</summary>
public static class RequestValidator
{
    public static List<string> Validate(IReadOnlyList<BackupRequest> requests, string destination)
    {
        var problems = new List<string>();

        if (requests.Count == 0)
            problems.Add(Loc.T("Tick at least one branch."));

        foreach (var r in requests)
        {
            if (!r.Branch.CamerasDetected)
                problems.Add(Loc.F("{0}: its cameras have not been detected yet. Select it and press Test connection.", r.Branch.Name));
            else if (r.Cameras.Count == 0)
                problems.Add(Loc.F("{0}: choose at least one camera.", r.Branch.Name));
        }

        foreach (var r in requests.Take(1))
        {
            if (r.From >= r.To)
                problems.Add(Loc.T("The start of the period must be before the end."));
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            problems.Add(Loc.T("Choose the folder to save the backup to (your external disk)."));
        }
        else
        {
            try
            {
                string full = Path.GetFullPath(destination);
                string? root = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(root) && !root.StartsWith(@"\\") && !Directory.Exists(root))
                {
                    problems.Add(Loc.F("Drive {0} was not found. Is the external disk connected?", root));
                }
                else if (!OperatingSystem.IsWindows() && IsUnmountedLinuxMediaPath(full, out string mountRoot))
                {
                    problems.Add(Loc.F("Drive {0} was not found. Is the external disk connected?", mountRoot));
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                problems.Add(Loc.T("The save folder is not a valid path."));
            }
        }

        return problems;
    }

    /// <summary>
    /// On Linux, external disks are typically mounted under /mnt/&lt;disk&gt; or /media/&lt;user&gt;/&lt;disk&gt; or /run/media/&lt;user&gt;/&lt;disk&gt;.
    /// If the mount directory itself does not exist, warn the user that the disk is not connected.
    /// </summary>
    private static bool IsUnmountedLinuxMediaPath(string fullPath, out string mountRoot)
    {
        mountRoot = string.Empty;
        string[] parts = fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "mnt")
        {
            mountRoot = $"/{parts[0]}/{parts[1]}";
            return !Directory.Exists(mountRoot);
        }
        if (parts.Length >= 3 && parts[0] == "media")
        {
            mountRoot = $"/{parts[0]}/{parts[1]}/{parts[2]}";
            return !Directory.Exists(mountRoot);
        }
        if (parts.Length >= 4 && parts[0] == "run" && parts[1] == "media")
        {
            mountRoot = $"/{parts[0]}/{parts[1]}/{parts[2]}/{parts[3]}";
            return !Directory.Exists(mountRoot);
        }
        return false;
    }
}
