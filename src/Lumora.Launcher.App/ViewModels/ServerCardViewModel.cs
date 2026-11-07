using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Contracts;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;
using Material.Icons;

namespace Lumora.Launcher.App.ViewModels;

/// <summary>One server card: status, build info, install/launch with progress.</summary>
public partial class ServerCardViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ShellViewModel _shell;
    private readonly Action _statusChanged;
    private CancellationTokenSource? _cts;

    public ServerCardViewModel(AppServices services, ShellViewModel shell, LauncherServerDto dto, Action statusChanged)
    {
        _services = services;
        _shell = shell;
        _statusChanged = statusChanged;
        Dto = dto;
        State = services.Game.GetState(dto.Build);
    }

    public LauncherServerDto Dto { get; }
    public string Name => Dto.Name;
    public string? Description => Dto.Description;
    /// <summary>
    /// Letter for the icon tile: first letter of the last word, so "Lumora Survival" and
    /// "Lumora Tech" get "S" and "T" instead of the same "L".
    /// </summary>
    public string Initial
    {
        get
        {
            var words = Dto.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0 ? "?" : words[^1][..1].ToUpperInvariant();
        }
    }

    private static readonly Color[] TilePalette =
    [
        Color.Parse("#3DD68C"), Color.Parse("#6EA8FE"), Color.Parse("#A78BFA"),
        Color.Parse("#F2B84B"), Color.Parse("#F472B6"), Color.Parse("#22D3EE"),
    ];

    private Color TileColor
    {
        get
        {
            // Stable across runs (string.GetHashCode is randomized per process).
            var hash = 0;
            foreach (var ch in Dto.Name)
                hash = hash * 31 + ch;
            return TilePalette[(hash & int.MaxValue) % TilePalette.Length];
        }
    }

    public IBrush TileForeground => new SolidColorBrush(TileColor);
    public IBrush TileBackground => new SolidColorBrush(TileColor, 0.16);
    public string Address => Dto.Port == 25565 ? Dto.Address : $"{Dto.Address}:{Dto.Port}";
    public string MinecraftVersion => Dto.Build.MinecraftVersion;

    public string LoaderText => Dto.Build.Loader switch
    {
        LoaderType.Vanilla => "Vanilla",
        LoaderType.NeoForge => $"NeoForge {Dto.Build.LoaderVersion}",
        LoaderType.Forge => $"Forge {Dto.Build.LoaderVersion}",
        LoaderType.Fabric => $"Fabric {Dto.Build.LoaderVersion}",
        _ => Dto.Build.Loader.ToString(),
    };

    public bool HasLoader => Dto.Build.Loader != LoaderType.Vanilla;

    public BuildManifestDto? Manifest { get; private set; }

    [ObservableProperty]
    public partial string? ModsText { get; set; }

    // --- Live status ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial ServerStatus? Status { get; set; }

    public bool IsOnline => Status?.IsOnline == true;
    public int Players => Status?.Players ?? 0;

    public string StatusText => Status switch
    {
        null => "Проверка…",
        { IsOnline: false } => "Не в сети",
        var s => $"{s.Players} / {s.MaxPlayers} · {s.PingMs} мс",
    };

    // --- Install / launch state ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText), nameof(PrimaryIcon), nameof(NeedsUpdate))]
    public partial InstanceState State { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle), nameof(PrimaryText), nameof(PrimaryIcon))]
    public partial bool IsInstalling { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(IsIdle), nameof(PrimaryText), nameof(PrimaryIcon))]
    public partial bool IsRunning { get; set; }

    public bool IsBusy => IsInstalling || IsRunning;
    public bool IsIdle => !IsBusy;
    public bool NeedsUpdate => State == InstanceState.UpdateAvailable;

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial bool ProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial string ProgressStage { get; set; } = "";

    [ObservableProperty]
    public partial string? ProgressDetail { get; set; }

    public string PrimaryText => IsRunning ? "Запущено" : State switch
    {
        InstanceState.NotInstalled => "Установить",
        InstanceState.UpdateAvailable => "Обновить",
        _ => "Играть",
    };

    public MaterialIconKind PrimaryIcon => IsRunning ? MaterialIconKind.Check : State switch
    {
        InstanceState.NotInstalled => MaterialIconKind.Download,
        InstanceState.UpdateAvailable => MaterialIconKind.Update,
        _ => MaterialIconKind.Play,
    };

    public async Task LoadDetailsAsync()
    {
        var statusTask = _services.Status.GetStatusAsync(Dto.Address, Dto.Port);
        try
        {
            Manifest = await _services.Api.GetManifestAsync(_shell.Session, Dto.Build.Id);
            var mods = Manifest.Files.Count(f => f.Path.StartsWith("mods/", StringComparison.Ordinal));
            ModsText = mods > 0 ? Text.Plural(mods, "мод", "мода", "модов") : null;
        }
        catch (Exception)
        {
            ModsText = null;
        }

        try
        {
            Status = await statusTask;
        }
        catch (Exception)
        {
            Status = ServerStatus.Offline;
        }
        OnPropertyChanged(nameof(IsOnline));
        _statusChanged();
    }

    [RelayCommand]
    private async Task PrimaryAsync()
    {
        if (IsBusy)
            return;
        if (State != InstanceState.Ready)
        {
            await InstallAsync();
            return;
        }
        await LaunchAsync();
    }

    private async Task InstallAsync()
    {
        _cts = new CancellationTokenSource();
        IsInstalling = true;
        ProgressValue = 0;
        ProgressStage = "Подготовка…";
        ProgressDetail = null;
        var progress = new Progress<InstallProgress>(p =>
        {
            ProgressStage = p.Stage;
            ProgressDetail = p.Detail;
            ProgressIndeterminate = p.Fraction is null;
            ProgressValue = (p.Fraction ?? 0) * 100;
        });
        try
        {
            var wasUpdate = State == InstanceState.UpdateAvailable;
            await _services.Game.InstallAsync(_shell.Session, Dto.Build, progress, _cts.Token);
            State = _services.Game.GetState(Dto.Build);
            _services.Toasts.Success(wasUpdate ? $"Сборка «{Name}» обновлена" : $"Сборка «{Name}» установлена");
        }
        catch (OperationCanceledException)
        {
            _services.Toasts.Show("Установка отменена");
        }
        catch (Exception e)
        {
            _services.Toasts.Error($"Не удалось установить «{Name}»: {e.Message}");
        }
        finally
        {
            IsInstalling = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task LaunchAsync()
    {
        var instance = _services.Store.LoadInstance(_services.Settings, Dto.Build.Id);
        var settings = SettingsResolver.Resolve(_services.Settings, instance);
        if (Dto.AutoConnect && settings.AutoConnect is null)
            settings.AutoConnect = true;

        _cts = new CancellationTokenSource();
        IsRunning = true;
        var console = _shell.Console;
        console.BeginSession(Name, () => _cts?.Cancel());
        if (_services.Settings.ShowGameConsole)
            _shell.ShowConsole();
        _services.Platform.ApplyGameStartBehavior(_services.Settings.OnGameStart);

        try
        {
            var exitCode = await _services.Game.LaunchAsync(_shell.Session, Dto, settings, console.AppendFromAnyThread, _cts.Token);
            console.EndSession(exitCode);
            if (exitCode != 0)
            {
                _services.Platform.RestoreMainWindow();
                _services.Toasts.Error($"Игра завершилась с ошибкой (код {exitCode})", "Открыть консоль", _shell.ShowConsole);
            }
        }
        catch (OperationCanceledException)
        {
            console.EndSession(null);
        }
        catch (Exception e)
        {
            console.EndSession(null);
            _services.Platform.RestoreMainWindow();
            _services.Toasts.Error($"Не удалось запустить игру: {e.Message}", "Открыть консоль", _shell.ShowConsole);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            if (_services.Settings.OnGameStart != LauncherVisibility.KeepOpen)
                _services.Platform.RestoreMainWindow();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenSettings() => _shell.OpenSubPage(new InstanceSettingsViewModel(_services, _shell, this));

    [RelayCommand]
    private void OpenFolder() =>
        _services.Platform.OpenFolder(_services.Paths.InstanceDirectory(_services.Settings, Dto.Build.Id));

    [RelayCommand]
    private async Task CopyAddressAsync()
    {
        await _services.Platform.CopyTextAsync(Address);
        _services.Toasts.Show($"Адрес {Address} скопирован");
    }

    /// <summary>Marks the build as needing a full resync (used by "Переустановить").</summary>
    public void MarkForReinstall() => State = InstanceState.NotInstalled;
}
