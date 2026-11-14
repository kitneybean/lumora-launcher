using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lumora.Launcher.Core.Custom;

namespace Lumora.Launcher.Core.Content;

/// <summary>Modrinth API v2 (no key needed): https://docs.modrinth.com/api</summary>
public sealed class ModrinthProvider(HttpClient http, string baseUrl = "https://api.modrinth.com/v2") : IContentProvider
{
    public ContentSource Source => ContentSource.Modrinth;

    public bool IsAvailable => true;

    public async Task<SearchPage> SearchAsync(ContentQuery query, CancellationToken ct = default)
    {
        var facets = new List<string[]>
        {
            new[] { $"project_type:{ProjectType(query.Kind)}" },
        };
        if (!string.IsNullOrWhiteSpace(query.MinecraftVersion))
            facets.Add([$"versions:{query.MinecraftVersion}"]);
        if (query.Loader is { } loader && query.Kind is null or ContentKind.Mod)
            facets.Add(ContentLoaders.For(query.Kind, loader).Select(l => $"categories:{l}").ToArray());

        var url = $"{baseUrl}/search?query={Uri.EscapeDataString(query.Text ?? "")}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}" +
                  $"&index={Index(query.Sort)}&offset={query.Offset}&limit={Math.Clamp(query.Limit, 1, 100)}";
        var json = JsonNode.Parse(await http.GetStringAsync(url, ct))!;
        var items = json["hits"]!.AsArray().Select(h => ParseHit(h!)).ToList();
        return new SearchPage(items, json["total_hits"]?.GetValue<int>() ?? items.Count);
    }

    public async Task<ContentProject> GetProjectAsync(string projectId, CancellationToken ct = default)
    {
        var p = JsonNode.Parse(await http.GetStringAsync($"{baseUrl}/project/{Uri.EscapeDataString(projectId)}", ct))!;
        var slug = p["slug"]!.GetValue<string>();
        var type = p["project_type"]!.GetValue<string>();
        return new ContentProject(ContentSource.Modrinth, p["id"]!.GetValue<string>(), slug, p["title"]!.GetValue<string>(),
            p["description"]?.GetValue<string>() ?? "", null, p["icon_url"]?.GetValue<string>(), p["downloads"]?.GetValue<long>() ?? 0,
            $"https://modrinth.com/{type}/{slug}", KindOf(type), Strings(p["categories"]));
    }

    public async Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(string projectId, ContentKind? kind, string? minecraftVersion, GameLoader? loader, CancellationToken ct = default)
    {
        var url = $"{baseUrl}/project/{Uri.EscapeDataString(projectId)}/version";
        var query = new List<string>();
        if (loader is { } l && kind is null or ContentKind.Mod)
            query.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(ContentLoaders.For(kind, l))));
        if (!string.IsNullOrWhiteSpace(minecraftVersion))
            query.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion })));
        if (query.Count > 0)
            url += "?" + string.Join('&', query);
        var json = JsonNode.Parse(await http.GetStringAsync(url, ct))!;
        return json.AsArray().Select(v => ParseVersion(v!)).OrderByDescending(v => v.Published).ToList();
    }

    public async Task<ContentVersion> GetVersionAsync(string projectId, string versionId, CancellationToken ct = default) =>
        ParseVersion(JsonNode.Parse(await http.GetStringAsync($"{baseUrl}/version/{Uri.EscapeDataString(versionId)}", ct))!);

    public async Task<IReadOnlyDictionary<string, ContentVersion>> IdentifyAsync(IReadOnlyList<string> sha1, CancellationToken ct = default)
    {
        if (sha1.Count == 0)
            return new Dictionary<string, ContentVersion>();
        using var response = await http.PostAsJsonAsync($"{baseUrl}/version_files", new { hashes = sha1, algorithm = "sha1" }, ct);
        response.EnsureSuccessStatusCode();
        return ParseHashMap(await response.Content.ReadAsStringAsync(ct));
    }

    public async Task<IReadOnlyDictionary<string, ContentVersion>> GetUpdatesAsync(IReadOnlyList<string> sha1, ContentKind kind, string minecraftVersion, GameLoader loader, CancellationToken ct = default)
    {
        if (sha1.Count == 0)
            return new Dictionary<string, ContentVersion>();
        object body = kind == ContentKind.Mod
            ? new { hashes = sha1, algorithm = "sha1", loaders = ContentLoaders.For(kind, loader), game_versions = new[] { minecraftVersion } }
            : new { hashes = sha1, algorithm = "sha1", game_versions = new[] { minecraftVersion } };
        using var response = await http.PostAsJsonAsync($"{baseUrl}/version_files/update", body, ct);
        response.EnsureSuccessStatusCode();
        return ParseHashMap(await response.Content.ReadAsStringAsync(ct));
    }

    // ---------------- parsing (internal for tests) ----------------

    internal static ContentProject ParseHit(JsonNode h)
    {
        var slug = h["slug"]!.GetValue<string>();
        var type = h["project_type"]?.GetValue<string>() ?? "mod";
        return new ContentProject(ContentSource.Modrinth, h["project_id"]!.GetValue<string>(), slug, h["title"]!.GetValue<string>(),
            h["description"]?.GetValue<string>() ?? "", h["author"]?.GetValue<string>(), NullIfEmpty(h["icon_url"]?.GetValue<string>()),
            h["downloads"]?.GetValue<long>() ?? 0, $"https://modrinth.com/{type}/{slug}", KindOf(type), Strings(h["display_categories"] ?? h["categories"]));
    }

    internal static ContentVersion ParseVersion(JsonNode v) => new(
        ContentSource.Modrinth,
        v["project_id"]!.GetValue<string>(),
        v["id"]!.GetValue<string>(),
        v["name"]?.GetValue<string>() ?? "",
        v["version_number"]?.GetValue<string>() ?? "",
        Strings(v["game_versions"]),
        Strings(v["loaders"]),
        v["date_published"]?.GetValue<DateTimeOffset>() ?? DateTimeOffset.MinValue,
        v["files"]!.AsArray().Select(f => new ContentFile(
            f!["filename"]!.GetValue<string>(),
            f["url"]!.GetValue<string>(),
            f["size"]?.GetValue<long>() ?? 0,
            f["hashes"]?["sha1"]?.GetValue<string>(),
            f["hashes"]?["sha512"]?.GetValue<string>(),
            f["primary"]?.GetValue<bool>() ?? false)).ToList(),
        (v["dependencies"]?.AsArray() ?? [])
            .Where(d => d!["project_id"] is not null)
            .Select(d => new ContentDependency(
                d!["project_id"]!.GetValue<string>(),
                d["version_id"]?.GetValue<string>(),
                d["dependency_type"]?.GetValue<string>() == "required"))
            .ToList());

    private static Dictionary<string, ContentVersion> ParseHashMap(string json)
    {
        var map = new Dictionary<string, ContentVersion>(StringComparer.OrdinalIgnoreCase);
        foreach (var (hash, version) in JsonNode.Parse(json)!.AsObject())
            if (version is not null)
                map[hash] = ParseVersion(version);
        return map;
    }

    private static string ProjectType(ContentKind? kind) => kind switch
    {
        ContentKind.Mod => "mod",
        ContentKind.ResourcePack => "resourcepack",
        ContentKind.Shader => "shader",
        _ => "modpack",
    };

    private static ContentKind? KindOf(string type) => type switch
    {
        "mod" => ContentKind.Mod,
        "resourcepack" => ContentKind.ResourcePack,
        "shader" => ContentKind.Shader,
        _ => null,
    };

    private static string Index(ContentSort sort) => sort switch
    {
        ContentSort.Downloads => "downloads",
        ContentSort.Updated => "updated",
        ContentSort.Newest => "newest",
        _ => "relevance",
    };

    private static IReadOnlyList<string> Strings(JsonNode? node) =>
        node is JsonArray array ? array.Select(n => n!.GetValue<string>()).ToList() : [];

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
