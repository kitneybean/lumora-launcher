using Lumora.Launcher.Core.Accounts;
using Lumora.Launcher.Core.Custom;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;

/// <summary>Smoke test of the player's own builds: version lists, install, launch offline to the main menu.</summary>
internal static class CustomSmoke
{
    public static async Task<int> RunAsync(string spec, string nick, string mods, string dataDir, int seconds, bool keepRunning)
    {
        var parts = spec.Split(':');
        var loader = Enum.Parse<GameLoader>(parts[0], ignoreCase: true);
        var mc = parts.Length > 1 ? parts[1] : "1.21.1";

        var store = new SettingsStore(new LauncherPaths(dataDir));
        var settings = store.LoadGlobal();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Lumora-Launcher-Smoke/1.0");
        var catalog = new VersionCatalog(http);
        var instances = new CustomInstanceStore(store.Paths, () => settings);
        var service = new CustomGameService(instances, store, () => settings, http, _ => Task.FromResult<IReadOnlyList<string>>([]));

        var loaderVersion = parts.Length > 2 ? parts[2] : VersionCatalog.Recommended(await catalog.LoaderVersionsAsync(loader, mc))?.Version;
        Console.WriteLine($"[custom] {loader} {loaderVersion} for Minecraft {mc}");
        var instance = instances.List().FirstOrDefault(i => i.Name == $"smoke-{spec}")
            ?? instances.Create($"smoke-{spec}", mc, loader, loaderVersion);

        var lastStage = "";
        var lastPercent = -1;
        if (instance.NeedsInstall)
        {
            await service.InstallAsync(instance, new Progress(p =>
            {
                var percent = (int)((p.Fraction ?? 0) * 100);
                if (p.Stage != lastStage || percent >= lastPercent + 10)
                {
                    Console.WriteLine($"[install] {percent,3}% {p.Stage} {p.Detail}");
                    lastStage = p.Stage;
                    lastPercent = percent;
                }
            }));
        }
        Console.WriteLine($"[custom] installed as {instance.InstalledVersionName}");

        if (!string.IsNullOrWhiteSpace(mods))
            await ContentSmoke.AddModsAsync(http, instances, instance, mods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var reachedMenu = false;
        var loadedMods = 0;
        try
        {
            var code = await service.LaunchAsync(instance, LaunchIdentity.Offline(nick), line =>
            {
                Console.WriteLine("[game] " + line);
                if (line.Contains("Loading") && line.Contains(" mods"))
                    loadedMods++;
                if (line.Contains("Sound engine started") || line.Contains("Created: ") && line.Contains("atlas"))
                {
                    reachedMenu = true;
                    if (!keepRunning)
                        cts.CancelAfter(TimeSpan.FromSeconds(8));
                }
            }, cts.Token);
            Console.WriteLine($"[custom] game exited with {code}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[custom] game stopped by the smoke test");
        }
        Console.WriteLine($"[custom] reached menu: {reachedMenu}");
        return reachedMenu ? 0 : 1;
    }

    private sealed class Progress(Action<InstallProgress> action) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => action(value);
    }
}

/// <summary>Filled in once the content catalog exists.</summary>
internal static class ContentSmoke
{
    public static Task AddModsAsync(HttpClient http, CustomInstanceStore store, CustomInstance instance, string[] slugs) =>
        throw new NotSupportedException("--mods появится вместе с каталогом Modrinth.");
}
