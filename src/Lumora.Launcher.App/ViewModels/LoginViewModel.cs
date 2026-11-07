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
            Error = "Сервер авторизации недоступен. Проверьте подключение к интернету.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenRegister() => services.Platform.OpenUrl(ServerUrl.TrimEnd('/') + "/Account/Register");

    [RelayCommand]
    private void OpenForgotPassword() => services.Platform.OpenUrl(ServerUrl.TrimEnd('/') + "/Account/ForgotPassword");
}
