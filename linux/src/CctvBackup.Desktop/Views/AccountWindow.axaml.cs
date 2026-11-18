using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CctvBackup.Core.Auth;

namespace CctvBackup.Desktop.Views;

public partial class AccountWindow : Window
{
    private Border _boxError = null!;
    private TextBlock _txtError = null!;
    private TextBox _txtCurrentPassword = null!;
    private TextBox _txtUsername = null!;
    private TextBox _txtNewPassword = null!;
    private TextBox _txtConfirmPassword = null!;

    public bool WasChanged { get; private set; }

    public AccountWindow()
    {
        InitializeComponent();
        _boxError = this.FindControl<Border>("BoxError")!;
        _txtError = this.FindControl<TextBlock>("TxtError")!;
        _txtCurrentPassword = this.FindControl<TextBox>("TxtCurrentPassword")!;
        _txtUsername = this.FindControl<TextBox>("TxtUsername")!;
        _txtNewPassword = this.FindControl<TextBox>("TxtNewPassword")!;
        _txtConfirmPassword = this.FindControl<TextBox>("TxtConfirmPassword")!;

        _txtUsername.Text = AppAuthStore.GetUsername() ?? "admin";

        var btnCancel = this.FindControl<Button>("BtnCancel");
        if (btnCancel != null) btnCancel.Click += (s, e) => Close();

        var btnSave = this.FindControl<Button>("BtnSave");
        if (btnSave != null) btnSave.Click += (s, e) => OnSave();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnSave()
    {
        _boxError.IsVisible = false;

        string currentPass = _txtCurrentPassword.Text ?? "";
        string user = _txtUsername.Text?.Trim() ?? "";
        string newPass = _txtNewPassword.Text ?? "";
        string confirm = _txtConfirmPassword.Text ?? "";

        if (string.IsNullOrEmpty(currentPass))
        {
            ShowError("Please enter your current password.");
            return;
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            ShowError("Username cannot be empty.");
            return;
        }

        if (string.IsNullOrEmpty(newPass) || newPass.Length < 4)
        {
            ShowError("New password must be at least 4 characters long.");
            return;
        }

        if (newPass != confirm)
        {
            ShowError("New passwords do not match.");
            return;
        }

        if (AppAuthStore.ChangePassword(currentPass, newPass, user))
        {
            WasChanged = true;
            Close();
        }
        else
        {
            ShowError("Current password is incorrect.");
            _txtCurrentPassword.Text = "";
            _txtCurrentPassword.Focus();
        }
    }

    private void ShowError(string message)
    {
        _txtError.Text = message;
        _boxError.IsVisible = true;
    }
}
