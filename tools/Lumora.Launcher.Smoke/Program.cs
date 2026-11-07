// End-to-end check of the launcher core without the UI:
// login -> server list -> install build -> launch the game -> watch the log -> stop.
//
//   dotnet run --project tools/Lumora.Launcher.Smoke -- --server http://localhost:5080 --login <nick> --password <pass>
//       [--data <dir>] [--seconds 120] [--keep-running]
//
// The game window really opens. By default it is closed after the client reaches the main menu
// (or after --seconds).
using Lumora.Launcher.Core.Api;
using Lumora.Launcher.Core.Game;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;

string Arg(string name, string? fallback = null) =>
    args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback ?? throw new ArgumentException($"Missing {name}");

var server = Arg("--server");
var dataDir = Arg("--data", Path.Combine(Path.GetTempPath(), "lumora-smoke"));
var seconds = int.Parse(Arg("--seconds", "180"));
var keepRunning = args.Contains("--keep-running");

var store = new SettingsStore(new LauncherPaths(dataDir));
var settings = store.LoadGlobal();
settings.AuthServerUrl = server;
store.SaveGlobal(settings);

using var api = new LumoraApiClient(() => settings.AuthServerUrl);
var game = new GameService(api, store, () => settings);

Console.WriteLine($"[smoke] data: {dataDir}");
var session = await api.LoginAsync(Arg("--login"), Arg("--password"));
Console.WriteLine($"[smoke] logged in as {session.PlayerName} ({session.ProfileId:N}), skin: {session.SkinUrl ?? "default"} slim={session.SlimModel}");

var servers = await api.GetServersAsync(session);
Console.WriteLine($"[smoke] servers: {string.Join(", ", servers.Select(s => $"{s.Name} [{s.Build.MinecraftVersion} {s.Build.Loader} {s.Build.LoaderVersion} r{s.Build.Revision}]"))}");
var target = servers.First();

Console.WriteLine($"[smoke] state before: {game.GetState(target.Build)}");
var lastStage = "";
var lastPercent = -1;
await game.InstallAsync(session, target.Build, new SyncProgress<InstallProgress>(p =>
{
    var percent = (int)((p.Fraction ?? 0) * 100);
    if (p.Stage != lastStage || percent >= lastPercent + 5)
    {
        Console.WriteLine($"[install] {percent,3}% {p.Stage} {p.Detail}");
        lastStage = p.Stage;
        lastPercent = percent;
    }
}));
Console.WriteLine($"[smoke] state after: {game.GetState(target.Build)}");

var instance = store.LoadInstance(settings, target.Build.Id);
var effective = SettingsResolver.Resolve(settings, instance);
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
var sawUser = false;
var sawAuthlib = false;
var reachedMenu = false;
try
{
    var code = await game.LaunchAsync(session, target, effective, line =>
    {
        Console.WriteLine("[game] " + line);
        if (line.Contains("authlib-injector", StringComparison.OrdinalIgnoreCase) || line.Contains("authlibinjector", StringComparison.OrdinalIgnoreCase))
            sawAuthlib = true;
        if (line.Contains($"Setting user: {session.PlayerName}"))
            sawUser = true;
        // Sound engine start is one of the last steps before the title screen.
        if (line.Contains("Sound engine started") || line.Contains("Created: ") && line.Contains("atlas"))
        {
            reachedMenu = true;
            if (!keepRunning)
                cts.CancelAfter(TimeSpan.FromSeconds(8));
        }
    }, cts.Token);
    Console.WriteLine($"[smoke] game exited with {code}");
}
catch (OperationCanceledException)
{
    Console.WriteLine("[smoke] game stopped by the smoke test");
}

Console.WriteLine($"[smoke] authlib-injector seen: {sawAuthlib}, user set: {sawUser}, reached menu: {reachedMenu}");
var serversDat = Path.Combine(store.Paths.InstanceDirectory(settings, target.Build.Id), "servers.dat");
var entries = Lumora.Launcher.Core.GameServers.ServersDatFile.Read(serversDat);
Console.WriteLine($"[smoke] servers.dat: {string.Join(", ", entries.Select(e => $"{e.Name} = {e.Address}"))}");
return sawAuthlib && sawUser && reachedMenu ? 0 : 1;

internal sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
