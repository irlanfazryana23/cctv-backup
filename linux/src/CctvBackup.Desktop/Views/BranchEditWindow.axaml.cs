using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CctvBackup.Core.Branches;
using CctvBackup.Core.Credentials;
using CctvBackup.Core.Sdk;

namespace CctvBackup.Desktop.Views;

public partial class BranchEditWindow : Window
{
    private readonly BranchBook? _book;
    private readonly CredentialStore? _credentials;
    private readonly Branch? _editingBranch;

    public bool WasSaved { get; private set; }

    public BranchEditWindow()
    {
        InitializeComponent();
    }

    public BranchEditWindow(BranchBook book, CredentialStore credentials, Branch? branchToEdit = null) : this()
    {
        _book = book;
        _credentials = credentials;
        _editingBranch = branchToEdit;

        Title = _editingBranch != null ? $"Edit Branch — {_editingBranch.Name}" : "Add New Branch";
        SetupFields();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

        var rbCloud = this.FindControl<RadioButton>("RbCloud");
        var rbIp = this.FindControl<RadioButton>("RbIp");
        var panelCloud = this.FindControl<StackPanel>("PanelCloud");
        var panelIp = this.FindControl<Grid>("PanelIp");

        void UpdateModeVisibility()
        {
            bool isCloud = rbCloud?.IsChecked == true;
            if (panelCloud != null) panelCloud.IsVisible = isCloud;
            if (panelIp != null) panelIp.IsVisible = !isCloud;
        }

        if (rbCloud != null) rbCloud.IsCheckedChanged += (s, e) => UpdateModeVisibility();
        if (rbIp != null) rbIp.IsCheckedChanged += (s, e) => UpdateModeVisibility();

        var btnCancel = this.FindControl<Button>("BtnCancel");
        if (btnCancel != null) btnCancel.Click += (s, e) => Close(false);

        var btnSave = this.FindControl<Button>("BtnSave");
        if (btnSave != null) btnSave.Click += (s, e) => OnSave();
    }

    private void SetupFields()
    {
        var rbCloud = this.FindControl<RadioButton>("RbCloud");
        var rbIp = this.FindControl<RadioButton>("RbIp");
        var panelCloud = this.FindControl<StackPanel>("PanelCloud");
        var panelIp = this.FindControl<Grid>("PanelIp");
        var txtName = this.FindControl<TextBox>("TxtName");
        var txtArea = this.FindControl<TextBox>("TxtArea");
        var txtSerialId = this.FindControl<TextBox>("TxtSerialId");
        var txtAddress = this.FindControl<TextBox>("TxtAddress");
        var txtPort = this.FindControl<TextBox>("TxtPort");
        var txtUsername = this.FindControl<TextBox>("TxtUsername");
        var txtCameraCount = this.FindControl<TextBox>("TxtCameraCount");

        if (_editingBranch != null)
        {
            if (txtName != null) txtName.Text = _editingBranch.Name;
            if (txtArea != null) txtArea.Text = _editingBranch.Area;
            if (txtUsername != null) txtUsername.Text = _editingBranch.Username;
            if (txtCameraCount != null) txtCameraCount.Text = _editingBranch.Cameras.Count.ToString();

            if (_editingBranch.Mode == LoginMode.Ip)
            {
                if (rbIp != null) rbIp.IsChecked = true;
                if (txtAddress != null) txtAddress.Text = _editingBranch.Address;
                if (txtPort != null) txtPort.Text = _editingBranch.Port.ToString();
                if (panelCloud != null) panelCloud.IsVisible = false;
                if (panelIp != null) panelIp.IsVisible = true;
            }
            else
            {
                if (rbCloud != null) rbCloud.IsChecked = true;
                if (txtSerialId != null) txtSerialId.Text = _editingBranch.CloudId;
                if (panelCloud != null) panelCloud.IsVisible = true;
                if (panelIp != null) panelIp.IsVisible = false;
            }
        }
        else
        {
            if (panelCloud != null) panelCloud.IsVisible = true;
            if (panelIp != null) panelIp.IsVisible = false;
        }
    }

    private void OnSave()
    {
        if (_book == null || _credentials == null) return;

        var txtName = this.FindControl<TextBox>("TxtName");
        var txtArea = this.FindControl<TextBox>("TxtArea");
        var rbCloud = this.FindControl<RadioButton>("RbCloud");
        var txtSerialId = this.FindControl<TextBox>("TxtSerialId");
        var txtAddress = this.FindControl<TextBox>("TxtAddress");
        var txtPort = this.FindControl<TextBox>("TxtPort");
        var txtUsername = this.FindControl<TextBox>("TxtUsername");
        var txtPassword = this.FindControl<TextBox>("TxtPassword");
        var txtCameraCount = this.FindControl<TextBox>("TxtCameraCount");
        var txtErrors = this.FindControl<TextBlock>("TxtErrors");

        string name = txtName?.Text?.Trim() ?? "";
        string area = txtArea?.Text?.Trim() ?? "";
        var mode = rbCloud?.IsChecked == true ? LoginMode.Cloud : LoginMode.Ip;
        string serialId = txtSerialId?.Text?.Trim() ?? "";
        string address = txtAddress?.Text?.Trim() ?? "";
        int.TryParse(txtPort?.Text?.Trim(), out int port);
        if (port <= 0) port = Branch.DefaultPort;
        string username = txtUsername?.Text?.Trim() ?? "admin";
        string password = txtPassword?.Text ?? "";
        int.TryParse(txtCameraCount?.Text?.Trim(), out int manualCamCount);

        string? editingName = _editingBranch?.Name;
        bool hasSavedPassword = editingName != null && _credentials.Get(editingName) != null;
        var existingNames = _book.All.Select(b => b.Name).ToList();

        var problems = BranchValidator.Validate(
            name, mode, serialId, address, port, username, password,
            existingNames, editingName, passwordRequired: !hasSavedPassword);

        if (problems.Count > 0)
        {
            if (txtErrors != null) txtErrors.Text = string.Join("\n", problems);
            return;
        }

        var cameras = manualCamCount > 0
            ? Camera.Range(name, Math.Min(manualCamCount, DeviceDetails.MaxSaneChannels))
            : (_editingBranch?.Cameras ?? (IReadOnlyList<Camera>)Array.Empty<Camera>());

        var newBranch = new Branch(
            Name: name,
            Area: area,
            CloudId: mode == LoginMode.Cloud ? serialId : string.Empty,
            Port: mode == LoginMode.Ip ? port : Branch.DefaultPort,
            Username: username,
            Cameras: cameras,
            Mode: mode,
            Address: mode == LoginMode.Ip ? address : string.Empty);

        if (editingName != null)
        {
            _book.Update(editingName, newBranch);
            if (!string.Equals(editingName, name, StringComparison.OrdinalIgnoreCase))
                _credentials.Rename(editingName, name);
        }
        else
        {
            _book.Add(newBranch);
        }

        if (!string.IsNullOrEmpty(password))
        {
            _credentials.Set(name, new Credential(username, password));
        }
        else if (hasSavedPassword)
        {
            var prev = _credentials.Get(name);
            if (prev != null)
                _credentials.Set(name, new Credential(username, prev.Password));
        }

        WasSaved = true;
        Close(true);
    }
}
