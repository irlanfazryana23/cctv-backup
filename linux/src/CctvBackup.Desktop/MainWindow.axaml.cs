using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Localization;
using CctvBackup.Desktop.ViewModels;
using CctvBackup.Desktop.Views;

namespace CctvBackup.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;

        AttachEventHandlers();

        _vm.Logs.CollectionChanged += (s, e) =>
        {
            var scroll = this.FindControl<ScrollViewer>("ScrollLog");
            scroll?.ScrollToEnd();
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AttachEventHandlers()
    {
        // Branch actions
        var btnAddBranch = this.FindControl<Button>("BtnAddBranch");
        if (btnAddBranch != null) btnAddBranch.Click += async (s, e) => await OnAddBranchAsync();

        var btnEditBranch = this.FindControl<Button>("BtnEditBranch");
        if (btnEditBranch != null) btnEditBranch.Click += async (s, e) => await OnEditBranchAsync();

        var btnRemoveBranch = this.FindControl<Button>("BtnRemoveBranch");
        if (btnRemoveBranch != null) btnRemoveBranch.Click += async (s, e) => await OnRemoveBranchAsync();

        var btnTestConn = this.FindControl<Button>("BtnTestConn");
        if (btnTestConn != null) btnTestConn.Click += async (s, e) => await OnTestConnectionAsync();

        var btnImportCms = this.FindControl<Button>("BtnImportCms");
        if (btnImportCms != null) btnImportCms.Click += async (s, e) => await OnImportCmsAsync();

        var btnBranchTickAll = this.FindControl<Button>("BtnBranchTickAll");
        if (btnBranchTickAll != null) btnBranchTickAll.Click += (s, e) => _vm.SelectAllBranches(true);

        var btnBranchUntickAll = this.FindControl<Button>("BtnBranchUntickAll");
        if (btnBranchUntickAll != null) btnBranchUntickAll.Click += (s, e) => _vm.SelectAllBranches(false);

        // Camera actions
        var btnCamTickAll = this.FindControl<Button>("BtnCamTickAll");
        if (btnCamTickAll != null) btnCamTickAll.Click += (s, e) => _vm.SelectAllCameras(true);

        var btnCamUntickAll = this.FindControl<Button>("BtnCamUntickAll");
        if (btnCamUntickAll != null) btnCamUntickAll.Click += (s, e) => _vm.SelectAllCameras(false);

        // Period shortcuts
        var btnYesterday = this.FindControl<Button>("BtnYesterday");
        if (btnYesterday != null) btnYesterday.Click += (s, e) => _vm.SetPeriodYesterday();

        var btnToday = this.FindControl<Button>("BtnToday");
        if (btnToday != null) btnToday.Click += (s, e) => _vm.SetPeriodTodaySoFar();

        var btnLast7Days = this.FindControl<Button>("BtnLast7Days");
        if (btnLast7Days != null) btnLast7Days.Click += (s, e) => _vm.SetPeriodLast7Days();

        // Destination actions
        var btnBrowseDest = this.FindControl<Button>("BtnBrowseDest");
        if (btnBrowseDest != null) btnBrowseDest.Click += async (s, e) => await OnBrowseDestinationAsync();

        var btnOpenDest = this.FindControl<Button>("BtnOpenDest");
        if (btnOpenDest != null) btnOpenDest.Click += (s, e) => OnOpenDestination();

        // Main operations
        var btnFind = this.FindControl<Button>("BtnFind");
        if (btnFind != null) btnFind.Click += async (s, e) => await OnFindRecordingsAsync();

        var btnDownload = this.FindControl<Button>("BtnDownload");
        if (btnDownload != null) btnDownload.Click += async (s, e) => await OnDownloadRecordingsAsync();

        var btnCancel = this.FindControl<Button>("BtnCancel");
        if (btnCancel != null) btnCancel.Click += (s, e) => _vm.Cancel();

        // Recording selection actions
        var btnRecTickAll = this.FindControl<Button>("BtnRecTickAll");
        if (btnRecTickAll != null) btnRecTickAll.Click += (s, e) => _vm.SelectAllRecordings(true);

        var btnRecUntickAll = this.FindControl<Button>("BtnRecUntickAll");
        if (btnRecUntickAll != null) btnRecUntickAll.Click += (s, e) => _vm.SelectAllRecordings(false);

        // Activity log
        var btnClearLog = this.FindControl<Button>("BtnClearLog");
        if (btnClearLog != null) btnClearLog.Click += (s, e) => _vm.ClearLogs();

        var btnCopyLog = this.FindControl<Button>("BtnCopyLog");
        if (btnCopyLog != null) btnCopyLog.Click += async (s, e) =>
        {
            var text = string.Join(Environment.NewLine, _vm.Logs);
            try
            {
                if (Clipboard != null)
                {
                    var dt = new Avalonia.Input.DataTransfer();
                    var item = new Avalonia.Input.DataTransferItem();
                    item.GetType().GetMethod("Add", new[] { typeof(Avalonia.Input.DataFormat), typeof(object) })
                        ?.Invoke(item, new object[] { Avalonia.Input.DataFormat.Text, text });
                    dt.Add(item);
                    await Clipboard.SetDataAsync(dt);
                }
                await new MessageDialog("Copied", "Activity log copied to clipboard!").ShowDialog(this);
            }
            catch { }
        };

        var btnAccount = this.FindControl<Button>("BtnAccount");
        if (btnAccount != null) btnAccount.Click += async (s, e) =>
        {
            var dlg = new AccountWindow();
            await dlg.ShowDialog(this);
            if (dlg.WasChanged)
            {
                await new MessageDialog("Account Updated", "Admin credentials updated successfully.").ShowDialog(this);
            }
        };

        var btnLock = this.FindControl<Button>("BtnLock");
        if (btnLock != null) btnLock.Click += (s, e) =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var login = new LoginWindow();
                desktop.MainWindow = login;
                login.Show();
                this.Close();
            }
        };
    }

    private async Task OnAddBranchAsync()
    {
        var dlg = new BranchEditWindow(_vm.Book, _vm.Credentials);
        await dlg.ShowDialog(this);
        if (dlg.WasSaved)
        {
            _vm.ReloadBranches();
        }
    }

    private async Task OnEditBranchAsync()
    {
        if (_vm.SelectedBranch == null)
        {
            await new MessageDialog("Notice", "Please select a branch to edit.").ShowDialog(this);
            return;
        }

        var dlg = new BranchEditWindow(_vm.Book, _vm.Credentials, _vm.SelectedBranch.Branch);
        await dlg.ShowDialog(this);
        if (dlg.WasSaved)
        {
            _vm.ReloadBranches();
        }
    }

    private async Task OnRemoveBranchAsync()
    {
        if (_vm.SelectedBranch == null)
        {
            await new MessageDialog("Notice", "Please select a branch to remove.").ShowDialog(this);
            return;
        }

        string branchName = _vm.SelectedBranch.Name;
        _vm.Book.Remove(branchName);
        _vm.Credentials.Remove(branchName);
        _vm.ReloadBranches();
        _vm.AppendLog($"{DateTime.Now:HH:mm:ss}  Branch '{branchName}' removed.");
    }

    private async Task OnTestConnectionAsync()
    {
        if (_vm.SelectedBranch == null)
        {
            await new MessageDialog("Notice", "Please select a branch to test.").ShowDialog(this);
            return;
        }

        var res = await _vm.TestConnectionAsync(_vm.SelectedBranch.Name);
        if (res.Ok)
        {
            string info = $"{res.Message}\n\n• Model: {res.Model}\n• Serial: {res.Serial}";
            await new MessageDialog("Connection Success", info).ShowDialog(this);
        }
        else
        {
            await new MessageDialog("Connection Failed", res.Message).ShowDialog(this);
        }
    }

    private async Task OnImportCmsAsync()
    {
        var dlg = new CmsImportWindow(_vm.Book, _vm.Credentials);
        await dlg.ShowDialog(this);
        if (dlg.ImportedCount > 0)
        {
            _vm.ReloadBranches();
            await new MessageDialog("CMS Import", $"Successfully imported {dlg.ImportedCount} branch(es).").ShowDialog(this);
        }
    }

    private async Task OnBrowseDestinationAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Destination Folder for CCTV Recordings",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string? localPath = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                _vm.Destination = localPath;
            }
        }
    }

    private void OnOpenDestination()
    {
        try
        {
            string path = _vm.Destination?.Trim() ?? "";
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", path);
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = true });
                }
            }
        }
        catch { }
    }

    private async Task OnFindRecordingsAsync()
    {
        var (ok, error) = await _vm.StartFindAsync();
        if (!ok && !string.IsNullOrEmpty(error))
        {
            await new MessageDialog("Search Notice", error).ShowDialog(this);
        }
    }

    private async Task OnDownloadRecordingsAsync()
    {
        var (ok, error) = await _vm.StartDownloadAsync();
        if (!ok && !string.IsNullOrEmpty(error))
        {
            await new MessageDialog("Download Notice", error).ShowDialog(this);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.Dispose();
        base.OnClosed(e);
    }
}
