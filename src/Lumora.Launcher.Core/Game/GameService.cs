using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using Lumora.Contracts;
using Lumora.Launcher.Core.Api;
using Lumora.Launcher.Core.GameServers;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;
using Lumora.Shared.Sync;

namespace Lumora.Launcher.Core.Game;

/// <summary>
/// Installs builds with CmlLib (vanilla + NeoForge + Mojang Java runtime), syncs build files,
/// writes servers.dat and starts the game with authlib-injector.
/// Each build lives in instances/&lt;id&gt; (game folder); libraries, versions, assets and Java are shared.
/// </summary>
public sealed class GameService(LumoraApiClient api, SettingsStore store, Func<LauncherSettings> settings) : IGameService
{
    private const string MetaFolder = ".lumora";

    private LauncherPaths Paths => store.Paths;
    private LauncherSettings Settings => settings();

    // ------------------------------------------------------------------ state

    public InstanceState GetState(LauncherBuildSummaryDto build)
    {
        var instance = store.LoadInstance(Settings, build.Id);
        if (instance.InstalledRevision <= 0 || instance.InstalledVersionName is null)
            return InstanceState.NotInstalled;
        return instance.InstalledRevision < build.Revision ? InstanceState.UpdateAvailable : InstanceState.Ready;
    }

    public void MarkForReinstall(Guid buildId)
    {
        var instance = store.LoadInstance(Settings, buildId);
        instance.InstalledRevision = 0;
        store.SaveInstance(Settings, buildId, instance);
        // Forget cached hashes so every file is checked again.
        var index = Path.Combine(InstanceDir(buildId), MetaFolder, "files.json");
        if (File.Exists(index))
            File.Delete(index);
    }

    // ------------------------------------------------------------------ install

    public async Task InstallAsync(AccountSession session, LauncherBuildSummaryDto build, IProgress<InstallProgress> progress, CancellationToken ct = default)
    {
        var manifest = await api.GetManifestAsync(session, build.Id, ct);
        var instance = store.LoadInstance(Settings, build.Id);
        var effective = SettingsResolver.Resolve(Settings, instance);
        var reporter = new StageReporter(progress);

        // 1. Minecraft + its Java runtime.
        var launcher = CreateLauncher(build.Id);
        reporter.Begin($"Minecraft {manifest.MinecraftVersion}", 0.0, 0.45);
        await launcher.InstallAsync(manifest.MinecraftVersion, reporter.Files, reporter.Bytes, ct);

        // 2. Loader.
        string versionName;
        switch (manifest.Loader)
        {
            case LoaderType.Vanilla:
                versionName = manifest.MinecraftVersion;
                break;
            case LoaderType.NeoForge:
                reporter.Begin($"NeoForge {manifest.LoaderVersion}", 0.45, 0.25);
                var installer = new NeoForgeInstaller(launcher);
                versionName = await installer.Install(manifest.MinecraftVersion, manifest.LoaderVersion!, new NeoForgeInstallOptions
                {
                    FileProgress = reporter.Files,
                    ByteProgress = reporter.Bytes,
                    CancellationToken = ct,
                    SkipIfAlreadyInstalled = true,
                    JavaPath = effective.JavaPath,
                });
                await launcher.InstallAsync(versionName, reporter.Files, reporter.Bytes, ct);
                break;
            default:
                throw new NotSupportedException($"Лоадер {manifest.Loader} пока не поддерживается лаунчером.");
        }

        // 3. Build files.
        reporter.Begin("Файлы сборки", 0.70, 0.25);
        await Task.Run(() => SyncFilesAsync(session, manifest, instance, reporter, null, ct), ct);

        // 4. authlib-injector and the server list.
        reporter.Begin("authlib-injector", 0.95, 0.05);
        await EnsureAuthlibInjectorAsync(ct);
        WriteServerList(build.Id, manifest, instance);

        instance.InstalledRevision = manifest.Revision;
        instance.InstalledVersionName = versionName;
        store.SaveInstance(Settings, build.Id, instance);
        SaveManifest(build.Id, manifest);
        progress.Report(new InstallProgress("Готово", 1));
    }

    // ------------------------------------------------------------------ launch

    public async Task<int> LaunchAsync(AccountSession session, LauncherServerDto server, GameSettings game, Action<string> log, CancellationToken ct = default)
    {
        var buildId = server.Build.Id;
        var instance = store.LoadInstance(Settings, buildId);
        if (instance.InstalledVersionName is null)
            throw new InvalidOperationException("Сборка не установлена.");

        BuildManifestDto manifest;
        try
        {
            manifest = await api.GetManifestAsync(session, buildId, ct);
            if (manifest.Revision != instance.InstalledRevision)
                throw new InvalidOperationException("Вышло обновление сборки — нажмите «Обновить».");
        }
        catch (HttpRequestException) when (LoadManifest(buildId) is { } cached)
        {
            log("[Lumora] Сервер недоступен — запускаю сохранённую версию сборки.");
            manifest = cached;
        }

        log("[Lumora] Проверка файлов сборки…");
        await Task.Run(() => SyncFilesAsync(session, manifest, instance, null, log, ct), ct);
        WriteServerList(buildId, manifest, instance);
        store.SaveInstance(Settings, buildId, instance);
        var authlib = await EnsureAuthlibInjectorAsync(ct);
        var metadata = await api.GetYggdrasilMetadataAsync(ct);

        var launcher = CreateLauncher(buildId);
        var jvm = new List<MArgument>();
        foreach (var arg in manifest.JvmArguments.Concat(game.ExtraJvmArguments ?? []))
            jvm.Add(new MArgument(arg));
        jvm.Add(new MArgument($"-javaagent:{authlib}={LoopbackToIPv4(api.YggdrasilUrl)}"));
        jvm.Add(new MArgument("-Dauthlibinjector.yggdrasil.prefetched=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata))));

        var gameArgs = new List<MArgument>();
        if (game.AutoConnect == true || server.AutoConnect)
            // Same form as in servers.dat ("host" for the default port), so the game does not add a duplicate entry.
            gameArgs.Add(new MArgument(["--quickPlayMultiplayer", server.Port == 25565 ? server.Address : $"{server.Address}:{server.Port}"]));

        var option = new MLaunchOption
        {
            Session = new MSession(session.PlayerName, session.AccessToken, session.ProfileId.ToString("N"))
            {
                UserType = "mojang",
                ClientToken = session.ClientToken,
            },
            MaximumRamMb = game.MaxMemoryMb ?? 4096,
            MinimumRamMb = game.MinMemoryMb ?? 1024,
            ScreenWidth = game.WindowWidth ?? 1280,
            ScreenHeight = game.WindowHeight ?? 720,
            FullScreen = game.Fullscreen ?? false,
            GameLauncherName = "Lumora",
            GameLauncherVersion = typeof(GameService).Assembly.GetName().Version?.ToString(3) ?? "0",
            ExtraJvmArguments = jvm,
            ExtraGameArguments = gameArgs,
        };
        if (!string.IsNullOrWhiteSpace(game.JavaPath))
            option.JavaPath = game.JavaPath;

        var process = await launcher.BuildProcessAsync(instance.InstalledVersionName, option, ct);
        var start = process.StartInfo;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        start.WorkingDirectory = InstanceDir(buildId);
        foreach (var (key, value) in game.EnvironmentVariables ?? [])
            start.Environment[key] = value;
        start.Environment["INST_DIR"] = InstanceDir(buildId);
        start.Environment["INST_NAME"] = manifest.Name;
        start.Environment["INST_JAVA"] = start.FileName;
        ApplyWrapper(start, game.WrapperCommand);

        if (!string.IsNullOrWhiteSpace(game.PreLaunchCommand))
        {
            log($"[Lumora] Команда перед запуском: {game.PreLaunchCommand}");
            var code = await RunShellAsync(game.PreLaunchCommand, start, log, ct);
            if (code != 0)
                throw new InvalidOperationException($"Команда перед запуском завершилась с кодом {code}.");
        }

        log($"[Lumora] {manifest.Name} r{manifest.Revision} · {instance.InstalledVersionName} · {session.PlayerName}");
        log($"[Lumora] Java: {start.FileName}");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
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

    // ------------------------------------------------------------------ files

    private async Task SyncFilesAsync(AccountSession session, BuildManifestDto manifest, InstanceSettings instance,
        StageReporter? reporter, Action<string>? log, CancellationToken ct)
    {
        var root = InstanceDir(manifest.Id);
        Directory.CreateDirectory(root);
        var index = new FileHashIndex(root, Path.Combine(root, MetaFolder, "files.json"));

        var strictContents = new List<string>();
        foreach (var rule in manifest.SyncRules.Where(r => r.Policy == SyncPolicy.Strict))
        {
            var dir = Path.Combine(root, rule.Path);
            if (Directory.Exists(dir))
                strictContents.AddRange(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/')));
            else if (File.Exists(dir))
                strictContents.Add(rule.Path);
        }

        var plan = await Task.Run(() => FileSyncPlanner.Plan(manifest.Files, manifest.SyncRules, instance.OptionalFiles, index.Sha1Of, strictContents), ct);

        foreach (var path in plan.Deletions)
        {
            File.Delete(Path.Combine(root, path));
            index.Forget(path);
            log?.Invoke($"[Lumora] Удалён {path}");
        }

        if (plan.Downloads.Count > 0)
        {
            var total = plan.Downloads.Sum(f => f.Size);
            long done = 0;
            reporter?.SetBytesTotal(total);
            var cacheDir = Paths.FileCacheDirectory(Settings);
            using var gate = new SemaphoreSlim(Math.Clamp(Settings.DownloadThreads, 1, 32));
            var bytes = new SyncProgress<long>(n =>
            {
                var d = Interlocked.Add(ref done, n);
                reporter?.ReportBytes(d, total);
            });

            await Task.WhenAll(plan.Downloads.Select(async file =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var cached = Path.Combine(cacheDir, file.Sha1[..2], file.Sha1);
                    if (!File.Exists(cached) || FileHashIndex.FileSha1(cached) != file.Sha1)
                    {
                        await api.DownloadAsync(file.Url, cached, session, bytes, ct);
                        if (FileHashIndex.FileSha1(cached) != file.Sha1)
                        {
                            File.Delete(cached);
                            throw new InvalidDataException($"Файл {file.Path} повреждён при загрузке. Попробуйте ещё раз.");
                        }
                    }
                    else
                    {
                        ((IProgress<long>)bytes).Report(file.Size);
                    }
                    var target = Path.Combine(root, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(cached, target, overwrite: true);
                    log?.Invoke($"[Lumora] Обновлён {file.Path}");
                }
                finally
                {
                    gate.Release();
                }
            }));
            foreach (var file in plan.Downloads)
                index.Forget(file.Path);
        }
        index.Save();
    }

    private void WriteServerList(Guid buildId, BuildManifestDto manifest, InstanceSettings instance)
    {
        var entries = manifest.Servers.Select(s => new ServerListEntry(s.Name, s.Address, s.IconBase64)).ToList();
        ServersDatFile.Update(Path.Combine(InstanceDir(buildId), ServersDatFile.FileName), entries, instance.ManagedServerAddresses);
        instance.ManagedServerAddresses = entries.Select(e => e.Address).ToList();
    }

    /// <summary>Downloads authlib-injector from the auth server once per version; returns the jar path.</summary>
    private async Task<string> EnsureAuthlibInjectorAsync(CancellationToken ct)
    {
        var dir = Path.Combine(Paths.SharedDirectory(Settings), "authlib-injector");
        AuthlibInjectorDto info;
        try
        {
            info = await api.GetAuthlibInjectorAsync(ct);
        }
        catch (HttpRequestException) when (Directory.Exists(dir) && Directory.GetFiles(dir, "*.jar").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() is { } existing)
        {
            return existing;
        }

        var jar = Path.Combine(dir, $"authlib-injector-{info.Version}.jar");
        if (File.Exists(jar) && Sha256(jar) == info.Sha256.ToLowerInvariant())
            return jar;
        await api.DownloadAsync(info.Url, jar, null, null, ct);
        if (Sha256(jar) != info.Sha256.ToLowerInvariant())
        {
            File.Delete(jar);
            throw new InvalidDataException("authlib-injector скачан с ошибкой (не совпала контрольная сумма).");
        }
        return jar;
    }

    // ------------------------------------------------------------------ helpers

    private string InstanceDir(Guid buildId) => Paths.InstanceDirectory(Settings, buildId);

    private MinecraftLauncher CreateLauncher(Guid buildId)
    {
        var shared = Paths.SharedDirectory(Settings);
        var path = new MinecraftPath(InstanceDir(buildId))
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

    private void SaveManifest(Guid buildId, BuildManifestDto manifest)
    {
        var file = Path.Combine(InstanceDir(buildId), MetaFolder, "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(manifest, LumoraJson.Options));
    }

    private BuildManifestDto? LoadManifest(Guid buildId)
    {
        var file = Path.Combine(InstanceDir(buildId), MetaFolder, "manifest.json");
        return File.Exists(file) ? JsonSerializer.Deserialize<BuildManifestDto>(File.ReadAllText(file), LumoraJson.Options) : null;
    }

    /// <summary>
    /// NeoForge starts Java with -Djava.net.preferIPv6Addresses=system, so "localhost" may resolve to ::1
    /// where a local dev server is not listening. Use the IPv4 loopback explicitly.
    /// </summary>
    public static string LoopbackToIPv4(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? new UriBuilder(uri) { Host = "127.0.0.1" }.Uri.ToString().TrimEnd('/')
            : url;

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
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

    /// <summary>Maps CmlLib file/byte events to one overall progress bar with stage weights, throttled.</summary>
    private sealed class StageReporter(IProgress<InstallProgress> target)
    {
        private string _stage = "";
        private double _start, _weight;
        private long _lastReport;
        private long _bytesTotal;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _stageStartMs;

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

        private double _stageFraction;

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
    private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        public void Report(T value) => action(value);
    }
}
