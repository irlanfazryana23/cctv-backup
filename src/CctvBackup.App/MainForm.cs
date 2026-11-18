using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using CctvBackup.Core.Auth;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Playback;
using CctvBackup.Core.Sdk;
using CctvBackup.Core.Settings;
using CctvBackup.Core.Updates;
using CctvBackup.Core.Workflow;

namespace CctvBackup.App;

/// <summary>
/// Modern Windows GUI for CCTV Backup matching the streamlined Avalonia desktop edition.
/// Features a dark navy gradient app bar, balanced card-based settings, rich custom-rendered
/// recordings table with pill badges, in-cell progress bar, and built-in app lock.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly FileLog _log;
    private readonly AppSettings _settings;
    private readonly BranchBook _book = new(AppPaths.BranchesFile);
    private readonly CredentialStore _store = new(AppPaths.CredentialsFile);
    private readonly XmDvrClient _client;
    private readonly string? _sdkDir;

    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private readonly Dictionary<string, HashSet<int>> _cameraChoice = new(StringComparer.OrdinalIgnoreCase);
    private readonly BindingList<DownloadJob> _jobs = new();
    private CancellationTokenSource? _cts;
    private bool _running, _closeWhenDone, _loadingCameras, _fillingBranches, _settingLanguage;
    private readonly HashSet<string> _ticked = new(StringComparer.OrdinalIgnoreCase);

    // Current download run stats
    private List<DownloadJob>? _runJobs;
    private DateTime _runStartedUtc, _runFinishedUtc;

    // Plan & list staleness
    private IReadOnlyList<BranchPlan>? _plans;
    private string _planDestination = string.Empty;
    private bool _listStale;

    // Relabel actions for runtime language switching
    private readonly List<Action> _relabel = new();

    // ---- Top App Bar ----
    private readonly Panel _appBar = new() { Dock = DockStyle.Top, Height = 58 };
    private readonly ModernButton _accountBtn = new() { Text = "👤 admin", AutoSize = true, MinimumSize = new Size(95, 32), BorderRadius = 6 };
    private readonly ModernButton _lockBtn = new() { AutoSize = true, MinimumSize = new Size(80, 32), BorderRadius = 6 };
    private readonly ModernButton _copyLogBtn = new() { AutoSize = true, MinimumSize = new Size(100, 32), BorderRadius = 6 };
    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Font = new Font("Segoe UI", 9f), AccessibleName = "Language / Bahasa" };

    // ---- Update Notification Banner ----
    private readonly TableLayoutPanel _updateBanner = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Padding = new Padding(14, 6, 10, 6), Margin = new Padding(0, 0, 0, 6), Visible = false, BackColor = Color.FromArgb(240, 253, 244) };
    private readonly Label _updateIcon = new() { Text = "✨", Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(22, 163, 74), AutoSize = true, Margin = new Padding(0, 3, 8, 0), BackColor = Color.Transparent };
    private readonly Label _updateText = new() { AutoSize = true, Font = Ui.Font(9f, FontStyle.Bold), ForeColor = Color.FromArgb(22, 101, 52), Margin = new Padding(0, 4, 8, 0), BackColor = Color.Transparent };
    private readonly ModernButton _updateActionBtn = new() { AutoSize = true, MinimumSize = new Size(110, 26), BorderRadius = 4, UseMnemonic = false, Margin = new Padding(0, 0, 6, 0) };
    private readonly ModernButton _updateCloseBtn = new() { Text = "✕", AutoSize = false, Size = new Size(26, 26), BorderRadius = 4, UseMnemonic = false, Margin = new Padding(0) };
    private ReleaseInfo? _pendingUpdate;

    // ---- Guide Banner ----
    private readonly TableLayoutPanel _guide = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(14, 6, 14, 6), Margin = new Padding(0, 2, 0, 6) };
    private readonly Label _guideIcon = new() { Text = "ℹ", Font = new Font("Segoe UI", 12f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 2, 8, 0), BackColor = Color.Transparent };
    private readonly Label _guideTitle = new() { AutoSize = true, Margin = new Padding(0), BackColor = Color.Transparent };
    private readonly Label _guideText = new() { AutoSize = true, Margin = new Padding(0, 1, 0, 0), BackColor = Color.Transparent };
    private Color _guideAccent = Color.FromArgb(37, 99, 235);

    // ---- 4 Cards Controls ----
    private ModernCard _cardBranches = null!;
    private ModernCard _cardCameras = null!;
    private ModernCard _cardPeriod = null!;
    private ModernCard _cardSave = null!;

    private readonly ModernTextBox _branchSearch = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Font = new Font("Segoe UI", 9.5f) };
    private readonly CheckList _branchList = new() { Dock = DockStyle.Fill, RowHeight = 44 };
    private readonly CheckList _cameraList = new() { Dock = DockStyle.Fill, RowHeight = 28, CheckOnRowClick = true };
    private readonly Label _cameraSub = new() { Dock = DockStyle.Top, Height = 22, AutoEllipsis = true, ForeColor = Color.FromArgb(71, 85, 105), Font = new Font("Segoe UI", 9f), TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.White };
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm", Font = new Font("Segoe UI", 9.5f) };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd  HH:mm", Font = new Font("Segoe UI", 9.5f) };
    private readonly ModernTextBox _dest = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Font = new Font("Segoe UI", 9.5f) };
    private readonly Label _destSpaceText = new() { Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(100, 116, 139), Font = new Font("Segoe UI", 8.5f), Margin = new Padding(0, 2, 0, 0) };

    // Branch management buttons
    private readonly ModernButton _addBtn = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 4), BorderRadius = 4 };
    private readonly ModernButton _editBtn = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 4), BorderRadius = 4 };
    private readonly ModernButton _removeBtn = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4), BorderRadius = 4 };
    private readonly ModernButton _testBtn = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), BorderRadius = 4 };
    private readonly ModernButton _importBtn = new() { Dock = DockStyle.Fill, Margin = new Padding(0), BorderRadius = 4 };

    // Destination buttons
    private readonly ModernButton _browseBtn = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 4, 0), BorderRadius = 4 };
    private readonly ModernButton _openFolder = new() { Dock = DockStyle.Fill, Margin = new Padding(0), BorderRadius = 4 };

    // Quick presets
    private readonly ModernButton _quickYesterday = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 30, Margin = new Padding(0, 0, 4, 0), BorderRadius = 12 };
    private readonly ModernButton _quickToday = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 30, Margin = new Padding(0, 0, 4, 0), BorderRadius = 12 };
    private readonly ModernButton _quickWeek = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = 30, Margin = new Padding(0), BorderRadius = 12 };

    // ---- Action Bar ----
    private readonly ModernButton _find = new() { MinimumSize = new Size(160, 40), BorderRadius = 6, Margin = new Padding(0, 0, 8, 0) };
    private readonly ModernButton _start = new() { MinimumSize = new Size(180, 40), BorderRadius = 6, Enabled = false, Margin = new Padding(0, 0, 8, 0) };
    private readonly ModernButton _cancel = new() { MinimumSize = new Size(90, 40), BorderRadius = 6, Enabled = false, Margin = new Padding(0, 0, 8, 0) };
    private readonly ModernButton _playBtn = new() { MinimumSize = new Size(120, 40), BorderRadius = 6, Margin = new Padding(0, 0, 8, 0) };
    private readonly ModernProgressBar _overall = new() { Dock = DockStyle.Fill, Height = 8 };
    private readonly Label _overallText = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) };

    // ---- Recordings Table ----
    private readonly Panel _recCard = new() { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1) };
    private readonly Label _recCountBadge = new() { AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), ForeColor = Color.FromArgb(71, 85, 105), BackColor = Color.FromArgb(241, 245, 249), Padding = new Padding(6, 2, 6, 2) };
    private readonly LinkLabel _recSelectAll = new() { AutoSize = true, LinkColor = Color.FromArgb(37, 99, 235), Font = new Font("Segoe UI", 9f), Margin = new Padding(0, 0, 4, 0) };
    private readonly LinkLabel _recSelectNone = new() { AutoSize = true, LinkColor = Color.FromArgb(37, 99, 235), Font = new Font("Segoe UI", 9f), Margin = new Padding(4, 0, 0, 0) };
    private readonly DataGridView _grid = new();
    private readonly Label _emptyHint = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(148, 163, 184), BackColor = Color.White, Font = new Font("Segoe UI", 10f) };

    // ---- Activity Log ----
    private readonly Panel _logCard = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(1) };
    private readonly TextBox _logBox = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(15, 23, 42), ForeColor = Color.FromArgb(226, 232, 240), BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f) };
    private readonly ModernButton _clearLogBtn = new() { AutoSize = true, MinimumSize = new Size(84, 22), BorderRadius = 4 };

    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 500 };
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
        MinimumSize = new Size(1100, 680);
        Size = new Size(1240, 760);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;

        BuildLayout();
        SetupTooltips();
        WireEvents();
        ApplyLanguage();

        Load += (_, _) => OnFirstShow();
        Ui.EnableScaling(this);   // last: only controls that already exist get scaled
    }

    // ------------------------------------------------------------------ Language Helpers

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

    private void ApplyLanguage()
    {
        foreach (var apply in _relabel) apply();

        PositionEmptyHint();
        UpdateBranchGroupTitle();
        UpdateBranchEmptyText();
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

    // ------------------------------------------------------------------ Layout Construction

    private void BuildLayout()
    {
        // 1. Top App Bar (Dock = Top, Height = 58)
        BuildAppBar();

        // 2. Main Content Root
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12, 8, 12, 10),
            BackColor = Color.FromArgb(248, 250, 252)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 0: Update Banner (hidden unless newer release found)
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // 1: Guide Banner (~36px)
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // 2: Main Workspace (Branches sidebar + Right panel)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));     // 3: Activity Log

        // Update Banner + Guide Banner
        BuildUpdateBanner();
        root.Controls.Add(_updateBanner, 0, 0);

        BuildGuide();
        root.Controls.Add(_guide, 0, 1);

        // Main Workspace TableLayoutPanel: Left Sidebar (Branches) + Right Panel
        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300)); // Column 0: 1. Branches sidebar
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // Column 1: Right panel

        // Card 1: Branches in Sidebar (Column 0)
        _cardBranches = new ModernCard("1", string.Empty)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0)
        };
        BuildBranchesCard(_cardBranches);
        workspace.Controls.Add(_cardBranches, 0, 0);

        // Right Panel (Column 1): Top settings row + Run bar + Recordings table
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0)
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 190)); // Row 0: Cards 2, 3, 4
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));  // Row 1: Action Run Bar
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Row 2: Recordings Grid Card

        // Cards 2, 3, 4 Row (Cameras, Period, Save to)
        var topCardsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 6)
        };
        topCardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29f)); // Card 2: Cameras
        topCardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f)); // Card 3: Period
        topCardsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f)); // Card 4: Save to

        _cardCameras = new ModernCard("2", string.Empty)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0)
        };
        BindTitle(_cardCameras, "Cameras");
        BuildCamerasCard(_cardCameras);
        topCardsRow.Controls.Add(_cardCameras, 0, 0);

        _cardPeriod = new ModernCard("3", string.Empty)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0)
        };
        BindTitle(_cardPeriod, "Period (DVR clock)");
        BuildPeriodCard(_cardPeriod);
        topCardsRow.Controls.Add(_cardPeriod, 1, 0);

        _cardSave = new ModernCard("4", string.Empty)
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        BindTitle(_cardSave, "Save to");
        BuildSaveCard(_cardSave);
        topCardsRow.Controls.Add(_cardSave, 2, 0);

        rightPanel.Controls.Add(topCardsRow, 0, 0);

        // Action Run Bar
        var runBar = BuildRunBar();
        runBar.Margin = new Padding(0, 0, 0, 6);
        rightPanel.Controls.Add(runBar, 0, 1);

        // Recordings Grid Card
        BuildRecordingsCard();
        _recCard.Dock = DockStyle.Fill;
        _recCard.Margin = new Padding(0);
        rightPanel.Controls.Add(_recCard, 0, 2);

        workspace.Controls.Add(rightPanel, 1, 0);
        root.Controls.Add(workspace, 0, 2);

        // Activity Log Card
        BuildLogCard();
        _logCard.Dock = DockStyle.Fill;
        _logCard.Margin = new Padding(0, 8, 0, 0);
        root.Controls.Add(_logCard, 0, 3);

        // Dock to Form
        Controls.Add(_appBar);
        Controls.Add(root);
        _appBar.SendToBack();
    }

    private void BindTitle(ModernCard card, string english) =>
        _relabel.Add(() => card.CardTitle = Loc.T(english));

    private void BuildAppBar()
    {
        _appBar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using var brush = new LinearGradientBrush(_appBar.ClientRectangle,
                Color.FromArgb(15, 23, 42),
                Color.FromArgb(30, 58, 138),
                LinearGradientMode.Horizontal);
            g.FillRectangle(brush, _appBar.ClientRectangle);

            using var pen = new Pen(Color.FromArgb(51, 65, 85), 1);
            g.DrawLine(pen, 0, _appBar.Height - 1, _appBar.Width, _appBar.Height - 1);

            int s(int px) => Ui.S(_appBar, px);

            // Icon
            var iconRect = new Rectangle(s(14), s(11), s(36), s(36));
            using var iconBg = new SolidBrush(Color.White);
            using (var badgePath = Ui.RoundedRect(iconRect, s(8)))
                g.FillPath(iconBg, badgePath);

            var icon = AppIcon.Get();
            if (icon != null)
            {
                g.DrawIcon(icon, new Rectangle(s(18), s(15), s(28), s(28)));
            }

            TextRenderer.DrawText(g, "CCTV Backup", Ui.Font(13.5f, FontStyle.Bold), new Point(s(60), s(8)), Color.White);
            TextRenderer.DrawText(g, Loc.T("Xiongmai DVR recordings backup • Windows edition"), Ui.Font(8.5f), new Point(s(62), s(33)), Color.FromArgb(203, 213, 225));
        };

        _appBar.Resize += (_, _) => _appBar.Invalidate();

        // Right side buttons
        var rightFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 13, 14, 0),
            BackColor = Color.Transparent
        };

        StyleAppBarButton(_accountBtn, Color.FromArgb(30, 41, 59), Color.FromArgb(51, 65, 85), Color.White);
        StyleAppBarButton(_lockBtn, Color.FromArgb(30, 41, 59), Color.FromArgb(51, 65, 85), Color.White);
        StyleAppBarButton(_copyLogBtn, Color.FromArgb(30, 41, 59), Color.FromArgb(51, 65, 85), Color.White);
        _accountBtn.Margin = _lockBtn.Margin = _copyLogBtn.Margin = new Padding(0, 0, 8, 0);
        _language.Margin = new Padding(4, 5, 0, 0);

        _accountBtn.Click += (_, _) => OpenAccountSettings();
        _lockBtn.Click += (_, _) => LockApplication();
        _copyLogBtn.Click += (_, _) => CopyActivityLog();
        _relabel.Add(UpdateAppBarTexts);

        rightFlow.Controls.Add(_accountBtn);
        rightFlow.Controls.Add(_lockBtn);
        rightFlow.Controls.Add(_copyLogBtn);

        SetupLanguageBox();
        rightFlow.Controls.Add(_language);

        _appBar.Controls.Add(rightFlow);
    }

    private void UpdateAppBarTexts()
    {
        _accountBtn.Text = $"👤 {AppAuthStore.GetUsername() ?? "admin"}";
        _lockBtn.Text = "🔒 " + Loc.T("Lock");
        _copyLogBtn.Text = "📋 " + Loc.T("Copy log");
        _appBar.Invalidate();
    }

    private static void StyleAppBarButton(ModernButton b, Color bg, Color hover, Color fg)
    {
        b.NormalColor = bg;
        b.HoverColor = hover;
        b.ForeColor = fg;
        b.BorderColor = Color.FromArgb(71, 85, 105);
        b.Font = Ui.Font(9f, FontStyle.Bold);
    }

    private LinkLabel NewLink(string english)
    {
        var link = new LinkLabel
        {
            AutoSize = true,
            LinkColor = Ui.Accent,
            ActiveLinkColor = Ui.AccentDark,
            VisitedLinkColor = Ui.Accent,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Font = Ui.Font(8.5f),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 0),
        };
        Bind(link, english);
        return link;
    }

    /// <summary>"All · None" shortcuts in a card header that tick or untick every row of the list.</summary>
    private void AddAllNone(ModernCard card, CheckList list)
    {
        var all = NewLink("All");
        var none = NewLink("None");
        all.Click += (_, _) => SetAll(list, true);
        none.Click += (_, _) => SetAll(list, false);
        card.HeaderActions.Controls.Add(all);
        card.HeaderActions.Controls.Add(new Label { Text = "·", AutoSize = true, ForeColor = Ui.Faint, BackColor = Color.Transparent, Margin = new Padding(3, 2, 3, 0) });
        card.HeaderActions.Controls.Add(none);
    }

    private Branch? BranchAt(int index) =>
        index >= 0 && index < _branchList.Items.Count
            ? _branches.FirstOrDefault(b => b.Name == _branchList.Items[index]?.ToString())
            : null;

    private void BuildBranchesCard(ModernCard card)
    {
        AddAllNone(card, _branchList);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));

        _relabel.Add(() =>
        {
            _branchSearch.PlaceholderText = Loc.T("Search branches...");
            _branchSearch.AccessibleName = Loc.T("Search branches...");
        });
        _branchSearch.TextChanged += (_, _) => FilterBranchList();
        layout.Controls.Add(_branchSearch, 0, 0);

        _branchList.SubtitleOf = i => BranchAt(i) is { } b
            ? (b.Mode == LoginMode.Ip ? Loc.F("IP: {0}", $"{b.Address}:{b.Port}") : Loc.F("Cloud: {0}", b.CloudId))
            : null;
        _branchList.BadgeOf = i => BranchAt(i) is { } b
            ? (b.CamerasDetected ? Loc.F("{0} cams", b.Cameras.Count) : Loc.T("? cams"))
            : null;
        layout.Controls.Add(_branchList, 0, 1);

        // Two tidy rows of equal buttons: they keep their shape in both languages.
        var tools = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0, 8, 0, 0),
        };
        for (int i = 0; i < 6; i++) tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.67f));
        tools.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tools.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        Bind(_addBtn, "+ Add");
        Bind(_editBtn, "Edit");
        Bind(_removeBtn, "Remove");
        Bind(_testBtn, "Test connection");
        Bind(_importBtn, "Import CMS...");

        tools.Controls.Add(_addBtn, 0, 0);
        tools.SetColumnSpan(_addBtn, 2);
        tools.Controls.Add(_editBtn, 2, 0);
        tools.SetColumnSpan(_editBtn, 2);
        tools.Controls.Add(_removeBtn, 4, 0);
        tools.SetColumnSpan(_removeBtn, 2);
        tools.Controls.Add(_testBtn, 0, 1);
        tools.SetColumnSpan(_testBtn, 3);
        tools.Controls.Add(_importBtn, 3, 1);
        tools.SetColumnSpan(_importBtn, 3);
        layout.Controls.Add(tools, 0, 2);

        card.Body.Controls.Add(layout);

        Ui.StyleSecondary(_addBtn);
        Ui.StyleSecondary(_editBtn);
        Ui.StyleSecondary(_removeBtn);
        Ui.StyleSecondary(_testBtn);
        Ui.StyleSecondary(_importBtn);
        _addBtn.ForeColor = Ui.Accent;
        _removeBtn.ForeColor = Color.FromArgb(185, 28, 28);
    }

    private void BuildCamerasCard(ModernCard card)
    {
        AddAllNone(card, _cameraList);

        card.Body.Controls.Add(_cameraSub);
        card.Body.Controls.Add(_cameraList);
        _cameraList.BringToFront();
    }

    private void BuildPeriodCard(ModernCard card)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        Label FieldLabel(string english)
        {
            var l = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.FromArgb(71, 85, 105), Font = Ui.Font(9f, FontStyle.Bold), Margin = new Padding(0, 0, 10, 0) };
            Bind(l, english);
            return l;
        }

        _from.Anchor = _to.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _from.Margin = _to.Margin = new Padding(0);

        table.Controls.Add(FieldLabel("&From"), 0, 0);
        table.Controls.Add(_from, 1, 0);
        table.Controls.Add(FieldLabel("&To"), 0, 1);
        table.Controls.Add(_to, 1, 1);

        var presets = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 6, 0, 0) };
        for (int i = 0; i < 3; i++) presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        presets.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _quickYesterday.Click += (_, _) => { _from.Value = DateTime.Today.AddDays(-1); _to.Value = DateTime.Today; };
        _quickToday.Click += (_, _) => { _from.Value = DateTime.Today; _to.Value = DateTime.Now; };
        _quickWeek.Click += (_, _) => { _from.Value = DateTime.Today.AddDays(-7); _to.Value = DateTime.Today; };

        Bind(_quickYesterday, "Yesterday");
        Bind(_quickToday, "Today");
        Bind(_quickWeek, "Last 7 days");
        Ui.StyleSecondary(_quickYesterday);
        Ui.StyleSecondary(_quickToday);
        Ui.StyleSecondary(_quickWeek);

        presets.Controls.Add(_quickYesterday, 0, 0);
        presets.Controls.Add(_quickToday, 1, 0);
        presets.Controls.Add(_quickWeek, 2, 0);
        table.Controls.Add(presets, 0, 2);
        table.SetColumnSpan(presets, 2);

        card.Body.Controls.Add(table);

        _from.Value = DateTime.Today.AddDays(-1);
        _to.Value = DateTime.Today;
    }

    private void BuildSaveCard(ModernCard card)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _relabel.Add(() =>
        {
            _dest.PlaceholderText = Loc.T("Choose a folder on your external disk");
            _dest.AccessibleName = Loc.T("Save folder");
        });
        table.Controls.Add(_dest, 0, 0);

        var btnRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        btnRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        btnRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Bind(_browseBtn, "Browse...");
        Bind(_openFolder, "Open folder");
        Ui.StyleSecondary(_browseBtn);
        Ui.StyleSecondary(_openFolder);
        _browseBtn.Click += (_, _) => BrowseDestination();
        _openFolder.Click += (_, _) => OpenDestination();

        btnRow.Controls.Add(_browseBtn, 0, 0);
        btnRow.Controls.Add(_openFolder, 1, 0);
        table.Controls.Add(btnRow, 0, 1);

        _destSpaceText.AutoSize = false;
        _destSpaceText.Dock = DockStyle.Fill;
        _destSpaceText.TextAlign = ContentAlignment.MiddleLeft;
        _destSpaceText.AutoEllipsis = true;
        _relabel.Add(UpdateDiskSpace);
        _dest.TextChanged += (_, _) => UpdateDiskSpace();
        table.Controls.Add(_destSpaceText, 0, 2);

        card.Body.Controls.Add(table);
    }

    private void UpdateDiskSpace()
    {
        string path = _dest.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            _destSpaceText.Text = string.Empty;
            return;
        }

        try
        {
            string root = Path.GetPathRoot(path) ?? path;
            if (Directory.Exists(root))
            {
                var drive = new DriveInfo(root);
                long free = drive.AvailableFreeSpace;
                _destSpaceText.Text = Loc.F("{0} free on {1}", DiskCheck.Format(free), root);
                return;
            }
        }
        catch { }
        _destSpaceText.Text = string.Empty;
    }

    private Panel BuildRunBar()
    {
        var run = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0) };
        for (int i = 0; i < 4; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Bind(_find, "5. Find recordings");
        Bind(_start, "6. Download selected");
        Bind(_cancel, "Cancel");
        Bind(_playBtn, "Play video...");

        Ui.StylePrimary(_find);
        Ui.StyleSuccess(_start);
        Ui.StyleDanger(_cancel);
        _cancel.DisabledColor = Color.White;
        _cancel.DisabledForeColor = Ui.Faint;
        Ui.StyleSecondary(_playBtn);
        _playBtn.Font = Ui.Font(9.5f, FontStyle.Bold);

        foreach (var b in new[] { _find, _start, _cancel, _playBtn }) b.Anchor = AnchorStyles.Left;

        table.Controls.Add(_find, 0, 0);
        table.Controls.Add(_start, 1, 0);
        table.Controls.Add(_cancel, 2, 0);
        table.Controls.Add(_playBtn, 3, 0);

        // Overall progress: one line of text above a slim bar
        var progressBox = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(14, 5, 14, 7),
            Margin = new Padding(0, 5, 0, 5),
        };
        progressBox.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, progressBox.Width - 1, progressBox.Height - 1);
        };

        var progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));

        _overallText.AutoEllipsis = true;
        _overallText.Margin = new Padding(0);
        _overall.Margin = new Padding(0);
        progressLayout.Controls.Add(_overallText, 0, 0);
        progressLayout.Controls.Add(_overall, 0, 1);
        progressBox.Controls.Add(progressLayout);

        table.Controls.Add(progressBox, 4, 0);
        run.Controls.Add(table);

        return run;
    }

    private void BuildRecordingsCard()
    {
        _recCard.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _recCard.Width - 1, _recCard.Height - 1);
        };

        // Header strip: title + count on the left, tick shortcuts on the right
        var head = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Color.White };
        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12, 8, 0, 0), BackColor = Color.White };
        var lblTitle = new Label { Font = Ui.Font(10f, FontStyle.Bold), ForeColor = Ui.Ink, AutoSize = true, Margin = new Padding(0, 1, 10, 0) };
        Bind(lblTitle, "Recordings");
        _recCountBadge.Margin = new Padding(0, 0, 0, 0);
        left.Controls.Add(lblTitle);
        left.Controls.Add(_recCountBadge);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 10, 12, 0), BackColor = Color.White };
        Bind(_recSelectAll, "Select all");
        Bind(_recSelectNone, "Select none");
        _recSelectAll.LinkBehavior = _recSelectNone.LinkBehavior = LinkBehavior.HoverUnderline;
        _recSelectAll.ActiveLinkColor = _recSelectNone.ActiveLinkColor = Ui.AccentDark;
        _recSelectAll.VisitedLinkColor = _recSelectNone.VisitedLinkColor = Ui.Accent;
        _recSelectAll.Click += (_, _) => SetTicks(_jobs, true);
        _recSelectNone.Click += (_, _) => SetTicks(_jobs, false);
        actions.Controls.Add(_recSelectAll);
        actions.Controls.Add(new Label { Text = "·", AutoSize = true, ForeColor = Ui.Faint, Margin = new Padding(0) });
        actions.Controls.Add(_recSelectNone);

        head.Controls.Add(left);
        head.Controls.Add(actions);
        left.BringToFront();
        _recCard.Controls.Add(head);

        // DataGridView
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = false;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ScrollBars = ScrollBars.Vertical;
        _grid.RowTemplate.Height = 40;
        _grid.ColumnHeadersHeight = 34;
        _grid.EnableHeadersVisualStyles = false;
        _grid.DefaultCellStyle.Font = Ui.Font(9f);
        _grid.DefaultCellStyle.ForeColor = Ui.Ink;
        _grid.DefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 245, 249);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(71, 85, 105);
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(71, 85, 105);
        _grid.ColumnHeadersDefaultCellStyle.Font = Ui.Font(8.5f, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
        _grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 242, 254);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(15, 23, 42);
        _grid.GridColor = Color.FromArgb(241, 245, 249);

        typeof(DataGridView).InvokeMember("DoubleBuffered",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
            null, _grid, new object[] { true });

        // The check box column has no header text, so it never collides with "Branch" / "Cabang"
        var saveCol = new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(DownloadJob.Selected),
            Width = 40,
            MinimumWidth = 40,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            HeaderText = "",
            SortMode = DataGridViewColumnSortMode.NotSortable,
            FlatStyle = FlatStyle.Flat,
            Resizable = DataGridViewTriState.False,
        };
        saveCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        var problemCol = new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DownloadJob.Error),
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 14,
            MinimumWidth = 40,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _relabel.Add(() => problemCol.HeaderText = Loc.T("Problem"));

        // Columns share the width in proportion, so the table fits the window without a sideways scrollbar.
        _grid.Columns.AddRange(
            saveCol,
            Col("Branch", nameof(DownloadJob.BranchName), 14, 80),
            Col("Camera", nameof(DownloadJob.CameraName), 9, 60),
            Col("Recorded", nameof(DownloadJob.Period), 15, 108),
            Col("Size", nameof(DownloadJob.SizeText), 9, 68),
            Col("Status", nameof(DownloadJob.StatusText), 15, 112),
            Col("Progress", nameof(DownloadJob.Percent), 17, 84),
            Col("Speed", nameof(DownloadJob.SpeedText), 10, 88),
            Col("Time Left", nameof(DownloadJob.LeftText), 10, 82),
            problemCol);

        _grid.DataSource = _jobs;
        _grid.CellPainting += OnGridCellPainting;
        _grid.Controls.Add(_emptyHint);
        _grid.Resize += (_, _) => PositionEmptyHint();
        _jobs.ListChanged += (_, _) => PositionEmptyHint();

        _recCard.Controls.Add(_grid);
        _grid.BringToFront();
    }

    private void BuildLogCard()
    {
        _logCard.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(30, 41, 59), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _logCard.Width - 1, _logCard.Height - 1);
        };

        var head = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(12, 4, 8, 4) };
        var dot = new Label { Text = "●", ForeColor = Color.FromArgb(34, 197, 94), AutoSize = true, Location = new Point(12, 8), Font = Ui.Font(8f) };
        var title = new Label { ForeColor = Color.FromArgb(241, 245, 249), AutoSize = true, Location = new Point(28, 7), Font = Ui.Font(9f, FontStyle.Bold) };
        Bind(title, "Activity log");
        Bind(_clearLogBtn, "Clear");

        _clearLogBtn.NormalColor = Color.FromArgb(30, 41, 59);
        _clearLogBtn.HoverColor = Color.FromArgb(51, 65, 85);
        _clearLogBtn.ForeColor = Color.FromArgb(203, 213, 225);
        _clearLogBtn.BorderColor = Color.FromArgb(51, 65, 85);
        _clearLogBtn.Font = Ui.Font(8.5f);
        _clearLogBtn.Dock = DockStyle.Right;
        _clearLogBtn.Click += (_, _) => _logBox.Clear();

        head.Controls.Add(dot);
        head.Controls.Add(title);
        head.Controls.Add(_clearLogBtn);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42), Padding = new Padding(12, 0, 6, 6) };
        host.Controls.Add(_logBox);

        _logCard.Controls.Add(head);
        _logCard.Controls.Add(host);
        host.BringToFront();
    }

    private void BuildUpdateBanner()
    {
        _updateBanner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _updateBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _updateBanner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _updateBanner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Ui.StyleSuccess(_updateActionBtn);
        _updateActionBtn.Font = Ui.Font(8.5f, FontStyle.Bold);
        _updateActionBtn.Click += (_, _) => OpenUpdateDialog();

        _updateCloseBtn.NormalColor = Color.Transparent;
        _updateCloseBtn.HoverColor = Color.FromArgb(220, 252, 231);
        _updateCloseBtn.ForeColor = Color.FromArgb(22, 101, 52);
        _updateCloseBtn.BorderColor = Color.Transparent;
        _updateCloseBtn.Font = Ui.Font(9f, FontStyle.Bold);
        _updateCloseBtn.Click += (_, _) => _updateBanner.Visible = false;

        _updateBanner.Controls.Add(_updateIcon, 0, 0);
        _updateBanner.Controls.Add(_updateText, 1, 0);
        _updateBanner.Controls.Add(_updateActionBtn, 2, 0);
        _updateBanner.Controls.Add(_updateCloseBtn, 3, 0);

        _updateBanner.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(134, 239, 172), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _updateBanner.Width - 1, _updateBanner.Height - 1);

            using var stripeBrush = new SolidBrush(Color.FromArgb(22, 163, 74));
            e.Graphics.FillRectangle(stripeBrush, 0, 0, Ui.S(_updateBanner, 4), _updateBanner.Height);
        };

        _relabel.Add(RefreshUpdateBannerText);
    }

    private void RefreshUpdateBannerText()
    {
        _updateActionBtn.Text = Loc.T("Update Now");
        if (_pendingUpdate is not null)
        {
            _updateText.Text = Loc.F("Update available: v{0} — A newer version of CCTV Backup is ready.", AppUpdater.FormatVersion(_pendingUpdate.Version));
        }
    }

    private void OpenUpdateDialog()
    {
        if (_pendingUpdate is null) return;
        using var dlg = new UpdateDialog(_pendingUpdate);
        dlg.ShowDialog(this);
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (Environment.GetCommandLineArgs().Contains("--screenshot", StringComparer.OrdinalIgnoreCase)) return;
        var release = await AppUpdater.TryCheckForUpdateSilentAsync().ConfigureAwait(false);
        if (release is null) return;

        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() =>
            {
                if (IsDisposed) return;
                _pendingUpdate = release;
                RefreshUpdateBannerText();
                _updateBanner.Visible = true;
            });
        }
    }

    private void BuildGuide()
    {
        _guide.BackColor = Color.FromArgb(239, 246, 255);
        _guideIcon.ForeColor = Color.FromArgb(37, 99, 235);
        _guideTitle.Font = Ui.Font(10.5f, FontStyle.Bold);
        _guideText.Font = Ui.Font(9f);
        _guideText.ForeColor = Ui.Text;

        var texts = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        texts.Controls.Add(_guideTitle, 0, 0);
        texts.Controls.Add(_guideText, 0, 1);

        _guide.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _guide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _guide.Controls.Add(_guideIcon, 0, 0);
        _guide.Controls.Add(texts, 1, 0);

        _guide.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.FromArgb(191, 219, 254), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, _guide.Width - 1, _guide.Height - 1);

            using var stripeBrush = new SolidBrush(_guideAccent);
            e.Graphics.FillRectangle(stripeBrush, 0, 0, Ui.S(_guide, 4), _guide.Height);
        };
    }

    private DataGridViewTextBoxColumn Col(string header, string prop, float weight, int minWidth)
    {
        var col = new DataGridViewTextBoxColumn
        {
            DataPropertyName = prop,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = weight,
            MinimumWidth = minWidth,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _relabel.Add(() => col.HeaderText = Loc.T(header));
        return col;
    }

    // ------------------------------------------------------------------ Custom Painting

    private void OnGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _jobs.Count || e.Graphics is null) return;
        var job = _jobs[e.RowIndex];
        int S(int px) => Ui.S(_grid, px);

        // Camera column -> rounded chip
        if (e.ColumnIndex == 2)
        {
            e.PaintBackground(e.CellBounds, true);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = Math.Min(S(58), e.CellBounds.Width - S(8)), h = S(22);
            var chipRect = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - w) / 2, e.CellBounds.Y + (e.CellBounds.Height - h) / 2, w, h);
            using var path = Ui.RoundedRect(chipRect, S(6));
            using var bg = new SolidBrush(Color.FromArgb(241, 245, 249));
            using var pen = new Pen(Color.FromArgb(226, 232, 240), 1);
            g.FillPath(bg, path);
            g.DrawPath(pen, path);

            TextRenderer.DrawText(g, job.CameraName, Ui.Font(8.5f, FontStyle.Bold), chipRect,
                Color.FromArgb(51, 65, 85), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            e.Handled = true;
            return;
        }

        // Recorded column -> 2 lines (time range bold, date gray), the weekday in the chosen language
        if (e.ColumnIndex == 3)
        {
            e.PaintBackground(e.CellBounds, true);
            var g = e.Graphics;

            var culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.IsIndonesian ? "id-ID" : "en-US");
            string timeRange = $"{job.File.Begin:HH:mm} - {job.File.End:HH:mm}";
            string dateStr = job.File.Begin.ToString("ddd, yyyy-MM-dd", culture);

            int half = e.CellBounds.Height / 2;
            var timeRect = new Rectangle(e.CellBounds.X + S(8), e.CellBounds.Y + S(3), e.CellBounds.Width - S(12), half - S(1));
            var dateRect = new Rectangle(e.CellBounds.X + S(8), e.CellBounds.Y + half, e.CellBounds.Width - S(12), half - S(3));

            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, timeRange, Ui.Font(9f, FontStyle.Bold), timeRect, Color.FromArgb(15, 23, 42), flags);
            TextRenderer.DrawText(g, dateStr, Ui.Font(8f), dateRect, Color.FromArgb(100, 116, 139), flags);

            e.Handled = true;
            return;
        }

        // Status column -> bullet dot + status
        if (e.ColumnIndex == 5)
        {
            e.PaintBackground(e.CellBounds, true);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            (Color dotColor, Color textColor, string label) = job.Status switch
            {
                JobStatus.Done => (Color.FromArgb(22, 163, 74), Color.FromArgb(22, 163, 74), Loc.T("Done")),
                JobStatus.Downloading => (Color.FromArgb(37, 99, 235), Color.FromArgb(37, 99, 235), Loc.T("Downloading")),
                JobStatus.Skipped => (Color.FromArgb(148, 163, 184), Color.FromArgb(100, 116, 139), Loc.T("Already saved")),
                JobStatus.Pending when !job.Selected => (Color.FromArgb(203, 213, 225), Color.FromArgb(148, 163, 184), Loc.T("Not selected")),
                JobStatus.Pending => (Color.FromArgb(79, 70, 229), Color.FromArgb(71, 85, 105), Loc.T("Waiting")),
                JobStatus.Failed => (Color.FromArgb(220, 38, 38), Color.FromArgb(185, 28, 28), Loc.T("Failed")),
                JobStatus.Cancelled => (Color.FromArgb(217, 119, 6), Color.FromArgb(180, 83, 9), Loc.T("Cancelled")),
                _ => (Color.FromArgb(148, 163, 184), Color.FromArgb(100, 116, 139), job.StatusText)
            };

            int dot = S(8);
            using (var dotBrush = new SolidBrush(dotColor))
                g.FillEllipse(dotBrush, e.CellBounds.X + S(8), e.CellBounds.Y + (e.CellBounds.Height - dot) / 2, dot, dot);

            var textRect = new Rectangle(e.CellBounds.X + S(22), e.CellBounds.Y, Math.Max(10, e.CellBounds.Width - S(24)), e.CellBounds.Height);
            TextRenderer.DrawText(g, label, Ui.Font(8.5f, FontStyle.Bold), textRect,
                textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            e.Handled = true;
            return;
        }

        // Progress column -> in-cell rounded progress bar
        if (e.ColumnIndex == 6)
        {
            e.PaintBackground(e.CellBounds, true);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int textW = S(40), barH = S(8);
            var trackRect = new Rectangle(e.CellBounds.X + S(8), e.CellBounds.Y + (e.CellBounds.Height - barH) / 2, Math.Max(S(20), e.CellBounds.Width - S(12) - textW), barH);
            using (var trackPath = Ui.RoundedRect(trackRect, barH / 2))
            using (var trackBrush = new SolidBrush(Color.FromArgb(226, 232, 240)))
            {
                g.FillPath(trackBrush, trackPath);
            }

            int pct = Math.Clamp(job.Percent, 0, 100);
            if (pct > 0)
            {
                int barWidth = Math.Max(barH, (int)(trackRect.Width * (pct / 100.0)));
                var barRect = new Rectangle(trackRect.X, trackRect.Y, barWidth, trackRect.Height);
                Color barColor = job.Status == JobStatus.Done ? Color.FromArgb(22, 163, 74) : Color.FromArgb(37, 99, 235);
                using var barPath = Ui.RoundedRect(barRect, barH / 2);
                using var barBrush = new SolidBrush(barColor);
                g.FillPath(barBrush, barPath);
            }

            var textRect = new Rectangle(trackRect.Right + S(4), e.CellBounds.Y, textW, e.CellBounds.Height);
            TextRenderer.DrawText(g, $"{pct}%", Ui.Font(8f, FontStyle.Bold), textRect,
                Color.FromArgb(100, 116, 139), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            e.Handled = true;
            return;
        }

        // Speed column -> subtle blue text
        if (e.ColumnIndex == 7 && !string.IsNullOrEmpty(job.SpeedText))
        {
            e.PaintBackground(e.CellBounds, true);
            var cell = Rectangle.Inflate(e.CellBounds, -S(6), 0);
            TextRenderer.DrawText(e.Graphics, job.SpeedText, Ui.Font(8.5f, FontStyle.Bold), cell,
                Color.FromArgb(37, 99, 235), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            e.Handled = true;
            return;
        }
    }

    private static void SetAll(CheckList box, bool value)
    {
        for (int i = 0; i < box.Items.Count; i++) box.SetItemChecked(i, value);
    }

    // ------------------------------------------------------------------ Event Wiring

    private void WireEvents()
    {
        _branchList.SelectedIndexChanged += (_, _) => ShowCamerasOfSelectedBranch();
        _cameraList.ItemCheck += OnCameraChecked;
        _find.Click += async (_, _) => await FindAsync();
        _start.Click += async (_, _) => await DownloadSelectedAsync();
        _grid.CellBeginEdit += OnGridCellBeginEdit;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (_, e) => { if (e.RowIndex >= 0) UpdateSelectionSummary(); };
        _grid.KeyDown += OnGridKeyDown;

        _branchList.ItemCheck += (_, e) =>
        {
            if (_fillingBranches) return;
            string? name = _branchList.Items[e.Index]?.ToString();
            if (name is not null)
            {
                if (e.NewValue == CheckState.Checked) _ticked.Add(name); else _ticked.Remove(name);
            }
            MarkStale();
            if (e.NewValue == CheckState.Checked) BeginInvoke(() => { if (e.Index < _branchList.Items.Count) _branchList.SelectedIndex = e.Index; });
            RequestGuideUpdate();
        };

        _from.ValueChanged += (_, _) => MarkStale();
        _to.ValueChanged += (_, _) => MarkStale();
        _dest.TextChanged += (_, _) => { MarkStale(); RequestGuideUpdate(); };

        _cancel.Click += (_, _) => { _cts?.Cancel(); _cancel.Enabled = false; Append(Loc.T("Stopping after the current step...")); };
        _playBtn.Click += (_, _) => PlayHighlightedOrChoose();
        _grid.CellDoubleClick += OnGridCellDoubleClick;
        _addBtn.Click += (_, _) => AddBranch();
        _editBtn.Click += (_, _) => EditBranch();
        _removeBtn.Click += (_, _) => RemoveBranch();
        _branchList.DoubleClick += (_, _) => EditBranch();
        _testBtn.Click += async (_, _) => await TestConnectionAsync();
        _importBtn.Click += (_, _) => ImportFromCms();
        _tick.Tick += (_, _) => RefreshProgress();
        FormClosing += OnFormClosing;
        Shown += (_, _) => PositionEmptyHint();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F5 when _find.Enabled:
                _find.PerformClick();
                return true;
            case Keys.Control | Keys.F:
                _branchSearch.Focus();
                return true;
            case Keys.Control | Keys.L:
                LockApplication();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnFirstShow()
    {
        _dest.Text = _settings.LastDestination;
        EnsureSdkFolder();
        LoadBranches();
        _ = CheckForUpdatesOnStartupAsync();
    }

    private void EnsureSdkFolder()
    {
        if (_sdkDir is not null) return;

        _start.Enabled = false;
        Append(Loc.T("Backups are disabled: the DVR driver (NetSdk.dll) was not found."));
        if (Environment.GetCommandLineArgs().Contains("--screenshot", StringComparer.OrdinalIgnoreCase)) return;
        MessageBox.Show(this,
            Loc.T("The DVR driver (NetSdk.dll) was not found. It should be in the \"sdk\" folder next to this program.\n\nCopy the \"sdk\" folder from where the program was supplied, then start the program again."),
            "CCTV Backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void LoadBranches()
    {
        _branches = _book.All;
        FillBranchList(_settings.LastBranch);
        if (_branches.Count == 0)
            Append(Loc.T("No branches yet. Press \"Add\" to add the first one."));
    }

    /// <summary>Names of the ticked branches that still exist (also the ones a search currently hides).</summary>
    private List<string> TickedBranchNames() =>
        _branches.Where(b => _ticked.Contains(b.Name)).Select(b => b.Name).ToList();

    private void UpdateBranchEmptyText()
    {
        _branchList.EmptyText = _branches.Count == 0
            ? Loc.T("No branches yet. Press \"Add\" to add the first one.")
            : _branchList.Items.Count == 0 ? Loc.T("No branch matches your search.") : string.Empty;
    }

    private void UpdateBranchGroupTitle() =>
        _cardBranches.CardTitle = _branches.Count == 0
            ? Loc.T("Branches (none yet)")
            : Loc.T("Branches");

    private void FilterBranchList()
    {
        string query = _branchSearch.Text.Trim();
        string? keep = SelectedBranch?.Name;

        _fillingBranches = true;
        try
        {
            _branchList.Reset();
            var matches = string.IsNullOrEmpty(query)
                ? _branches
                : _branches.Where(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                     || b.CloudId.Contains(query, StringComparison.OrdinalIgnoreCase)
                                     || b.Address.Contains(query, StringComparison.OrdinalIgnoreCase));

            foreach (var b in matches)
            {
                _branchList.AddItem(b.Name, _ticked.Contains(b.Name));
            }
            int again = keep is null ? -1 : _branchList.Items.IndexOf(keep);
            if (again >= 0) _branchList.SelectedIndex = again;
        }
        finally
        {
            _fillingBranches = false;
        }
        UpdateBranchEmptyText();
    }

    private void FillBranchList(string? select)
    {
        _ticked.IntersectWith(_branches.Select(b => b.Name));

        FilterBranchList();

        _fillingBranches = true;
        try
        {
            if (_branchList.Items.Count > 0)
            {
                int idx = select is null ? 0 : _branchList.Items.IndexOf(select);
                _branchList.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                _cameraList.Reset();
            }
        }
        finally { _fillingBranches = false; }

        UpdateBranchEmptyText();
        ShowCamerasOfSelectedBranch();
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
        _find.Enabled = has && TickedBranchNames().Count > 0 && !_running && _sdkDir is not null;
        _start.Enabled = listReady && anyTicked && !_running && _sdkDir is not null;
        _recSelectAll.Enabled = _recSelectNone.Enabled = _jobs.Count > 0 && !_running;
        UpdateGuide();
    }

    // ------------------------------------------------------------------ Branch Actions

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
        var credential = dlg.ResultCredential.Password.Length == 0 && saved is not null
            ? new Credential(dlg.ResultCredential.Username, saved.Password)
            : dlg.ResultCredential;

        if (current.Name != updated.Name)
        {
            _store.Rename(current.Name, updated.Name);
            if (_ticked.Remove(current.Name)) _ticked.Add(updated.Name);
            if (_cameraChoice.Remove(current.Name, out var choice)) _cameraChoice[updated.Name] = choice;
        }
        else if (updated.Cameras.Count != current.Cameras.Count)
        {
            _cameraChoice.Remove(current.Name);
        }

        _store.Set(updated.Name, credential);
        _book.Update(current.Name, updated);
        _branches = _book.All;
        FillBranchList(updated.Name);
        MarkStale();
        Append(Loc.F("Saved changes to {0}.", updated.Name));
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

    private void ApplyImport(List<Branch> selected, string password)
    {
        int added = 0, withPassword = 0;
        foreach (var branch in selected)
        {
            try { _book.Add(branch); }
            catch (InvalidOperationException) { continue; }
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
                Loc.F("Imported {0} branch(es).\n\nThe CMS passwords are not imported. For each branch without a password: select it, press Edit and enter it, then press Test connection.", added),
                Loc.T("Import from CMS"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void UpdateDetectedCameras(Branch branch, int count)
    {
        var updated = branch with { Cameras = Camera.Range(branch.Name, count) };
        _book.Update(branch.Name, updated);
        _cameraChoice.Remove(branch.Name);
        _branches = _book.All;
        FillBranchList(_settings.LastBranch.Length > 0 ? _settings.LastBranch : branch.Name);
        MarkStale();
        Append(Loc.F("{0}: camera list updated ({1} cameras).", branch.Name, count));
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
            _cameraList.Reset();
            if (branch is null)
            {
                _cameraList.EmptyText = Loc.T("Cameras (click a branch)");
                _cameraList.Enabled = false;
                return;
            }
            _settings.LastBranch = branch.Name;

            if (!branch.CamerasDetected)
            {
                _cameraList.EmptyText = Loc.T("Cameras not detected yet - press Test connection");
                _cameraList.Enabled = false;
                return;
            }
            _cameraList.EmptyText = string.Empty;
            _cameraList.Enabled = !_running;

            var chosen = _cameraChoice.TryGetValue(branch.Name, out var set) ? set : null;
            foreach (var cam in branch.Cameras)
                _cameraList.AddItem(Loc.F("Camera {0}", cam.Channel + 1), chosen is null || chosen.Contains(cam.Channel));
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

    private void OpenDestination()
    {
        string path = _dest.Text.Trim();
        if (path.Length > 0 && Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else
            MessageBox.Show(this, Loc.T("The save folder does not exist yet."), "CCTV Backup");
    }

    // ------------------------------------------------------------------ Video Playback

    private PlayerForm? _player;

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex <= 0) return;
        if (_grid.Rows[e.RowIndex].DataBoundItem is DownloadJob job) PlayVideo(job.FinalPath);
    }

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

    // ------------------------------------------------------------------ Core Search & Download

    private List<BackupRequest> BuildRequests()
    {
        var list = new List<BackupRequest>();
        foreach (string name in TickedBranchNames())
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

    private (List<BackupRequest> Requests, string Dest)? ValidateInputs()
    {
        var requests = BuildRequests();
        var problems = RequestValidator.Validate(requests, _dest.Text.Trim());
        var missing = requests.Where(r => _store.Get(r.Branch.Name) is null).Select(r => r.Branch.Name).ToList();
        if (missing.Count > 0)
            problems.Add(Loc.F("No password saved for: {0}. Select the branch, press Edit and enter it.", string.Join(", ", missing)));
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

    // ------------------------------------------------------------------ Ticking & Keyboard Shortcuts

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
        if (_running || !_jobs[e.RowIndex].CanChangeSelection) e.Cancel = true;
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || _running) return;
        var rows = HighlightedJobs().Where(j => j.CanChangeSelection).ToList();
        if (rows.Count == 0) return;
        SetTicks(rows, !rows[0].Selected);
        e.Handled = e.SuppressKeyPress = true;
    }

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

        _recCountBadge.Text = Loc.F("{0} files", _jobs.Count);

        if (_listStale)
        {
            _overallText.Text = Loc.T("Choices changed - press Find recordings");
        }
        else if (_plans is null && _jobs.Count == 0)
        {
            _overallText.Text = Loc.T("Ready.");
        }
        else
        {
            string size = DiskCheck.Format(chosen.Sum(j => j.SizeKb) * 1024);
            int done = _jobs.Count(j => j.Status == JobStatus.Done);
            string count = chosen.Count == 0 && selectable == 0 && done > 0
                ? Loc.F("{0} recordings saved", done)
                : Loc.F("{0} of {1} recordings ticked ({2})", chosen.Count, selectable, size);
            _overallText.Text = count + (saved > 0 ? Loc.F(", {0} already saved", saved) : "");
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
            MessageBox.Show(this, Loc.T("No password saved for this branch yet. Press Edit and enter it."), "CCTV Backup");
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

    private string OverallSpeedText()
    {
        if (_runJobs is not { Count: > 0 } || _runStartedUtc == default) return string.Empty;

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
            e.Cancel = true;
            return;
        }
        _client.Dispose();
    }

    // ------------------------------------------------------------------ Guidance Strip

    private void RequestGuideUpdate()
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(UpdateButtons);
    }

    private void UpdateGuide()
    {
        var ticked = TickedBranchNames()
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
                text = Loc.T("Press \"Add\" in the Branches box and type its name, Serial ID, user name and password. If the CMS is on this PC, \"Import CMS\" brings all its branches in at once.");
                break;
            case GuideStep.TickBranch:
                title = Loc.T("Step 1: tick the branches to back up");
                text = Loc.T("Tick the box in front of each branch in the list below on the left.");
                break;
            case GuideStep.CamerasUnknown:
                tone = Tone.Warn;
                title = Loc.F("Step 2: read the cameras of {0}", unknownName);
                text = Loc.T("The program does not know the cameras of this branch yet. It reads them from the DVR when you press \"Test connection\" in the Branches box.");
                break;
            case GuideStep.TickCamera:
                tone = Tone.Warn;
                title = Loc.T("Step 2: tick at least one camera");
                text = Loc.T("Click a branch to see its cameras, then tick the ones you want.");
                break;
            case GuideStep.ChooseFolder:
                title = Loc.T("Step 4: choose where to save the backup");
                text = Loc.T("Press \"Browse...\" in the \"Save to\" box and pick a folder on your external disk.");
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
            default:
                tone = Tone.Go;
                title = Loc.F("Done: {0} recordings saved", s.Done);
                text = Loc.F("They are in {0}. Watch them with \"Play video\", or search another period.", _planDestination.Length > 0 ? _planDestination : _dest.Text.Trim());
                break;
        }

        (Color back, Color accent) = tone switch
        {
            Tone.Go => (Color.FromArgb(240, 253, 244), Color.FromArgb(22, 163, 74)),
            Tone.Warn => (Color.FromArgb(254, 252, 232), Color.FromArgb(202, 138, 4)),
            Tone.Busy => (Color.FromArgb(241, 245, 249), Color.FromArgb(71, 85, 105)),
            _ => (Color.FromArgb(239, 246, 255), Color.FromArgb(37, 99, 235)),
        };

        _guide.BackColor = back;
        _guideAccent = accent;
        _guideIcon.ForeColor = accent;
        _guideTitle.ForeColor = accent;
        _guideTitle.Text = title;
        _guideText.Text = text;
        _guide.Invalidate();
    }

    private void UpdateCameraGroupTitle(Branch? branch)
    {
        _cameraSub.Text = branch is null ? string.Empty : Loc.F("Cameras of {0}", branch.Name);
        _cameraSub.Visible = branch is not null;
    }

    private void PositionEmptyHint()
    {
        _emptyHint.Text = Loc.T("No recordings yet.\nTick one or more branches, set the date range, then click \"Find recordings\".");
        _emptyHint.Visible = _jobs.Count == 0;
        if (!_emptyHint.Visible) return;
        _emptyHint.MaximumSize = new Size(Math.Max(100, _grid.ClientSize.Width - 40), 0);
        var size = _emptyHint.PreferredSize;
        int top = _grid.ColumnHeadersHeight + Math.Max(20, (_grid.ClientSize.Height - _grid.ColumnHeadersHeight - size.Height) / 3);
        _emptyHint.Location = new Point(Math.Max(0, (_grid.ClientSize.Width - size.Width) / 2), top);
    }

    /// <summary>Switches the language without saving it (used by the --screenshot mode).</summary>
    internal void PreviewLanguage(string code)
    {
        Loc.Set(code);
        _settingLanguage = true;
        _language.SelectedIndex = Loc.IsIndonesian ? 1 : 0;
        _settingLanguage = false;
        ApplyLanguage();
    }

    internal void AddTestJob(DownloadJob job)
    {
        _jobs.Add(job);
        UpdateSelectionSummary();
    }

    // ------------------------------------------------------------------ App Lock & Account Handlers

    private void LockApplication()
    {
        using var login = new LoginForm();
        if (login.ShowDialog(this) != DialogResult.OK)
        {
            Close();
        }
    }

    private void OpenAccountSettings()
    {
        using var account = new AccountForm();
        account.ShowDialog(this);
        if (account.RequestLock)
        {
            LockApplication();
        }
        else
        {
            UpdateAppBarTexts();
        }
    }

    private void CopyActivityLog()
    {
        try
        {
            string text = _logBox.Text;
            if (!string.IsNullOrEmpty(text))
            {
                Clipboard.SetText(text);
                _copyLogBtn.Text = "✓ " + Loc.T("Copied!");
                Task.Delay(1500).ContinueWith(_ =>
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(UpdateAppBarTexts);
                });
            }
        }
        catch { }
    }

    // ------------------------------------------------------------------ Tooltips & Logging

    private void SetupTooltips()
    {
        void Tip(Control c, string english) => _relabel.Add(() => _tips.SetToolTip(c, Loc.T(english)));
        Tip(_addBtn, "Add a branch: its name, Serial ID or IP address, user name and password.");
        Tip(_editBtn, "Change the settings or the password of the highlighted branch.");
        Tip(_removeBtn, "Remove the highlighted branch from this program. Nothing is deleted from the DVR or from your backups.");
        Tip(_testBtn, "Check that the highlighted branch answers and the login works. This also reads how many cameras it has.");
        Tip(_importBtn, "Copy the branch list from the CMS program installed on this PC.");
        Tip(_find, "Ask each ticked branch which recordings it has for this period. Nothing is downloaded yet. (F5)");
        Tip(_start, "Save the ticked recordings to the folder above, one after the other.");
        Tip(_cancel, "Stop after the current step. Files that are finished are kept.");
        Tip(_openFolder, "Open the save folder in Windows Explorer.");
        Tip(_playBtn, "Watch a saved recording in the built-in player.");
        Tip(_dest, "The folder where the backup is saved, usually on your external disk.");
        Tip(_dest.InnerTextBox, "The folder where the backup is saved, usually on your external disk.");
        Tip(_from, "Start of the period, by the DVR's own clock.");
        Tip(_to, "End of the period, by the DVR's own clock.");
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
