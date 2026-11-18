using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;
using CctvBackup.Core.Settings;

namespace CctvBackup.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly BranchBook _book;
    private readonly CredentialStore _credentials;
    private readonly FileLog _fileLog;
    private readonly AppSettings _settings;
    private LinuxDvrClient _dvrClient;
    private readonly Dictionary<string, HashSet<int>> _tickedCameras = new(StringComparer.OrdinalIgnoreCase);

    private BranchItemViewModel? _selectedBranch;
    private string _branchFilter = string.Empty;
    private DateTimeOffset _fromDate;
    private TimeSpan _fromTime;
    private DateTimeOffset _toDate;
    private TimeSpan _toTime;
    private string _destination = string.Empty;
    private string _freeDiskSpaceText = string.Empty;
    private string _summaryText = string.Empty;
    private string _guideText = string.Empty;
    private bool _isBusy;
    private bool _isSearching;
    private bool _isDownloading;
    private CancellationTokenSource? _activeCts;
    private IReadOnlyList<BranchPlan>? _plans;
    private DispatcherTimer? _liveTimer;

    public ObservableCollection<BranchItemViewModel> Branches { get; } = new();
    public ObservableCollection<BranchItemViewModel> FilteredBranches { get; } = new();
    public ObservableCollection<CameraItemViewModel> Cameras { get; } = new();
    public ObservableCollection<RecordingItemViewModel> Recordings { get; } = new();
    public ObservableCollection<string> Logs { get; } = new();

    public MainWindowViewModel()
    {
        _settings = AppSettings.Load(AppPaths.SettingsFile);
        Loc.Set(Loc.Resolve(_settings.Language));
        _book = new BranchBook(AppPaths.BranchesFile);
        _credentials = new CredentialStore(AppPaths.CredentialsFile);
        _fileLog = new FileLog(AppPaths.LogDirectory);

        string sdkDir = SdkLocator.Find(_settings.SdkDirectory) ?? SdkLocator.BundledDirectory;
        _dvrClient = new LinuxDvrClient(sdkDir);
        _dvrClient.Notice += OnDvrNotice;

        _destination = !string.IsNullOrWhiteSpace(_settings.LastDestination)
            ? _settings.LastDestination
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        var now = DateTime.Now;
        var today = now.Date;
        FromDate = today;
        FromTime = new TimeSpan(8, 0, 0);
        ToDate = today;
        ToTime = now > today.AddHours(8)
            ? new TimeSpan(now.Hour, now.Minute, 0)
            : new TimeSpan(17, 0, 0);

        ReloadBranches();
        UpdateGuideText();
        UpdateDiskSpace();

        AppendLog($"{DateTime.Now:HH:mm:ss}  CCTV Backup (Linux Native Edition) started.");
        AppendLog($"{DateTime.Now:HH:mm:ss}  Active DVR Backend: {_dvrClient.ActiveBackendDescription}");

        _liveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _liveTimer.Tick += (s, e) => OnLiveTick();
        _liveTimer.Start();

        Recordings.CollectionChanged += (s, e) => OnRecordingsChanged();
    }

    public string BackendDescription => _dvrClient.ActiveBackendDescription;

    public BranchItemViewModel? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (SetProperty(ref _selectedBranch, value))
            {
                LoadCamerasForSelectedBranch();
            }
        }
    }

    public string BranchFilter
    {
        get => _branchFilter;
        set
        {
            if (SetProperty(ref _branchFilter, value))
            {
                ApplyBranchFilter();
            }
        }
    }

    public DateTimeOffset FromDate
    {
        get => _fromDate;
        set { if (SetProperty(ref _fromDate, value)) OnPropertyChanged(nameof(FromDay)); }
    }

    public TimeSpan FromTime
    {
        get => _fromTime;
        set { if (SetProperty(ref _fromTime, value)) OnPropertyChanged(nameof(FromTimeText)); }
    }

    public DateTimeOffset ToDate
    {
        get => _toDate;
        set { if (SetProperty(ref _toDate, value)) OnPropertyChanged(nameof(ToDay)); }
    }

    public TimeSpan ToTime
    {
        get => _toTime;
        set { if (SetProperty(ref _toTime, value)) OnPropertyChanged(nameof(ToTimeText)); }
    }

    /// <summary>CalendarDatePicker-friendly wrapper of <see cref="FromDate"/>.</summary>
    public DateTime? FromDay
    {
        get => _fromDate.Date;
        set { if (value.HasValue) FromDate = value.Value.Date; else OnPropertyChanged(); }
    }

    /// <summary>CalendarDatePicker-friendly wrapper of <see cref="ToDate"/>.</summary>
    public DateTime? ToDay
    {
        get => _toDate.Date;
        set { if (value.HasValue) ToDate = value.Value.Date; else OnPropertyChanged(); }
    }

    /// <summary>"HH:mm" text wrapper of <see cref="FromTime"/> (invalid input is reverted).</summary>
    public string FromTimeText
    {
        get => FormatTime(_fromTime);
        set { if (TryParseTime(value, out var t)) FromTime = t; OnPropertyChanged(); }
    }

    /// <summary>"HH:mm" text wrapper of <see cref="ToTime"/> (invalid input is reverted).</summary>
    public string ToTimeText
    {
        get => FormatTime(_toTime);
        set { if (TryParseTime(value, out var t)) ToTime = t; OnPropertyChanged(); }
    }

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours % 24:00}:{t.Minutes:00}";

    private static bool TryParseTime(string? text, out TimeSpan result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Replace('.', ':').Split(':');
        if (parts.Length is < 1 or > 3) return false;
        if (!int.TryParse(parts[0], out int h) || h is < 0 or > 23) return false;
        int m = 0;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out m) || m is < 0 or > 59)) return false;
        result = new TimeSpan(h, m, 0);
        return true;
    }

    // ---- recordings overview (bound by the table header / action bar) ----
    private double _overallPercent;
    private string _overallProgressText = string.Empty;
    private bool _showOverallProgress;

    public bool HasRecordings => Recordings.Count > 0;
    public bool ShowEmptyState => Recordings.Count == 0 && !IsSearching;
    public bool ShowSearchingState => Recordings.Count == 0 && IsSearching;
    public string RecordingCountText => Recordings.Count == 1 ? "1 file" : $"{Recordings.Count} files";

    public double OverallPercent
    {
        get => _overallPercent;
        private set => SetProperty(ref _overallPercent, value);
    }

    public string OverallProgressText
    {
        get => _overallProgressText;
        private set => SetProperty(ref _overallProgressText, value);
    }

    public bool ShowOverallProgress
    {
        get => _showOverallProgress;
        private set => SetProperty(ref _showOverallProgress, value);
    }

    private void OnRecordingsChanged()
    {
        OnPropertyChanged(nameof(HasRecordings));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSearchingState));
        OnPropertyChanged(nameof(RecordingCountText));
        OnPropertyChanged(nameof(CanDownload));
        UpdateSummary();
    }

    public string Destination
    {
        get => _destination;
        set
        {
            if (SetProperty(ref _destination, value))
            {
                _settings.LastDestination = value;
                _settings.Save(AppPaths.SettingsFile);
                UpdateDiskSpace();
            }
        }
    }

    public string FreeDiskSpaceText
    {
        get => _freeDiskSpaceText;
        private set => SetProperty(ref _freeDiskSpaceText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string GuideText
    {
        get => _guideText;
        private set => SetProperty(ref _guideText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanFind));
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool IsSearching
    {
        get => _isSearching;
        private set
        {
            if (SetProperty(ref _isSearching, value))
            {
                IsBusy = _isSearching || _isDownloading;
                UpdateGuideText();
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ShowSearchingState));
            }
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (SetProperty(ref _isDownloading, value))
            {
                IsBusy = _isSearching || _isDownloading;
                UpdateGuideText();
            }
        }
    }

    public bool CanFind => !IsBusy;
    public bool CanDownload => !IsBusy && Recordings.Count > 0 && Recordings.Any(r => r.IsSelected);
    public bool CanCancel => IsBusy;

    public BranchBook Book => _book;
    public CredentialStore Credentials => _credentials;
    public LinuxDvrClient DvrClient => _dvrClient;

    public async Task<(bool Ok, string Message, int Channels, string Model, string Serial)> TestConnectionAsync(string branchName)
    {
        var branch = _book.Find(branchName);
        if (branch is null) return (false, "Branch not found.", 0, "", "");
        var cred = _credentials.Get(branch.Name);
        if (cred is null)
            return (false, "No password saved for this branch. Click Edit... and enter it.", 0, "", "");

        try
        {
            AppendLog($"{DateTime.Now:HH:mm:ss}  {branch.Name}: {branch.ConnectingText}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var conn = await _dvrClient.ConnectAsync(branch, cred, cts.Token);
            int ch = conn.Info.ChannelCount;
            if (ch > 0 && ch != branch.Cameras.Count)
            {
                var updated = branch with { Cameras = Camera.Range(branch.Name, ch) };
                _book.Update(branch.Name, updated);
                _tickedCameras[branch.Name] = updated.Cameras.Select(c => c.Channel).ToHashSet();
                ReloadBranches();
            }

            string msg = $"{branch.Name}: ONLINE, login OK! {ch} cameras detected.";
            AppendLog($"{DateTime.Now:HH:mm:ss}  {msg}");
            return (true, msg, ch > 0 ? ch : branch.Cameras.Count, conn.Info.Model, conn.Info.SerialNumber);
        }
        catch (Exception ex)
        {
            AppendLog($"{DateTime.Now:HH:mm:ss}  {branch.Name}: {ex.Message}");
            return (false, ex.Message, 0, "", "");
        }
    }

    public void ReloadBranches()
    {
        Branches.Clear();
        foreach (var b in _book.All)
        {
            var vm = new BranchItemViewModel(b, isChecked: true);
            Branches.Add(vm);

            if (!_tickedCameras.ContainsKey(b.Name))
            {
                _tickedCameras[b.Name] = b.Cameras.Select(c => c.Channel).ToHashSet();
            }
        }

        ApplyBranchFilter();
        if (FilteredBranches.Count > 0 && SelectedBranch == null)
        {
            SelectedBranch = FilteredBranches[0];
        }
    }

    public void ApplyBranchFilter()
    {
        FilteredBranches.Clear();
        string filter = _branchFilter.Trim();
        foreach (var b in Branches)
        {
            if (string.IsNullOrEmpty(filter) ||
                b.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                b.Area.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                b.Subtitle.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredBranches.Add(b);
            }
        }
    }

    private void LoadCamerasForSelectedBranch()
    {
        Cameras.Clear();
        if (_selectedBranch == null) return;

        var b = _selectedBranch.Branch;
        if (!_tickedCameras.TryGetValue(b.Name, out var tickedSet))
        {
            tickedSet = b.Cameras.Select(c => c.Channel).ToHashSet();
            _tickedCameras[b.Name] = tickedSet;
        }

        foreach (var cam in b.Cameras)
        {
            var camVm = new CameraItemViewModel(cam, isChecked: tickedSet.Contains(cam.Channel));
            camVm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(CameraItemViewModel.IsChecked) && _selectedBranch != null)
                {
                    if (camVm.IsChecked)
                        _tickedCameras[_selectedBranch.Name].Add(camVm.Channel);
                    else
                        _tickedCameras[_selectedBranch.Name].Remove(camVm.Channel);
                }
            };
            Cameras.Add(camVm);
        }
    }

    public void SetPeriodYesterday()
    {
        var yest = DateTime.Today.AddDays(-1);
        FromDate = yest;
        FromTime = new TimeSpan(0, 0, 0);
        ToDate = yest;
        ToTime = new TimeSpan(23, 59, 59);
    }

    public void SetPeriodTodaySoFar()
    {
        var now = DateTime.Now;
        FromDate = DateTime.Today;
        FromTime = new TimeSpan(0, 0, 0);
        ToDate = DateTime.Today;
        ToTime = new TimeSpan(now.Hour, now.Minute, 0);
    }

    public void SetPeriodLast7Days()
    {
        var now = DateTime.Now;
        FromDate = DateTime.Today.AddDays(-7);
        FromTime = new TimeSpan(0, 0, 0);
        ToDate = DateTime.Today;
        ToTime = new TimeSpan(now.Hour, now.Minute, 0);
    }

    public void SelectAllBranches(bool select)
    {
        foreach (var b in FilteredBranches)
            b.IsChecked = select;
    }

    public void SelectAllCameras(bool select)
    {
        foreach (var c in Cameras)
            c.IsChecked = select;
    }

    public void SelectAllRecordings(bool select)
    {
        foreach (var r in Recordings)
        {
            if (r.CanChangeSelection)
                r.IsSelected = select;
        }
        UpdateSummary();
        UpdateDiskSpace();
        OnPropertyChanged(nameof(CanDownload));
    }

    public void AppendLog(string message)
    {
        _fileLog.Write(message);
        Dispatcher.UIThread.Post(() =>
        {
            Logs.Add(message);
            if (Logs.Count > 1000) Logs.RemoveAt(0);
        });
    }

    public void ClearLogs()
    {
        Logs.Clear();
    }

    private void OnDvrNotice(string line) => AppendLog(line);

    public DateTime GetFromDateTime()
    {
        var d = FromDate.DateTime;
        return new DateTime(d.Year, d.Month, d.Day, FromTime.Hours, FromTime.Minutes, FromTime.Seconds);
    }

    public DateTime GetToDateTime()
    {
        var d = ToDate.DateTime;
        return new DateTime(d.Year, d.Month, d.Day, ToTime.Hours, ToTime.Minutes, ToTime.Seconds);
    }

    public List<BackupRequest> BuildRequests()
    {
        var list = new List<BackupRequest>();
        var from = GetFromDateTime();
        var to = GetToDateTime();

        foreach (var bVm in Branches)
        {
            if (!bVm.IsChecked) continue;
            var b = bVm.Branch;
            if (!_tickedCameras.TryGetValue(b.Name, out var camSet))
                camSet = b.Cameras.Select(c => c.Channel).ToHashSet();

            var cams = b.Cameras.Where(c => camSet.Contains(c.Channel)).ToList();
            list.Add(new BackupRequest(b, cams, from, to));
        }
        return list;
    }

    public async Task<(bool Ok, string Message)> StartFindAsync()
    {
        if (IsBusy) return (false, "An operation is already in progress.");

        var requests = BuildRequests();
        if (requests.Count == 0)
            return (false, "Please select at least one branch to back up.");

        string dest = Destination.Trim();
        var problems = RequestValidator.Validate(requests, dest);
        if (problems.Count > 0)
            return (false, string.Join("\n", problems));

        var cts = new CancellationTokenSource();
        _activeCts = cts;
        IsSearching = true;

        Recordings.Clear();
        _plans = null;
        UpdateSummary();
        UpdateGuideText();

        AppendLog($"{DateTime.Now:HH:mm:ss}  === Starting recording search across {requests.Count} branch(es) ===");

        try
        {
            var engine = new BackupEngine(_dvrClient, name => _credentials.Get(name));
            engine.Log += AppendLog;

            var plans = await Task.Run(async () => await engine.FindAsync(requests, dest, cts.Token));
            _plans = plans;

            Dispatcher.UIThread.Post(() =>
            {
                Recordings.Clear();
                int totalCount = 0;
                foreach (var plan in plans)
                {
                    foreach (var job in plan.Jobs)
                    {
                        var recVm = new RecordingItemViewModel(job);
                        recVm.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName == nameof(RecordingItemViewModel.IsSelected))
                            {
                                UpdateSummary();
                                UpdateDiskSpace();
                                OnPropertyChanged(nameof(CanDownload));
                            }
                        };
                        Recordings.Add(recVm);
                        totalCount++;
                    }
                }

                AppendLog($"{DateTime.Now:HH:mm:ss}  Search complete. Found {totalCount} recording(s).");
                UpdateSummary();
                UpdateDiskSpace();
                UpdateGuideText();
                OnPropertyChanged(nameof(CanDownload));
            });

            return (true, string.Empty);
        }
        catch (OperationCanceledException)
        {
            AppendLog($"{DateTime.Now:HH:mm:ss}  Search cancelled by user.");
            return (false, "Search was cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog($"{DateTime.Now:HH:mm:ss}  Search error: {ex.Message}");
            return (false, $"Search error: {ex.Message}");
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(CanDownload));
            UpdateGuideText();
        }
    }

    public async Task<(bool Ok, string Message)> StartDownloadAsync()
    {
        if (IsBusy) return (false, "An operation is already in progress.");
        if (_plans == null || _plans.Count == 0)
            return (false, "Please search for recordings first.");

        var selectedJobs = Recordings.Where(r => r.IsSelected).ToList();
        if (selectedJobs.Count == 0)
            return (false, "No recordings selected for download.");

        string dest = Destination.Trim();
        if (string.IsNullOrWhiteSpace(dest))
            return (false, "Please specify a destination folder.");

        var cts = new CancellationTokenSource();
        _activeCts = cts;
        IsDownloading = true;
        UpdateGuideText();

        AppendLog($"{DateTime.Now:HH:mm:ss}  === Starting download of {selectedJobs.Count} recording(s) ===");

        try
        {
            var engine = new BackupEngine(_dvrClient, name => _credentials.Get(name));
            engine.Log += AppendLog;
            engine.JobUpdated += job =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var rec = Recordings.FirstOrDefault(r => r.Job == job);
                    rec?.Refresh();
                    UpdateSummary();
                });
            };

            await Task.Run(async () => await engine.DownloadAsync(_plans, dest, cts.Token));

            AppendLog($"{DateTime.Now:HH:mm:ss}  Download run finished.");
            return (true, string.Empty);
        }
        catch (OperationCanceledException)
        {
            AppendLog($"{DateTime.Now:HH:mm:ss}  Download cancelled by user.");
            return (false, "Download was cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog($"{DateTime.Now:HH:mm:ss}  Download error: {ex.Message}");
            return (false, $"Download error: {ex.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsDownloading = false;
                foreach (var r in Recordings)
                {
                    r.Refresh();
                }
                UpdateGuideText();
                UpdateSummary();
                UpdateDiskSpace();
                OnPropertyChanged(nameof(CanDownload));
                OnPropertyChanged(nameof(CanCancel));
            });
        }
    }

    public void Cancel()
    {
        _activeCts?.Cancel();
    }

    private void OnLiveTick()
    {
        if (IsDownloading || IsSearching)
        {
            foreach (var r in Recordings)
            {
                r.Refresh();
            }
            UpdateSummary();
            UpdateDiskSpace();
        }
    }

    private void UpdateSummary()
    {
        var selected = Recordings.Where(r => r.IsSelected).ToList();
        long totalKb = selected.Sum(r => r.Job.SizeKb);
        string sizeFormatted = DiskCheck.Format(totalKb * 1024);

        int doneCount = selected.Count(r => r.Job.Status == JobStatus.Done);

        var activeDownload = selected.FirstOrDefault(r => r.Job.Status == JobStatus.Downloading);
        if (activeDownload != null)
        {
            string speed = activeDownload.SpeedText;
            string left = activeDownload.LeftText;
            SummaryText = string.IsNullOrEmpty(left)
                ? $"Downloading {activeDownload.CameraName} • {speed}"
                : $"Downloading {activeDownload.CameraName} • {speed} • {left} left";
        }
        else if (doneCount > 0 && doneCount == selected.Count)
        {
            SummaryText = $"✔ All {doneCount} recordings saved ({sizeFormatted})";
        }
        else if (doneCount > 0)
        {
            SummaryText = $"{doneCount}/{selected.Count} saved ({sizeFormatted})";
        }
        else
        {
            SummaryText = selected.Count == 0
                ? "No recordings selected"
                : $"{selected.Count} selected • {sizeFormatted}";
        }

        // Overall progress (by size, so big files weigh more than small ones)
        int failedCount = selected.Count(r => r.Job.Status == JobStatus.Failed);
        bool anyActivity = IsDownloading || doneCount > 0 || failedCount > 0;
        ShowOverallProgress = anyActivity && selected.Count > 0;
        if (ShowOverallProgress)
        {
            double doneKb = selected.Sum(r => r.Job.Status switch
            {
                JobStatus.Done or JobStatus.Skipped => (double)r.Job.SizeKb,
                JobStatus.Downloading => r.Job.SizeKb * (r.Job.Percent / 100.0),
                _ => 0d
            });
            OverallPercent = totalKb > 0 ? Math.Clamp(doneKb * 100.0 / totalKb, 0, 100) : 0;
            string failed = failedCount > 0 ? $" • {failedCount} failed" : string.Empty;
            OverallProgressText = $"{doneCount}/{selected.Count} saved • {OverallPercent:0}%{failed}";
        }
    }

    private void UpdateDiskSpace()
    {
        try
        {
            string dest = Destination?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(dest) || !Directory.Exists(dest))
            {
                FreeDiskSpaceText = "Select destination folder";
                return;
            }

            var selected = Recordings.Where(r => r.IsSelected).ToList();
            long totalKb = selected.Sum(r => r.Job.SizeKb);
            long largestKb = selected.Count > 0 ? selected.Max(r => r.Job.SizeKb) : 0;

            var check = DiskCheck.Check(dest, totalKb, largestKb);
            FreeDiskSpaceText = check.Message;
        }
        catch
        {
            FreeDiskSpaceText = string.Empty;
        }
    }

    private void UpdateGuideText()
    {
        if (IsSearching)
            GuideText = "Searching recordings on the selected DVR(s)… please wait.";
        else if (IsDownloading)
            GuideText = "Downloading recordings… keep the app open. You can cancel at any time.";
        else if (Recordings.Count > 0)
            GuideText = "Tick the recordings you want to keep, then press “Download selected”.";
        else
            GuideText = "Tick one or more branches, pick a period, then press “Find recordings”.";
    }

    public void Dispose()
    {
        _liveTimer?.Stop();
        _liveTimer = null;
        _activeCts?.Dispose();
        _dvrClient?.Dispose();
    }
}
