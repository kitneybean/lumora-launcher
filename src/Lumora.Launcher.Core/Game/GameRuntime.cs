using System.Diagnostics;
using System.Text;
using CmlLib.Core;
using CmlLib.Core.Installers;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.Core.Game;

/// <summary>Pieces shared by server builds and the player's own builds: game folders, progress and the game process.</summary>
public static class GameRuntime
{
    /// <summary>CmlLib launcher for a game folder; libraries, versions, assets and Java live in the shared folder.</summary>
    public static MinecraftLauncher CreateLauncher(LauncherPaths paths, LauncherSettings settings, string gameDir)
    {
        var shared = paths.SharedDirectory(settings);
        var path = new MinecraftPath(gameDir)
        {
            Library = Path.Combine(shared, "libraries"),
            Versions = Path.Combine(shared, "versions"),
            Assets = Path.Combine(shared, "assets"),
            Runtime = Path.Combine(shared, "runtime"),
            Resource = Path.Combine(shared, "resources"),
        };
        path.CreateDirs();
        return new MinecraftLauncher(path);
    }

    /// <summary>
    /// Prepares and runs the game process: output to <paramref name="log"/>, environment, wrapper, pre-launch and
    /// post-exit commands. Returns the exit code; cancelling kills the game.
    /// </summary>
    public static async Task<int> RunAsync(Process process, string gameDir, string displayName, GameSettings game,
        Action<string> log, CancellationToken ct)
    {
        var start = process.StartInfo;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        start.WorkingDirectory = gameDir;
        foreach (var (key, value) in game.EnvironmentVariables ?? [])
            start.Environment[key] = value;
        start.Environment["INST_DIR"] = gameDir;
        start.Environment["INST_NAME"] = displayName;
        start.Environment["INST_JAVA"] = start.FileName;
        ApplyWrapper(start, game.WrapperCommand);

        if (!string.IsNullOrWhiteSpace(game.PreLaunchCommand))
        {
            log($"[Lumora] Команда перед запуском: {game.PreLaunchCommand}");
            var code = await RunShellAsync(game.PreLaunchCommand, start, log, ct);
            if (code != 0)
                throw new InvalidOperationException($"Команда перед запуском завершилась с кодом {code}.");
        }

        log($"[Lumora] Java: {start.FileName}");
        var output = new Log4jXmlReader(log);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Line(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            log("[Lumora] Остановка игры…");
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        var exitCode = process.ExitCode;
        if (!string.IsNullOrWhiteSpace(game.PostExitCommand))
        {
            log($"[Lumora] Команда после выхода: {game.PostExitCommand}");
            await RunShellAsync(game.PostExitCommand, start, log, CancellationToken.None);
        }
        return exitCode;
    }

    /// <summary>"gamemoderun" or "prime-run": runs java through the wrapper.</summary>
    private static void ApplyWrapper(ProcessStartInfo start, string? wrapper)
    {
        if (string.IsNullOrWhiteSpace(wrapper))
            return;
        var parts = wrapper.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var originalArgs = start.Arguments;
        var java = start.FileName;
        start.FileName = parts[0];
        start.Arguments = (parts.Length > 1 ? parts[1] + " " : "") + Quote(java) + " " + originalArgs;
    }

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    private static async Task<int> RunShellAsync(string command, ProcessStartInfo game, Action<string> log, CancellationToken ct)
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", ["/c", command])
            : new ProcessStartInfo("/bin/sh", ["-c", command]);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.WorkingDirectory = game.WorkingDirectory;
        foreach (var key in new[] { "INST_DIR", "INST_NAME", "INST_JAVA" })
            start.Environment[key] = game.Environment[key];

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return process.ExitCode;
    }
}

/// <summary>Maps CmlLib file/byte events to one overall progress bar with stage weights, throttled.</summary>
public sealed class StageReporter(IProgress<InstallProgress> target)
{
    private string _stage = "";
    private double _start, _weight;
    private long _lastReport;
    private long _bytesTotal;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _stageStartMs;
    private double _stageFraction;

    public IProgress<InstallerProgressChangedEventArgs> Files { get; private set; } = null!;
    public IProgress<ByteProgress> Bytes { get; private set; } = null!;

    public void Begin(string stage, double start, double weight)
    {
        _stage = stage;
        _start = start;
        _weight = weight;
        _bytesTotal = 0;
        _stageFraction = 0;
        _stageStartMs = _clock.ElapsedMilliseconds;
        Files = new SyncProgress<InstallerProgressChangedEventArgs>(e =>
        {
            if (e.TotalTasks > 0)
                Report((double)e.ProgressedTasks / e.TotalTasks, $"{e.ProgressedTasks}/{e.TotalTasks} файлов", force: e.ProgressedTasks == e.TotalTasks);
        });
        Bytes = new SyncProgress<ByteProgress>(b =>
        {
            if (b.TotalBytes > 0)
                ReportBytes(b.ProgressedBytes, b.TotalBytes);
        });
        target.Report(new InstallProgress(stage, start));
    }

    public void SetBytesTotal(long total) => _bytesTotal = total;

    public void ReportBytes(long done, long total)
    {
        total = Math.Max(total, _bytesTotal);
        if (total <= 0)
            return;
        var seconds = Math.Max(0.5, (_clock.ElapsedMilliseconds - _stageStartMs) / 1000.0);
        var speed = done / seconds / 1024 / 1024;
        Report((double)done / total, $"{done / 1024.0 / 1024:0.0} из {total / 1024.0 / 1024:0.0} МБ · {speed:0.0} МБ/с", done >= total);
    }

    /// <summary>Progress inside the current stage, 0..1, with a free-form detail line.</summary>
    public void ReportStage(double fraction, string detail) => Report(fraction, detail, force: fraction >= 1);

    public void Done() => target.Report(new InstallProgress("Готово", 1));

    private void Report(double fraction, string detail, bool force)
    {
        // File and byte events both drive the bar: never let it move backwards within a stage.
        fraction = Math.Max(Math.Clamp(fraction, 0, 1), _stageFraction);
        _stageFraction = fraction;
        var now = _clock.ElapsedMilliseconds;
        if (!force && now - Interlocked.Read(ref _lastReport) < 120)
            return;
        Interlocked.Exchange(ref _lastReport, now);
        target.Report(new InstallProgress(_stage, _start + Math.Clamp(fraction, 0, 1) * _weight, detail));
    }
}

/// <summary>IProgress that calls back synchronously (CmlLib reports from worker threads).</summary>
public sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}

/// <summary>
/// Vanilla and Fabric print the game log as log4j XML events (meant for launchers). Turns each event into the
/// usual "[time] [thread/LEVEL] [logger]: message" line; other lines pass through unchanged.
/// </summary>
public sealed partial class Log4jXmlReader(Action<string> log)
{
    private readonly object _gate = new();
    private System.Text.StringBuilder? _event;

    [System.Text.RegularExpressions.GeneratedRegex("(\\w+)=\"([^\"]*)\"")]
    private static partial System.Text.RegularExpressions.Regex Attributes();

    [System.Text.RegularExpressions.GeneratedRegex(@"<!\[CDATA\[(.*?)\]\]>", System.Text.RegularExpressions.RegexOptions.Singleline)]
    private static partial System.Text.RegularExpressions.Regex Cdata();

    public void Line(string line)
    {
        lock (_gate)
        {
            var trimmed = line.TrimStart();
            if (_event is null && !trimmed.StartsWith("<log4j:Event", StringComparison.Ordinal))
            {
                log(line);
                return;
            }
            _event ??= new System.Text.StringBuilder();
            _event.AppendLine(line);
            if (!trimmed.StartsWith("</log4j:Event>", StringComparison.Ordinal))
                return;
            var text = _event.ToString();
            _event = null;
            foreach (var formatted in Format(text))
                log(formatted);
        }
    }

    public static IEnumerable<string> Format(string xmlEvent)
    {
        var header = xmlEvent[..Math.Max(0, xmlEvent.IndexOf('>'))];
        var attrs = Attributes().Matches(header).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var time = attrs.TryGetValue("timestamp", out var ts) && long.TryParse(ts, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("HH:mm:ss")
            : "";
        var prefix = $"[{time}] [{attrs.GetValueOrDefault("thread")}/{attrs.GetValueOrDefault("level")}] [{attrs.GetValueOrDefault("logger")}]: ";
        var blocks = Cdata().Matches(xmlEvent).Select(m => m.Groups[1].Value).ToList();
        if (blocks.Count == 0)
            yield break;
        var lines = blocks[0].Replace("\r", "").Split('\n');
        yield return prefix + lines[0];
        foreach (var rest in lines.Skip(1).Concat(blocks.Skip(1).SelectMany(b => b.Replace("\r", "").Split('\n'))))
            if (rest.Length > 0)
                yield return rest;
    }
}
