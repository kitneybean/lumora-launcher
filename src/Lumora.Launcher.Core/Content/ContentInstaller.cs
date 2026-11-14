using System.Security.Cryptography;
using Lumora.Launcher.Core.Custom;

namespace Lumora.Launcher.Core.Content;

/// <summary>Result of installing a project: what was added and what the player must do by hand.</summary>
public sealed record InstallResult(IReadOnlyList<ContentEntry> Added, IReadOnlyList<string> Notes);

public sealed record ContentUpdate(ContentEntry Entry, ContentVersion Version);

/// <summary>
/// Puts catalog files into a player's build: dependencies, enabling and disabling, removal, updates and
/// recognition of files the player dropped into the folders by hand.
/// </summary>
public sealed class ContentInstaller(HttpClient http, CustomInstanceStore store, IReadOnlyList<IContentProvider> providers)
{
    /// <summary>Hosts files may come from; anything else is refused.</summary>
    public static readonly IReadOnlySet<string> AllowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.modrinth.com", "github.com", "objects.githubusercontent.com", "raw.githubusercontent.com",
        "edge.forgecdn.net", "mediafilez.forgecdn.net",
    };

    public const string DisabledSuffix = ".disabled";

    public IContentProvider Provider(ContentSource source) =>
        providers.FirstOrDefault(p => p.Source == source) ?? throw new InvalidOperationException($"Каталог {source} недоступен.");

    // ---------------- install ----------------

    /// <summary>Installs the newest compatible version of a project (or the given one) with its required dependencies.</summary>
    public async Task<InstallResult> InstallAsync(CustomInstance instance, ContentSource source, string projectId, ContentKind kind,
        string? versionId = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var provider = Provider(source);
        var added = new List<ContentEntry>();
        var notes = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task AddAsync(string id, string? vid, ContentKind k, bool asDependency)
        {
            if (!visited.Add(id))
                return;
            var existing = instance.Content.FirstOrDefault(e => e.Source == source && e.ProjectId == id);
            if (existing is not null && (vid is null || existing.VersionId == vid))
                return;

            var version = vid is not null
                ? await provider.GetVersionAsync(id, vid, ct)
                : (await provider.GetVersionsAsync(id, k, instance.MinecraftVersion, instance.Loader, ct)).FirstOrDefault();
            if (version is null)
            {
                var project = await provider.GetProjectAsync(id, ct);
                notes.Add($"«{project.Title}» нет для Minecraft {instance.MinecraftVersion} ({instance.LoaderText}).");
                return;
            }
            var meta = await provider.GetProjectAsync(id, ct);
            progress?.Report($"{meta.Title} {version.VersionNumber}");
            if (!version.DownloadAllowed || version.PrimaryFile is null)
            {
                notes.Add($"Автор «{meta.Title}» запретил скачивание из лаунчера — скачайте файл на {meta.PageUrl} и положите в папку {ContentEntry.FolderOf(k)}.");
                return;
            }

            if (existing is not null)
                Delete(instance, existing);
            var entry = await DownloadAsync(instance, k, version, meta, asDependency, ct);
            instance.Content.RemoveAll(e => e.Source == source && e.ProjectId == id);
            instance.Content.Add(entry);
            added.Add(entry);

            foreach (var dep in version.Dependencies.Where(d => d.Required))
                await AddAsync(dep.ProjectId, dep.VersionId, ContentKind.Mod, asDependency: true);
        }

        await AddAsync(projectId, versionId, kind, asDependency: false);

        // Shader packs need Iris (with Sodium) on Fabric and NeoForge.
        if (kind == ContentKind.Shader && instance.Loader != GameLoader.Vanilla && source == ContentSource.Modrinth
            && !instance.Content.Any(e => e.Kind == ContentKind.Mod && e.Title?.Contains("Iris", StringComparison.OrdinalIgnoreCase) == true))
        {
            notes.Add("Для шейдеров поставлен Iris (вместе с Sodium).");
            await AddAsync("YL57xq9U", null, ContentKind.Mod, asDependency: true); // Iris Shaders on Modrinth
        }

        store.Save(instance);
        return new InstallResult(added, notes);
    }

    private async Task<ContentEntry> DownloadAsync(CustomInstance instance, ContentKind kind, ContentVersion version, ContentProject meta,
        bool asDependency, CancellationToken ct)
    {
        var file = version.PrimaryFile!;
        var name = SafeFileName(file.FileName);
        var folder = Path.Combine(store.GameDir(instance.Id), ContentEntry.FolderOf(kind));
        Directory.CreateDirectory(folder);
        await DownloadFileAsync(http, file.Url, Path.Combine(folder, name), file.Sha1, file.Sha512, ct);
        return new ContentEntry
        {
            Kind = kind,
            Source = version.Source,
            ProjectId = version.ProjectId,
            VersionId = version.VersionId,
            Title = meta.Title,
            VersionNumber = version.VersionNumber,
            IconUrl = meta.IconUrl,
            FileName = name,
            Sha1 = file.Sha1,
            IsDependency = asDependency,
        };
    }

    /// <summary>Downloads to a temp file, checks the host and the hash, then moves into place.</summary>
    public static async Task DownloadFileAsync(HttpClient http, string url, string target, string? sha1, string? sha512, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(uri.Host))
            throw new InvalidDataException($"Файл с адреса {url} не скачивается: этот сайт не входит в список доверенных.");
        var tmp = target + ".download";
        try
        {
            using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(tmp);
                await input.CopyToAsync(output, ct);
            }
            if (sha512 is not null && !HashEquals(tmp, SHA512.Create(), sha512) || sha512 is null && sha1 is not null && !HashEquals(tmp, SHA1.Create(), sha1))
                throw new InvalidDataException($"Файл {Path.GetFileName(target)} повреждён: контрольная сумма не совпала.");
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    // ---------------- manage ----------------

    public void SetEnabled(CustomInstance instance, ContentEntry entry, bool enabled)
    {
        var folder = Path.Combine(store.GameDir(instance.Id), ContentEntry.FolderOf(entry.Kind));
        var on = Path.Combine(folder, entry.FileName);
        var off = on + DisabledSuffix;
        if (enabled && File.Exists(off))
            File.Move(off, on, overwrite: true);
        else if (!enabled && File.Exists(on))
            File.Move(on, off, overwrite: true);
        entry.Enabled = enabled;
        store.Save(instance);
    }

    public void Remove(CustomInstance instance, ContentEntry entry)
    {
        Delete(instance, entry);
        instance.Content.Remove(entry);
        store.Save(instance);
    }

    private void Delete(CustomInstance instance, ContentEntry entry)
    {
        var path = Path.Combine(store.GameDir(instance.Id), ContentEntry.FolderOf(entry.Kind), entry.FileName);
        foreach (var p in new[] { path, path + DisabledSuffix })
            if (File.Exists(p))
                File.Delete(p);
    }

    /// <summary>Newest compatible versions for catalog content (Modrinth by hash; others by their own provider).</summary>
    public async Task<IReadOnlyList<ContentUpdate>> CheckUpdatesAsync(CustomInstance instance, CancellationToken ct = default)
    {
        var result = new List<ContentUpdate>();
        foreach (var group in instance.Content.Where(e => e.Sha1 is not null && e.Source != ContentSource.Local).GroupBy(e => (e.Source, e.Kind)))
        {
            var provider = providers.FirstOrDefault(p => p.Source == group.Key.Source && p.IsAvailable);
            if (provider is null)
                continue;
            var latest = await provider.GetUpdatesAsync(group.Select(e => e.Sha1!).ToList(), group.Key.Kind, instance.MinecraftVersion, instance.Loader, ct);
            foreach (var entry in group)
                if (latest.TryGetValue(entry.Sha1!, out var version) && version.VersionId != entry.VersionId)
                    result.Add(new ContentUpdate(entry, version));
        }
        return result;
    }

    public async Task UpdateAsync(CustomInstance instance, ContentUpdate update, CancellationToken ct = default)
    {
        var meta = await Provider(update.Version.Source).GetProjectAsync(update.Version.ProjectId, ct);
        var wasEnabled = update.Entry.Enabled;
        var entry = await DownloadAsync(instance, update.Entry.Kind, update.Version, meta, update.Entry.IsDependency, ct);
        if (!string.Equals(entry.FileName, update.Entry.FileName, StringComparison.Ordinal))
            Delete(instance, update.Entry);
        instance.Content.Remove(update.Entry);
        instance.Content.Add(entry);
        store.Save(instance);
        if (!wasEnabled)
            SetEnabled(instance, entry, false);
    }

    /// <summary>
    /// Syncs the list with the folders: adds files the player dropped in by hand (and asks Modrinth what they are),
    /// forgets entries whose files were deleted.
    /// </summary>
    public async Task<int> ScanAsync(CustomInstance instance, CancellationToken ct = default)
    {
        var game = store.GameDir(instance.Id);
        var found = new List<ContentEntry>();
        foreach (var kind in Enum.GetValues<ContentKind>())
        {
            var folder = Path.Combine(game, ContentEntry.FolderOf(kind));
            if (!Directory.Exists(folder))
                continue;
            foreach (var path in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(path);
                var enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                var baseName = enabled ? name : name[..^DisabledSuffix.Length];
                if (!IsContentFile(kind, baseName) || instance.Content.Any(e => e.Kind == kind && e.FileName == baseName))
                    continue;
                found.Add(new ContentEntry
                {
                    Kind = kind,
                    Source = ContentSource.Local,
                    FileName = baseName,
                    Title = Path.GetFileNameWithoutExtension(baseName),
                    Sha1 = Sha1Of(path),
                    Enabled = enabled,
                });
            }
        }
        var removed = instance.Content.RemoveAll(e =>
        {
            var path = Path.Combine(game, ContentEntry.FolderOf(e.Kind), e.FileName);
            return !File.Exists(path) && !File.Exists(path + DisabledSuffix);
        });

        if (found.Count > 0 && providers.FirstOrDefault(p => p.Source == ContentSource.Modrinth) is { } modrinth)
        {
            try
            {
                var known = await modrinth.IdentifyAsync(found.Select(f => f.Sha1!).ToList(), ct);
                foreach (var entry in found)
                {
                    if (!known.TryGetValue(entry.Sha1!, out var version))
                        continue;
                    var meta = await modrinth.GetProjectAsync(version.ProjectId, ct);
                    entry.Source = ContentSource.Modrinth;
                    entry.ProjectId = version.ProjectId;
                    entry.VersionId = version.VersionId;
                    entry.VersionNumber = version.VersionNumber;
                    entry.Title = meta.Title;
                    entry.IconUrl = meta.IconUrl;
                }
            }
            catch (HttpRequestException)
            {
                // Offline: keep them as local files.
            }
        }
        instance.Content.AddRange(found);
        if (found.Count > 0 || removed > 0)
            store.Save(instance);
        return found.Count;
    }

    private static bool IsContentFile(ContentKind kind, string name) => kind == ContentKind.Mod
        ? name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
        : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static string SafeFileName(string name)
    {
        var clean = Path.GetFileName(name.Replace('\\', '/'));
        if (clean.Length == 0 || clean.StartsWith('.') || clean.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"Недопустимое имя файла: {name}");
        return clean;
    }

    private static string Sha1Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA1.HashData(stream));
    }

    private static bool HashEquals(string path, HashAlgorithm algorithm, string expected)
    {
        using (algorithm)
        using (var stream = File.OpenRead(path))
            return string.Equals(Convert.ToHexStringLower(algorithm.ComputeHash(stream)), expected, StringComparison.OrdinalIgnoreCase);
    }
}
