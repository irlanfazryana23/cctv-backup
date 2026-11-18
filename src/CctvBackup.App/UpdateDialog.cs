using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Updates;

namespace CctvBackup.App;

/// <summary>
/// Shows release notes, downloads the updated Windows installer from Private GitHub Releases
/// with progress + SHA-256 verification, and starts an in-place upgrade.
/// </summary>
internal sealed class UpdateDialog : Form
{
    private readonly ReleaseInfo _release;
    private readonly Version _currentVersion;
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 68 };
    private readonly Panel _body = new() { Dock = DockStyle.Fill, BackColor = Ui.Surface };

    private ModernProgressBar _progress = null!;
    private Label _lblStatus = null!;
    private ModernButton _btnInstall = null!;
    private ModernButton _btnCancel = null!;
    private CancellationTokenSource? _cts;
    private bool _downloading;

    public UpdateDialog(ReleaseInfo release)
    {
        _release = release;
        _currentVersion = AppUpdater.CurrentAppVersion();

        Text = Loc.T("Software Update");
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 414);
        BackColor = Ui.Surface;
        Font = Ui.Font(9.5f);
        DoubleBuffered = true;

        BuildChrome();
        BuildBody();
        Ui.EnableScaling(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildChrome()
    {
        _header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using var brush = new LinearGradientBrush(_header.ClientRectangle,
                Color.FromArgb(15, 23, 42),
                Color.FromArgb(30, 58, 138),
                LinearGradientMode.Horizontal);
            g.FillRectangle(brush, _header.ClientRectangle);

            using var pen = new Pen(Color.FromArgb(51, 65, 85), 1);
            g.DrawLine(pen, 0, _header.Height - 1, _header.Width, _header.Height - 1);

            int sx = Ui.S(this, 16);
            var badgeRect = new Rectangle(sx, Ui.S(this, 16), Ui.S(this, 36), Ui.S(this, 36));
            using var badgeBg = new SolidBrush(Color.White);
            using var badgePath = Ui.RoundedRect(badgeRect, Ui.S(this, 8));
            g.FillPath(badgeBg, badgePath);

            var icon = AppIcon.Get();
            if (icon != null)
            {
                g.DrawIcon(icon, new Rectangle(sx + Ui.S(this, 4), Ui.S(this, 20), Ui.S(this, 28), Ui.S(this, 28)));
            }

            TextRenderer.DrawText(g, Loc.T("Software Update Available"), Ui.Font(12.5f, FontStyle.Bold),
                new Point(sx + Ui.S(this, 46), Ui.S(this, 13)), Color.White, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Loc.F("Current version: v{0}", AppUpdater.FormatVersion(_currentVersion)), Ui.Font(8.5f),
                new Point(sx + Ui.S(this, 48), Ui.S(this, 38)), Color.FromArgb(203, 213, 225), TextFormatFlags.NoPrefix);
        };

        Controls.Add(_body);
        Controls.Add(_header);
        _header.SendToBack();
    }

    private void BuildBody()
    {
        int x = 22;
        int w = 416;

        // Version summary card
        var summaryCard = new Panel
        {
            Location = new Point(x, 16),
            Size = new Size(w, 50),
            BackColor = Color.FromArgb(240, 253, 244)
        };
        summaryCard.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(134, 239, 172), 1.2f);
            using var path = Ui.RoundedRect(new Rectangle(0, 0, summaryCard.Width - 1, summaryCard.Height - 1), 6);
            e.Graphics.DrawPath(pen, path);
        };

        string sizeText = _release.AssetSizeBytes > 0
            ? $"{_release.AssetSizeBytes / (1024.0 * 1024.0):F1} MB"
            : string.Empty;

        summaryCard.Controls.Add(new Label
        {
            Text = $"✨  v{AppUpdater.FormatVersion(_currentVersion)}  →  v{AppUpdater.FormatVersion(_release.Version)}",
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 101, 52),
            Location = new Point(14, 12),
            Size = new Size(270, 26),
            BackColor = Color.Transparent
        });
        summaryCard.Controls.Add(new Label
        {
            Text = sizeText,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(21, 128, 61),
            Location = new Point(290, 14),
            Size = new Size(112, 22),
            TextAlign = ContentAlignment.MiddleRight,
            BackColor = Color.Transparent
        });

        var lblNotes = new Label
        {
            Text = Loc.T("Release notes:"),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Ui.Text,
            Location = new Point(x, 78),
            Size = new Size(w, 18)
        };

        string notesText = string.IsNullOrWhiteSpace(_release.ReleaseNotes)
            ? Loc.T("Includes bug fixes and improvements.")
            : _release.ReleaseNotes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);

        var notesBox = new TextBox
        {
            Text = notesText,
            Multiline = true,
            ReadOnly = true,
            TabStop = false,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White,
            ForeColor = Ui.Ink,
            Font = new Font("Segoe UI", 9f),
            Location = new Point(x, 98),
            Size = new Size(w, 132)
        };

        _lblStatus = new Label
        {
            Text = Loc.T("Your saved branches and settings will be preserved during the update."),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Ui.Muted,
            AutoEllipsis = true,
            Location = new Point(x, 240),
            Size = new Size(w, 20)
        };

        _progress = new ModernProgressBar
        {
            Location = new Point(x, 264),
            Size = new Size(w, 12),
            Maximum = 1000,
            Value = 0,
            ProgressColor = Ui.Accent
        };

        int btnW = (w - 10) / 2;
        _btnCancel = new ModernButton
        {
            Text = Loc.T("Later"),
            Location = new Point(x, 290),
            Size = new Size(btnW, 38),
            UseMnemonic = false
        };
        Ui.StyleSecondary(_btnCancel);
        _btnCancel.Click += (_, _) =>
        {
            if (_downloading)
            {
                _cts?.Cancel();
            }
            else
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        _btnInstall = new ModernButton
        {
            Text = Loc.T("Download & Install"),
            Location = new Point(x + btnW + 10, 290),
            Size = new Size(btnW, 38),
            UseMnemonic = false
        };
        Ui.StyleSuccess(_btnInstall);
        _btnInstall.Click += async (_, _) => await StartDownloadAndInstallAsync();

        _body.Controls.AddRange(new Control[] { summaryCard, lblNotes, notesBox, _lblStatus, _progress, _btnCancel, _btnInstall });
        Shown += (_, _) => _btnInstall.Focus();
        Load += (_, _) =>
        {
            int needH = _header.Bottom + _btnInstall.Bottom + Ui.S(this, 18);
            if (ClientSize.Height != needH)
                ClientSize = new Size(ClientSize.Width, needH);
        };
    }

    private async Task StartDownloadAndInstallAsync()
    {
        if (_downloading) return;
        _downloading = true;
        _btnInstall.Enabled = false;
        _btnCancel.Text = Loc.T("Cancel");
        _progress.ProgressColor = Ui.Accent;
        _progress.Value = 0;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        string tempFile = Path.Combine(Path.GetTempPath(), _release.AssetName);

        try
        {
            var progressReporter = new Progress<(long Received, long Total)>(p =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    double rxMb = p.Received / (1024.0 * 1024.0);
                    if (p.Total > 0)
                    {
                        double totMb = p.Total / (1024.0 * 1024.0);
                        int pct = (int)Math.Clamp((double)p.Received / p.Total * 1000.0, 0, 1000);
                        _progress.Value = pct;
                        _lblStatus.ForeColor = Ui.AccentDark;
                        _lblStatus.Text = Loc.F("Downloading update: {0} / {1} MB ({2}%)", $"{rxMb:F1}", $"{totMb:F1}", pct / 10);
                    }
                    else
                    {
                        _lblStatus.ForeColor = Ui.AccentDark;
                        _lblStatus.Text = Loc.F("Downloading update: {0} MB...", $"{rxMb:F1}");
                    }
                });
            });

            _lblStatus.ForeColor = Ui.AccentDark;
            _lblStatus.Text = Loc.T("Connecting to update server...");

            await AppUpdater.DownloadInstallerAsync(_release, tempFile, progressReporter, null, _cts.Token);

            _progress.Value = 1000;
            _progress.ProgressColor = Color.FromArgb(22, 163, 74);
            _lblStatus.ForeColor = Color.FromArgb(22, 101, 52);
            _lblStatus.Text = Loc.T("Starting installer...");

            var psi = new ProcessStartInfo(tempFile)
            {
                Arguments = "/SILENT /SP- /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true
            };
            Process.Start(psi);

            _downloading = false;
            _btnCancel.Enabled = false;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            _downloading = false;
            _btnInstall.Enabled = true;
            _btnCancel.Text = Loc.T("Later");
            _progress.Value = 0;
            _lblStatus.ForeColor = Ui.Muted;
            _lblStatus.Text = Loc.T("Update download cancelled.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _downloading = false;
            _btnInstall.Enabled = true;
            _btnCancel.Text = Loc.T("Later");
            _lblStatus.ForeColor = Color.FromArgb(180, 83, 9);
            _lblStatus.Text = Loc.T("Installer elevation was cancelled. Click Download & Install to retry.");
        }
        catch (Exception ex)
        {
            _downloading = false;
            _btnInstall.Enabled = true;
            _btnCancel.Text = Loc.T("Later");
            _progress.ProgressColor = Ui.Danger;
            _lblStatus.ForeColor = Ui.Danger;
            _lblStatus.Text = Loc.F("Update failed: {0}", ex.Message);
        }
    }
}
