using System.Text.Json;
using System.Text.Json.Serialization;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.Core.Custom;

[JsonConverter(typeof(JsonStringEnumConverter<GameLoader>))]
public enum GameLoader
{
    Vanilla,
    NeoForge,
    Fabric,
}

[JsonConverter(typeof(JsonStringEnumConverter<ContentKind>))]
public enum ContentKind
{
    Mod,
    ResourcePack,
    Shader,
}

[JsonConverter(typeof(JsonStringEnumConverter<ContentSource>))]
public enum ContentSource
{
    Local,
    Modrinth,
    CurseForge,
}

/// <summary>A mod, resource pack or shader pack installed into a player's own build.</summary>
public sealed class ContentEntry
{
    public ContentKind Kind { get; set; }
    public ContentSource Source { get; set; }
    /// <summary>Project id in the catalog (Modrinth project id or CurseForge mod id).</summary>
    public string? ProjectId { get; set; }
    /// <summary>Version id (Modrinth) or file id (CurseForge).</summary>
    public string? VersionId { get; set; }
    public string? Title { get; set; }
    public string? VersionNumber { get; set; }
    public string? IconUrl { get; set; }
    /// <summary>File name inside the content folder (without ".disabled").</summary>
    public required string FileName { get; set; }
    public string? Sha1 { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>Installed only because another entry requires it.</summary>
    public bool IsDependency { get; set; }

    public static string FolderOf(ContentKind kind) => kind switch
    {
        ContentKind.Mod => "mods",
        ContentKind.ResourcePack => "resourcepacks",
        ContentKind.Shader => "shaderpacks",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>A build the player assembled: Minecraft version, loader, content and own game settings.</summary>
public sealed class CustomInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    /// <summary>PNG file name inside the instance folder, or null for the default icon.</summary>
    public string? Icon { get; set; }
    public required string MinecraftVersion { get; set; }
    public GameLoader Loader { get; set; }
    public string? LoaderVersion { get; set; }
    /// <summary>CmlLib version name after installation (e.g. "fabric-loader-0.16.9-1.21.1").</summary>
    public string? InstalledVersionName { get; set; }
    /// <summary>Loader and Minecraft version that <see cref="InstalledVersionName"/> belongs to.</summary>
    public string? InstalledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastPlayedAt { get; set; }
    /// <summary>Where it came from: "modrinth:&lt;version id&gt;", "curseforge:&lt;file id&gt;", "import" or null.</summary>
    public string? Origin { get; set; }
    public bool OverrideGlobal { get; set; }
    public GameSettings Game { get; set; } = new();
    public List<ContentEntry> Content { get; set; } = [];

    [JsonIgnore]
    public string LoaderText => Loader switch
    {
        GameLoader.Vanilla => "Vanilla",
        _ => $"{Loader} {LoaderVersion}",
    };

    /// <summary>Key of what must be installed; when it changes the version is installed again.</summary>
    [JsonIgnore]
    public string InstallKey => $"{MinecraftVersion}|{Loader}|{LoaderVersion}";

    [JsonIgnore]
    public bool NeedsInstall => InstalledVersionName is null || InstalledFor != InstallKey;
}

/// <summary>Player builds in &lt;data&gt;/custom/&lt;id&gt;: instance.json plus the game folder "minecraft".</summary>
public sealed class CustomInstanceStore(LauncherPaths paths, Func<LauncherSettings> settings)
{
    public const string FileName = "instance.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string Root => Path.Combine(paths.DataDirectory(settings()), "custom");

    public string Folder(Guid id) => Path.Combine(Root, id.ToString("N"));

    /// <summary>The game directory (.minecraft equivalent) of a build.</summary>
    public string GameDir(Guid id) => Path.Combine(Folder(id), "minecraft");

    public IReadOnlyList<CustomInstance> List()
    {
        if (!Directory.Exists(Root))
            return [];
        var result = new List<CustomInstance>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            if (Load(Path.Combine(dir, FileName)) is { } instance)
                result.Add(instance);
        }
        return result.OrderByDescending(i => i.LastPlayedAt ?? i.CreatedAt).ToList();
    }

    public CustomInstance? Get(Guid id) => Load(Path.Combine(Folder(id), FileName));

    public void Save(CustomInstance instance)
    {
        var folder = Folder(instance.Id);
        Directory.CreateDirectory(GameDir(instance.Id));
        var file = Path.Combine(folder, FileName);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(instance, Json));
        File.Move(tmp, file, overwrite: true);
    }

    public CustomInstance Create(string name, string minecraftVersion, GameLoader loader, string? loaderVersion)
    {
        var instance = new CustomInstance
        {
            Name = CleanName(name),
            MinecraftVersion = minecraftVersion,
            Loader = loader,
            LoaderVersion = loader == GameLoader.Vanilla ? null : loaderVersion,
        };
        Save(instance);
        return instance;
    }

    public void Delete(Guid id)
    {
        var folder = Folder(id);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>Copies a build with all its worlds and content under a new id.</summary>
    public CustomInstance Duplicate(Guid id)
    {
        var source = Get(id) ?? throw new InvalidOperationException("Сборка не найдена.");
        var copy = JsonSerializer.Deserialize<CustomInstance>(JsonSerializer.Serialize(source, Json), Json)!;
        copy.Id = Guid.NewGuid();
        copy.Name = CleanName(source.Name + " (копия)");
        copy.CreatedAt = DateTimeOffset.UtcNow;
        copy.LastPlayedAt = null;
        CopyDirectory(Folder(id), Folder(copy.Id));
        Save(copy);
        return copy;
    }

    public static string CleanName(string name)
    {
        var clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0)
            clean = "Моя сборка";
        return clean.Length > 64 ? clean[..64] : clean;
    }

    private static CustomInstance? Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<CustomInstance>(File.ReadAllText(file), Json) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
