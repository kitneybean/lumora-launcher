using CommunityToolkit.Mvvm.ComponentModel;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Services;

namespace Lumora.Launcher.App.ViewModels;

/// <summary>Root: shows the login screen or the main shell.</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainWindowViewModel(AppServices services)
    {
        _services = services;
        CurrentScreen = new LoginViewModel(services, OnLoggedIn);
    }

    public ToastService Toasts => _services.Toasts;

    [ObservableProperty]
    public partial ViewModelBase CurrentScreen { get; set; }

    private void OnLoggedIn(AccountSession session) =>
        CurrentScreen = new ShellViewModel(_services, session, OnLoggedOut);

    private void OnLoggedOut() =>
        CurrentScreen = new LoginViewModel(_services, OnLoggedIn);
}
