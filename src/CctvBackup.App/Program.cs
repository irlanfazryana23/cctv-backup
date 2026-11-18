using System.Runtime.InteropServices;
using CctvBackup.Core.Auth;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Playback;
using CctvBackup.Core.Settings;

namespace CctvBackup.App;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    private const int ATTACH_PARENT_PROCESS = -1;

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

        // CLI flags for IT Admins / Maintenance
        if (args.Length > 0)
        {
            if (args.Contains("--screenshot", StringComparer.OrdinalIgnoreCase))
            {
                int idx = Array.FindIndex(args, a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
                string outDir = (idx >= 0 && idx + 1 < args.Length) ? args[idx + 1] : ".";
                Directory.CreateDirectory(outDir);

                void Snap(Form form, string fileName)
                {
                    form.Show();
                    form.Refresh();
                    Application.DoEvents();
                    var bmp = new Bitmap(form.Width, form.Height);
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                    bmp.Save(Path.Combine(outDir, fileName), System.Drawing.Imaging.ImageFormat.Png);
                    form.Close();
                }

                using (var f = new LoginForm(testMode: 1)) Snap(f, "test_login.png");
                using (var f = new LoginForm(testMode: 2)) Snap(f, "test_setup.png");
                using (var f = new LoginForm(testMode: 3)) Snap(f, "test_key.png");
                using (var f = new LoginForm(testMode: 4)) Snap(f, "test_recovery.png");
                using (var f = new LoginForm(testMode: 1))
                {
                    f.Show();
                    Application.DoEvents();
                    var sw = typeof(LoginForm).GetMethod("SwitchMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    var modeType = typeof(LoginForm).GetNestedType("FormMode", System.Reflection.BindingFlags.NonPublic)!;
                    sw.Invoke(f, new[] { Enum.Parse(modeType, "Recovery") });

                    Snap(f, "test_recovery_visible.png");
                }

                Loc.Set(Loc.Indonesian);
                using (var fId = new LoginForm(testMode: 1)) Snap(fId, "test_login_id.png");
                using (var fSetupId = new LoginForm(testMode: 2)) Snap(fSetupId, "test_setup_id.png");
                Loc.Set(Loc.English);
                using (var main = new MainForm(log))
                {
                    var cam1 = new CctvBackup.Core.Branches.Camera(1, "Cam01");
                    var f1 = new CctvBackup.Core.Sdk.RemoteFile(0, "rec1", new DateTime(2026, 11, 11, 0, 0, 0), new DateTime(2026, 11, 11, 2, 0, 0), 128 * 1024, default);
                    var j1 = new CctvBackup.Core.Backup.DownloadJob("Main Branch", cam1, f1, "C:\\rec1.h264") { Status = CctvBackup.Core.Backup.JobStatus.Downloading, Percent = 20 };
                    main.AddTestJob(j1);

                    var f2 = new CctvBackup.Core.Sdk.RemoteFile(0, "rec2", new DateTime(2026, 11, 11, 2, 0, 0), new DateTime(2026, 11, 11, 4, 0, 0), (long)(127.7 * 1024), default);
                    var j2 = new CctvBackup.Core.Backup.DownloadJob("Main Branch", cam1, f2, "C:\\rec2.h264") { Status = CctvBackup.Core.Backup.JobStatus.Pending, Percent = 0, Selected = true };
                    main.AddTestJob(j2);

                    var f3 = new CctvBackup.Core.Sdk.RemoteFile(0, "rec3", new DateTime(2026, 11, 11, 4, 0, 0), new DateTime(2026, 11, 11, 6, 0, 0), (long)(182.5 * 1024), default);
                    var j3 = new CctvBackup.Core.Backup.DownloadJob("Main Branch", cam1, f3, "C:\\rec3.h264") { Status = CctvBackup.Core.Backup.JobStatus.Pending, Percent = 0, Selected = false };
                    main.AddTestJob(j3);

                    main.PreviewLanguage(Loc.English);
                    Snap(main, "test_main.png");
                }

                using (var mainId = new MainForm(log))
                {
                    var cam1 = new CctvBackup.Core.Branches.Camera(1, "Cam01");
                    var f1 = new CctvBackup.Core.Sdk.RemoteFile(0, "rec1", new DateTime(2026, 11, 11, 0, 0, 0), new DateTime(2026, 11, 11, 2, 0, 0), 128 * 1024, default);
                    mainId.AddTestJob(new CctvBackup.Core.Backup.DownloadJob("Main Branch", cam1, f1, "C:\\rec1.h264") { Status = CctvBackup.Core.Backup.JobStatus.Downloading, Percent = 45 });
                    mainId.PreviewLanguage(Loc.Indonesian);
                    Snap(mainId, "test_main_id.png");
                }

                using (var account = new AccountForm()) Snap(account, "test_account.png");
                using (var client = new CctvBackup.Core.Dvr.XmDvrClient(CctvBackup.Core.Sdk.SdkLocator.BundledDirectory))
                {
                    using (var branch = new BranchDialog(client, Array.Empty<string>(), null, null)) Snap(branch, "test_branch.png");
                    Loc.Set(Loc.Indonesian);
                    using (var branchId = new BranchDialog(client, Array.Empty<string>(), null, null)) Snap(branchId, "test_branch_id.png");
                    Loc.Set(Loc.English);
                }

                Environment.Exit(0);
                return;
            }

            if (args.Contains("--reset-auth", StringComparer.OrdinalIgnoreCase))
            {
                AppAuthStore.Clear();
                EnsureConsole();
                Console.WriteLine("CCTV Backup: Authentication store cleared. Next launch will run first-time setup.");
                return;
            }

            if (args.Contains("--reset-password", StringComparer.OrdinalIgnoreCase))
            {
                EnsureConsole();
                AppAuthStore.ResetFromConsole();
                return;
            }

            // Windows starts the program with a recording as its argument when a .h264 file is double-clicked:
            // then only the player opens, not the backup window.
            string? recording = args.FirstOrDefault(a => !a.StartsWith("-") && a.Trim().Length > 0);
            if (recording is not null)
            {
                RunPlayerOnly(recording);
                return;
            }
        }

        // Authentication & App Lock Gate
        using (var login = new LoginForm())
        {
            if (login.ShowDialog() != DialogResult.OK)
            {
                // User closed login without authenticating
                return;
            }
        }

        Application.Run(new MainForm(log));
    }

    private static void EnsureConsole()
    {
        if (Console.IsOutputRedirected) return;
        if (AttachConsole(ATTACH_PARENT_PROCESS) || AllocConsole())
        {
            try
            {
                var stdOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
                Console.SetOut(stdOut);
                var stdErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
                Console.SetError(stdErr);
            }
            catch { }
        }
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
