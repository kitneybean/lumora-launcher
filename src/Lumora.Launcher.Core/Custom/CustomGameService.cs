using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;
using CmlLib.Core.ProcessBuilder;
using Lumora.Launcher.Core.Accounts;
using Lumora.Launcher.Core.Game;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.Core.Custom;

/// <summary>Installs and starts the player's own builds (Vanilla, NeoForge, Fabric).</summary>
public sealed class CustomGameService(
    CustomInstanceStore instances,
    SettingsStore store,
    Func<LauncherSettings> settings,
    HttpClient http,
    Func<CancellationToken, Task<IReadOnlyList<string>>> lumoraJvmArguments)
{
    private LauncherSettings Settings => settings();

    public GameSettings EffectiveSettings(CustomInstance instance)
    {
        var effective = Settings.Game.Over(GameSettings.Defaults);
        return instance.OverrideGlobal ? instance.Game.Over(effective) : effective;
    }

    // ---------------- install ----------------

    public async Task InstallAsync(CustomInstance instance, IProgress<InstallProgress> progress, CancellationToken ct = default)
    {
        var reporter = new StageReporter(progress);
        var launcher = CreateLauncher(instance);
        var game = EffectiveSettings(instance);

        reporter.Begin($"Minecraft {instance.MinecraftVersion}", 0.0, 0.6);
        await launcher.InstallAsync(instance.MinecraftVersion, reporter.Files, reporter.Bytes, ct);

        string versionName;
        switch (instance.Loader)
        {
            case GameLoader.Vanilla:
                versionName = instance.MinecraftVersion;
                break;
            case GameLoader.NeoForge:
                reporter.Begin($"NeoForge {instance.LoaderVersion}", 0.6, 0.4);
                versionName = await new NeoForgeInstaller(launcher).Install(instance.MinecraftVersion, instance.LoaderVersion!, new NeoForgeInstallOptions
                {
                    FileProgress = reporter.Files,
                    ByteProgress = reporter.Bytes,
                    CancellationToken = ct,
                    SkipIfAlreadyInstalled = true,
                    JavaPath = game.JavaPath,
                });
                await launcher.InstallAsync(versionName, reporter.Files, reporter.Bytes, ct);
                break;
            case GameLoader.Fabric:
                reporter.Begin($"Fabric {instance.LoaderVersion}", 0.6, 0.4);
                versionName = await FabricProfile.InstallAsync(http, launcher.MinecraftPath.Versions, instance.MinecraftVersion, instance.LoaderVersion!, ct);
                await launcher.InstallAsync(versionName, reporter.Files, reporter.Bytes, ct);
                break;
            default:
                throw new NotSupportedException($"Загрузчик {instance.Loader} не поддерживается.");
        }

        instance.InstalledVersionName = versionName;
        instance.InstalledFor = instance.InstallKey;
        instances.Save(instance);
        reporter.Done();
    }

    // ---------------- launch ----------------

    public async Task<int> LaunchAsync(CustomInstance instance, LaunchIdentity identity, Action<string> log, CancellationToken ct = default)
    {
        if (instance.NeedsInstall)
            throw new InvalidOperationException("Сборка ещё не установлена.");
        var game = EffectiveSettings(instance);
        var launcher = CreateLauncher(instance);

        var jvm = new List<MArgument>();
        foreach (var arg in game.ExtraJvmArguments ?? [])
            jvm.Add(new MArgument(arg));
        if (identity.UseLumoraAuth)
            foreach (var arg in await lumoraJvmArguments(ct))
                jvm.Add(new MArgument(arg));

        var option = new MLaunchOption
        {
            Session = new MSession(identity.PlayerName, identity.AccessToken, identity.Uuid)
            {
                UserType = identity.UserType,
                ClientToken = identity.ClientToken,
                Xuid = identity.Xuid,
            },
            MaximumRamMb = game.MaxMemoryMb ?? 4096,
            MinimumRamMb = game.MinMemoryMb ?? 1024,
            ScreenWidth = game.WindowWidth ?? 1280,
            ScreenHeight = game.WindowHeight ?? 720,
            FullScreen = game.Fullscreen ?? false,
            GameLauncherName = "Lumora",
            GameLauncherVersion = typeof(CustomGameService).Assembly.GetName().Version?.ToString(3) ?? "0",
            ExtraJvmArguments = jvm,
        };
        if (!string.IsNullOrWhiteSpace(game.JavaPath))
            option.JavaPath = game.JavaPath;

        var process = await launcher.BuildProcessAsync(instance.InstalledVersionName!, option, ct);
        instance.LastPlayedAt = DateTimeOffset.UtcNow;
        instances.Save(instance);
        log($"[Lumora] {instance.Name} · {instance.MinecraftVersion} {instance.LoaderText} · {identity.PlayerName}");
        return await GameRuntime.RunAsync(process, instances.GameDir(instance.Id), instance.Name, game, log, ct);
    }

    private MinecraftLauncher CreateLauncher(CustomInstance instance) =>
        GameRuntime.CreateLauncher(store.Paths, Settings, instances.GameDir(instance.Id));
}

/// <summary>
/// Fabric is installed by saving its launcher profile (a version JSON that inherits the vanilla version)
/// into the versions folder; CmlLib then downloads its libraries like for any other version.
/// </summary>
public static class FabricProfile
{
    public static async Task<string> InstallAsync(HttpClient http, string versionsDir, string minecraftVersion, string loaderVersion, CancellationToken ct)
    {
        var url = $"{VersionCatalog.FabricMeta}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json";
        var json = await http.GetStringAsync(url, ct);
        var id = VersionId(json);
        var dir = Path.Combine(versionsDir, id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".json"), json, ct);
        return id;
    }

    /// <summary>"id" of the profile, checked to be a plain folder name.</summary>
    public static string VersionId(string profileJson)
    {
        using var doc = JsonDocument.Parse(profileJson);
        var id = doc.RootElement.GetProperty("id").GetString() ?? "";
        if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
            throw new InvalidDataException("Fabric вернул некорректный профиль версии.");
        return id;
    }
}
