using System.Drawing.Drawing2D;
using CctvBackup.Core.Auth;
using CctvBackup.Core.Localization;

namespace CctvBackup.App;

internal sealed class AccountForm : Form
{
    public bool RequestLock { get; private set; }

    public AccountForm()
    {
        Text = Loc.T("Account Settings");
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 480);
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        BuildUi();
        Ui.EnableScaling(this);
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 68 };
        header.Paint += (_, e) =>
        {
            using var brush = new LinearGradientBrush(header.ClientRectangle,
                Color.FromArgb(15, 23, 42),
                Color.FromArgb(30, 58, 138),
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, header.ClientRectangle);

            using var pen = new Pen(Color.FromArgb(51, 65, 85), 1);
            e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var lblTitle = new Label
        {
            Text = Loc.T("Account & Security"),
            UseMnemonic = false,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(20, 14),
            BackColor = Color.Transparent
        };
        var lblUser = new Label
        {
            Text = $"👤 {Loc.T("Signed in as")}: {AppAuthStore.GetUsername() ?? "admin"}",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(203, 213, 225),
            AutoSize = true,
            Location = new Point(22, 38),
            BackColor = Color.Transparent
        };
        header.Controls.Add(lblTitle);
        header.Controls.Add(lblUser);
        Controls.Add(header);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 16, 28, 16), BackColor = Color.FromArgb(248, 250, 252) };
        Controls.Add(body);
        header.SendToBack();   // the Top strip must be laid out before the Fill area, or it ends up hidden under it

        int contentWidth = 380;
        int startX = (body.ClientSize.Width - contentWidth) / 2;

        var lblChange = new Label
        {
            Text = Loc.T("Change Password"),
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            AutoSize = true,
            Location = new Point(startX, 6)
        };

        int curY = 38;
        var lblCurr = new Label { Text = Loc.T("Current Password:"), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85), AutoSize = true, Location = new Point(startX, curY) };
        curY += 20;
        var txtCurr = new ModernTextBox { UseSystemPasswordChar = true, Location = new Point(startX, curY), Size = new Size(contentWidth, 34) };

        curY += 42;
        var lblNew = new Label { Text = Loc.T("New Password:"), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85), AutoSize = true, Location = new Point(startX, curY) };
        curY += 20;
        var txtNew = new ModernTextBox { UseSystemPasswordChar = true, Location = new Point(startX, curY), Size = new Size(contentWidth, 34) };

        curY += 42;
        var lblConf = new Label { Text = Loc.T("Confirm New Password:"), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85), AutoSize = true, Location = new Point(startX, curY) };
        curY += 20;
        var txtConf = new ModernTextBox { UseSystemPasswordChar = true, Location = new Point(startX, curY), Size = new Size(contentWidth, 34) };

        curY += 40;
        var lblError = new Label { ForeColor = Color.FromArgb(220, 38, 38), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), AutoSize = true, Location = new Point(startX, curY), Visible = false };

        curY += 24;
        var btnUpdate = new ModernButton
        {
            Text = Loc.T("Update Password"),
            Location = new Point(startX, curY),
            Size = new Size(contentWidth, 38),
            NormalColor = Color.FromArgb(37, 99, 235), // Blue-600
            HoverColor = Color.FromArgb(29, 78, 216),  // Blue-700
            ForeColor = Color.White,
            BorderColor = Color.Transparent,
            BorderRadius = 6,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };

        btnUpdate.Click += (_, _) =>
        {
            lblError.Visible = false;
            string curr = txtCurr.Text?.Trim() ?? string.Empty;
            string nw = txtNew.Text ?? string.Empty;
            string conf = txtConf.Text ?? string.Empty;

            if (string.IsNullOrEmpty(curr) || string.IsNullOrEmpty(nw))
            {
                lblError.Text = "⚠️ " + Loc.T("Please fill in all password fields.");
                lblError.Visible = true;
                return;
            }
            if (nw != conf)
            {
                lblError.Text = "⚠️ " + Loc.T("Passwords do not match.");
                lblError.Visible = true;
                return;
            }

            if (AppAuthStore.ChangePassword(curr, nw))
            {
                MessageBox.Show(this, Loc.T("Password changed successfully."), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                txtCurr.Text = string.Empty;
                txtNew.Text = string.Empty;
                txtConf.Text = string.Empty;
            }
            else
            {
                lblError.Text = "⚠️ " + Loc.T("Current password is incorrect.");
                lblError.Visible = true;
            }
        };

        curY += 48;
        var divider = new Panel
        {
            Location = new Point(startX, curY),
            Size = new Size(contentWidth, 1),
            BackColor = Color.FromArgb(226, 232, 240)
        };

        curY += 12;
        var btnLock = new ModernButton
        {
            Text = "🔒 " + Loc.T("Lock App Now"),
            Location = new Point(startX, curY),
            Size = new Size(contentWidth, 36),
            NormalColor = Color.White,
            HoverColor = Color.FromArgb(241, 245, 249),
            ForeColor = Color.FromArgb(15, 23, 42),
            BorderColor = Color.FromArgb(203, 213, 225),
            BorderRadius = 6,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        btnLock.Click += (_, _) =>
        {
            RequestLock = true;
            Close();
        };

        body.Controls.AddRange(new Control[] {
            lblChange, lblCurr, txtCurr, lblNew, txtNew, lblConf, txtConf, lblError, btnUpdate, divider, btnLock
        });
    }
}
