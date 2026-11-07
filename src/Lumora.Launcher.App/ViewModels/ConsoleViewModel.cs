using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lumora.Launcher.App.Services;

namespace Lumora.Launcher.App.ViewModels;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Launcher,
}

public sealed record LogLine(string Time, string Text, LogLevel Level)
{
    public bool IsWarning => Level == LogLevel.Warning;
    public bool IsError => Level == LogLevel.Error;
    public bool IsLauncher => Level == LogLevel.Launcher;
}

/// <summary>Game output, like Prism's log window.</summary>
public partial class ConsoleViewModel(AppServices services) : ViewModelBase
{
    private const int MaxLines = 5000;
    private readonly List<LogLine> _all = [];
    private Action? _stop;
    private StreamWriter? _file;

    public ObservableCollection<LogLine> Lines { get; } = [];

    public IReadOnlyList<Choice<LogLevel?>> Filters { get; } =
    [
        new(null, "Все"),
        new(LogLevel.Warning, "Предупреждения"),
        new(LogLevel.Error, "Ошибки"),
    ];

    [ObservableProperty]
    public partial Choice<LogLevel?>? SelectedFilter { get; set; }

    [ObservableProperty]
    public partial bool AutoScroll { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StopGameCommand))]
    public partial bool IsGameRunning { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Игра не запущена";

    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Raised after a line was added so the view can scroll to the end.</summary>
    public event Action? LineAdded;

    partial void OnSelectedFilterChanged(Choice<LogLevel?>? value) => Rebuild();

    public void BeginSession(string serverName, Action stop)
    {
        _stop = stop;
        IsGameRunning = true;
        Title = serverName;
        try
        {
            Directory.CreateDirectory(services.Paths.LogsDirectory);
            var file = Path.Combine(services.Paths.LogsDirectory, $"game-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _file = new StreamWriter(file, append: false, Encoding.UTF8) { AutoFlush = true };
        }
        catch (IOException)
        {
            _file = null;
        }
        Append($"Запуск «{serverName}»", LogLevel.Launcher);
    }

    public void EndSession(int? exitCode)
    {
        Append(exitCode is null ? "Игра остановлена" : $"Игра завершилась с кодом {exitCode}",
            exitCode is null or 0 ? LogLevel.Launcher : LogLevel.Error);
        IsGameRunning = false;
        _stop = null;
        _file?.Dispose();
        _file = null;
    }

    public void AppendFromAnyThread(string text) => Dispatcher.UIThread.Post(() => Append(text, Classify(text)));

    private void Append(string text, LogLevel level)
    {
        var line = new LogLine(DateTime.Now.ToString("HH:mm:ss"), text, level);
        _file?.WriteLine($"[{line.Time}] {text}");
        _all.Add(line);
        if (_all.Count > MaxLines)
            _all.RemoveRange(0, _all.Count - MaxLines);

        if (Matches(line))
        {
            Lines.Add(line);
            if (Lines.Count > MaxLines)
                Lines.RemoveAt(0);
            LineAdded?.Invoke();
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool Matches(LogLine line) => SelectedFilter?.Value switch
    {
        null => true,
        LogLevel.Warning => line.Level is LogLevel.Warning or LogLevel.Error,
        var level => line.Level == level,
    };

    private void Rebuild()
    {
        Lines.Clear();
        foreach (var line in _all.Where(Matches))
            Lines.Add(line);
        OnPropertyChanged(nameof(IsEmpty));
    }

    private static LogLevel Classify(string text) =>
        text.StartsWith("[Lumora]", StringComparison.Ordinal) ? LogLevel.Launcher :
        text.Contains("/ERROR]", StringComparison.Ordinal) || text.Contains("Exception", StringComparison.Ordinal) ? LogLevel.Error :
        text.Contains("/WARN]", StringComparison.Ordinal) ? LogLevel.Warning :
        LogLevel.Info;

    [RelayCommand]
    private async Task CopyAsync()
    {
        await services.Platform.CopyTextAsync(string.Join(Environment.NewLine, Lines.Select(l => $"[{l.Time}] {l.Text}")));
        services.Toasts.Show("Лог скопирован в буфер обмена");
    }

    [RelayCommand]
    private void Clear()
    {
        _all.Clear();
        Lines.Clear();
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void OpenLogsFolder() => services.Platform.OpenFolder(services.Paths.LogsDirectory);

    [RelayCommand(CanExecute = nameof(IsGameRunning))]
    private void StopGame() => _stop?.Invoke();
}
