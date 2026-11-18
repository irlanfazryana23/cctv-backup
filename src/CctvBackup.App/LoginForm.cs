using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CctvBackup.Core.Auth;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Settings;

namespace CctvBackup.App;

/// <summary>
/// Modern authentication and app lock window matching the streamlined Avalonia desktop edition.
/// Features centered security branding, clean rounded inputs, segmented recovery options,
/// and branch challenge-response verification for forgotten passwords.
/// </summary>
internal sealed class LoginForm : Form
{
    private enum FormMode { SignIn, Setup, ShowRecoveryKey, Recovery }
    private enum RecoveryTab { RemoteIt, MasterKey }

    private FormMode _mode;
    private RecoveryTab _recTab = RecoveryTab.RemoteIt;
    private string _currentChallenge = string.Empty;
    private string _createdRecoveryKey = string.Empty;

    // Outer Chrome
    private readonly Panel _headerPanel = new() { Dock = DockStyle.Top, Height = 68 };
    private readonly Panel _contentPanel = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };
    private readonly ComboBox _langBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(105, 25), Font = new Font("Segoe UI", 8.5f) };

    public LoginForm(bool forceSetup = false, int testMode = 0)
    {
        Text = Loc.T("CCTV Backup — Authentication");
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        BuildChrome();
        Shown += (_, _) => FitRecoveryWindow();

        if (testMode == 1)
        {
            SwitchMode(FormMode.SignIn);
        }
        else if (testMode == 2)
        {
            SwitchMode(FormMode.Setup);
        }
        else if (testMode == 3)
        {
            _createdRecoveryKey = "CCTV-8924-1832";
            SwitchMode(FormMode.ShowRecoveryKey);
        }
        else if (testMode == 4)
        {
            SwitchMode(FormMode.Recovery);
        }
        else if (forceSetup || !AppAuthStore.IsConfigured())
        {
            SwitchMode(FormMode.Setup);
        }
        else
        {
            SwitchMode(FormMode.SignIn);
        }
    }

    // The layout below is written in 96-dpi pixels; it is scaled to the Windows display scale once a page is built.
    private new float Scale => DeviceDpi / 96f;
    private int DesignWidth => (int)Math.Round(_contentPanel.ClientSize.Width / Scale);
    private int S(int px) => (int)Math.Round(px * Scale);

    private void ScaleContent()
    {
        if (Scale <= 1.01f) return;
        foreach (Control c in _contentPanel.Controls)
            if (c.Tag as string != "noscale") c.Scale(new SizeF(Scale, Scale));
    }

    private void SetFormSize(int width, int height)
    {
        width = S(width);
        height = S(height);
        bool wasVisible = Visible;
        Point center = wasVisible
            ? new Point(Left + (Width / 2), Top + (Height / 2))
            : Point.Empty;

        ClientSize = new Size(width, height);

        if (wasVisible)
        {
            Location = new Point(center.X - (Width / 2), center.Y - (Height / 2));
        }
    }

    private void BuildChrome()
    {
        _langBox.Items.AddRange(new object[] { "English", "Indonesia" });
        _langBox.SelectedIndex = Loc.IsIndonesian ? 1 : 0;
        _headerPanel.Height = S(68);
        _langBox.Size = new Size(S(105), S(25));
        _langBox.Location = new Point(_headerPanel.Width - S(120), S(22));
        _langBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _langBox.SelectedIndexChanged += (_, _) =>
        {
            string code = _langBox.SelectedIndex == 1 ? Loc.Indonesian : Loc.English;
            if (code != Loc.Code)
            {
                Loc.Set(code);
                try
                {
                    var settings = AppSettings.Load(AppPaths.SettingsFile);
                    settings.Language = code;
                    settings.Save(AppPaths.SettingsFile);
                }
                catch { }

                Text = Loc.T("CCTV Backup — Authentication");
                _headerPanel.Invalidate();
                SwitchMode(_mode);
            }
        };

        _headerPanel.Controls.Add(_langBox);

        _headerPanel.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // Horizontal gradient from Slate-900 to Blue-900
            using var brush = new LinearGradientBrush(_headerPanel.ClientRectangle,
                Color.FromArgb(15, 23, 42),
                Color.FromArgb(30, 58, 138),
                LinearGradientMode.Horizontal);
            g.FillRectangle(brush, _headerPanel.ClientRectangle);

            using var pen = new Pen(Color.FromArgb(51, 65, 85), 1);
            g.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);

            int startX = S(16);

            // White rounded logo badge
            var badgeRect = new Rectangle(startX, S(16), S(36), S(36));
            using var badgeBg = new SolidBrush(Color.White);
            using var badgePath = CreateRoundedPath(badgeRect, S(8));
            g.FillPath(badgeBg, badgePath);

            var icon = AppIcon.Get();
            if (icon != null)
            {
                g.DrawIcon(icon, new Rectangle(startX + S(4), S(20), S(28), S(28)));
            }

            // Title & Subtitle text (NoPrefix ensures & is never turned into an underscore)
            TextRenderer.DrawText(g, "CCTV Backup", Ui.Font(13.5f, FontStyle.Bold), new Point(startX + S(46), S(12)), Color.White, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Loc.T("Authentication & Access Control"), Ui.Font(8.5f), new Point(startX + S(48), S(37)), Color.FromArgb(148, 163, 184), TextFormatFlags.NoPrefix);
        };

        _headerPanel.Resize += (_, _) =>
        {
            _langBox.Location = new Point(_headerPanel.Width - S(120), S(22));
            _headerPanel.Invalidate();
        };

        Controls.Add(_contentPanel);
        Controls.Add(_headerPanel);
        _headerPanel.SendToBack();
    }

    private void SwitchMode(FormMode mode)
    {
        _mode = mode;
        _contentPanel.Controls.Clear();

        switch (mode)
        {
            case FormMode.SignIn:
                SetFormSize(420, 440);
                BuildSignInUi();
                ScaleContent();
                break;
            case FormMode.Setup:
                SetFormSize(420, 500);
                BuildSetupUi();
                ScaleContent();
                break;
            case FormMode.ShowRecoveryKey:
                SetFormSize(440, 460);
                BuildShowKeyUi();
                ScaleContent();
                break;
            case FormMode.Recovery:
                SetFormSize(440, 500);
                BuildRecoveryUi();
                ScaleContent();
                FitRecoveryWindow();
                break;
        }
    }

    // ------------------------------------------------------------------ 1. SIGN IN
    private void BuildSignInUi()
    {
        int cardWidth = 340;
        int startX = (DesignWidth - cardWidth) / 2;

        var lblHeading = new Label
        {
            Text = Loc.T("Sign In"),
            Font = new Font("Segoe UI", 16.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(startX, 20),
            Size = new Size(cardWidth, 30),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblSub = new Label
        {
            Text = Loc.T("Enter your credentials to unlock the application."),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(startX, 50),
            Size = new Size(cardWidth, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var errBanner = CreateErrorBanner(new Point(startX, 74), cardWidth);

        // Username
        int curY = 112;
        var lblUser = new Label
        {
            Text = Loc.T("Username"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(startX, curY),
            AutoSize = true
        };
        curY += 20;
        var txtUser = new ModernTextBox
        {
            Text = AppAuthStore.GetUsername() ?? "admin",
            PlaceholderText = Loc.T("Username"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 36)
        };

        // Password
        curY += 44;
        var lblPass = new Label
        {
            Text = Loc.T("Password"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(startX, curY),
            AutoSize = true
        };
        curY += 20;
        var txtPass = new ModernTextBox
        {
            UseSystemPasswordChar = true,
            PlaceholderText = Loc.T("Password"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 36)
        };

        // Show password checkbox
        curY += 42;
        var chkShow = new CheckBox
        {
            Text = Loc.T("Show password"),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(startX + 2, curY),
            AutoSize = true
        };
        chkShow.CheckedChanged += (_, _) => txtPass.UseSystemPasswordChar = !chkShow.Checked;

        // Unlock button
        curY += 32;
        var btnUnlock = new ModernButton
        {
            Text = Loc.T("Unlock"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 38),
            NormalColor = Color.FromArgb(37, 99, 235), // Blue-600
            HoverColor = Color.FromArgb(29, 78, 216),  // Blue-700
            ForeColor = Color.White,
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };

        void DoLogin()
        {
            errBanner.Visible = false;
            string username = txtUser.Text?.Trim() ?? string.Empty;
            string password = txtPass.Text ?? string.Empty;

            if (AppAuthStore.VerifyLogin(username, password))
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                ShowError(errBanner, Loc.T("Invalid username or password."));
                txtPass.SelectAll();
                txtPass.Focus();
            }
        }

        btnUnlock.Click += (_, _) => DoLogin();
        txtUser.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };
        txtPass.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoLogin(); };

        // Forgot password link
        curY += 44;
        var btnForgot = new ModernButton
        {
            Text = Loc.T("Forgot password? (Master Key / Call IT)"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 26),
            NormalColor = Color.Transparent,
            HoverColor = Color.FromArgb(239, 246, 255),
            ForeColor = Color.FromArgb(37, 99, 235),
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
        };
        btnForgot.Click += (_, _) => SwitchMode(FormMode.Recovery);

        _contentPanel.Controls.AddRange(new Control[] {
            lblHeading, lblSub, errBanner, lblUser, txtUser, lblPass, txtPass, chkShow, btnUnlock, btnForgot
        });

        txtPass.Focus();
    }

    // ------------------------------------------------------------------ 2. FIRST-TIME SETUP
    private void BuildSetupUi()
    {
        int cardWidth = 340;
        int startX = (DesignWidth - cardWidth) / 2;

        var lblHeading = new Label
        {
            Text = Loc.T("Create Admin Account"),
            Font = new Font("Segoe UI", 16.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(startX, 16),
            Size = new Size(cardWidth, 28),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblSub = new Label
        {
            Text = Loc.T("Set up administrator credentials to protect branch CCTV backups."),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(startX, 44),
            Size = new Size(cardWidth, 32),
            TextAlign = ContentAlignment.TopCenter
        };

        var errBanner = CreateErrorBanner(new Point(startX, 78), cardWidth);

        // Username
        int curY = 114;
        var lblUser = new Label
        {
            Text = Loc.T("Admin Username"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(startX, curY),
            AutoSize = true
        };
        curY += 20;
        var txtUser = new ModernTextBox
        {
            Text = "admin",
            PlaceholderText = Loc.T("Username"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 36)
        };

        // Password
        curY += 42;
        var lblPass = new Label
        {
            Text = Loc.T("New Password"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(startX, curY),
            AutoSize = true
        };
        curY += 20;
        var txtPass = new ModernTextBox
        {
            UseSystemPasswordChar = true,
            PlaceholderText = Loc.T("New Password"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 36)
        };

        // Confirm Password
        curY += 42;
        var lblConf = new Label
        {
            Text = Loc.T("Confirm Password"),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(startX, curY),
            AutoSize = true
        };
        curY += 20;
        var txtConf = new ModernTextBox
        {
            UseSystemPasswordChar = true,
            PlaceholderText = Loc.T("Confirm Password"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 36)
        };

        // Show password checkbox
        curY += 40;
        var chkShow = new CheckBox
        {
            Text = Loc.T("Show password"),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(startX + 2, curY),
            AutoSize = true
        };
        chkShow.CheckedChanged += (_, _) =>
        {
            txtPass.UseSystemPasswordChar = !chkShow.Checked;
            txtConf.UseSystemPasswordChar = !chkShow.Checked;
        };

        // Create Account Button (Primary Blue)
        curY += 30;
        var btnCreate = new ModernButton
        {
            Text = Loc.T("Create Account & Continue"),
            Location = new Point(startX, curY),
            Size = new Size(cardWidth, 38),
            NormalColor = Color.FromArgb(37, 99, 235), // Blue-600
            HoverColor = Color.FromArgb(29, 78, 216),  // Blue-700
            ForeColor = Color.White,
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };

        void DoCreate()
        {
            errBanner.Visible = false;
            string username = txtUser.Text?.Trim() ?? string.Empty;
            string pass = txtPass.Text ?? string.Empty;
            string conf = txtConf.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError(errBanner, Loc.T("Username cannot be empty."));
                return;
            }
            if (string.IsNullOrEmpty(pass))
            {
                ShowError(errBanner, Loc.T("Password cannot be empty."));
                return;
            }
            if (pass != conf)
            {
                ShowError(errBanner, Loc.T("Passwords do not match."));
                return;
            }

            _createdRecoveryKey = AppAuthStore.Configure(username, pass);
            SwitchMode(FormMode.ShowRecoveryKey);
        }

        btnCreate.Click += (_, _) => DoCreate();
        txtPass.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoCreate(); };
        txtConf.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoCreate(); };

        _contentPanel.Controls.AddRange(new Control[] {
            lblHeading, lblSub, errBanner, lblUser, txtUser, lblPass, txtPass, lblConf, txtConf, chkShow, btnCreate
        });

        txtPass.Focus();
    }

    // ------------------------------------------------------------------ 3. SHOW KEY NOTICE
    private void BuildShowKeyUi()
    {
        int cardWidth = 360;
        int startX = (DesignWidth - cardWidth) / 2;

        // Success Circle Badge (Emerald-100 bg, Emerald-700 checkmark)
        var iconBox = new Panel
        {
            Size = new Size(48, 48),
            Location = new Point(startX + ((cardWidth - 48) / 2), 16),
            BackColor = Color.Transparent
        };
        iconBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(220, 252, 231)); // Emerald-100
            e.Graphics.FillEllipse(bg, 0, 0, iconBox.Width - 1, iconBox.Height - 1);

            using var font = new Font("Segoe UI", 20f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "✓", font, iconBox.ClientRectangle, Color.FromArgb(22, 101, 52),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };

        var lblHeading = new Label
        {
            Text = Loc.T("Save Your Recovery Key"),
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(startX, 72),
            Size = new Size(cardWidth, 26),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblSub = new Label
        {
            Text = Loc.T("If you ever forget your password, this key will unlock your account."),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 9f),
            Location = new Point(startX, 100),
            Size = new Size(cardWidth, 34),
            TextAlign = ContentAlignment.TopCenter
        };

        // Key Display Box with 2px blue accent border
        var keyBox = new Panel
        {
            Location = new Point(startX, 140),
            Size = new Size(cardWidth, 54),
            BackColor = Color.White
        };
        keyBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(37, 99, 235), 2);
            using var path = CreateRoundedPath(new Rectangle(1, 1, keyBox.Width - 3, keyBox.Height - 3), 8);
            e.Graphics.DrawPath(pen, path);
        };

        var lblKey = new Label
        {
            Text = _createdRecoveryKey,
            Font = new Font("Consolas", 17f, FontStyle.Bold),
            ForeColor = Color.FromArgb(29, 78, 216),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };
        keyBox.Controls.Add(lblKey);

        // Warning banner box
        var warnBox = new Panel
        {
            Location = new Point(startX, 206),
            Size = new Size(cardWidth, 50),
            BackColor = Color.FromArgb(254, 252, 232) // Amber-50
        };
        warnBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(253, 230, 138), 1);
            using var path = CreateRoundedPath(new Rectangle(0, 0, warnBox.Width - 1, warnBox.Height - 1), 6);
            e.Graphics.DrawPath(pen, path);
        };
        var lblWarn = new Label
        {
            Text = Loc.T("Write this key down or keep it in a password manager. It cannot be viewed again once you proceed."),
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.FromArgb(180, 83, 9), // Amber-700
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 4)
        };
        warnBox.Controls.Add(lblWarn);

        // Buttons side by side
        int btnWidth = (cardWidth - 10) / 2;
        var btnCopy = new ModernButton
        {
            Text = "📋 " + Loc.T("Copy Key"),
            Location = new Point(startX, 270),
            Size = new Size(btnWidth, 38),
            NormalColor = Color.White,
            HoverColor = Color.FromArgb(241, 245, 249),
            ForeColor = Color.FromArgb(51, 65, 85),
            BorderColor = Color.FromArgb(203, 213, 225),
            BorderRadius = 6,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        btnCopy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_createdRecoveryKey);
                btnCopy.Text = "✓ " + Loc.T("Copied!");
                Task.Delay(1500).ContinueWith(_ =>
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(() => btnCopy.Text = "📋 " + Loc.T("Copy Key"));
                });
            }
            catch { }
        };

        var btnOpen = new ModernButton
        {
            Text = Loc.T("Open CCTV Backup"),
            Location = new Point(startX + btnWidth + 10, 270),
            Size = new Size(btnWidth, 38),
            NormalColor = Color.FromArgb(22, 163, 74), // Emerald-600
            HoverColor = Color.FromArgb(21, 128, 61),  // Emerald-700
            ForeColor = Color.White,
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        btnOpen.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        _contentPanel.Controls.AddRange(new Control[] {
            iconBox, lblHeading, lblSub, keyBox, warnBox, btnCopy, btnOpen
        });
    }

    // ------------------------------------------------------------------ 4. RECOVERY (REMOTE OTP / MASTER KEY)
    private void BuildRecoveryUi()
    {
        int cardWidth = 360;
        int startX = (DesignWidth - cardWidth) / 2;

        var lblHeading = new Label
        {
            Text = Loc.T("Password Recovery"),
            Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(startX, 12),
            Size = new Size(cardWidth, 26),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblSub = new Label
        {
            Text = Loc.T("Select a recovery method to unlock your account:"),
            ForeColor = Color.FromArgb(100, 116, 139),
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(startX, 38),
            Size = new Size(cardWidth, 18),
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Modern Segmented Tab Switcher (iOS/Fluent Pill Style)
        var segment = new ModernSegmentControl
        {
            Location = new Point(startX, 62),
            Size = new Size(cardWidth, 36),
            Items = new[] { "📞  " + Loc.T("Call Main Office IT"), "🔑  " + Loc.T("Master Recovery Key") },
            SelectedIndex = _recTab == RecoveryTab.RemoteIt ? 0 : 1
        };

        // Body Container for the selected tab
        var bodyPanel = new Panel
        {
            Location = new Point(startX, 106),
            Size = new Size(cardWidth + 8, 300),   // a few spare pixels: scaled children can round up by one
            BackColor = Color.Transparent
        };

        // Back link: part of the body, right under the last button of the tab (same scale as the button).
        var btnBack = new ModernButton
        {
            Text = "← " + Loc.T("Back to Sign In"),
            NormalColor = Color.Transparent,
            HoverColor = Color.FromArgb(239, 246, 255),
            ForeColor = Color.FromArgb(37, 99, 235),
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular)
        };
        btnBack.Click += (_, _) => SwitchMode(FormMode.SignIn);
        _recBody = bodyPanel;
        _recBack = btnBack;

        void RenderTabBody()
        {
            bodyPanel.Controls.Clear();
            if (_recTab == RecoveryTab.RemoteIt)
            {
                BuildRemoteItBody(bodyPanel, cardWidth);
            }
            else
            {
                BuildMasterKeyBody(bodyPanel, cardWidth);
            }

            int bottom = bodyPanel.Controls.Cast<Control>().Max(c => c.Bottom);
            btnBack.Location = new Point(0, bottom + 10);
            btnBack.Size = new Size(cardWidth, 28);
            bodyPanel.Controls.Add(btnBack);
            bodyPanel.Height = btnBack.Bottom + 4;
        }

        segment.SelectedIndexChanged += (_, _) =>
        {
            _recTab = segment.SelectedIndex == 0 ? RecoveryTab.RemoteIt : RecoveryTab.MasterKey;
            RenderTabBody();
            if (Scale > 1.01f)
            {
                // the new tab is built in 96-dpi pixels
                bodyPanel.Height = (int)Math.Round(bodyPanel.Height * Scale);
                foreach (Control c in bodyPanel.Controls) c.Scale(new SizeF(Scale, Scale));
            }
            FitRecoveryWindow();
        };

        RenderTabBody();

        _contentPanel.Controls.AddRange(new Control[] {
            lblHeading, lblSub, segment, bodyPanel
        });
    }

    private Panel? _recBody;
    private ModernButton? _recBack;

    /// <summary>Sizes the window so that the back link (the last thing on the page) is fully inside it, measured from the real controls.</summary>
    private void FitRecoveryWindow()
    {
        if (_recBody is null || _recBack is null || _mode != FormMode.Recovery) return;
        int need = _headerPanel.Height + _recBody.Top + _recBack.Bottom + S(26);
        if (ClientSize.Height != need) ClientSize = new Size(ClientSize.Width, need);
    }

    private void BuildRemoteItBody(Panel body, int width)
    {
        _currentChallenge = AppAuthStore.GenerateBranchChallenge();

        var errBanner = CreateErrorBanner(new Point(0, 0), width);

        int curY = 36;
        var lblStep1 = new Label
        {
            Text = Loc.T("1. Call IT Admin at Main Office and read this code:"),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(0, curY),
            Size = new Size(width, 18)
        };

        // Big challenge box
        curY += 20;
        var chalBox = new Panel
        {
            Location = new Point(0, curY),
            Size = new Size(width, 50),
            BackColor = Color.FromArgb(239, 246, 255) // Blue-50
        };
        chalBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(191, 219, 254), 1.5f);
            using var path = CreateRoundedPath(new Rectangle(0, 0, chalBox.Width - 1, chalBox.Height - 1), 8);
            e.Graphics.DrawPath(pen, path);
        };

        var lblChalTag = new Label
        {
            Text = Loc.T("CHALLENGE CODE (Read to IT Admin)"),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(37, 99, 235),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 4),
            Size = new Size(width, 14),
            BackColor = Color.Transparent
        };
        var lblChal = new Label
        {
            Text = _currentChallenge,
            Font = new Font("Consolas", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(30, 58, 138),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 18),
            Size = new Size(width, 28),
            BackColor = Color.Transparent
        };
        chalBox.Controls.Add(lblChalTag);
        chalBox.Controls.Add(lblChal);

        // Step 2: Response Code
        curY += 56;
        var lblStep2 = new Label
        {
            Text = Loc.T("2. Response Code (from IT):"),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(0, curY),
            Size = new Size(width, 18)
        };
        curY += 18;
        var txtResp = new ModernTextBox
        {
            PlaceholderText = Loc.T("e.g. 123-456"),
            Location = new Point(0, curY),
            Size = new Size(width, 34)
        };

        // Step 3: New Password
        curY += 40;
        var lblStep3 = new Label
        {
            Text = Loc.T("3. New Password:"),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(0, curY),
            Size = new Size(width, 18)
        };
        curY += 18;
        var txtNewPass = new ModernTextBox
        {
            UseSystemPasswordChar = true,
            PlaceholderText = Loc.T("New Password"),
            Location = new Point(0, curY),
            Size = new Size(width, 34)
        };

        // Action button (Primary Blue)
        curY += 42;
        var btnReset = new ModernButton
        {
            Text = Loc.T("Verify & Reset Password"),
            Location = new Point(0, curY),
            Size = new Size(width, 38),
            NormalColor = Color.FromArgb(37, 99, 235), // Blue-600
            HoverColor = Color.FromArgb(29, 78, 216),  // Blue-700
            ForeColor = Color.White,
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        void DoOtpReset()
        {
            errBanner.Visible = false;
            string resp = txtResp.Text?.Trim() ?? string.Empty;
            string newPass = txtNewPass.Text ?? string.Empty;

            if (string.IsNullOrEmpty(resp))
            {
                ShowError(errBanner, Loc.T("Please enter the Admin Response Code from IT."));
                return;
            }
            if (string.IsNullOrEmpty(newPass))
            {
                ShowError(errBanner, Loc.T("Password cannot be empty."));
                return;
            }

            if (AppAuthStore.ResetWithOtpResponse(_currentChallenge, resp, newPass))
            {
                MessageBox.Show(this, Loc.T("Password reset successfully. You can now sign in."), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SwitchMode(FormMode.SignIn);
            }
            else
            {
                ShowError(errBanner, Loc.T("Invalid OTP Response Code."));
            }
        }

        btnReset.Click += (_, _) => DoOtpReset();
        txtResp.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoOtpReset(); };
        txtNewPass.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoOtpReset(); };

        body.Controls.AddRange(new Control[] {
            errBanner, lblStep1, chalBox, lblStep2, txtResp, lblStep3, txtNewPass, btnReset
        });
    }

    private void BuildMasterKeyBody(Panel body, int width)
    {
        var errBanner = CreateErrorBanner(new Point(0, 0), width);

        int curY = 36;
        var lblInstruct = new Label
        {
            Text = Loc.T("Enter the Master Recovery Key (e.g. CCTV-XXXX-XXXX):"),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(0, curY),
            Size = new Size(width, 18)
        };

        curY += 20;
        var txtKey = new ModernTextBox
        {
            PlaceholderText = "CCTV-XXXX-XXXX",
            Location = new Point(0, curY),
            Size = new Size(width, 34)
        };
        txtKey.InnerTextBox.Font = new Font("Consolas", 10.5f, FontStyle.Bold);

        curY += 44;
        var lblNewPass = new Label
        {
            Text = Loc.T("New Password:"),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(0, curY),
            Size = new Size(width, 18)
        };
        curY += 20;
        var txtNewPass = new ModernTextBox
        {
            UseSystemPasswordChar = true,
            PlaceholderText = Loc.T("New Password"),
            Location = new Point(0, curY),
            Size = new Size(width, 34)
        };

        curY += 44;
        var btnReset = new ModernButton
        {
            Text = Loc.T("Reset Password"),
            Location = new Point(0, curY),
            Size = new Size(width, 38),
            NormalColor = Color.FromArgb(37, 99, 235), // Blue-600
            HoverColor = Color.FromArgb(29, 78, 216),  // Blue-700
            ForeColor = Color.White,
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        void DoKeyReset()
        {
            errBanner.Visible = false;
            string key = txtKey.Text?.Trim() ?? string.Empty;
            string newPass = txtNewPass.Text ?? string.Empty;

            if (string.IsNullOrEmpty(key))
            {
                ShowError(errBanner, Loc.T("Please enter the Master Recovery Key."));
                return;
            }
            if (string.IsNullOrEmpty(newPass))
            {
                ShowError(errBanner, Loc.T("Password cannot be empty."));
                return;
            }

            if (AppAuthStore.ResetWithRecoveryKey(key, newPass))
            {
                MessageBox.Show(this, Loc.T("Password reset successfully. You can now sign in."), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                SwitchMode(FormMode.SignIn);
            }
            else
            {
                ShowError(errBanner, Loc.T("Invalid Master Recovery Key."));
            }
        }

        btnReset.Click += (_, _) => DoKeyReset();
        txtKey.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoKeyReset(); };
        txtNewPass.InnerTextBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) DoKeyReset(); };

        body.Controls.AddRange(new Control[] {
            errBanner, lblInstruct, txtKey, lblNewPass, txtNewPass, btnReset
        });
    }

    // ------------------------------------------------------------------ HELPERS
    private static Panel CreateErrorBanner(Point location, int width)
    {
        var p = new Panel
        {
            Location = location,
            Size = new Size(width, 32),
            BackColor = Color.FromArgb(254, 242, 242), // Red-50
            Visible = false
        };
        p.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(252, 165, 165), 1); // Red-300
            using var path = CreateRoundedPath(new Rectangle(0, 0, p.Width - 1, p.Height - 1), 6);
            e.Graphics.DrawPath(pen, path);
        };

        var lbl = new Label
        {
            Name = "ErrorMessage",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(185, 28, 28), // Red-700
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };
        p.Controls.Add(lbl);
        return p;
    }

    private static void ShowError(Panel banner, string message)
    {
        if (banner.Controls["ErrorMessage"] is Label lbl)
        {
            lbl.Text = $"⚠️  {message}";
        }
        banner.Visible = true;
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
