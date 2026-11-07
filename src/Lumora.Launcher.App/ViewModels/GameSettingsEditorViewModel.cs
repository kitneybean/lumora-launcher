using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;
using Lumora.Launcher.Core.Java;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.App.ViewModels;

/// <summary>
/// Edits a <see cref="GameSettings"/> object (global or per build).
/// Shown as three sections: Java, memory and game.
/// </summary>
public partial class GameSettingsEditorViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly GameSettings _target;
    private readonly Action _changed;
    private bool _loading;

    public GameSettingsEditorViewModel(AppServices services, GameSettings target, GameSettings effective, int recommendedMemoryMb, Action changed)
    {
        _services = services;
        _target = target;
        _changed = changed;
        RecommendedMemoryMb = recommendedMemoryMb;

        var total = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
        SystemMemoryMb = total > 0 ? total : 8192;
        MemoryMaximum = Math.Max(2048, SystemMemoryMb - 1024) / 256 * 256;

        _loading = true;
        UseCustomJava = effective.JavaPath is not null;
        JavaPath = effective.JavaPath ?? "";
        MaxMemoryMb = Math.Clamp(effective.MaxMemoryMb ?? 4096, 1024, MemoryMaximum);
        MinMemoryMb = Math.Clamp(effective.MinMemoryMb ?? 1024, 512, MaxMemoryMb);
        JvmArguments = string.Join(Environment.NewLine, effective.ExtraJvmArguments ?? []);
        EnvironmentVariables = string.Join(Environment.NewLine, (effective.EnvironmentVariables ?? []).Select(kv => $"{kv.Key}={kv.Value}"));
        WindowWidth = effective.WindowWidth ?? 1280;
        WindowHeight = effective.WindowHeight ?? 720;
        Fullscreen = effective.Fullscreen ?? false;
        AutoConnect = effective.AutoConnect ?? false;
        WrapperCommand = effective.WrapperCommand ?? "";
        PreLaunchCommand = effective.PreLaunchCommand ?? "";
        PostExitCommand = effective.PostExitCommand ?? "";
        _loading = false;
    }

    // --- Java ---

    [ObservableProperty]
    public partial bool UseCustomJava { get; set; }

    [ObservableProperty]
    public partial string JavaPath { get; set; }

    public ObservableCollection<JavaInstallation> DetectedJavas { get; } = [];

    [ObservableProperty]
    public partial JavaInstallation? SelectedJava { get; set; }

    [ObservableProperty]
    public partial bool IsDetecting { get; set; }

    [ObservableProperty]
    public partial string? DetectResult { get; set; }

    partial void OnSelectedJavaChanged(JavaInstallation? value)
    {
        if (value is not null)
            JavaPath = value.Executable;
    }

    [RelayCommand]
    private async Task DetectJavaAsync()
    {
        IsDetecting = true;
        try
        {
            var found = await Task.Run(() => JavaLocator.FindInstalled([_services.Paths.RuntimesDirectory(_services.Settings)]));
            DetectedJavas.Clear();
            foreach (var j in found)
                DetectedJavas.Add(j);
            DetectResult = found.Count == 0
                ? "Java не найдена. Лаунчер скачает нужную версию сам."
                : $"Найдено: {found.Count}. Для Minecraft 1.21.1 нужна Java 21 или новее.";
            SelectedJava = found.FirstOrDefault(j => string.Equals(j.Executable, JavaPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            IsDetecting = false;
        }
    }

    [RelayCommand]
    private async Task BrowseJavaAsync()
    {
        var types = OperatingSystem.IsWindows()
            ? new[] { new FilePickerFileType("Java") { Patterns = ["java.exe", "javaw.exe"] } }
            : [];
        if (await _services.Platform.PickFileAsync("Выберите исполняемый файл Java", types) is { } path)
            JavaPath = path;
    }

    // --- Memory ---

    public int SystemMemoryMb { get; }
    public int MemoryMaximum { get; }
    public int RecommendedMemoryMb { get; }
    public string SystemMemoryText => $"Всего памяти: {Text.Memory(SystemMemoryMb)}";
    public string RecommendedText => RecommendedMemoryMb > 0 ? $"Рекомендуется для сборки: {Text.Memory(RecommendedMemoryMb)}" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxMemoryText), nameof(MemoryWarning), nameof(HasMemoryWarning))]
    public partial int MaxMemoryMb { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MinMemoryText), nameof(SelectedMinMemory))]
    public partial int MinMemoryMb { get; set; }

    public IReadOnlyList<Choice<int>> MinMemoryChoices { get; } =
        [new(512, "512 МБ"), new(1024, "1 ГБ"), new(2048, "2 ГБ"), new(4096, "4 ГБ")];

    public Choice<int>? SelectedMinMemory
    {
        get => MinMemoryChoices.FirstOrDefault(c => c.Value == MinMemoryMb);
        set
        {
            if (value is not null)
                MinMemoryMb = Math.Min(value.Value, MaxMemoryMb);
        }
    }

    public string MaxMemoryText => Text.Memory(MaxMemoryMb);
    public string MemoryMaximumText => Text.Memory(MemoryMaximum);
    public bool HasRecommended => RecommendedMemoryMb > 0;
    public bool HasMemoryWarning => MemoryWarning is not null;
    public string MinMemoryText => Text.Memory(MinMemoryMb);

    public string? MemoryWarning =>
        RecommendedMemoryMb > 0 && MaxMemoryMb < RecommendedMemoryMb ? "Меньше рекомендуемого — возможны подвисания." :
        MaxMemoryMb > SystemMemoryMb * 3 / 4 ? "Слишком много — системе и другим программам может не хватить памяти." :
        null;

    [RelayCommand]
    private void UseRecommendedMemory()
    {
        if (RecommendedMemoryMb > 0)
            MaxMemoryMb = Math.Min(RecommendedMemoryMb, MemoryMaximum);
    }

    [ObservableProperty]
    public partial string JvmArguments { get; set; }

    [ObservableProperty]
    public partial string EnvironmentVariables { get; set; }

    // --- Game ---

    [ObservableProperty]
    public partial decimal WindowWidth { get; set; }

    [ObservableProperty]
    public partial decimal WindowHeight { get; set; }

    [ObservableProperty]
    public partial bool Fullscreen { get; set; }

    [ObservableProperty]
    public partial bool AutoConnect { get; set; }

    [ObservableProperty]
    public partial string WrapperCommand { get; set; }

    [ObservableProperty]
    public partial string PreLaunchCommand { get; set; }

    [ObservableProperty]
    public partial string PostExitCommand { get; set; }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(DetectResult) or nameof(IsDetecting) or nameof(SelectedJava)
                or nameof(SelectedMinMemory) or nameof(MaxMemoryText) or nameof(MinMemoryText) or nameof(MemoryWarning) or nameof(HasMemoryWarning))
            return;
        if (e.PropertyName == nameof(MaxMemoryMb) && MinMemoryMb > MaxMemoryMb)
            MinMemoryMb = MaxMemoryMb;
        WriteBack();
        _changed();
    }

    private void WriteBack()
    {
        _target.JavaPath = UseCustomJava && !string.IsNullOrWhiteSpace(JavaPath) ? JavaPath.Trim() : null;
        _target.MaxMemoryMb = MaxMemoryMb;
        _target.MinMemoryMb = MinMemoryMb;
        _target.ExtraJvmArguments = Lines(JvmArguments).ToList();
        _target.EnvironmentVariables = Lines(EnvironmentVariables)
            .Select(l => l.Split('=', 2))
            .Where(p => p.Length == 2 && p[0].Trim().Length > 0)
            .GroupBy(p => p[0].Trim())
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim());
        _target.WindowWidth = (int)WindowWidth;
        _target.WindowHeight = (int)WindowHeight;
        _target.Fullscreen = Fullscreen;
        _target.AutoConnect = AutoConnect;
        _target.WrapperCommand = NullIfEmpty(WrapperCommand);
        _target.PreLaunchCommand = NullIfEmpty(PreLaunchCommand);
        _target.PostExitCommand = NullIfEmpty(PostExitCommand);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
