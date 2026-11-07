namespace Lumora.Launcher.Core.Settings;

public enum LauncherVisibility
{
    /// <summary>Close the launcher when the game starts.</summary>
    Close,
    /// <summary>Minimize the launcher window.</summary>
    Minimize,
    /// <summary>Keep the launcher window open.</summary>
    KeepOpen,
}

/// <summary>
/// Game settings that can be set globally and overridden per build (like Prism Launcher).
/// Null means "inherit from the level above".
/// </summary>
public sealed class GameSettings
{
    /// <summary>Custom java executable; null = Mojang runtime for the build's Java version.</summary>
    public string? JavaPath { get; set; }

    public int? MinMemoryMb { get; set; }

    public int? MaxMemoryMb { get; set; }

    /// <summary>Extra JVM arguments appended after the build's mandatory ones.</summary>
    public List<string>? ExtraJvmArguments { get; set; }

    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    public int? WindowWidth { get; set; }

    public int? WindowHeight { get; set; }

    public bool? Fullscreen { get; set; }

    /// <summary>Join the server right after the game starts (--quickPlayMultiplayer).</summary>
    public bool? AutoConnect { get; set; }

    /// <summary>Command that wraps java, e.g. "gamemoderun" or "prime-run".</summary>
    public string? WrapperCommand { get; set; }

    public string? PreLaunchCommand { get; set; }

    public string? PostExitCommand { get; set; }

    /// <summary>Returns this (override) layered over <paramref name="baseline"/>.</summary>
    public GameSettings Over(GameSettings baseline) => new()
    {
        JavaPath = JavaPath ?? baseline.JavaPath,
        MinMemoryMb = MinMemoryMb ?? baseline.MinMemoryMb,
        MaxMemoryMb = MaxMemoryMb ?? baseline.MaxMemoryMb,
        ExtraJvmArguments = ExtraJvmArguments ?? baseline.ExtraJvmArguments,
        EnvironmentVariables = EnvironmentVariables ?? baseline.EnvironmentVariables,
        WindowWidth = WindowWidth ?? baseline.WindowWidth,
        WindowHeight = WindowHeight ?? baseline.WindowHeight,
        Fullscreen = Fullscreen ?? baseline.Fullscreen,
        AutoConnect = AutoConnect ?? baseline.AutoConnect,
        WrapperCommand = WrapperCommand ?? baseline.WrapperCommand,
        PreLaunchCommand = PreLaunchCommand ?? baseline.PreLaunchCommand,
        PostExitCommand = PostExitCommand ?? baseline.PostExitCommand,
    };

    /// <summary>Built-in defaults used when nothing is configured.</summary>
    public static GameSettings Defaults => new()
    {
        MinMemoryMb = 1024,
        MaxMemoryMb = 4096,
        ExtraJvmArguments = [],
        EnvironmentVariables = [],
        WindowWidth = 1280,
        WindowHeight = 720,
        Fullscreen = false,
        AutoConnect = false,
    };
}

/// <summary>Global launcher settings (settings.json).</summary>
public sealed class LauncherSettings
{
    /// <summary>Auth server base URL, e.g. https://auth.example.com.</summary>
    public string AuthServerUrl { get; set; } = "https://localhost:7443";

    /// <summary>Root folder for instances, runtimes and cache; null = default per-OS location.</summary>
    public string? DataDirectory { get; set; }

    public string Language { get; set; } = "ru";

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "dark";

    public LauncherVisibility OnGameStart { get; set; } = LauncherVisibility.Minimize;

    public bool ShowGameConsole { get; set; }

    public int DownloadThreads { get; set; } = 8;

    public string? ProxyUrl { get; set; }

    public GameSettings Game { get; set; } = new();
}

/// <summary>Per-build settings (instances/&lt;buildId&gt;/instance.json).</summary>
public sealed class InstanceSettings
{
    /// <summary>When false, <see cref="Game"/> is ignored and global settings are used.</summary>
    public bool OverrideGlobal { get; set; }

    public GameSettings Game { get; set; } = new();

    /// <summary>Player's choice for optional mods: build file path to enabled flag.</summary>
    public Dictionary<string, bool> OptionalFiles { get; set; } = [];

    /// <summary>Build revision that was last fully synced.</summary>
    public int InstalledRevision { get; set; }

    /// <summary>Addresses written to servers.dat last time (to remove servers the admin deleted).</summary>
    public List<string> ManagedServerAddresses { get; set; } = [];
}

public static class SettingsResolver
{
    /// <summary>Effective game settings: instance override, then global, then defaults.</summary>
    public static GameSettings Resolve(LauncherSettings global, InstanceSettings? instance)
    {
        var effective = global.Game.Over(GameSettings.Defaults);
        return instance is { OverrideGlobal: true } ? instance.Game.Over(effective) : effective;
    }
}
