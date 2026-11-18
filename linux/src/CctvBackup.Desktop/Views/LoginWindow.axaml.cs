using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CctvBackup.Core.Auth;

namespace CctvBackup.Desktop.Views;

public partial class LoginWindow : Window
{
    private StackPanel _panelLogin = null!;
    private StackPanel _panelSetup = null!;
    private StackPanel _panelRecovery = null!;
    private StackPanel _panelShowKey = null!;

    private Border _boxLoginError = null!;
    private TextBlock _txtLoginError = null!;
    private TextBox _txtLoginUsername = null!;
    private TextBox _txtLoginPassword = null!;

    private Border _boxSetupError = null!;
    private TextBlock _txtSetupError = null!;
    private TextBox _txtSetupUsername = null!;
    private TextBox _txtSetupPassword = null!;
    private TextBox _txtSetupConfirm = null!;

    private Border _boxRecoveryError = null!;
    private TextBlock _txtRecoveryError = null!;
    private TextBox _txtRecoveryKey = null!;
    private TextBox _txtRecoveryUsername = null!;
    private TextBox _txtRecoveryPassword = null!;
    private TextBox _txtRecoveryConfirm = null!;

    private SelectableTextBlock _txtGeneratedKey = null!;

    public LoginWindow()
    {
        InitializeComponent();
        BindControls();
        AttachEvents();

        if (!AppAuthStore.IsConfigured())
        {
            ShowPanel(_panelSetup);
        }
        else
        {
            ShowPanel(_panelLogin);
            _txtLoginUsername.Text = AppAuthStore.GetUsername() ?? "admin";
            _txtLoginPassword.Focus();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BindControls()
    {
        _panelLogin = this.FindControl<StackPanel>("PanelLogin")!;
        _panelSetup = this.FindControl<StackPanel>("PanelSetup")!;
        _panelRecovery = this.FindControl<StackPanel>("PanelRecovery")!;
        _panelShowKey = this.FindControl<StackPanel>("PanelShowKey")!;

        _boxLoginError = this.FindControl<Border>("BoxLoginError")!;
        _txtLoginError = this.FindControl<TextBlock>("TxtLoginError")!;
        _txtLoginUsername = this.FindControl<TextBox>("TxtLoginUsername")!;
        _txtLoginPassword = this.FindControl<TextBox>("TxtLoginPassword")!;

        _boxSetupError = this.FindControl<Border>("BoxSetupError")!;
        _txtSetupError = this.FindControl<TextBlock>("TxtSetupError")!;
        _txtSetupUsername = this.FindControl<TextBox>("TxtSetupUsername")!;
        _txtSetupPassword = this.FindControl<TextBox>("TxtSetupPassword")!;
        _txtSetupConfirm = this.FindControl<TextBox>("TxtSetupConfirm")!;

        _boxRecoveryError = this.FindControl<Border>("BoxRecoveryError")!;
        _txtRecoveryError = this.FindControl<TextBlock>("TxtRecoveryError")!;
        _txtRecoveryKey = this.FindControl<TextBox>("TxtRecoveryKey")!;
        _txtRecoveryUsername = this.FindControl<TextBox>("TxtRecoveryUsername")!;
        _txtRecoveryPassword = this.FindControl<TextBox>("TxtRecoveryPassword")!;
        _txtRecoveryConfirm = this.FindControl<TextBox>("TxtRecoveryConfirm")!;

        _txtGeneratedKey = this.FindControl<SelectableTextBlock>("TxtGeneratedKey")!;
    }

    private void AttachEvents()
    {
        var btnLogin = this.FindControl<Button>("BtnLogin");
        if (btnLogin != null) btnLogin.Click += (s, e) => OnLogin();

        var btnGoForgot = this.FindControl<Button>("BtnGoForgot");
        if (btnGoForgot != null) btnGoForgot.Click += (s, e) =>
        {
            HideErrors();
            _txtRecoveryUsername.Text = _txtLoginUsername.Text ?? "";
            ShowPanel(_panelRecovery);
        };

        var btnRecoveryCancel = this.FindControl<Button>("BtnRecoveryCancel");
        if (btnRecoveryCancel != null) btnRecoveryCancel.Click += (s, e) =>
        {
            HideErrors();
            ShowPanel(_panelLogin);
        };

        var btnRecoveryReset = this.FindControl<Button>("BtnRecoveryReset");
        if (btnRecoveryReset != null) btnRecoveryReset.Click += (s, e) => OnRecoveryReset();

        var btnSetupCreate = this.FindControl<Button>("BtnSetupCreate");
        if (btnSetupCreate != null) btnSetupCreate.Click += (s, e) => OnSetupCreate();

        var btnCopyKey = this.FindControl<Button>("BtnCopyKey");
        if (btnCopyKey != null) btnCopyKey.Click += async (s, e) =>
        {
            if (Clipboard != null && !string.IsNullOrEmpty(_txtGeneratedKey.Text))
            {
                try
                {
                    var dt = new Avalonia.Input.DataTransfer();
                    var item = new Avalonia.Input.DataTransferItem();
                    item.GetType().GetMethod("Add", new[] { typeof(Avalonia.Input.DataFormat), typeof(object) })
                        ?.Invoke(item, new object[] { Avalonia.Input.DataFormat.Text, _txtGeneratedKey.Text });
                    dt.Add(item);
                    await Clipboard.SetDataAsync(dt);
                }
                catch { }
                btnCopyKey.Content = "Copied!";
            }
        };

        var btnProceedMain = this.FindControl<Button>("BtnProceedMain");
        if (btnProceedMain != null) btnProceedMain.Click += (s, e) => LaunchMainWindow();
    }

    private void ShowPanel(StackPanel target)
    {
        _panelLogin.IsVisible = (_panelLogin == target);
        _panelSetup.IsVisible = (_panelSetup == target);
        _panelRecovery.IsVisible = (_panelRecovery == target);
        _panelShowKey.IsVisible = (_panelShowKey == target);
    }

    private void HideErrors()
    {
        _boxLoginError.IsVisible = false;
        _boxSetupError.IsVisible = false;
        _boxRecoveryError.IsVisible = false;
    }

    private void OnLogin()
    {
        HideErrors();
        string user = _txtLoginUsername.Text?.Trim() ?? "";
        string pass = _txtLoginPassword.Text ?? "";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            ShowLoginError("Please enter both username and password.");
            return;
        }

        if (AppAuthStore.VerifyLogin(user, pass))
        {
            LaunchMainWindow();
        }
        else
        {
            ShowLoginError("Incorrect username or password. Please try again.");
            _txtLoginPassword.Text = "";
            _txtLoginPassword.Focus();
        }
    }

    private void ShowLoginError(string message)
    {
        _txtLoginError.Text = message;
        _boxLoginError.IsVisible = true;
    }

    private void OnSetupCreate()
    {
        HideErrors();
        string user = _txtSetupUsername.Text?.Trim() ?? "";
        string pass = _txtSetupPassword.Text ?? "";
        string confirm = _txtSetupConfirm.Text ?? "";

        if (string.IsNullOrWhiteSpace(user))
        {
            ShowSetupError("Please enter an admin username.");
            return;
        }

        if (string.IsNullOrEmpty(pass) || pass.Length < 4)
        {
            ShowSetupError("Password must be at least 4 characters long.");
            return;
        }

        if (pass != confirm)
        {
            ShowSetupError("Passwords do not match.");
            return;
        }

        try
        {
            string recoveryKey = AppAuthStore.Configure(user, pass);
            _txtGeneratedKey.Text = recoveryKey;
            ShowPanel(_panelShowKey);
        }
        catch (Exception ex)
        {
            ShowSetupError($"Failed to save account: {ex.Message}");
        }
    }

    private void ShowSetupError(string message)
    {
        _txtSetupError.Text = message;
        _boxSetupError.IsVisible = true;
    }

    private void OnRecoveryReset()
    {
        HideErrors();
        string key = _txtRecoveryKey.Text?.Trim() ?? "";
        string newPass = _txtRecoveryPassword.Text ?? "";
        string confirm = _txtRecoveryConfirm.Text ?? "";
        string newUser = _txtRecoveryUsername.Text?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(key))
        {
            ShowRecoveryError("Please enter your Master Recovery Key.");
            return;
        }

        if (string.IsNullOrEmpty(newPass) || newPass.Length < 4)
        {
            ShowRecoveryError("New password must be at least 4 characters long.");
            return;
        }

        if (newPass != confirm)
        {
            ShowRecoveryError("Passwords do not match.");
            return;
        }

        if (AppAuthStore.ResetWithRecoveryKey(key, newPass, string.IsNullOrWhiteSpace(newUser) ? null : newUser))
        {
            LaunchMainWindow();
        }
        else
        {
            ShowRecoveryError("Invalid Recovery Key. Please verify the code and try again.");
        }
    }

    private void ShowRecoveryError(string message)
    {
        _txtRecoveryError.Text = message;
        _boxRecoveryError.IsVisible = true;
    }

    private void LaunchMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            this.Close();
        }
    }
}
