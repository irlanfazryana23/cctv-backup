using System.Xml.Linq;
using CctvBackup.Core.Backup;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Dvr;
using CctvBackup.Core.Localization;
using CctvBackup.Core.Sdk;
using CctvBackup.Core.Settings;
using CctvBackup.Core.Workflow;

namespace CctvBackup.App;

public sealed class AppState : IDisposable
{
    private readonly object _lock = new();
    private readonly BranchBook _book;
    private readonly CredentialStore _credentials;
    private readonly FileLog _fileLog;
    private readonly List<string> _logs = new();
    private readonly HashSet<string> _tickedBranches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<int>> _tickedCameras = new(StringComparer.OrdinalIgnoreCase);

    private AppSettings _settings;
    private LinuxDvrClient _dvrClient;
    private IReadOnlyList<BranchPlan>? _plans;
    private bool _listOutdated;
    private bool _searching;
    private bool _downloading;
    private DateTime? _downloadStartedUtc;
    private CancellationTokenSource? _activeCts;

    public DateTime FromTime { get; set; }
    public DateTime ToTime { get; set; }

    public AppState()
    {
        _settings = AppSettings.Load(AppPaths.SettingsFile);
        Loc.Set(Loc.Resolve(_settings.Language));
        _book = new BranchBook(AppPaths.BranchesFile);
        _credentials = new CredentialStore(AppPaths.CredentialsFile);
        _fileLog = new FileLog(AppPaths.LogDirectory);

        string sdkDir = SdkLocator.Find(_settings.SdkDirectory) ?? SdkLocator.BundledDirectory;
        _dvrClient = new LinuxDvrClient(sdkDir);
        _dvrClient.Notice += AppendLog;

        var now = DateTime.Now;
        var today = now.Date;
        FromTime = today.AddHours(8);
        ToTime = now > FromTime ? new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0) : today.AddHours(17);

        foreach (var b in _book.All)
        {
            _tickedCameras[b.Name] = b.Cameras.Select(c => c.Channel).ToHashSet();
        }

        AppendLog($"{DateTime.Now:HH:mm:ss}  CCTV Backup (Linux Edition) started. Active backend: {_dvrClient.ActiveBackendDescription}");
    }

    public AppSettings Settings => _settings;
    public BranchBook Book => _book;
    public CredentialStore Credentials => _credentials;
    public LinuxDvrClient DvrClient => _dvrClient;

    public void SetLanguage(string lang)
    {
        string resolved = Loc.Resolve(lang);
        Loc.Set(resolved);
        _settings.Language = resolved;
        _settings.Save(AppPaths.SettingsFile);
    }

    public void SetDestination(string folder)
    {
        _settings.LastDestination = folder.Trim();
        _settings.Save(AppPaths.SettingsFile);
    }

    public void SetSdkDirectory(string folder)
    {
        _settings.SdkDirectory = string.IsNullOrWhiteSpace(folder) ? string.Empty : folder.Trim();
        _settings.Save(AppPaths.SettingsFile);
        string sdkDir = SdkLocator.Find(_settings.SdkDirectory) ?? SdkLocator.BundledDirectory;
        var old = _dvrClient;
        _dvrClient = new LinuxDvrClient(sdkDir);
        _dvrClient.Notice += AppendLog;
        old.Dispose();
        AppendLog($"{DateTime.Now:HH:mm:ss}  SDK folder updated to '{sdkDir}'. Active backend: {_dvrClient.ActiveBackendDescription}");
    }

    public void AppendLog(string line)
    {
        _fileLog.Write(line);
        lock (_lock)
        {
            _logs.Add(line);
            if (_logs.Count > 1000) _logs.RemoveAt(0);
        }
    }

    public List<string> GetLogs()
    {
        lock (_lock) return _logs.ToList();
    }

    public void SetBranchSelection(IEnumerable<string> branchNames)
    {
        lock (_lock)
        {
            _tickedBranches.Clear();
            foreach (string name in branchNames) _tickedBranches.Add(name);
            if (_plans is not null) _listOutdated = true;
        }
    }

    public void SetCameraSelection(string branchName, IEnumerable<int> channels)
    {
        lock (_lock)
        {
            _tickedCameras[branchName] = channels.ToHashSet();
            if (_plans is not null) _listOutdated = true;
        }
    }

    public HashSet<int> GetTickedCameras(Branch branch)
    {
        lock (_lock)
        {
            if (!_tickedCameras.TryGetValue(branch.Name, out var set))
            {
                set = branch.Cameras.Select(c => c.Channel).ToHashSet();
                _tickedCameras[branch.Name] = set;
            }
            return new HashSet<int>(set);
        }
    }

    public void SetPeriod(DateTime from, DateTime to)
    {
        lock (_lock)
        {
            if (FromTime != from || ToTime != to)
            {
                FromTime = from;
                ToTime = to;
                if (_plans is not null) _listOutdated = true;
            }
        }
    }

    public void SetJobSelection(int index, bool selected)
    {
        lock (_lock)
        {
            if (_plans is null) return;
            var allJobs = _plans.SelectMany(p => p.Jobs).ToList();
            if (index >= 0 && index < allJobs.Count && allJobs[index].CanChangeSelection)
                allJobs[index].Selected = selected;
        }
    }

    public void SetAllJobsSelection(bool selected)
    {
        lock (_lock)
        {
            if (_plans is null) return;
            foreach (var job in _plans.SelectMany(p => p.Jobs))
            {
                if (job.CanChangeSelection)
                    job.Selected = selected;
            }
        }
    }

    public (bool Ok, List<string> Errors) SaveBranch(
        string? editingName, string name, string area, LoginMode mode, string serialId,
        string address, int port, string username, string password, int manualCameraCount)
    {
        var existingNames = _book.All.Select(b => b.Name).ToList();
        bool hasSavedPassword = editingName is not null && _credentials.Get(editingName) is not null;
        var problems = BranchValidator.Validate(
            name, mode, serialId, address, port, username, password,
            existingNames, editingName, passwordRequired: !hasSavedPassword);

        if (problems.Count > 0) return (false, problems);

        string cleanName = name.Trim();
        var oldBranch = editingName is not null ? _book.Find(editingName) : null;
        IReadOnlyList<Camera> cameras;
        if (manualCameraCount > 0)
        {
            cameras = Camera.Range(cleanName, Math.Min(manualCameraCount, DeviceDetails.MaxSaneChannels));
        }
        else if (oldBranch is not null && oldBranch.Cameras.Count > 0)
        {
            cameras = oldBranch.Cameras;
        }
        else
        {
            cameras = Array.Empty<Camera>();
        }

        var branch = new Branch(
            Name: cleanName,
            Area: area.Trim(),
            CloudId: mode == LoginMode.Cloud ? serialId.Trim() : string.Empty,
            Port: mode == LoginMode.Ip ? port : Branch.DefaultPort,
            Username: username.Trim(),
            Cameras: cameras,
            Mode: mode,
            Address: mode == LoginMode.Ip ? address.Trim() : string.Empty);

        if (editingName is not null)
        {
            _book.Update(editingName, branch);
            if (!string.Equals(editingName, cleanName, StringComparison.OrdinalIgnoreCase))
                _credentials.Rename(editingName, cleanName);
        }
        else
        {
            _book.Add(branch);
        }

        if (!string.IsNullOrEmpty(password))
        {
            _credentials.Set(cleanName, new Credential(username.Trim(), password));
        }
        else if (hasSavedPassword)
        {
            var prev = _credentials.Get(cleanName);
            if (prev is not null)
                _credentials.Set(cleanName, new Credential(username.Trim(), prev.Password));
        }

        lock (_lock)
        {
            _tickedCameras[cleanName] = branch.Cameras.Select(c => c.Channel).ToHashSet();
            _tickedBranches.Add(cleanName);
        }

        return (true, new List<string>());
    }

    public bool DeleteBranch(string name)
    {
        bool removed = _book.Remove(name);
        if (removed)
        {
            _credentials.Remove(name);
            lock (_lock)
            {
                _tickedBranches.Remove(name);
                _tickedCameras.Remove(name);
            }
        }
        return removed;
    }

    public async Task<(bool Ok, string Message, int Channels, string Model, string Serial)> TestConnectionAsync(
        string branchName)
    {
        var branch = _book.Find(branchName);
        if (branch is null) return (false, "Branch not found.", 0, "", "");
        var cred = _credentials.Get(branch.Name);
        if (cred is null)
            return (false, Loc.T("No password saved for this branch. Select it, press Edit branch... and enter it."), 0, "", "");

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
                lock (_lock)
                {
                    _tickedCameras[branch.Name] = updated.Cameras.Select(c => c.Channel).ToHashSet();
                }
            }
            else if (ch == 0 && branch.Cameras.Count == 0)
            {
                // Default to 8 channels if the device returned 0 in DEVICEINFO so the user can still back up
                var updated = branch with { Cameras = Camera.Range(branch.Name, 8) };
                _book.Update(branch.Name, updated);
                lock (_lock)
                {
                    _tickedCameras[branch.Name] = updated.Cameras.Select(c => c.Channel).ToHashSet();
                }
                ch = 8;
            }

            string msg = $"{Loc.F("{0}: ONLINE, login OK ({1} s). ", branch.Name, 1)}{Loc.F("{0} cameras detected.", ch > 0 ? ch : branch.Cameras.Count)}";
            AppendLog($"{DateTime.Now:HH:mm:ss}  {msg}");
            return (true, msg, ch > 0 ? ch : branch.Cameras.Count, conn.Info.Model, conn.Info.SerialNumber);
        }
        catch (Exception ex)
        {
            AppendLog($"{DateTime.Now:HH:mm:ss}  {branch.Name}: {ex.Message}");
            return (false, ex.Message, 0, "", "");
        }
    }

    public ImportPlan PreviewCmsXml(string xmlText)
    {
        var doc = XDocument.Parse(xmlText);
        var imported = CmsBranchImporter.ImportDetailed(doc);
        return CmsImportPlanner.Plan(imported, _book.All);
    }

    public int ApplyCmsImport(string xmlText, IReadOnlyCollection<string> selectedNames, string? defaultPassword)
    {
        var plan = PreviewCmsXml(xmlText);
        var chosen = new HashSet<string>(selectedNames, StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (var candidate in plan.Candidates)
        {
            if (!chosen.Contains(candidate.Branch.Name)) continue;
            _book.Add(candidate.Branch);
            if (!string.IsNullOrEmpty(defaultPassword))
                _credentials.Set(candidate.Branch.Name, new Credential(candidate.Branch.Username, defaultPassword));

            lock (_lock)
            {
                _tickedCameras[candidate.Branch.Name] = candidate.Branch.Cameras.Select(c => c.Channel).ToHashSet();
            }
            count++;
        }

        AppendLog($"{DateTime.Now:HH:mm:ss}  Imported {count} branch(es) from CMS XML.");
        return count;
    }

    public List<BackupRequest> BuildRequests()
    {
        lock (_lock)
        {
            var list = new List<BackupRequest>();
            foreach (var b in _book.All)
            {
                if (!_tickedBranches.Contains(b.Name)) continue;
                var camSet = GetTickedCameras(b);
                var cams = b.Cameras.Where(c => camSet.Contains(c.Channel)).ToList();
                list.Add(new BackupRequest(b, cams, FromTime, ToTime));
            }
            return list;
        }
    }

    public async Task<(bool Ok, List<string> Errors)> StartFindAsync()
    {
        if (_searching || _downloading)
            return (false, new List<string> { "An operation is already running." });

        var requests = BuildRequests();
        string dest = _settings.LastDestination ?? string.Empty;
        var problems = RequestValidator.Validate(requests, dest);
        if (problems.Count > 0) return (false, problems);

        var cts = new CancellationTokenSource();
        lock (_lock)
        {
            _activeCts?.Dispose();
            _activeCts = cts;
            _searching = true;
            _listOutdated = false;
            _plans = null;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var engine = new BackupEngine(_dvrClient, name => _credentials.Get(name));
                engine.Log += AppendLog;
                var plans = await engine.FindAsync(requests, dest, cts.Token);
                lock (_lock)
                {
                    _plans = plans;
                    _listOutdated = false;
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog($"{DateTime.Now:HH:mm:ss}  Search cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"{DateTime.Now:HH:mm:ss}  Search error: {ex.Message}");
            }
            finally
            {
                lock (_lock) _searching = false;
            }
        });

        await Task.CompletedTask;
        return (true, new List<string>());
    }

    public async Task<(bool Ok, List<string> Errors)> StartDownloadAsync()
    {
        IReadOnlyList<BranchPlan>? plans;
        lock (_lock)
        {
            if (_searching || _downloading)
                return (false, new List<string> { "An operation is already running." });
            plans = _plans;
        }

        if (plans is null || plans.Count == 0)
            return (false, new List<string> { "Run '1. Find recordings' first." });

        string dest = _settings.LastDestination ?? string.Empty;
        var cts = new CancellationTokenSource();
        lock (_lock)
        {
            _activeCts?.Dispose();
            _activeCts = cts;
            _downloading = true;
            _downloadStartedUtc = DateTime.UtcNow;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var engine = new BackupEngine(_dvrClient, name => _credentials.Get(name));
                engine.Log += AppendLog;
                await engine.DownloadAsync(plans, dest, cts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendLog($"{DateTime.Now:HH:mm:ss}  Download cancelled.");
            }
            catch (Exception ex)
            {
                AppendLog($"{DateTime.Now:HH:mm:ss}  Download error: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _downloading = false;
                    _downloadStartedUtc = null;
                }
            }
        });

        await Task.CompletedTask;
        return (true, new List<string>());
    }

    public void Cancel()
    {
        lock (_lock)
        {
            _activeCts?.Cancel();
        }
    }

    public object GetSnapshot()
    {
        lock (_lock)
        {
            var branches = _book.All;
            var tickedBranchList = branches.Where(b => _tickedBranches.Contains(b.Name)).ToList();
            int withoutCamInfo = tickedBranchList.Count(b => !b.CamerasDetected);
            int withoutCamChosen = tickedBranchList.Count(b => b.CamerasDetected && GetTickedCameras(b).Count == 0);

            var allJobs = _plans?.SelectMany(p => p.Jobs).ToList() ?? new List<DownloadJob>();
            int searchProblems = _plans?.Count(p => p.Error is not null) ?? 0;

            var guideState = new GuideState(
                DriverMissing: false,
                Branches: branches.Count,
                TickedBranches: tickedBranchList.Count,
                TickedWithoutCameraInfo: withoutCamInfo,
                TickedWithNoCameraChosen: withoutCamChosen,
                FolderChosen: !string.IsNullOrWhiteSpace(_settings.LastDestination),
                Searching: _searching,
                Downloading: _downloading,
                ListExists: _plans is not null,
                ListOutdated: _listOutdated,
                Recordings: allJobs.Count(j => j.Status != JobStatus.Skipped),
                Ticked: allJobs.Count(j => j.IsPendingDownload),
                Done: allJobs.Count(j => j.Status == JobStatus.Done),
                AlreadySaved: allJobs.Count(j => j.Status == JobStatus.Skipped),
                SearchProblems: searchProblems);

            var step = WorkflowGuide.Decide(guideState);

            var todoJobs = allJobs.Where(j => j.IsPendingDownload || j.Status == JobStatus.Downloading).ToList();
            long selectedKb = todoJobs.Sum(j => j.SizeKb);
            long largestKb = todoJobs.Count > 0 ? todoJobs.Max(j => j.SizeKb) : 0;

            string diskMsg = string.Empty;
            bool diskOk = true;
            if (!string.IsNullOrWhiteSpace(_settings.LastDestination))
            {
                var dc = DiskCheck.Check(_settings.LastDestination, selectedKb, largestKb);
                diskMsg = dc.Message;
                diskOk = dc.Ok;
            }

            string overallSpeed = string.Empty;
            string overallLeft = string.Empty;
            if (_downloading && _downloadStartedUtc is not null)
            {
                var work = allJobs
                    .Where(j => j.Selected || j.Status is JobStatus.Done or JobStatus.Downloading)
                    .Select(j => (j.SizeKb, j.Status == JobStatus.Done ? 1.0 : j.Percent / 100.0));
                var (spd, left) = TransferStats.Overall(work, DateTime.UtcNow - _downloadStartedUtc.Value);
                overallSpeed = TransferStats.FormatSpeed(spd);
                overallLeft = TransferStats.FormatDuration(left);
            }

            return new
            {
                language = Loc.Code,
                backend = _dvrClient.ActiveBackendDescription,
                cloudReady = _dvrClient.IsDriverReadyForCloud,
                sdkDirectory = !string.IsNullOrWhiteSpace(_settings.SdkDirectory) ? _settings.SdkDirectory : (SdkLocator.Find(null) ?? SdkLocator.BundledDirectory),
                configDirectory = AppPaths.DataDirectory,
                destination = _settings.LastDestination ?? string.Empty,
                diskMessage = diskMsg,
                diskOk,
                fromTime = FromTime.ToString("yyyy-MM-ddTHH:mm"),
                toTime = ToTime.ToString("yyyy-MM-ddTHH:mm"),
                searching = _searching,
                downloading = _downloading,
                guideStep = step.ToString(),
                guideText = DescribeGuideStep(step, guideState),
                selectedCount = todoJobs.Count,
                selectedSizeText = DiskCheck.Format(selectedKb * 1024),
                overallSpeed,
                overallLeft,
                branches = branches.Select(b =>
                {
                    var camTicked = GetTickedCameras(b);
                    return new
                    {
                        name = b.Name,
                        area = b.Area,
                        mode = b.Mode.ToString(),
                        cloudId = b.CloudId,
                        address = b.Address,
                        port = b.Port,
                        username = b.Username,
                        connectionText = b.ConnectionText,
                        hasPassword = _credentials.Get(b.Name) is not null,
                        camerasDetected = b.CamerasDetected,
                        ticked = _tickedBranches.Contains(b.Name),
                        cameras = b.Cameras.Select(c => new
                        {
                            channel = c.Channel,
                            name = c.Name,
                            folderName = c.FolderName,
                            ticked = camTicked.Contains(c.Channel),
                        }),
                    };
                }),
                planErrors = _plans?.Where(p => p.Error is not null).Select(p => new { branch = p.Branch.Name, error = p.Error }) ?? Enumerable.Empty<object>(),
                jobs = allJobs.Select((j, idx) => new
                {
                    index = idx,
                    selected = j.Selected,
                    canChange = j.CanChangeSelection,
                    branch = j.BranchName,
                    camera = j.CameraName,
                    period = j.Period,
                    sizeText = j.SizeText,
                    status = j.Status.ToString(),
                    statusText = j.StatusText,
                    percent = j.Percent,
                    speed = j.SpeedText,
                    left = j.LeftText,
                    error = j.Error ?? string.Empty,
                    finalPath = j.FinalPath,
                }),
                logs = _logs.TakeLast(250).ToList(),
                strings = UiStrings.GetCurrentTable(),
            };
        }
    }

    private static string DescribeGuideStep(GuideStep step, GuideState s) => step switch
    {
        GuideStep.DriverMissing => $"{Loc.T("The DVR driver is missing")} — {Loc.T("Install this program again. The \"sdk\" folder next to the program must contain NetSdk.dll.")}",
        GuideStep.NoBranches => $"{Loc.T("Welcome! Start by adding a branch")} — {Loc.T("Press \"Add branch\" at the top left and type its name, Serial ID, user name and password. If the CMS is on this PC, \"Import from CMS\" brings all its branches in at once.")}",
        GuideStep.TickBranch => $"{Loc.T("Step 1: tick the branches to back up")} — {Loc.T("Tick the box in front of each branch in the list below on the left.")}",
        GuideStep.CamerasUnknown => $"{Loc.F("Step 2: read the cameras of {0}", "DVR")} — {Loc.T("The program does not know the cameras of this branch yet. It reads them from the DVR when you press \"Test connection\" (top left).")}",
        GuideStep.TickCamera => $"{Loc.T("Step 2: tick at least one camera")} — {Loc.T("Click a branch to see its cameras, then tick the ones you want.")}",
        GuideStep.ChooseFolder => $"{Loc.T("Step 4: choose where to save the backup")} — {Loc.T("Press \"Browse...\" next to \"Save to\" and pick a folder on your external disk.")}",
        GuideStep.Find => $"{Loc.T("Step 5: find the recordings")} — {Loc.T("Check the period above, then press \"Find recordings\". Nothing is downloaded yet; you choose the files first.")}",
        GuideStep.Searching => $"{Loc.T("Searching the DVRs...")} — {Loc.T("This can take a minute on slow connections. You can press Cancel to stop.")}",
        GuideStep.ListOutdated => $"{Loc.T("Your choices changed")} — {Loc.T("The list below was made with the earlier choices. Press \"Find recordings\" again to refresh it.")}",
        GuideStep.NothingFound => $"{Loc.T("No recordings found for this period")} — {Loc.T("Change the dates (or the branches or cameras) and search again. The DVR may not have been recording.")}",
        GuideStep.SearchFailed => $"{Loc.T("The branches could not be searched")} — {Loc.T("See the activity log below for the reason (internet, address or password). Fix it, then search again.")}",
        GuideStep.AllAlreadySaved => $"{Loc.T("Everything found is already saved")} — {Loc.T("All recordings in this period are already in your save folder. Change the period to back up more.")}",
        GuideStep.TickRecordings => $"{Loc.T("Step 6: tick the recordings to save")} — {Loc.T("Tick the files you want in the list below, then press \"Download selected\".")}",
        GuideStep.ReadyToDownload => $"{Loc.F("Step 6: download {0} recordings ({1})", s.Ticked, "")} — {Loc.T("Press \"Download selected\". Finished files are kept, so you can stop and continue later.")}",
        GuideStep.Downloading => $"{Loc.T("Downloading...")} — {Loc.T("Keep the program open. Press Cancel to stop; finished files are kept.")}",
        GuideStep.Finished => $"{Loc.F("Done: {0} recordings saved", s.Done + s.AlreadySaved)}",
        _ => string.Empty,
    };

    public void Dispose()
    {
        _activeCts?.Cancel();
        _activeCts?.Dispose();
        _dvrClient.Dispose();
    }
}
