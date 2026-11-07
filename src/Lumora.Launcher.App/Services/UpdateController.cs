using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.Core.Updates;

namespace Lumora.Launcher.App.Services;

/// <summary>Launcher self-update: state for the settings page and the start-up check.</summary>
public partial class UpdateController : ObservableObject
{
    private readonly AppServices _services;
    private readonly UpdateService _updates;

    public UpdateController(AppServices services, HttpClient http)
    {
        _services = services;
        _updates = new UpdateService(http, () => Repository);
    }

    /// <summary>Version of the launcher itself (not of whatever process hosts it, e.g. the screenshot tool).</summary>
    public static Version AppVersion { get; } = UpdateService.ParseVersion(
        System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(UpdateController).Assembly)?.InformationalVersion)
        ?? new Version(0, 0, 0);

    public string CurrentVersion => AppVersion.ToString(3);

    /// <summary>Repository from settings, or the one built into the release.</summary>
    public string? Repository =>
        string.IsNullOrWhiteSpace(_services.Settings.UpdateRepository) ? UpdateService.BuiltInRepository : _services.Settings.UpdateRepository.Trim();

    public bool IsConfigured => Repository is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate), nameof(InstallText))]
    public partial ReleaseInfo? Available { get; set; }

    public bool HasUpdate => Available is not null;

    public string InstallText => Available is { } r ? $"Обновить до {r.Version.ToString(3)}" : "";

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand), nameof(InstallCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    /// <summary>Silent check at start: at most twice a day, only shows a toast when there is something new.</summary>
    public async Task CheckOnStartAsync()
    {
        var s = _services.Settings;
        if (_services.IsDemo || !s.CheckUpdates || !IsConfigured || s.LastUpdateCheck > DateTimeOffset.UtcNow.AddHours(-12))
            return;
        await CheckCoreAsync(silent: true);
        if (Available is { } release)
            _services.Toasts.Show($"Вышла новая версия лаунчера — {release.Version.ToString(3)}", ToastKind.Info, "Обновить", () => _ = InstallAsync());
    }

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task CheckAsync() => CheckCoreAsync(silent: false);

    private async Task CheckCoreAsync(bool silent)
    {
        if (!IsConfigured)
        {
            Status = "Источник обновлений не задан — это сборка для разработки.";
            return;
        }
        IsBusy = true;
        Status = silent ? Status : "Проверяю…";
        try
        {
            Available = await _updates.CheckAsync(AppVersion);
            _services.Settings.LastUpdateCheck = DateTimeOffset.UtcNow;
            _services.SaveSettingsSoon();
            Status = Available is { } r ? $"Доступна версия {r.Version.ToString(3)}." : "У вас последняя версия.";
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (!silent)
                Status = "Не удалось проверить обновления: " + e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task InstallAsync()
    {
        if (Available is not { } release)
            return;
        var installDir = AppContext.BaseDirectory;
        if (!UpdateService.CanWrite(installDir))
        {
            Status = "Нет прав на запись в папку лаунчера — скачайте новую версию со страницы релиза.";
            _services.Platform.OpenUrl(release.HtmlUrl);
            return;
        }

        IsBusy = true;
        IsDownloading = true;
        Status = $"Скачиваю версию {release.Version.ToString(3)}…";
        try
        {
            var progress = new Progress<double>(p => Progress = p * 100);
            var staging = await _updates.DownloadAsync(release, Path.Combine(_services.Paths.Root, "updates"), progress);
            var exe = Path.GetFileName(Environment.ProcessPath) ?? (OperatingSystem.IsWindows() ? "Lumora.exe" : "Lumora");
            _services.Store.SaveGlobal(_services.Settings);
            UpdateService.ApplyAndRestart(staging, installDir, exe, Environment.ProcessId);
            Status = "Перезапускаю лаунчер…";
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (Exception e)
        {
            Status = "Не удалось обновить: " + e.Message;
            _services.Toasts.Error(Status);
        }
        finally
        {
            IsBusy = false;
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (Available is { } r)
            _services.Platform.OpenUrl(r.HtmlUrl);
        else if (Repository is { } repo)
            _services.Platform.OpenUrl($"https://github.com/{repo}/releases");
    }
}
