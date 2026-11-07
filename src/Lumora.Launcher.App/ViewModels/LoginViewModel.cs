using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Services;

namespace Lumora.Launcher.App.ViewModels;

public partial class LoginViewModel(AppServices services, Action<AccountSession> onLoggedIn) : ViewModelBase
{
    public bool IsDemo => services.IsDemo;

    public Appearance Appearance => services.Appearance;

    public string ServerUrl => services.Settings.AuthServerUrl;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial string Login { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool RevealPassword { get; set; }

    [ObservableProperty]
    public partial bool Remember { get; set; } = true;

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial bool IsBusy { get; set; }

    partial void OnLoginChanged(string value) => Error = null;
    partial void OnPasswordChanged(string value) => Error = null;

    private bool CanLogin() => !IsBusy && Login.Trim().Length > 0 && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            var session = await services.Api.LoginAsync(Login.Trim(), Password);
            if (Remember)
                services.Sessions?.Save(session);
            else
                services.Sessions?.Clear();
            onLoggedIn(session);
        }
        catch (AuthenticationFailedException e)
        {
            Error = e.Message;
        }
        catch (Exception)
        {
            Error = $"Не удалось связаться с {ServerUrl}. Проверьте интернет или адрес сервера внизу.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [ObservableProperty]
    public partial bool IsEditingServer { get; set; }

    [ObservableProperty]
    public partial string ServerUrlEdit { get; set; } = "";

    [RelayCommand]
    private void EditServer()
    {
        ServerUrlEdit = ServerUrl;
        IsEditingServer = true;
    }

    [RelayCommand]
    private void SaveServer()
    {
        var url = ServerUrlEdit.Trim().TrimEnd('/');
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            Error = "Адрес сервера должен выглядеть как https://mc.example.com";
            return;
        }
        services.Settings.AuthServerUrl = url;
        services.SaveSettingsSoon();
        IsEditingServer = false;
        Error = null;
        OnPropertyChanged(nameof(ServerUrl));
    }

    [RelayCommand]
    private void OpenRegister() => services.Platform.OpenUrl(ServerUrl.TrimEnd('/') + "/Account/Register");

    [RelayCommand]
    private void OpenForgotPassword() => services.Platform.OpenUrl(ServerUrl.TrimEnd('/') + "/Account/ForgotPassword");
}
