using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumora.Launcher.Core.Settings;

/// <summary>Loads and saves launcher and per-build settings as JSON files.</summary>
public sealed class SettingsStore(LauncherPaths paths)
{
    public const string InstanceFileName = "instance.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public LauncherPaths Paths { get; } = paths;

    /// <summary>Reads settings.json; returns defaults if missing or unreadable.</summary>
    public LauncherSettings LoadGlobal() => Read<LauncherSettings>(Paths.SettingsFile) ?? new LauncherSettings();

    public void SaveGlobal(LauncherSettings settings) => Write(Paths.SettingsFile, settings);

    public InstanceSettings LoadInstance(LauncherSettings global, Guid buildId) =>
        Read<InstanceSettings>(InstanceFile(global, buildId)) ?? new InstanceSettings();

    public void SaveInstance(LauncherSettings global, Guid buildId, InstanceSettings settings) =>
        Write(InstanceFile(global, buildId), settings);

    private string InstanceFile(LauncherSettings global, Guid buildId) =>
        Path.Combine(Paths.InstanceDirectory(global, buildId), InstanceFileName);

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Json);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // A broken settings file must never prevent the launcher from starting.
            return null;
        }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, value, Json);
        File.Move(tmp, path, overwrite: true);
    }
}
