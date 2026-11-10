using CctvBackup.Core.Localization;
using CctvBackup.Core.Playback;
using CctvBackup.Core.Settings;

namespace CctvBackup.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Language first, so even an early error message is in the chosen language.
        Loc.Set(Loc.Resolve(AppSettings.Load(AppPaths.SettingsFile).Language));

        var log = new FileLog(AppPaths.LogDirectory);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(log, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(log, e.ExceptionObject as Exception);

        // Windows starts the program with a recording as its argument when a .h264 file is double-clicked:
        // then only the player opens, not the backup window.
        string? recording = args.FirstOrDefault(a => !a.StartsWith("-") && a.Trim().Length > 0);
        if (recording is not null)
        {
            RunPlayerOnly(recording);
            return;
        }

        Application.Run(new MainForm(log));
    }

    private static void RunPlayerOnly(string recording)
    {
        string path;
        try { path = Path.GetFullPath(recording.Trim()); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            MessageBox.Show(Loc.F("This file is not on the disk:\n{0}", recording), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? decoder = DecoderLocator.Find();
        if (decoder is null)
        {
            MessageBox.Show(Loc.T("The video decoder was not found. Install this program again."), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!File.Exists(path))
        {
            MessageBox.Show(Loc.F("This file is not on the disk:\n{0}", path), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var player = new PlayerForm(decoder);
        player.Shown += (_, _) =>
        {
            try { player.Open(path); }
            catch (PlaybackException ex)
            {
                MessageBox.Show(player, ex.Message, "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                player.Close();
            }
        };
        Application.Run(player);
    }

    private static void ReportCrash(FileLog log, Exception? ex)
    {
        log.Write($"{DateTime.Now:HH:mm:ss}  UNHANDLED: {ex}");
        MessageBox.Show(
            Loc.F("Something unexpected went wrong:\n\n{0}\n\nDetails were written to the log folder:\n{1}",
                ex?.Message ?? Loc.T("unknown error"), AppPaths.LogDirectory),
            "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
