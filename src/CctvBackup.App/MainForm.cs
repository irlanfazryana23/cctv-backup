using System.ComponentModel;
using System.Diagnostics;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Playback;
using CctvBackup.Core.Sdk;
using CctvBackup.Core.Settings;
using CctvBackup.Core.Workflow;

namespace CctvBackup.App;

/// <summary>The one screen: pick branches and cameras, pick the period, pick the disk, find the recordings, tick, download.</summary>
internal sealed class MainForm : Form
{
    private readonly FileLog _log;
    private readonly AppSettings _settings;
    private readonly BranchBook _book = new(AppPaths.BranchesFile);
    private readonly CredentialStore _store = new(AppPaths.CredentialsFile);
    private readonly XmDvrClient _client;
    private readonly string? _sdkDir;

    private GroupBox _branchGroup = null!;
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private readonly Dictionary<string, HashSet<int>> _cameraChoice = new(StringComparer.OrdinalIgnoreCase);
    private readonly BindingList<DownloadJob> _jobs = new();
    private CancellationTokenSource? _cts;
    private bool _running, _closeWhenDone, _loadingCameras, _fillingBranches, _settingLanguage;

    // The files of the current download run, for the overall speed and time left.
    private List<DownloadJob>? _runJobs;
    private DateTime _runStartedUtc, _runFinishedUtc;

    // The list the user is choosing from. It goes stale when branch, camera, period or folder change.
    private IReadOnlyList<BranchPlan>? _plans;
    private string _planDestination = string.Empty;
    private bool _listStale;

    // Everything that shows fixed text registers here, so the language can be switched while the program runs.
    private readonly List<Action> _relabel = new();

    // ---- controls ----
    private readonly CheckedListBox _branchList = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly CheckedListBox _cameraList = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm", Dock = DockStyle.Fill };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm", Dock = DockStyle.Fill };
    private readonly TextBox _dest = new() { Dock = DockStyle.Fill };
    private readonly Button _find = new() { AutoSize = true, MinimumSize = new Size(140, 34) };
    private readonly Button _start = new() { AutoSize = true, MinimumSize = new Size(150, 34), Enabled = false };
    private readonly Button _tickAll = new() { AutoSize = true };
    private readonly Button _untickAll = new() { AutoSize = true };
    private readonly Button _tickRows = new() { AutoSize = true };
    private readonly Button _untickRows = new() { AutoSize = true };
    private readonly Label _selectionText = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 8, 3, 3) };
    private readonly Button _cancel = new() { AutoSize = true, MinimumSize = new Size(90, 34), Enabled = false };
    private readonly Button _openFolder = new() { AutoSize = true, MinimumSize = new Size(100, 34) };
    private readonly Button _playBtn = new() { AutoSize = true, MinimumSize = new Size(100, 34) };
    private readonly ProgressBar _overall = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly Label _overallText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly DataGridView _grid = new();
    private readonly TextBox _logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 500 };
    private readonly Button _testBtn = new() { AutoSize = true };
    private readonly Button _addBtn = new() { AutoSize = true };
    private readonly Button _editBtn = new() { AutoSize = true };
    private readonly Button _removeBtn = new() { AutoSize = true };
    private readonly Button _importBtn = new() { AutoSize = true };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, AccessibleName = "Language / Bahasa" };

    // the "what to do now" banner
    private static readonly Color Accent = Color.FromArgb(31, 111, 235);
    private readonly TableLayoutPanel _guide = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 1, Padding = new Padding(18, 8, 12, 8), Margin = new Padding(0, 6, 0, 6) };
    private readonly Label _guideTitle = new() { AutoSize = true, Margin = new Padding(0) };
    private readonly Label _guideText = new() { AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
    private Color _guideAccent = Accent;
    private GroupBox _cameraGroup = null!;
    private readonly Label _emptyHint = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText, BackColor = SystemColors.Window };
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 350, ReshowDelay = 150 };

    public MainForm(FileLog log)
    {
        _log = log;
        _settings = AppSettings.Load(AppPaths.SettingsFile);
        Loc.Set(Loc.Resolve(_settings.Language));
        _sdkDir = SdkLocator.Find(_settings.SdkDirectory);
        _client = new XmDvrClient(_sdkDir ?? SdkLocator.BundledDirectory);
        _client.Notice += line => { if (IsHandleCreated) BeginInvoke(() => Append(line, alreadyStamped: true)); };

        Text = "CCTV Backup";
        Icon = AppIcon.Get();
        MinimumSize = new Size(1080, 780);
        Size = new Size(1240, 880);
        StartPosition = FormStartPosition.CenterScreen;
        var baseFont = SystemFonts.MessageBoxFont ?? Font;
        Font = new Font(baseFont.FontFamily, 10f);      // a little larger than the Windows default: easier to read

        BuildLayout();
        SetupTooltips();
        WireEvents();
        ApplyLanguage();
        Load += (_, _) => OnFirstShow();
    }

    // ------------------------------------------------------------------ language

    /// <summary>Shows <paramref name="english"/> on a control now and every time the language changes.</summary>
    private T Bind<T>(T control, string english) where T : Control
    {
        _relabel.Add(() => control.Text = Loc.T(english));
        return control;
    }

    private T BindAccessible<T>(T control, string english) where T : Control
    {
        _relabel.Add(() => control.AccessibleName = Loc.T(english));
        return control;
    }

    /// <summary>Re-writes every fixed text in the current language. Dynamic text is refreshed at the end.</summary>
    private void ApplyLanguage()
    {
        foreach (var apply in _relabel) apply();

        // text that depends on the current state
        PositionEmptyHint();
        UpdateBranchGroupTitle();
        ShowCamerasOfSelectedBranch();
        UpdateSelectionSummary();
        RefreshProgress();
        _grid.Invalidate();
    }

    private void SetupLanguageBox()
    {
        _settingLanguage = true;
        _language.Items.Clear();
        _language.Items.Add("English");
        _language.Items.Add("Bahasa Indonesia");
        _language.SelectedIndex = Loc.IsIndonesian ? 1 : 0;
        _settingLanguage = false;

        _language.SelectedIndexChanged += (_, _) =>
        {
            if (_settingLanguage) return;
            string code = _language.SelectedIndex == 1 ? Loc.Indonesian : Loc.English;
            _settings.Language = code;
            _settings.Save(AppPaths.SettingsFile);
            Loc.Set(code);
            ApplyLanguage();
        };
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // toolbar
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // "what to do now" banner
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 235));     // selections
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // destination
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // run row
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // tick buttons
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));       // grid
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));       // log
        Controls.Add(root);

        // toolbar: branch management on the left, language on the right
        Bind(_addBtn, "&Add branch...");
        Bind(_editBtn, "&Edit branch...");
        Bind(_removeBtn, "Re&move branch");
        Bind(_testBtn, "&Test connection");
        Bind(_importBtn, "&Import from CMS...");
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var branchTools = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        branchTools.Controls.Add(Bind(new Label { AutoSize = true, Margin = new Padding(3, 7, 8, 3), Font = new Font(Font, FontStyle.Bold) }, "Branches:"));
        branchTools.Controls.AddRange(new Control[] { _addBtn, _editBtn, _removeBtn, _testBtn, _importBtn });
        toolbar.Controls.Add(branchTools, 0, 0);
        var langBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Anchor = AnchorStyles.Right };
        langBox.Controls.Add(new Label { Text = "Language / Bahasa:", AutoSize = true, Margin = new Padding(3, 5, 4, 0) });
        langBox.Controls.Add(_language);
        toolbar.Controls.Add(langBox, 1, 0);
        SetupLanguageBox();
        root.Controls.Add(toolbar);

        BuildGuide();
        root.Controls.Add(_guide);
        // selections
        var sel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        sel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        sel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        sel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        BindAccessible(_branchList, "Branches to back up");
        _branchGroup = Group("1. Branches (tick the ones to back up)", _branchList,
            () => SetAll(_branchList, true), () => SetAll(_branchList, false), bindTitle: false);
        sel.Controls.Add(_branchGroup);

        BindAccessible(_cameraList, "Cameras");
        _cameraGroup = Group("2. Cameras", _cameraList, () => SetAll(_cameraList, true), () => SetAll(_cameraList, false), bindTitle: false);
        sel.Controls.Add(_cameraGroup);

        var period = Bind(new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(8) }, "3. Period (the DVR's own clock)");
        var pl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        pl.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pl.Controls.Add(Bind(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, "&From"), 0, 0);
        pl.Controls.Add(BindAccessible(_from, "Start of period"), 1, 0);
        pl.Controls.Add(Bind(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, "&To"), 0, 1);
        pl.Controls.Add(BindAccessible(_to, "End of period"), 1, 1);
        var quick = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        var yesterday = Bind(new Button { AutoSize = true }, "Yesterday");
        var today = Bind(new Button { AutoSize = true }, "Today so far");
        var week = Bind(new Button { AutoSize = true }, "Last 7 days");
        yesterday.Click += (_, _) => { _from.Value = DateTime.Today.AddDays(-1); _to.Value = DateTime.Today; };
        today.Click += (_, _) => { _from.Value = DateTime.Today; _to.Value = DateTime.Now; };
        week.Click += (_, _) => { _from.Value = DateTime.Today.AddDays(-7); _to.Value = DateTime.Today; };
        quick.Controls.AddRange(new Control[] { yesterday, today, week });
        pl.Controls.Add(quick, 0, 2);
        pl.SetColumnSpan(quick, 2);
        period.Controls.Add(pl);
        sel.Controls.Add(period);
        root.Controls.Add(sel);

        _from.Value = DateTime.Today.AddDays(-1);
        _to.Value = DateTime.Today;

        // destination
        var destRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        destRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destRow.Controls.Add(Bind(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 8, 3) }, "4. &Save to"), 0, 0);
        destRow.Controls.Add(BindAccessible(_dest, "Save folder"), 1, 0);
        var browse = Bind(new Button { AutoSize = true }, "&Browse...");
        browse.Click += (_, _) => BrowseDestination();
        destRow.Controls.Add(browse, 2, 0);
        root.Controls.Add(destRow);

        // run row: step 5 and 6 are the two big buttons; Cancel next to them; progress in the middle; folder / play on the right
        Bind(_find, "5. &Find recordings");
        Bind(_start, "6. &Download selected");
        Bind(_cancel, "&Cancel");
        Bind(_openFolder, "&Open folder");
        Bind(_playBtn, "&Play video...");
        BindAccessible(_overall, "Overall progress");
        _find.MinimumSize = new Size(190, 42);
        _start.MinimumSize = new Size(200, 42);
        _cancel.MinimumSize = new Size(100, 42);
        _openFolder.MinimumSize = new Size(110, 42);
        _playBtn.MinimumSize = new Size(120, 42);
        StylePrimary(_find);
        StylePrimary(_start);
        var run = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
        run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        run.Controls.Add(_find, 0, 0);
        run.Controls.Add(_start, 1, 0);
        run.Controls.Add(_cancel, 2, 0);
        var progressHost = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Height = 46, Margin = new Padding(12, 0, 12, 0) };
        progressHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        progressHost.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        progressHost.Controls.Add(_overallText, 0, 0);
        progressHost.Controls.Add(_overall, 0, 1);
        run.Controls.Add(progressHost, 3, 0);
        run.Controls.Add(_openFolder, 4, 0);
        run.Controls.Add(_playBtn, 5, 0);
        root.Controls.Add(run);
        // tick buttons + "selected N of M" summary
        Bind(BindAccessible(_tickAll, "Tick all recordings"), "Tick all");
        Bind(BindAccessible(_untickAll, "Untick all recordings"), "Untick all");
        Bind(BindAccessible(_tickRows, "Tick the highlighted rows"), "Tick highlighted");
        Bind(BindAccessible(_untickRows, "Untick the highlighted rows"), "Untick highlighted");
        var ticks = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 0, 0, 4) };
        ticks.Controls.AddRange(new Control[] { _tickAll, _untickAll, _tickRows, _untickRows, _selectionText });
        root.Controls.Add(ticks);

        // grid
        BindAccessible(_grid, "Recordings found. Tick the ones to download");
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = false;               // only the "Save" tick column is editable (the others are read-only below)
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = SystemColors.Window;
        var saveColumn = new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(DownloadJob.Selected),
            Width = 68,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };
        _relabel.Add(() => saveColumn.HeaderText = Loc.T("Save"));
        var problemColumn = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DownloadJob.Error),
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 160,
        };
        _relabel.Add(() => problemColumn.HeaderText = Loc.T("Problem"));
        _grid.Columns.AddRange(
            saveColumn,
            Col("Branch", nameof(DownloadJob.BranchName), 130),
            Col("Camera", nameof(DownloadJob.CameraName), 70),
            Col("Recorded", nameof(DownloadJob.Period), 190),
            Col("Size", nameof(DownloadJob.SizeText), 80),
            Col("Status", nameof(DownloadJob.StatusText), 110),
            Col("%", nameof(DownloadJob.Percent), 45),
            Col("Speed", nameof(DownloadJob.SpeedText), 85),
            Col("Left", nameof(DownloadJob.LeftText), 90),
            problemColumn);
        _grid.DataSource = _jobs;
        _grid.Controls.Add(_emptyHint);
        _grid.Resize += (_, _) => PositionEmptyHint();
        _jobs.ListChanged += (_, _) => PositionEmptyHint();
        root.Controls.Add(_grid);

        BindAccessible(_logBox, "Activity log");
        var logGroup = Bind(new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8) }, "Activity (what the program is doing)");
        logGroup.Controls.Add(_logBox);
        root.Controls.Add(logGroup);
    }

    private DataGridViewTextBoxColumn Col(string header, string prop, int width)
    {
        var col = new DataGridViewTextBoxColumn { DataPropertyName = prop, Width = width, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable };
        _relabel.Add(() => col.HeaderText = Loc.T(header));
        return col;
    }

    private GroupBox Group(string title, CheckedListBox list, Action all, Action none, bool bindTitle = true)
    {
        var g = new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(8) };
        if (bindTitle) Bind(g, title);
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(list, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var a = Bind(new Button { AutoSize = true }, "All");
        var n = Bind(new Button { AutoSize = true }, "None");
        _relabel.Add(() =>
        {
            a.AccessibleName = Loc.T("All") + " " + Loc.T(title);
            n.AccessibleName = Loc.T("None") + " " + Loc.T(title);
        });
        a.Click += (_, _) => all();
        n.Click += (_, _) => none();
        buttons.Controls.AddRange(new Control[] { a, n });
        t.Controls.Add(buttons, 0, 1);
        g.Controls.Add(t);
        return g;
    }

    private static void SetAll(CheckedListBox box, bool value)
    {
        for (int i = 0; i < box.Items.Count; i++) box.SetItemChecked(i, value);
    }

    // ------------------------------------------------------------------ events

    private void WireEvents()
    {
        _branchList.SelectedIndexChanged += (_, _) => ShowCamerasOfSelectedBranch();
        _cameraList.ItemCheck += OnCameraChecked;
        _find.Click += async (_, _) => await FindAsync();
        _start.Click += async (_, _) => await DownloadSelectedAsync();
        _tickAll.Click += (_, _) => SetTicks(_jobs, true);
        _untickAll.Click += (_, _) => SetTicks(_jobs, false);
        _tickRows.Click += (_, _) => SetTicks(HighlightedJobs(), true);
        _untickRows.Click += (_, _) => SetTicks(HighlightedJobs(), false);
        _grid.CellBeginEdit += OnGridCellBeginEdit;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) UpdateSelectionSummary(); };
        _grid.KeyDown += OnGridKeyDown;

        // Anything that changes what would be listed makes the list out of date.
        _branchList.ItemCheck += (_, e) =>
        {
            if (_fillingBranches) return;
            MarkStale();
            // Ticking a branch also selects it, so the camera box always shows the branch you just ticked.
            if (e.NewValue == CheckState.Checked) BeginInvoke(() => { if (e.Index < _branchList.Items.Count) _branchList.SelectedIndex = e.Index; });
            RequestGuideUpdate();
        };
        _from.ValueChanged += (_, _) => MarkStale();
        _to.ValueChanged += (_, _) => MarkStale();
        _dest.TextChanged += (_, _) => { MarkStale(); RequestGuideUpdate(); };

        _cancel.Click += (_, _) => { _cts?.Cancel(); _cancel.Enabled = false; Append(Loc.T("Stopping after the current step...")); };
        _openFolder.Click += (_, _) => OpenDestination();
        _playBtn.Click += (_, _) => PlayHighlightedOrChoose();
        _grid.CellDoubleClick += OnGridCellDoubleClick;
        _addBtn.Click += (_, _) => AddBranch();
        _editBtn.Click += (_, _) => EditBranch();
        _removeBtn.Click += (_, _) => RemoveBranch();
        _branchList.DoubleClick += (_, _) => EditBranch();
        _testBtn.Click += async (_, _) => await TestConnectionAsync();
        _importBtn.Click += (_, _) => ImportFromCms();
        _grid.CellFormatting += OnGridCellFormatting;
        _tick.Tick += (_, _) => RefreshProgress();
        FormClosing += OnFormClosing;
        Shown += (_, _) => PositionEmptyHint();      // the grid has its final size only once the window is shown
    }

    private void OnFirstShow()
    {
        _dest.Text = _settings.LastDestination;
        EnsureSdkFolder();
        LoadBranches();
    }

    private void EnsureSdkFolder()
    {
        if (_sdkDir is not null) return;

        _start.Enabled = false;
        Append(Loc.T("Backups are disabled: the DVR driver (NetSdk.dll) was not found."));
        MessageBox.Show(this,
            Loc.T("The DVR driver (NetSdk.dll) was not found. It should be in the \"sdk\" folder next to this program.\n\nCopy the \"sdk\" folder from where the program was supplied, then start the program again."),
            "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void LoadBranches()
    {
        _branches = _book.All;
        FillBranchList(_settings.LastBranch);
        if (_branches.Count == 0)
            Append(Loc.T("No branches yet. Press \"Add branch...\" to add the first one."));
    }

    private void UpdateBranchGroupTitle() =>
        _branchGroup.Text = _branches.Count == 0
            ? Loc.T("1. Branches - none yet, press \"Add branch...\"")
            : Loc.T("1. Branches (tick the ones to back up)");

    private void FillBranchList(string? select)
    {
        // Keep the ticks the user already made when the list is rebuilt (add / edit / remove).
        var ticked = _branchList.CheckedItems.Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        _fillingBranches = true;     // rebuilding the list is not a change the user made to the choices
        try
        {
            _branchList.Items.Clear();
            foreach (var b in _branches) _branchList.Items.Add(b.Name, ticked.Contains(b.Name));

            if (_branchList.Items.Count > 0)
            {
                int idx = select is null ? 0 : _branchList.Items.IndexOf(select);
                _branchList.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                _cameraList.Items.Clear();
            }
        }
        finally { _fillingBranches = false; }

        UpdateBranchGroupTitle();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool has = _branches.Count > 0;
        bool listReady = _plans is not null && !_listStale;
        bool anyTicked = _jobs.Any(j => j.IsPendingDownload);

        _editBtn.Enabled = _removeBtn.Enabled = _testBtn.Enabled = has && !_running;
        _addBtn.Enabled = _importBtn.Enabled = !_running;
        _find.Enabled = has && _branchList.CheckedItems.Count > 0 && !_running && _sdkDir is not null;
        _start.Enabled = listReady && anyTicked && !_running && _sdkDir is not null;
        _tickAll.Enabled = _untickAll.Enabled = _tickRows.Enabled = _untickRows.Enabled = _jobs.Count > 0 && !_running;
        UpdateGuide();
    }

    // ---- add / edit / remove ------------------------------------------------

    private void AddBranch()
    {
        using var dlg = new BranchDialog(_client, _branches.Select(b => b.Name).ToList(), null, null);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ResultBranch is null || dlg.ResultCredential is null) return;

        var branch = dlg.ResultBranch;
        _store.Set(branch.Name, dlg.ResultCredential);
        _book.Add(branch);
        _branches = _book.All;
        FillBranchList(branch.Name);
        Append(Loc.F("Added branch {0}.", branch.Name));
    }

    private void EditBranch()
    {
        var current = SelectedBranch;
        if (current is null || _running) return;

        var saved = _store.Get(current.Name);
        using var dlg = new BranchDialog(_client, _branches.Select(b => b.Name).ToList(), current, saved);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.ResultBranch is null || dlg.ResultCredential is null) return;

        var updated = dlg.ResultBranch;
        // An empty password box means "keep the saved password".
        var credential = dlg.ResultCredential.Password.Length == 0 && saved is not null
            ? new Credential(dlg.ResultCredential.Username, saved.Password)
            : dlg.ResultCredential;

        if (current.Name != updated.Name)
        {
            _store.Rename(current.Name, updated.Name);
            if (_cameraChoice.Remove(current.Name, out var choice)) _cameraChoice[updated.Name] = choice;
        }
        else if (updated.Cameras.Count != current.Cameras.Count)
        {
            _cameraChoice.Remove(current.Name);   // camera count changed: forget the old ticks
        }

        _store.Set(updated.Name, credential);
        _book.Update(current.Name, updated);
        _branches = _book.All;
        FillBranchList(updated.Name);
        MarkStale();                // its name, DVR or cameras may have changed
        Append(Loc.F("Saved changes to {0}.", updated.Name));
    }

    // ---- import from the CMS ------------------------------------------------

    /// <summary>The CMS file to read: the remembered one, else the CMS default location, else ask the user.</summary>
    private string? ResolveCmsFile()
    {
        foreach (string candidate in new[] { _settings.CmsDataFile, CmsBranchImporter.DefaultDataFile })
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
        return PickCmsFile();
    }

    private string? PickCmsFile()
    {
        string cmsXml = Path.GetDirectoryName(CmsBranchImporter.DefaultDataFile)!;
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("Find the CMS file Data.xml"),
            Filter = Loc.T("CMS branch list (Data.xml)|Data.xml|XML files (*.xml)|*.xml"),
            CheckFileExists = true,
            InitialDirectory = Directory.Exists(cmsXml) ? cmsXml : string.Empty,
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    private void ImportFromCms()
    {
        if (_running) return;

        string? path = ResolveCmsFile();
        while (path is not null)
        {
            CmsImport import;
            try
            {
                import = CmsBranchImporter.ImportFileDetailed(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                MessageBox.Show(this, Loc.F("Could not read the CMS branch list:\n{0}\n\nFile: {1}", ex.Message, path),
                    "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                path = PickCmsFile();
                continue;
            }

            _settings.CmsDataFile = path;
            _settings.Save(AppPaths.SettingsFile);

            var plan = CmsImportPlanner.Plan(import, _branches);
            if (plan.Candidates.Count == 0)
            {
                string why = ImportCmsDialog.BuildLeftOutText(plan);
                MessageBox.Show(this,
                    Loc.T("There are no new branches to import.") + (why.Length > 0 ? "\n\n" + why : string.Empty),
                    Loc.T("Nothing to import"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var dlg = new ImportCmsDialog(path, plan);
            var result = dlg.ShowDialog(this);
            if (result == DialogResult.Retry) { path = PickCmsFile(); continue; }
            if (result == DialogResult.OK) ApplyImport(dlg.SelectedBranches, dlg.Password);
            return;
        }
    }

    private void ApplyImport(List<Branch> selected, string password)
    {
        int added = 0, withPassword = 0;
        foreach (var branch in selected)
        {
            try { _book.Add(branch); }
            catch (InvalidOperationException) { continue; }       // the name was taken in the meantime
            added++;
            if (password.Length > 0 && branch.Username.Length > 0)
            {
                _store.Set(branch.Name, new Credential(branch.Username, password));
                withPassword++;
            }
        }
        if (added == 0) return;

        _branches = _book.All;
        FillBranchList(selected[0].Name);
        MarkStale();
        Append(Loc.F("Imported {0} branch(es) from the CMS.", added));

        if (withPassword < added)
        {
            MessageBox.Show(this,
                Loc.F("Imported {0} branch(es).\n\nThe CMS passwords are not imported. For each branch without a password: select it, press Edit branch... and enter it, then press Test connection.", added),
                Loc.T("Import from CMS"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>Stores the camera count the DVR reported and refreshes the camera list.</summary>
    private void UpdateDetectedCameras(Branch branch, int count)
    {
        var updated = branch with { Cameras = Camera.Range(branch.Name, count) };
        _book.Update(branch.Name, updated);
        _cameraChoice.Remove(branch.Name);       // old ticks no longer match the camera list
        _branches = _book.All;
        FillBranchList(_settings.LastBranch.Length > 0 ? _settings.LastBranch : branch.Name);
        MarkStale();
        Append(Loc.F("{0}: camera list updated ({1} cameras).", branch.Name, count));
    }

    private void RemoveBranch()
    {
        var current = SelectedBranch;
        if (current is null || _running) return;

        var answer = MessageBox.Show(this,
            Loc.F("Remove \"{0}\" from this program?\n\nIts saved login is deleted. Footage already backed up is not touched.", current.Name),
            Loc.T("Remove branch"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _book.Remove(current.Name);
        _store.Remove(current.Name);
        _cameraChoice.Remove(current.Name);
        _branches = _book.All;
        FillBranchList(null);
        MarkStale();
        Append(Loc.F("Removed branch {0}.", current.Name));
    }

    private Branch? SelectedBranch =>
        _branchList.SelectedItem is string name ? _branches.FirstOrDefault(b => b.Name == name) : null;

    private void ShowCamerasOfSelectedBranch()
    {
        var branch = SelectedBranch;
        UpdateCameraGroupTitle(branch);
        _loadingCameras = true;
        try
        {
            _cameraList.Items.Clear();
            if (branch is null) { _cameraList.Enabled = false; return; }
            _settings.LastBranch = branch.Name;

            if (!branch.CamerasDetected)
            {
                _cameraList.Items.Add(Loc.T("Cameras not detected yet - press Test connection"), false);
                _cameraList.Enabled = false;
                return;
            }
            _cameraList.Enabled = !_running;

            var chosen = _cameraChoice.TryGetValue(branch.Name, out var set) ? set : null;
            foreach (var cam in branch.Cameras)
                _cameraList.Items.Add(Loc.F("Camera {0}", cam.Channel + 1), chosen is null || chosen.Contains(cam.Channel));
        }
        finally { _loadingCameras = false; }
    }

    private void OnCameraChecked(object? sender, ItemCheckEventArgs e)
    {
        if (_loadingCameras) return;
        var branch = SelectedBranch;
        if (branch is null || !branch.CamerasDetected) return;

        if (!_cameraChoice.TryGetValue(branch.Name, out var set))
            _cameraChoice[branch.Name] = set = branch.Cameras.Select(c => c.Channel).ToHashSet();

        int channel = branch.Cameras[e.Index].Channel;
        if (e.NewValue == CheckState.Checked) set.Add(channel); else set.Remove(channel);
        MarkStale();
        RequestGuideUpdate();
    }

    private void BrowseDestination()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = Loc.T("Choose the folder on your external disk for the backup"),
            UseDescriptionForTitle = true,
            SelectedPath = _dest.Text,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _dest.Text = dlg.SelectedPath;
    }

    /// <summary>Double-click on a saved recording opens it in the player. (Column 0 is the tick box.)</summary>
    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex <= 0) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is DownloadJob job) PlayVideo(job.FinalPath);
    }

    /// <summary>The Play button: the highlighted recording if it is on the disk, otherwise ask which file to play.</summary>
    private void PlayHighlightedOrChoose()
    {
        var onDisk = HighlightedJobs().FirstOrDefault(j => File.Exists(j.FinalPath));
        if (onDisk is not null) { PlayVideo(onDisk.FinalPath); return; }

        string start = _dest.Text.Trim();
        using var dlg = new OpenFileDialog
        {
            Title = Loc.T("Choose a recording to play"),
            Filter = Loc.T("Recordings (*.h264)|*.h264|All files (*.*)|*.*"),
            InitialDirectory = Directory.Exists(start) ? start : string.Empty,
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) PlayVideo(dlg.FileName);
    }

    private PlayerForm? _player;

    /// <summary>Plays a recording in the built-in player window (one window, reused for the next recording).</summary>
    private void PlayVideo(string path)
    {
        string? decoder = DecoderLocator.Find();
        if (decoder is null)
        {
            MessageBox.Show(this, Loc.T("The video decoder was not found. Install this program again."),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!File.Exists(path))
        {
            MessageBox.Show(this, Loc.F("This file is not on the disk:\n{0}", path), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            if (_player is null || _player.IsDisposed) _player = new PlayerForm(decoder);
            if (!_player.Visible) _player.Show(this);
            _player.Open(path);
            if (_player.WindowState == FormWindowState.Minimized) _player.WindowState = FormWindowState.Normal;
            _player.Activate();
        }
        catch (PlaybackException ex)
        {
            Append(Loc.F("Could not play the video: {0}", ex.Message));
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void OpenDestination()
    {
        string path = _dest.Text.Trim();
        if (path.Length > 0 && Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else
            MessageBox.Show(this, Loc.T("The save folder does not exist yet."), "CCTV Backup");
    }

    // ------------------------------------------------------------------ actions

    private List<BackupRequest> BuildRequests()
    {
        var list = new List<BackupRequest>();
        foreach (string name in _branchList.CheckedItems.Cast<string>())
        {
            var b = _branches.First(x => x.Name == name);
            var cams = _cameraChoice.TryGetValue(b.Name, out var set)
                ? b.Cameras.Where(c => set.Contains(c.Channel)).ToList()
                : b.Cameras.ToList();
            list.Add(new BackupRequest(b, cams, _from.Value, _to.Value));
        }
        return list;
    }

    private BackupEngine NewEngine()
    {
        var engine = new BackupEngine(_client, name => _store.Get(name), _settings.MaxRetries);
        engine.Log += line => BeginInvoke(() => Append(line, alreadyStamped: true));
        return engine;
    }

    /// <summary>Checks what the user chose. Returns the requests and the save folder, or null after saying what to fix.</summary>
    private (List<BackupRequest> Requests, string Dest)? ValidateInputs()
    {
        var requests = BuildRequests();
        var problems = RequestValidator.Validate(requests, _dest.Text.Trim());
        var missing = requests.Where(r => _store.Get(r.Branch.Name) is null).Select(r => r.Branch.Name).ToList();
        if (missing.Count > 0)
            problems.Add(Loc.F("No password saved for: {0}. Select the branch, press Edit branch... and enter it.", string.Join(", ", missing)));
        if (problems.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", problems.Select(p => "• " + p)), Loc.T("Check these first"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        string dest = Path.GetFullPath(_dest.Text.Trim());
        try { Directory.CreateDirectory(dest); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Loc.F("Cannot use the save folder:\n{0}", ex.Message), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        _settings.LastDestination = dest;
        _settings.Save(AppPaths.SettingsFile);
        return (requests, dest);
    }

    // ---- step 1: find the recordings (nothing is downloaded) ----------------------

    private async Task FindAsync()
    {
        if (_running) return;
        if (ValidateInputs() is not { } input) return;
        var (requests, dest) = input;

        var engine = NewEngine();
        engine.JobsPlanned += planned => BeginInvoke(() =>
        {
            foreach (var j in planned) _jobs.Add(j);
            UpdateSelectionSummary();
        });

        _jobs.Clear();
        _plans = null;
        _runJobs = null;
        _listStale = false;
        UpdateSelectionSummary();
        _cts = new CancellationTokenSource();
        SetRunning(true);
        Append(Loc.F("Looking for recordings on {0} branch(es)... this can take a while on slow connections.", requests.Count));

        IReadOnlyList<BranchPlan>? plans = null;
        try
        {
            plans = await Task.Run(() => engine.FindAsync(requests, dest, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            Append(Loc.T("Search stopped."));
        }
        catch (Exception ex)
        {
            _log.Write($"{DateTime.Now:HH:mm:ss}  FIND ERROR: {ex}");
            Append(Loc.F("The search stopped because of an unexpected error: {0}", ex.Message));
        }
        finally
        {
            SetRunning(false);
        }

        if (_closeWhenDone) { Close(); return; }
        if (plans is null) { UpdateSelectionSummary(); return; }

        _plans = plans;
        _planDestination = dest;
        _listStale = false;
        UpdateSelectionSummary();

        int found = plans.Sum(p => p.Jobs.Count);
        var failed = plans.Where(p => p.Error is not null).ToList();
        Append(Loc.F("Found {0} recording(s). Tick the ones to keep, then press Download selected.", found));

        if (failed.Count > 0)
        {
            MessageBox.Show(this,
                Loc.T("Some branches could not be searched:") + "\n\n" + string.Join("\n", failed.Select(p => $"• {p.Branch.Name}: {p.Error}")),
                Loc.T("Search finished with problems"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else if (found == 0)
        {
            MessageBox.Show(this, Loc.T("No recordings were found for that period.\nCheck the dates, or whether the DVRs were recording."),
                Loc.T("Nothing found"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    // ---- step 2: download what is ticked -----------------------------------------

    private async Task DownloadSelectedAsync()
    {
        if (_running || _plans is null || _listStale) return;

        var chosen = _jobs.Where(j => j.IsPendingDownload).ToList();
        if (chosen.Count == 0)
        {
            MessageBox.Show(this, Loc.T("Tick at least one recording first."), "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string dest = _planDestination;
        var engine = NewEngine();
        _cts = new CancellationTokenSource();
        _runJobs = chosen;
        _runStartedUtc = DateTime.UtcNow;
        _runFinishedUtc = default;
        SetRunning(true);
        Append(Loc.F("Downloading {0} file(s) ({1}) to {2}", chosen.Count, DiskCheck.Format(chosen.Sum(j => j.SizeKb) * 1024), dest));

        BackupSummary? summary = null;
        try
        {
            summary = await Task.Run(() => engine.DownloadAsync(_plans, dest, _cts.Token));
        }
        catch (Exception ex)
        {
            _log.Write($"{DateTime.Now:HH:mm:ss}  ENGINE ERROR: {ex}");
            Append(Loc.F("The backup stopped because of an unexpected error: {0}", ex.Message));
        }
        finally
        {
            _runFinishedUtc = DateTime.UtcNow;
            SetRunning(false);
            RefreshProgress();
            UpdateSelectionSummary();
        }

        if (_closeWhenDone) { Close(); return; }
        if (summary is not null) ShowSummary(summary, dest);
    }

    // ---- ticking ---------------------------------------------------------------

    private IEnumerable<DownloadJob> HighlightedJobs() =>
        _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<DownloadJob>().ToList();

    private void SetTicks(IEnumerable<DownloadJob> jobs, bool value)
    {
        if (_running) return;
        foreach (var j in jobs.ToList())
            if (j.CanChangeSelection) j.Selected = value;
        _grid.Refresh();
        UpdateSelectionSummary();
    }

    private void OnGridCellBeginEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _jobs.Count) return;
        // Finished, already-saved and running rows cannot be ticked or unticked.
        if (_running || !_jobs[e.RowIndex].CanChangeSelection) e.Cancel = true;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || _running) return;
        var rows = HighlightedJobs().Where(j => j.CanChangeSelection).ToList();
        if (rows.Count == 0) return;
        SetTicks(rows, !rows[0].Selected);      // Space ticks/unticks every highlighted row together
        e.Handled = e.SuppressKeyPress = true;
    }

    /// <summary>The choices above changed, so the list on screen no longer matches them.</summary>
    private void MarkStale()
    {
        if (_plans is null || _running || _listStale) return;
        _listStale = true;
        UpdateSelectionSummary();
    }

    private void UpdateSelectionSummary()
    {
        var chosen = _jobs.Where(j => j.IsPendingDownload).ToList();
        int selectable = _jobs.Count(j => j.CanChangeSelection);
        int saved = _jobs.Count(j => j.Status == JobStatus.Skipped);

        if (_listStale)
        {
            _selectionText.Text = string.Empty;       // the banner above explains it
            _selectionText.ForeColor = SystemColors.GrayText;
        }
        else if (_plans is null && _jobs.Count == 0)
        {
            _selectionText.Text = string.Empty;
            _selectionText.ForeColor = SystemColors.GrayText;
        }
        else
        {
            string size = DiskCheck.Format(chosen.Sum(j => j.SizeKb) * 1024);
            int done = _jobs.Count(j => j.Status == JobStatus.Done);
            string count = chosen.Count == 0 && selectable == 0 && done > 0
                ? Loc.F("{0} recordings saved", done)                      // after a finished run there is nothing left to tick
                : Loc.F("{0} of {1} recordings ticked ({2})", chosen.Count, selectable, size);
            _selectionText.Text = count + (saved > 0 ? Loc.F(", {0} already saved", saved) : "");
            _selectionText.ForeColor = SystemColors.ControlText;
        }
        UpdateButtons();
    }

    private void ShowSummary(BackupSummary summary, string dest)
    {
        var lines = summary.Branches.Select(r =>
            r.Error is not null ? $"• {r.Branch}: {r.Error}"
            : r.Total == 0 ? "• " + Loc.F("{0}: no recordings in that period", r.Branch)
            : "• " + Loc.F("{0}: {1} saved, {2} already saved, {3} failed", r.Branch, r.Done, r.Skipped, r.Failed));

        string title = summary.Cancelled ? Loc.T("Backup stopped")
            : summary.Branches.All(r => r.Succeeded) ? Loc.T("Backup finished")
            : Loc.T("Backup finished with problems");
        string text = string.Join("\n", lines);
        if (summary.Cancelled) text += "\n\n" + Loc.T("You stopped it. Press Download selected again to continue where it left off.");
        else if (summary.Branches.Any(r => r.Failed > 0)) text += "\n\n" + Loc.T("Press Download selected again to retry the files that failed.");
        Append(title + ".");

        bool anySaved = summary.Branches.Any(r => r.Done > 0);
        var icon = summary.Branches.All(r => r.Succeeded) && !summary.Cancelled ? MessageBoxIcon.Information : MessageBoxIcon.Warning;
        if (anySaved)
        {
            if (MessageBox.Show(this, text + "\n\n" + Loc.T("Open the backup folder?"), title, MessageBoxButtons.YesNo, icon) == DialogResult.Yes)
                OpenDestination();
        }
        else
        {
            MessageBox.Show(this, text, title, MessageBoxButtons.OK, icon);
        }
    }

    private async Task TestConnectionAsync()
    {
        var branch = SelectedBranch;
        if (branch is null) return;
        var cred = _store.Get(branch.Name);
        if (cred is null)
        {
            MessageBox.Show(this, Loc.T("No password saved for this branch yet. Press Edit branch... and enter it."), "CCTV Backup");
            return;
        }

        _testBtn.Enabled = false;
        Append(Loc.F("Testing {0}...", branch.Name));
        var sw = Stopwatch.StartNew();
        try
        {
            using var conn = await _client.ConnectAsync(branch, cred, CancellationToken.None);
            int n = conn.Info.ChannelCount;
            Append(Loc.F("{0}: ONLINE, login OK ({1} s). ", branch.Name, sw.Elapsed.TotalSeconds.ToString("0.#")) +
                   (n > 0 ? Loc.F("{0} cameras detected.", n) : Loc.T("The DVR did not report its camera count.")));
            if (n > 0 && n != branch.Cameras.Count) UpdateDetectedCameras(branch, n);
        }
        catch (DvrException ex)
        {
            Append($"{branch.Name}: {ex.Message}");
        }
        finally { UpdateButtons(); }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _cancel.Enabled = running;
        foreach (Control c in new Control[] { _branchList, _from, _to, _dest, _language })
            c.Enabled = !running;
        _cameraList.Enabled = !running && (SelectedBranch?.CamerasDetected ?? false);
        UpdateButtons();
        if (running) _tick.Start(); else _tick.Stop();
    }

    private void RefreshProgress()
    {
        // Only ticked files count, plus any that already ran. "Already saved" rows are not part of the work.
        var counted = _jobs.Where(j => j.Selected || j.Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Downloading).ToList();
        bool started = counted.Any(j => j.Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Downloading);
        if (counted.Count == 0 || (!started && !_running))
        {
            _overall.Value = 0;
            _overallText.Text = _running ? Loc.T("Working...") : Loc.T("Ready.");
            _grid.Invalidate();
            return;
        }

        int finished = counted.Count(j => j.Status is JobStatus.Done or JobStatus.Failed or JobStatus.Cancelled);
        int failed = counted.Count(j => j.Status == JobStatus.Failed);
        double partial = counted.Where(j => j.Status == JobStatus.Downloading).Sum(j => j.Percent / 100.0);
        _overall.Value = Math.Clamp((int)((finished + partial) * 100.0 / counted.Count), 0, 100);
        _overallText.Text = Loc.F("{0} of {1} files", finished, counted.Count) + (failed > 0 ? Loc.F("  ({0} failed)", failed) : "")
                            + (_running ? Loc.T("  - working...") : "") + OverallSpeedText();
        _grid.Invalidate();
    }

    /// <summary>"  -  1.2 MB/s in total, about 25 min left" while downloading; the average once it has finished.</summary>
    private string OverallSpeedText()
    {
        if (_runJobs is not { Count: > 0 } || _runStartedUtc == default) return string.Empty;

        // Failed, cancelled and already-saved files are not part of the pace or of what is left.
        var work = _runJobs
            .Where(j => j.Status is JobStatus.Done or JobStatus.Downloading or JobStatus.Pending)
            .Select(j => (j.SizeKb, j.Status == JobStatus.Done ? 1.0 : j.Status == JobStatus.Downloading ? j.Percent / 100.0 : 0.0));
        var end = _running ? DateTime.UtcNow : _runFinishedUtc;
        var (speed, left) = TransferStats.Overall(work, end - _runStartedUtc);
        if (speed is null) return string.Empty;

        return _running
            ? "  -  " + Loc.F("{0} in total", TransferStats.FormatSpeed(speed)) + (left is null ? "" : Loc.F(", about {0} left", TransferStats.FormatDuration(left)))
            : "  -  " + Loc.F("average {0}", TransferStats.FormatSpeed(speed));
    }

    private void OnGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _jobs.Count) return;
        var job = _jobs[e.RowIndex];
        e.CellStyle!.ForeColor = job.Status switch
        {
            JobStatus.Failed => Color.Firebrick,
            JobStatus.Done => Color.DarkGreen,
            JobStatus.Skipped => Color.DimGray,
            JobStatus.Pending when !job.Selected => Color.DimGray,
            _ => SystemColors.ControlText,
        };
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        _settings.LastDestination = _dest.Text.Trim();
        _settings.Save(AppPaths.SettingsFile);

        if (_running)
        {
            if (MessageBox.Show(this, Loc.T("A backup is running. Stop it and close the program?\n(You can continue later; finished files are kept.)"),
                    "CCTV Backup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _closeWhenDone = true;
            _cts?.Cancel();
            e.Cancel = true;      // closes itself once the engine has stopped cleanly
            return;
        }
        _client.Dispose();
    }

    // ------------------------------------------------------------------ guidance ("what do I do now?")

    private void BuildGuide()
    {
        _guideTitle.Font = new Font(Font.FontFamily, 12.5f, FontStyle.Bold);
        var texts = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        texts.Controls.Add(_guideTitle, 0, 0);
        texts.Controls.Add(_guideText, 0, 1);
        _guide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _guide.Controls.Add(texts, 0, 0);
        // a coloured bar on the left edge makes it read as a message, not as part of the form
        _guide.Paint += (_, e) =>
        {
            using var b = new SolidBrush(_guideAccent);
            e.Graphics.FillRectangle(b, 0, 0, 6, _guide.Height);
        };
        _guide.Resize += (_, _) =>
        {
            int w = Math.Max(300, _guide.ClientSize.Width - _guide.Padding.Horizontal - 10);
            _guideTitle.MaximumSize = new Size(w, 0);
            _guideText.MaximumSize = new Size(w, 0);
        };
    }

    /// <summary>Gives a button the blue "main action" look (a flat button, because the standard one cannot be coloured).</summary>
    private void StylePrimary(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 90, 200);
        b.UseVisualStyleBackColor = false;
        b.Font = new Font(Font, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
        void Paint()
        {
            b.BackColor = b.Enabled ? Accent : Color.FromArgb(222, 224, 228);
            b.ForeColor = b.Enabled ? Color.White : Color.FromArgb(120, 124, 130);
            b.Cursor = b.Enabled ? Cursors.Hand : Cursors.Default;
        }
        b.EnabledChanged += (_, _) => Paint();
        Paint();
    }

    private void RequestGuideUpdate()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(UpdateButtons);      // buttons first, then the banner (UpdateButtons ends with UpdateGuide)
    }

    private void UpdateGuide()
    {
        var ticked = _branchList.CheckedItems.Cast<string>()
            .Select(n => _branches.FirstOrDefault(b => string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase)))
            .OfType<Branch>().ToList();
        int downloadingFiles = _running && _runJobs is not null ? 1 : 0;
        var state = new GuideState(
            DriverMissing: _sdkDir is null,
            Branches: _branches.Count,
            TickedBranches: ticked.Count,
            TickedWithoutCameraInfo: ticked.Count(b => !b.CamerasDetected),
            TickedWithNoCameraChosen: ticked.Count(b => b.CamerasDetected && _cameraChoice.TryGetValue(b.Name, out var set) && !b.Cameras.Any(c => set.Contains(c.Channel))),
            FolderChosen: _dest.Text.Trim().Length > 0,
            Searching: _running && downloadingFiles == 0,
            Downloading: downloadingFiles > 0,
            ListExists: _plans is not null,
            ListOutdated: _listStale,
            Recordings: _jobs.Count(j => j.Status != JobStatus.Skipped),
            Ticked: _jobs.Count(j => j.IsPendingDownload),
            Done: _jobs.Count(j => j.Status == JobStatus.Done),
            AlreadySaved: _jobs.Count(j => j.Status == JobStatus.Skipped),
            SearchProblems: _plans?.Count(p => p.Error is not null) ?? 0);

        ShowGuide(WorkflowGuide.Decide(state), state, ticked);
    }

    private enum Tone { Info, Go, Warn, Busy }

    private void ShowGuide(GuideStep step, GuideState s, List<Branch> ticked)
    {
        Tone tone = Tone.Info;
        string title, text;
        string size = DiskCheck.Format(_jobs.Where(j => j.IsPendingDownload).Sum(j => j.SizeKb) * 1024);
        string unknownName = ticked.FirstOrDefault(b => !b.CamerasDetected)?.Name ?? string.Empty;

        switch (step)
        {
            case GuideStep.DriverMissing:
                tone = Tone.Warn;
                title = Loc.T("The DVR driver is missing");
                text = Loc.T("Install this program again. The \"sdk\" folder next to the program must contain NetSdk.dll.");
                break;
            case GuideStep.NoBranches:
                title = Loc.T("Welcome! Start by adding a branch");
                text = Loc.T("Press \"Add branch\" at the top left and type its name, Serial ID, user name and password. If the CMS is on this PC, \"Import from CMS\" brings all its branches in at once.");
                break;
            case GuideStep.TickBranch:
                title = Loc.T("Step 1: tick the branches to back up");
                text = Loc.T("Tick the box in front of each branch in the list below on the left.");
                break;
            case GuideStep.CamerasUnknown:
                tone = Tone.Warn;
                title = Loc.F("Step 2: read the cameras of {0}", unknownName);
                text = Loc.T("The program does not know the cameras of this branch yet. It reads them from the DVR when you press \"Test connection\" (top left).");
                break;
            case GuideStep.TickCamera:
                tone = Tone.Warn;
                title = Loc.T("Step 2: tick at least one camera");
                text = Loc.T("Click a branch to see its cameras, then tick the ones you want.");
                break;
            case GuideStep.ChooseFolder:
                title = Loc.T("Step 4: choose where to save the backup");
                text = Loc.T("Press \"Browse...\" next to \"Save to\" and pick a folder on your external disk.");
                break;
            case GuideStep.Find:
                tone = Tone.Go;
                title = Loc.T("Step 5: find the recordings");
                text = Loc.T("Check the period above, then press \"Find recordings\". Nothing is downloaded yet; you choose the files first.");
                break;
            case GuideStep.Searching:
                tone = Tone.Busy;
                title = Loc.T("Searching the DVRs...");
                text = Loc.T("This can take a minute on slow connections. You can press Cancel to stop.");
                break;
            case GuideStep.ListOutdated:
                tone = Tone.Warn;
                title = Loc.T("Your choices changed");
                text = Loc.T("The list below was made with the earlier choices. Press \"Find recordings\" again to refresh it.");
                break;
            case GuideStep.NothingFound:
                tone = Tone.Warn;
                title = Loc.T("No recordings found for this period");
                text = Loc.T("Change the dates (or the branches or cameras) and search again. The DVR may not have been recording.");
                break;
            case GuideStep.SearchFailed:
                tone = Tone.Warn;
                title = Loc.T("The branches could not be searched");
                text = Loc.T("See the activity log below for the reason (internet, address or password). Fix it, then search again.");
                break;
            case GuideStep.AllAlreadySaved:
                tone = Tone.Go;
                title = Loc.T("Everything found is already saved");
                text = Loc.T("All recordings in this period are already in your save folder. Change the period to back up more.");
                break;
            case GuideStep.TickRecordings:
                title = Loc.T("Step 6: tick the recordings to save");
                text = Loc.T("Tick the files you want in the list below, then press \"Download selected\".");
                break;
            case GuideStep.ReadyToDownload:
                tone = Tone.Go;
                title = Loc.F("Step 6: download {0} recordings ({1})", s.Ticked, size);
                text = Loc.T("Press \"Download selected\". Finished files are kept, so you can stop and continue later.");
                break;
            case GuideStep.Downloading:
                tone = Tone.Busy;
                title = Loc.T("Downloading...");
                text = Loc.T("Keep the program open. Press Cancel to stop; finished files are kept.");
                break;
            default:   // Finished
                tone = Tone.Go;
                title = Loc.F("Done: {0} recordings saved", s.Done);
                text = Loc.F("They are in {0}. Watch them with \"Play video\", or search another period.", _planDestination.Length > 0 ? _planDestination : _dest.Text.Trim());
                break;
        }

        (Color back, Color accent) = tone switch
        {
            Tone.Go => (Color.FromArgb(232, 245, 233), Color.FromArgb(34, 120, 52)),
            Tone.Warn => (Color.FromArgb(255, 244, 214), Color.FromArgb(160, 95, 0)),
            Tone.Busy => (Color.FromArgb(238, 241, 245), Color.FromArgb(80, 90, 105)),
            _ => (Color.FromArgb(232, 242, 253), Color.FromArgb(25, 95, 200)),
        };
        _guide.BackColor = back;
        _guideAccent = accent;
        _guideTitle.ForeColor = accent;
        _guideTitle.Text = title;
        _guideText.Text = text;
        _guide.Invalidate();
    }

    private void UpdateCameraGroupTitle(Branch? branch)
    {
        _cameraGroup.Text = branch is null
            ? Loc.T("2. Cameras (click a branch)")
            : Loc.F("2. Cameras of {0}", branch.Name);
    }

    private void PositionEmptyHint()
    {
        _emptyHint.Text = Loc.T("The recordings found will be listed here.\nTick the ones you want, then press \"Download selected\".");
        _emptyHint.Visible = _jobs.Count == 0;
        if (!_emptyHint.Visible) return;
        var size = _emptyHint.PreferredSize;      // the label may not have resized yet when the text just changed
        int top = _grid.ColumnHeadersHeight + Math.Max(20, (_grid.ClientSize.Height - _grid.ColumnHeadersHeight - size.Height) / 3);
        _emptyHint.Location = new Point(Math.Max(0, (_grid.ClientSize.Width - size.Width) / 2), top);
    }

    /// <summary>Explanations that appear when the mouse rests on a button.</summary>
    private void SetupTooltips()
    {
        void Tip(Control c, string english) => _relabel.Add(() => _tips.SetToolTip(c, Loc.T(english)));
        Tip(_addBtn, "Add a branch: its name, Serial ID or IP address, user name and password.");
        Tip(_editBtn, "Change the settings or the password of the highlighted branch.");
        Tip(_removeBtn, "Remove the highlighted branch from this program. Nothing is deleted from the DVR or from your backups.");
        Tip(_testBtn, "Check that the highlighted branch answers and the login works. This also reads how many cameras it has.");
        Tip(_importBtn, "Copy the branch list from the CMS program installed on this PC.");
        Tip(_find, "Ask each ticked branch which recordings it has for this period. Nothing is downloaded yet.");
        Tip(_start, "Save the ticked recordings to the folder above, one after the other.");
        Tip(_cancel, "Stop after the current step. Files that are finished are kept.");
        Tip(_openFolder, "Open the save folder in Windows Explorer.");
        Tip(_playBtn, "Watch a saved recording in the built-in player.");
        Tip(_dest, "The folder where the backup is saved, usually on your external disk.");
        Tip(_from, "Start of the period, by the DVR's own clock.");
        Tip(_to, "End of the period, by the DVR's own clock.");
        Tip(_tickAll, "Tick every recording in the list.");
        Tip(_untickAll, "Untick every recording in the list.");
        Tip(_tickRows, "Tick the rows you have highlighted (hold Ctrl or Shift to highlight several).");
        Tip(_untickRows, "Untick the rows you have highlighted.");
        Tip(_branchList, "Tick the branches to back up. Click a name to see and change its cameras.");
        Tip(_cameraList, "Tick the cameras to back up for the highlighted branch.");
    }
    private void Append(string text, bool alreadyStamped = false)
    {
        string line = alreadyStamped ? text : $"{DateTime.Now:HH:mm:ss}  {text}";
        _logBox.AppendText(line + Environment.NewLine);
        _log.Write(line);
    }
}
