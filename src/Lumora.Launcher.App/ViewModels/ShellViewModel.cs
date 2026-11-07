using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Services;
using Material.Icons;

namespace Lumora.Launcher.App.ViewModels;

public sealed record NavItem(string Title, MaterialIconKind Icon, ViewModelBase Page);

/// <summary>Main window after login: sidebar navigation and the current page.</summary>
public partial class ShellViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly Action _onLoggedOut;

    public ShellViewModel(AppServices services, AccountSession session, Action onLoggedOut)
    {
        _services = services;
        _onLoggedOut = onLoggedOut;
        Session = session;

        Console = new ConsoleViewModel(services);
        Servers = new ServersViewModel(services, this);
        Skin = new SkinViewModel(services, this);
        Settings = new SettingsViewModel(services);

        NavItems =
        [
            new("Серверы", MaterialIconKind.ServerOutline, Servers),
            new("Скин", MaterialIconKind.TshirtCrewOutline, Skin),
            new("Настройки", MaterialIconKind.CogOutline, Settings),
            new("Консоль", MaterialIconKind.Console, Console),
        ];
        SelectedNav = NavItems[0];
        CurrentPage = Servers;

        _ = LoadSkinAsync();
    }

    public AccountSession Session { get; }
    public string PlayerName => Session.PlayerName;
    public bool IsDemo => _services.IsDemo;
    public Appearance Appearance => _services.Appearance;
    public string Version => typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public ServersViewModel Servers { get; }
    public SkinViewModel Skin { get; }
    public SettingsViewModel Settings { get; }
    public ConsoleViewModel Console { get; }

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    public partial NavItem? SelectedNav { get; set; }

    [ObservableProperty]
    public partial ViewModelBase CurrentPage { get; set; }

    /// <summary>Player skin shared by the sidebar avatar and the skin page.</summary>
    [ObservableProperty]
    public partial Bitmap? SkinBitmap { get; set; }

    [ObservableProperty]
    public partial bool SlimModel { get; set; }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null)
            CurrentPage = value.Page;
    }

    /// <summary>Opens a page that is not in the sidebar (e.g. build settings).</summary>
    public void OpenSubPage(ViewModelBase page)
    {
        SelectedNav = null;
        CurrentPage = page;
    }

    public void GoTo(ViewModelBase page)
    {
        foreach (var item in NavItems)
        {
            if (item.Page == page)
            {
                SelectedNav = item;
                return;
            }
        }
    }

    public void ShowConsole() => GoTo(Console);

    public void SetSkin(byte[] png, bool slim)
    {
        using var stream = new MemoryStream(png);
        SkinBitmap = new Bitmap(stream);
        SlimModel = slim;
    }

    private async Task LoadSkinAsync()
    {
        try
        {
            if (await _services.Api.GetSkinAsync(Session) is { } png)
                SetSkin(png, Session.SlimModel);
        }
        catch (Exception)
        {
            // Default avatar is shown when the skin cannot be loaded.
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        try
        {
            await _services.Api.LogoutAsync(Session);
        }
        catch (Exception)
        {
            // Logging out locally is enough if the server is unreachable.
        }
        _onLoggedOut();
    }
}
