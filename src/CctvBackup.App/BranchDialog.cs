using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;

namespace CctvBackup.App;

/// <summary>
/// Add a branch, or edit one: name, how to reach the DVR (cloud Serial ID, or IP address) and login.
/// The number of cameras is never typed: it is read from the DVR when the branch is saved
/// (or when Test connection succeeds).
/// </summary>
internal sealed class BranchDialog : Form
{
    private readonly IDvrClient _client;
    private readonly IReadOnlyList<string> _existingNames;
    private readonly Branch? _existing;
    private readonly Credential? _saved;

    private readonly TextBox _name = new() { Dock = DockStyle.Fill, AccessibleName = Loc.T("Branch name") };
    private readonly RadioButton _modeCloud = new() { Text = Loc.T("&Cloud (Serial ID)"), AutoSize = true, Checked = true, Margin = new Padding(0, 6, 18, 0) };
    private readonly RadioButton _modeIp = new() { Text = Loc.T("&IP address"), AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
    private readonly TextBox _serial = new() { Dock = DockStyle.Fill, AccessibleName = Loc.T("Serial ID"), MaxLength = 40 };
    private readonly TextBox _address = new() { Dock = DockStyle.Fill, AccessibleName = Loc.T("IP address or domain"), MaxLength = 253 };
    private readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = Branch.DefaultPort, Width = 90, AccessibleName = Loc.T("Port") };
    private readonly TextBox _user = new() { Dock = DockStyle.Fill, AccessibleName = Loc.T("User name") };
    private readonly TextBox _pass = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, AccessibleName = Loc.T("Password") };
    private readonly CheckBox _show = new() { Text = Loc.T("Show password"), AutoSize = true };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.TopLeft };
    private readonly Button _test = new() { Text = Loc.T("&Test connection"), AutoSize = true };
    private readonly Button _save = new() { Text = Loc.T("&Save"), AutoSize = true, MinimumSize = new Size(90, 32) };
    private readonly Button _cancel = new() { Text = Loc.T("Cancel"), AutoSize = true, MinimumSize = new Size(90, 32), DialogResult = DialogResult.Cancel };
    private CancellationTokenSource? _cts;
    private TableLayoutPanel _layout = null!;

    // Rows shown only for one login type: (row index, normal height, controls in that row).
    private readonly List<(int Row, int Height, Control[] Controls)> _cloudRows = new();
    private readonly List<(int Row, int Height, Control[] Controls)> _ipRows = new();

    // The result of the last successful connection made from this dialog, so Save does not connect twice.
    private (string Key, DvrInfo Info)? _lastDetection;
    private bool _busy;

    /// <summary>The branch and login to save (valid only when the dialog returned OK). An empty password means "keep the saved one".</summary>
    public Branch? ResultBranch { get; private set; }
    public Credential? ResultCredential { get; private set; }

    /// <param name="existing">The branch to edit, or null to add a new one.</param>
    /// <param name="savedCredential">Its saved login (edit only).</param>
    public BranchDialog(IDvrClient client, IReadOnlyList<string> existingNames, Branch? existing, Credential? savedCredential)
    {
        _client = client;
        _existingNames = existingNames;
        _existing = existing;
        _saved = savedCredential;

        Text = existing is null ? Loc.T("Add branch") : Loc.T("Edit branch");
        Icon = AppIcon.Get();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 420);
        Font = SystemFonts.MessageBoxFont ?? Font;
        CancelButton = _cancel;
        AcceptButton = _save;

        _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14) };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_layout);

        // Every control gets an explicit cell: a table that places controls automatically skips hidden ones,
        // which would shift the rows below when a login type hides its fields.
        (int Row, Label Label) Row(string label, Control c, int height = 32)
        {
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            int r = _layout.RowStyles.Count - 1;
            var l = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = true };
            _layout.Controls.Add(l, 0, r);
            _layout.Controls.Add(c, 1, r);
            return (r, l);
        }
        int Full(Control c, int height)
        {
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            int r = _layout.RowStyles.Count - 1;
            _layout.Controls.Add(c, 0, r);
            _layout.SetColumnSpan(c, 2);
            return r;
        }
        Label Hint(string text) => new() { Text = text, ForeColor = SystemColors.GrayText, Dock = DockStyle.Fill };

        Row(Loc.T("Branch &name"), _name);

        var modePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        modePanel.Controls.Add(_modeCloud);
        modePanel.Controls.Add(_modeIp);
        Row(Loc.T("Login t&ype"), modePanel);

        // --- cloud ---
        var serialRow = Row(Loc.T("&Serial ID"), _serial);
        var serialHint = Hint(Loc.T("The cloud ID shown in the DVR (the \"Serial ID\" in the CMS)."));
        var serialHintRow = Full(serialHint, 24);
        _cloudRows.Add((serialRow.Row, 32, new Control[] { serialRow.Label, _serial }));
        _cloudRows.Add((serialHintRow, 24, new Control[] { serialHint }));

        // --- IP ---
        var addrRow = Row(Loc.T("IP &address"), _address);
        var portPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        portPanel.Controls.Add(_port);
        var portRow = Row(Loc.T("&Port"), portPanel);
        var ipHint = Hint(Loc.T("The DVR's local address and Media Port (usually 34567). This PC must be able to reach it: same network, VPN or leased line."));
        var ipHintRow = Full(ipHint, 40);
        _ipRows.Add((addrRow.Row, 32, new Control[] { addrRow.Label, _address }));
        _ipRows.Add((portRow.Row, 32, new Control[] { portRow.Label, portPanel }));
        _ipRows.Add((ipHintRow, 40, new Control[] { ipHint }));

        Row(Loc.T("&User name"), _user);
        Row(Loc.T("&Password"), _pass);

        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        _layout.Controls.Add(_show, 1, _layout.RowStyles.Count - 1);

        var testRow = new FlowLayoutPanel { Dock = DockStyle.Fill };
        testRow.Controls.Add(_test);
        Full(testRow, 40);

        Full(_status, 60);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_save);
        Full(buttons, 42);

        _layout.RowCount = _layout.RowStyles.Count;

        if (existing is not null)
        {
            _name.Text = existing.Name;
            _modeIp.Checked = existing.Mode == LoginMode.Ip;
            _modeCloud.Checked = existing.Mode == LoginMode.Cloud;
            _serial.Text = existing.CloudId;
            _address.Text = existing.Address;
            _port.Value = Math.Clamp(existing.Port, (int)_port.Minimum, (int)_port.Maximum);
            _user.Text = savedCredential?.Username ?? existing.Username;
            Say(savedCredential is null
                ? Loc.T("No password is saved for this branch. Enter it.")
                : Loc.T("Leave the password empty to keep the saved one."));
        }
        else
        {
            Say(Loc.T("The cameras of the DVR are detected automatically when you save."));
        }

        ApplyMode();
        _modeCloud.CheckedChanged += (_, _) => ApplyMode();
        _modeIp.CheckedChanged += (_, _) => ApplyMode();
        _show.CheckedChanged += (_, _) => _pass.UseSystemPasswordChar = !_show.Checked;
        _test.Click += async (_, _) => await TestAsync();
        _save.Click += async (_, _) => await SaveAsync();
        FormClosing += (_, e) =>
        {
            if (_busy) { e.Cancel = true; _cts?.Cancel(); }   // wait for the connection attempt to stop
        };
        Shown += (_, _) => _name.Focus();
    }

    private LoginMode Mode => _modeIp.Checked ? LoginMode.Ip : LoginMode.Cloud;

    /// <summary>Shows only the fields that belong to the chosen login type.</summary>
    private void ApplyMode()
    {
        _layout.SuspendLayout();
        void Show(List<(int Row, int Height, Control[] Controls)> rows, bool visible)
        {
            foreach (var (row, height, controls) in rows)
            {
                _layout.RowStyles[row].Height = visible ? height : 0;
                foreach (var c in controls) c.Visible = visible;
            }
        }
        Show(_cloudRows, Mode == LoginMode.Cloud);
        Show(_ipRows, Mode == LoginMode.Ip);
        _layout.ResumeLayout();
    }

    private string Password => _pass.Text.Length > 0 ? _pass.Text : _saved?.Password ?? string.Empty;

    private Branch Candidate(int cameras) => new(
        _name.Text.Trim(), string.Empty,
        Mode == LoginMode.Cloud ? _serial.Text.Trim() : string.Empty,
        Mode == LoginMode.Ip ? (int)_port.Value : Branch.DefaultPort,
        _user.Text.Trim(), Camera.Range(_name.Text.Trim(), cameras),
        Mode, Mode == LoginMode.Ip ? _address.Text.Trim() : string.Empty);

    private string Key => $"{Candidate(0).ConnectionKey}|{_user.Text.Trim()}|{Password}";

    private List<string> Problems() => BranchValidator.Validate(
        _name.Text, Mode, _serial.Text, _address.Text, (int)_port.Value, _user.Text, _pass.Text,
        _existingNames, _existing?.Name, passwordRequired: _saved is null);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (Control c in new Control[] { _name, _modeCloud, _modeIp, _serial, _address, _port, _user, _pass, _show, _test, _save })
            c.Enabled = !busy;
    }

    /// <summary>Logs in and reads the DVR's details. Returns null (after showing why) when it fails.</summary>
    private async Task<DvrInfo?> DetectAsync()
    {
        if (_lastDetection is { } cached && cached.Key == Key) return cached.Info;

        var probe = Candidate(0);
        _cts = new CancellationTokenSource();
        SetBusy(true);
        Say(Mode == LoginMode.Ip
            ? Loc.F("Connecting to {0}...", probe.ConnectionText)
            : Loc.T("Connecting through the cloud... this can take up to a minute."));
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            using var conn = await _client.ConnectAsync(probe, new Credential(probe.Username, Password), _cts.Token);
            _lastDetection = (Key, conn.Info);
            Say(conn.Info.ChannelCount > 0
                ? Loc.F("Online ({0} s). {1} cameras found", sw.Elapsed.TotalSeconds.ToString("0.#"), conn.Info.ChannelCount) +
                  (string.IsNullOrEmpty(conn.Info.Model) ? "." : Loc.F(" ({0}).", conn.Info.Model))
                : Loc.T("Online, but the DVR did not say how many cameras it has."));
            return conn.Info;
        }
        catch (OperationCanceledException)
        {
            Say(Loc.T("Cancelled."));
            return null;
        }
        catch (DvrException ex)
        {
            Say(ex.Message + Loc.T("\nA wrong password locks the account after a few tries, so check it before trying again."), error: true);
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task TestAsync()
    {
        bool target = Mode == LoginMode.Cloud ? _serial.Text.Trim().Length > 0 : _address.Text.Trim().Length > 0;
        if (!target || _user.Text.Trim().Length == 0 || Password.Length == 0)
        {
            Say(Mode == LoginMode.Cloud
                ? Loc.T("Enter the Serial ID, user name and password first.")
                : Loc.T("Enter the IP address, user name and password first."), error: true);
            return;
        }

        // The branch name does not matter for a test, so check the connection fields only.
        var problems = BranchValidator.Validate("test", Mode, _serial.Text, _address.Text, (int)_port.Value, _user.Text, Password,
            Array.Empty<string>(), null, passwordRequired: false);
        if (problems.Count > 0) { Say(string.Join("\n", problems.Select(p => "• " + p)), error: true); return; }

        _lastDetection = null;   // always make a fresh attempt when the user presses the button
        await DetectAsync();
    }

    private async Task SaveAsync()
    {
        var problems = Problems();
        if (problems.Count > 0) { Say(string.Join("\n", problems.Select(p => "• " + p)), error: true); return; }

        string name = _name.Text.Trim();
        string user = _user.Text.Trim();
        var candidate = Candidate(0);

        // Keep the cameras we already know when nothing that matters for the connection was changed.
        bool sameConnection = _existing is not null && _existing.CamerasDetected
            && _existing.ConnectionKey == candidate.ConnectionKey
            && _saved is not null && _saved.Username == user && _saved.Password == Password;

        int cameras;
        if (sameConnection)
        {
            cameras = _existing!.Cameras.Count;
        }
        else
        {
            var info = await DetectAsync();
            if (_busy || IsDisposed) return;

            if (info is not null && info.ChannelCount > 0)
            {
                cameras = info.ChannelCount;
            }
            else
            {
                string why = info is null ? Loc.T("The DVR could not be reached.") : Loc.T("The DVR did not report its cameras.");
                var answer = MessageBox.Show(this,
                    Loc.F("{0}\n\nSave the branch anyway? Its cameras are detected later: select it in the main window and press Test connection.", why),
                    Loc.T("Save without detecting cameras?"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;

                bool sameDvr = _existing is not null && _existing.ConnectionKey == candidate.ConnectionKey;
                cameras = sameDvr ? _existing!.Cameras.Count : 0;
            }
        }

        ResultBranch = Candidate(cameras) with { Name = name, Username = user };
        ResultCredential = new Credential(user, _pass.Text);   // empty password = keep saved (resolved by the caller)
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Say(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.Firebrick : SystemColors.ControlText;
    }
}
