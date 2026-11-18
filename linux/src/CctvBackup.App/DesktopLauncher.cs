using System.Diagnostics;

namespace CctvBackup.App;

/// <summary>
/// Launches the Linux desktop window and integrates with native Linux desktop utilities
/// (xdg-open, zenity, kdialog, Chromium/Chrome/Firefox app-mode window).
/// </summary>
public static class DesktopLauncher
{
    public static void OpenAppWindow(string url)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                // Prefer launching a dedicated chromeless desktop window (--app=...) if a Chromium-based browser is installed.
                string[] appBrowsers =
                {
                    "chromium",
                    "chromium-browser",
                    "google-chrome",
                    "google-chrome-stable",
                    "brave-browser",
                    "microsoft-edge",
                };

                foreach (string bin in appBrowsers)
                {
                    if (CommandExists(bin))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = bin,
                            ArgumentList = { $"--app={url}", "--new-window" },
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        });
                        return;
                    }
                }

                if (CommandExists("xdg-open"))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "xdg-open",
                        ArgumentList = { url },
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    return;
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
        }
        catch
        {
            // If no desktop session is running (e.g. SSH), the user can open the printed URL directly.
        }
    }

    public static bool TryPickDirectoryNative(string? initialDir, out string? pickedPath)
    {
        pickedPath = null;
        if (!OperatingSystem.IsLinux()) return false;

        try
        {
            if (CommandExists("zenity"))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "zenity",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("--file-selection");
                psi.ArgumentList.Add("--directory");
                psi.ArgumentList.Add("--title=Choose Backup Destination Folder");
                if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
                    psi.ArgumentList.Add($"--filename={initialDir.TrimEnd('/')}/");

                using var p = Process.Start(psi);
                if (p is not null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        pickedPath = output;
                        return true;
                    }
                    return true; // user cancelled native dialog
                }
            }

            if (CommandExists("kdialog"))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "kdialog",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("--getexistingdirectory");
                psi.ArgumentList.Add(!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir)
                    ? initialDir
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

                using var p = Process.Start(psi);
                if (p is not null)
                {
                    string output = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        pickedPath = output;
                        return true;
                    }
                    return true;
                }
            }
        }
        catch
        {
            // Fall back to in-app filesystem browser modal
        }

        return false;
    }

    public static bool TryOpenFolderInFileManager(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    ArgumentList = { folderPath },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return true;
            }
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    ArgumentList = { folderPath },
                    UseShellExecute = true,
                });
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool CommandExists(string command)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/bin:/usr/bin:/bin";
        foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string full = Path.Combine(dir, command);
            if (File.Exists(full)) return true;
        }
        return false;
    }
}
