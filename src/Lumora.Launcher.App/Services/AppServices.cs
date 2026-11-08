using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;
using Lumora.Shared.Status;

namespace Lumora.Launcher.App.Services;

/// <summary>Composition root: every service the view models need.</summary>
public sealed class AppServices
{
    public AppServices(SettingsStore store, ILumoraApi api, IGameService game, IServerStatusService status, bool isDemo)
    {
        Store = store;
        Api = api;
        Game = game;
        Status = status;
        IsDemo = isDemo;
        Settings = store.LoadGlobal();
        // First builds pointed at a local dev server; switch such installs to the real one.
        if (string.IsNullOrWhiteSpace(Settings.AuthServerUrl) || Settings.AuthServerUrl.TrimEnd('/') == LauncherSettings.LegacyDevAuthServerUrl)
            Settings.AuthServerUrl = LauncherSettings.DefaultAuthServerUrl;
        Appearance = new Appearance
        {
            ShowBackground = Settings.ShowBackground,
            AnimateBackground = Settings.AnimateBackground,
        };
        Updates = new UpdateController(this, Lumora.Launcher.Core.Api.LumoraApiClient.CreateHttpClient(Settings.ProxyUrl));
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Store.SaveGlobal(Settings);
        };
    }

    private readonly DispatcherTimer _saveTimer;

    public SettingsStore Store { get; }
    public LauncherPaths Paths => Store.Paths;
    public LauncherSettings Settings { get; }
    public Appearance Appearance { get; }
    public UpdateController Updates { get; }
    public ILumoraApi Api { get; }
    public IGameService Game { get; }
    public IServerStatusService Status { get; }
    public bool IsDemo { get; }
    public Platform Platform { get; } = new();

    /// <summary>Saved login ("Запомнить меня"); null in demo mode.</summary>
    public Lumora.Launcher.Core.Api.SessionStore? Sessions { get; init; }
    public ToastService Toasts { get; } = new();

    /// <summary>Saves settings.json shortly after the last change (debounced).</summary>
    public void SaveSettingsSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void ApplyTheme()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = Settings.Theme switch
            {
                "light" => ThemeVariant.Light,
                // Resolve "system" explicitly: resource lookups need a concrete Light/Dark variant.
                "system" => app.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Light
                    ? ThemeVariant.Light
                    : ThemeVariant.Dark,
                _ => ThemeVariant.Dark,
            };
        }
    }
}

/// <summary>OS integration: browser, file manager, clipboard, pickers, main window state.</summary>
public sealed class Platform
{
    public Window? MainWindow { get; set; }

    public void OpenUrl(string url) => Launch(url);

    public void OpenFolder(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        Launch(path);
    }

    private static void Launch(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Nothing sensible to do: the OS has no handler for it.
        }
    }

    public async Task CopyTextAsync(string text)
    {
        if (MainWindow?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    public async Task<string?> PickFileAsync(string title, params FilePickerFileType[] types)
    {
        if (MainWindow is null)
            return null;
        var files = await MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types.Length > 0 ? types : null,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        if (MainWindow is null)
            return null;
        var folders = await MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public void ApplyGameStartBehavior(LauncherVisibility visibility)
    {
        if (MainWindow is null)
            return;
        switch (visibility)
        {
            case LauncherVisibility.Minimize:
                MainWindow.WindowState = WindowState.Minimized;
                break;
            case LauncherVisibility.Close:
                MainWindow.Hide();
                break;
        }
    }

    public void RestoreMainWindow()
    {
        if (MainWindow is null)
            return;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }
}
