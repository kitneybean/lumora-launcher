namespace Lumora.Launcher.Core.Settings;

/// <summary>Well-known launcher folders.</summary>
public sealed class LauncherPaths
{
    public LauncherPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    /// <summary>Default per-OS location: %APPDATA%\Lumora, ~/.local/share/Lumora, ~/Library/Application Support/Lumora.</summary>
    public static string DefaultRoot
    {
        get
        {
            var baseDir = OperatingSystem.IsWindows()
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(baseDir, "Lumora");
        }
    }

    /// <summary>Folder that holds settings.json (never moved, even if game data is moved).</summary>
    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "settings.json");

    public string LogsDirectory => Path.Combine(Root, "logs");

    /// <summary>Folder for instances, runtimes and caches; may be overridden in settings.</summary>
    public string DataDirectory(LauncherSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DataDirectory) ? Root : Path.GetFullPath(settings.DataDirectory);

    public string InstancesDirectory(LauncherSettings settings) => Path.Combine(DataDirectory(settings), "instances");

    public string InstanceDirectory(LauncherSettings settings, Guid buildId) =>
        Path.Combine(InstancesDirectory(settings), buildId.ToString("N"));

    public string CacheDirectory(LauncherSettings settings) => Path.Combine(DataDirectory(settings), "cache");

    public string RuntimesDirectory(LauncherSettings settings) => Path.Combine(DataDirectory(settings), "runtimes");
}
