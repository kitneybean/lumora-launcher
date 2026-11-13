using System.Text.Json;
using System.Xml.Linq;

namespace Lumora.Launcher.Core.Custom;

public sealed record MinecraftVersionInfo(string Id, bool IsRelease, DateTimeOffset ReleaseTime);

public sealed record LoaderVersionInfo(string Version, bool IsStable);

/// <summary>Minecraft versions (Mojang), NeoForge versions (maven.neoforged.net) and Fabric Loader versions (meta.fabricmc.net).</summary>
public sealed class VersionCatalog(HttpClient http)
{
    public const string MojangManifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    public const string FabricMeta = "https://meta.fabricmc.net/v2";

    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private readonly Dictionary<string, (DateTimeOffset At, object Value)> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task<IReadOnlyList<MinecraftVersionInfo>> MinecraftVersionsAsync(CancellationToken ct = default) =>
        CachedAsync("mc", async () => ParseMojang(await http.GetStringAsync(MojangManifest, ct)), ct);

    public async Task<IReadOnlyList<LoaderVersionInfo>> LoaderVersionsAsync(GameLoader loader, string minecraftVersion, CancellationToken ct = default) =>
        loader switch
        {
            GameLoader.NeoForge => ForMinecraft(await CachedAsync("neoforge", async () => ParseNeoForge(await http.GetStringAsync(NeoForgeMetadata, ct)), ct), minecraftVersion),
            GameLoader.Fabric => await CachedAsync($"fabric:{minecraftVersion}",
                async () => ParseFabric(await http.GetStringAsync($"{FabricMeta}/versions/loader/{Uri.EscapeDataString(minecraftVersion)}", ct)), ct),
            _ => [],
        };

    /// <summary>The version to offer by default: the newest stable one, otherwise the newest.</summary>
    public static LoaderVersionInfo? Recommended(IReadOnlyList<LoaderVersionInfo> versions) =>
        versions.FirstOrDefault(v => v.IsStable) ?? versions.FirstOrDefault();

    // ---------------- parsing (public for tests) ----------------

    public static IReadOnlyList<MinecraftVersionInfo> ParseMojang(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("versions").EnumerateArray()
            .Select(v => new MinecraftVersionInfo(
                v.GetProperty("id").GetString()!,
                v.GetProperty("type").GetString() == "release",
                v.GetProperty("releaseTime").GetDateTimeOffset()))
            .Where(v => v.IsRelease || v.ReleaseTime.Year >= 2019) // old alphas/betas are of no use here
            .ToList();
    }

    /// <summary>All NeoForge versions, newest first (beta ones marked unstable).</summary>
    public static IReadOnlyList<LoaderVersionInfo> ParseNeoForge(string xml) =>
        XDocument.Parse(xml).Descendants("version")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .Select(v => new LoaderVersionInfo(v, !v.Contains("beta", StringComparison.OrdinalIgnoreCase) && !v.Contains("alpha", StringComparison.OrdinalIgnoreCase)))
            .Reverse()
            .ToList();

    /// <summary>
    /// NeoForge numbers follow Minecraft: 1.21.1 → 21.1.x, 1.21 → 21.0.x, 1.20.6 → 20.6.x.
    /// </summary>
    public static IReadOnlyList<LoaderVersionInfo> ForMinecraft(IReadOnlyList<LoaderVersionInfo> all, string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.');
        if (parts.Length < 2 || parts[0] != "1")
            return [];
        var prefix = $"{parts[1]}.{(parts.Length > 2 ? parts[2] : "0")}.";
        return all.Where(v => v.Version.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    public static IReadOnlyList<LoaderVersionInfo> ParseFabric(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("loader"))
            .Select(l => new LoaderVersionInfo(l.GetProperty("version").GetString()!, l.TryGetProperty("stable", out var s) && s.GetBoolean()))
            .ToList();
    }

    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> load, CancellationToken ct) where T : class
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out var hit) && DateTimeOffset.UtcNow - hit.At < Ttl)
                return (T)hit.Value;
        }
        finally
        {
            _lock.Release();
        }
        var value = await load();
        await _lock.WaitAsync(ct);
        try
        {
            _cache[key] = (DateTimeOffset.UtcNow, value);
        }
        finally
        {
            _lock.Release();
        }
        return value;
    }
}
