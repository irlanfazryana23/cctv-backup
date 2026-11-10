using CctvBackup.Core.Branches;
using CctvBackup.Core.Localization;

namespace CctvBackup.App;

/// <summary>
/// Shows the branches found in the CMS file so the user can tick the ones to bring into this program.
/// The CMS passwords cannot be imported, so an optional password for all imported branches can be typed here.
/// Result: OK = import the ticked branches, Retry = the user wants to pick another CMS file.
/// </summary>
internal sealed class ImportCmsDialog : Form
{
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true,
        HideSelection = false, MultiSelect = false, AccessibleName = Loc.T("Branches found in the CMS"),
    };
    private readonly Label _count = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 3, 3) };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, AccessibleName = Loc.T("Password for the imported branches (optional)") };
    private readonly CheckBox _show = new() { Text = Loc.T("Show password"), AutoSize = true };
    private readonly Button _import = new() { Text = Loc.T("&Import"), AutoSize = true, MinimumSize = new Size(100, 32) };
    private readonly Button _cancel = new() { Text = Loc.T("Cancel"), AutoSize = true, MinimumSize = new Size(100, 32), DialogResult = DialogResult.Cancel };
    private readonly Button _other = new() { Text = Loc.T("Choose another file..."), AutoSize = true, MinimumSize = new Size(100, 32) };
    private bool _loading;

    /// <summary>The branches the user ticked (valid when the dialog returned OK).</summary>
    public List<Branch> SelectedBranches { get; private set; } = new();

    /// <summary>Password to save for every imported branch that has a user name. Empty = none.</summary>
    public string Password => _password.Text;

    public ImportCmsDialog(string path, ImportPlan plan)
    {

        Text = Loc.T("Import branches from the CMS");
        Icon = AppIcon.Get();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(700, 480);
        Font = SystemFonts.MessageBoxFont ?? Font;
        AcceptButton = _import;
        CancelButton = _cancel;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
        Controls.Add(root);
        void AddRow(Control c, SizeType type = SizeType.AutoSize, float height = 0)
        {
            root.RowStyles.Add(new RowStyle(type, height));
            root.Controls.Add(c, 0, root.RowStyles.Count - 1);
        }

        AddRow(new Label { Text = Loc.F("New branches found in {0}:", path), AutoSize = true, MaximumSize = new Size(780, 0), Margin = new Padding(0, 0, 0, 6) });

        _list.Columns.Add(Loc.T("Branch"), 180);
        _list.Columns.Add(Loc.T("Connection"), 230);
        _list.Columns.Add(Loc.T("Cameras"), 70);
        _list.Columns.Add(Loc.T("User name"), 110);
        _list.Columns.Add(Loc.T("Note"), 190);
        AddRow(_list, SizeType.Percent, 100);

        var pick = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        var all = new Button { Text = Loc.T("All"), AutoSize = true };
        var none = new Button { Text = Loc.T("None"), AutoSize = true };
        all.Click += (_, _) => SetAll(true);
        none.Click += (_, _) => SetAll(false);
        pick.Controls.AddRange(new Control[] { all, none, _count });
        AddRow(pick);

        string left = BuildLeftOutText(plan);
        if (left.Length > 0)
        {
            AddRow(new TextBox
            {
                Text = left, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                Height = 70, BackColor = SystemColors.Control, AccessibleName = Loc.T("Branches that are not imported"),
            });
        }

        var pw = new GroupBox { Text = Loc.T("Password for the imported branches (optional)"), Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var pwLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        pwLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pwLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pwLayout.Controls.Add(_password, 0, 0);
        pwLayout.Controls.Add(_show, 1, 0);
        var hint = new Label
        {
            Text = Loc.T("The CMS keeps its passwords in a form this program does not read. If these branches share one password, type it here; otherwise leave it empty and enter each one later with Edit branch..."),
            ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(760, 0), Margin = new Padding(0, 6, 0, 0),
        };
        pwLayout.Controls.Add(hint, 0, 1);
        pwLayout.SetColumnSpan(hint, 2);
        pw.Controls.Add(pwLayout);
        AddRow(pw);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(_other, 0, 0);
        buttons.Controls.Add(_import, 2, 0);
        buttons.Controls.Add(_cancel, 3, 0);
        AddRow(buttons);

        _loading = true;
        foreach (var c in plan.Candidates)
        {
            var b = c.Branch;
            var item = new ListViewItem(b.Name) { Checked = c.DefaultChecked };
            item.SubItems.Add(b.Mode == LoginMode.Ip ? Loc.F("IP: {0}", $"{b.Address}:{b.Port}") : Loc.F("Cloud: {0}", b.CloudId));
            item.SubItems.Add(b.Cameras.Count > 0 ? b.Cameras.Count.ToString() : "-");
            item.SubItems.Add(b.Username);
            item.SubItems.Add(c.Note ?? string.Empty);
            item.Tag = b;
            _list.Items.Add(item);
        }
        _loading = false;
        UpdateCount();

        _list.ItemChecked += (_, _) => { if (!_loading) UpdateCount(); };
        _show.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_show.Checked;
        _import.Click += (_, _) =>
        {
            SelectedBranches = _list.CheckedItems.Cast<ListViewItem>().Select(i => (Branch)i.Tag!).ToList();
            DialogResult = DialogResult.OK;
            Close();
        };
        _other.Click += (_, _) => { DialogResult = DialogResult.Retry; Close(); };
    }

    private void SetAll(bool value)
    {
        _loading = true;
        foreach (ListViewItem i in _list.Items) i.Checked = value;
        _loading = false;
        UpdateCount();
    }

    private void UpdateCount()
    {
        int ticked = _list.CheckedItems.Count;
        _count.Text = Loc.F("{0} of {1} selected", ticked, _list.Items.Count);
        _import.Enabled = ticked > 0;
    }

    /// <summary>The "not imported" explanation: same-name branches, and the ones the program cannot use.</summary>
    internal static string BuildLeftOutText(ImportPlan plan)
    {
        var lines = new List<string>();
        if (plan.AlreadyPresent.Count > 0)
            lines.Add(Loc.F("Already in this program (not listed): {0}", string.Join(", ", plan.AlreadyPresent)));
        foreach (var s in plan.Unsupported)
            lines.Add($"• {s.Name}: {s.Reason}");
        return string.Join(Environment.NewLine, lines);
    }
}
