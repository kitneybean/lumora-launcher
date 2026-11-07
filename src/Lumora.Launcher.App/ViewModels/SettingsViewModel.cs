using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.App.ViewModels;

public sealed record Choice<T>(T Value, string Title);

/// <summary>Global launcher settings page.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private LauncherSettings S => _services.Settings;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Game = new GameSettingsEditorViewModel(services, S.Game, S.Game.Over(GameSettings.Defaults), 0, services.SaveSettingsSoon);

        SelectedTheme = Find(Themes, S.Theme);
        SelectedVisibility = Find(Visibilities, S.OnGameStart);
        ShowGameConsole = S.ShowGameConsole;
        DownloadThreads = S.DownloadThreads;
        ProxyUrl = S.ProxyUrl ?? "";
        DataDirectory = S.DataDirectory ?? "";
        AuthServerUrl = S.AuthServerUrl;
        _ready = true;
    }

    /// <summary>False while the constructor fills the fields, so loading does not count as a change.</summary>
    private readonly bool _ready;

    public GameSettingsEditorViewModel Game { get; }

    public IReadOnlyList<Choice<string>> Themes { get; } =
    [
        new("dark", "Тёмная"),
        new("light", "Светлая"),
        new("system", "Как в системе"),
    ];

    public IReadOnlyList<Choice<LauncherVisibility>> Visibilities { get; } =
    [
        new(LauncherVisibility.Minimize, "Свернуть лаунчер"),
        new(LauncherVisibility.Close, "Скрыть лаунчер до выхода из игры"),
        new(LauncherVisibility.KeepOpen, "Оставить открытым"),
    ];

    public IReadOnlyList<Choice<string>> Languages { get; } = [new("ru", "Русский")];

    [ObservableProperty]
    public partial Choice<string> SelectedTheme { get; set; }

    [ObservableProperty]
    public partial Choice<LauncherVisibility> SelectedVisibility { get; set; }

    public Choice<string> SelectedLanguage => Languages[0];

    [ObservableProperty]
    public partial bool ShowGameConsole { get; set; }

    [ObservableProperty]
    public partial decimal DownloadThreads { get; set; }

    [ObservableProperty]
    public partial string ProxyUrl { get; set; }

    [ObservableProperty]
    public partial string DataDirectory { get; set; }

    [ObservableProperty]
    public partial string AuthServerUrl { get; set; }

    public string DefaultDataDirectory => _services.Paths.Root;

    partial void OnSelectedThemeChanged(Choice<string> value)
    {
        if (!_ready)
            return;
        S.Theme = value.Value;
        _services.ApplyTheme();
        _services.SaveSettingsSoon();
    }

    partial void OnSelectedVisibilityChanged(Choice<LauncherVisibility> value) => Save(() => S.OnGameStart = value.Value);
    partial void OnShowGameConsoleChanged(bool value) => Save(() => S.ShowGameConsole = value);
    partial void OnDownloadThreadsChanged(decimal value) => Save(() => S.DownloadThreads = (int)value);
    partial void OnProxyUrlChanged(string value) => Save(() => S.ProxyUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    partial void OnDataDirectoryChanged(string value) => Save(() => S.DataDirectory = string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    partial void OnAuthServerUrlChanged(string value) => Save(() => S.AuthServerUrl = value.Trim());

    private void Save(Action apply)
    {
        if (!_ready)
            return;
        apply();
        _services.SaveSettingsSoon();
    }

    [RelayCommand]
    private async Task BrowseDataDirectoryAsync()
    {
        if (await _services.Platform.PickFolderAsync("Папка для игровых данных") is { } path)
            DataDirectory = path;
    }

    [RelayCommand]
    private void ResetDataDirectory() => DataDirectory = "";

    // --- Maintenance ---

    [RelayCommand]
    private void OpenDataFolder() => _services.Platform.OpenFolder(_services.Paths.DataDirectory(S));

    [RelayCommand]
    private void OpenLogsFolder() => _services.Platform.OpenFolder(_services.Paths.LogsDirectory);

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var cache = _services.Paths.CacheDirectory(S);
        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(cache))
                    Directory.Delete(cache, recursive: true);
            });
            _services.Toasts.Success("Кэш очищен");
        }
        catch (Exception e)
        {
            _services.Toasts.Error($"Не удалось очистить кэш: {e.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        try
        {
            var logs = _services.Paths.LogsDirectory;
            Directory.CreateDirectory(logs);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var zip = Path.Combine(desktop, $"lumora-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            await Task.Run(() => ZipFile.CreateFromDirectory(logs, zip));
            _services.Toasts.Show($"Логи сохранены: {Path.GetFileName(zip)} на рабочем столе", ToastKind.Success,
                "Показать", () => _services.Platform.OpenFolder(desktop));
        }
        catch (Exception e)
        {
            _services.Toasts.Error($"Не удалось сохранить логи: {e.Message}");
        }
    }

    private static Choice<T> Find<T>(IReadOnlyList<Choice<T>> list, T value)
    {
        foreach (var c in list)
        {
            if (EqualityComparer<T>.Default.Equals(c.Value, value))
                return c;
        }
        return list[0];
    }
}
