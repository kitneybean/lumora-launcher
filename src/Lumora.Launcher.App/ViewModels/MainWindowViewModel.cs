using System;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Services;

namespace Lumora.Launcher.App.ViewModels;

/// <summary>Root: shows the login screen or the main shell; restores a remembered session on start.</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        var login = new LoginViewModel(services, OnLoggedIn);
        CurrentScreen = login;
        _ = services.Updates.CheckOnStartAsync();

        if (services.Sessions?.Load() is { } saved)
        {
            login.Login = saved.PlayerName;
            login.IsBusy = true;
            _ = RestoreAsync(saved, login);
        }
    }

    public ToastService Toasts => _services.Toasts;

    [ObservableProperty]
    public partial ViewModelBase CurrentScreen { get; set; }

    private async Task RestoreAsync(AccountSession saved, LoginViewModel login)
    {
        try
        {
            var session = await _services.Api.RefreshAsync(saved);
            _services.Sessions?.Save(session);
            OnLoggedIn(session);
        }
        catch (SessionExpiredException)
        {
            _services.Sessions?.Clear();
            login.IsBusy = false;
            login.Error = "Сессия истекла — войдите заново.";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or AuthenticationFailedException)
        {
            // Server unreachable: keep the saved session, the servers page shows the connection error.
            OnLoggedIn(saved);
        }
    }

    private void OnLoggedIn(AccountSession session) =>
        CurrentScreen = new ShellViewModel(_services, session, OnLoggedOut);

    private void OnLoggedOut()
    {
        _services.Sessions?.Clear();
        CurrentScreen = new LoginViewModel(_services, OnLoggedIn);
    }
}
