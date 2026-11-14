using Lumora.Launcher.Core.Custom;

namespace Lumora.Launcher.Core.Content;

public enum ContentSort
{
    Relevance,
    Downloads,
    Updated,
    Newest,
}

/// <summary>What to look for: <see cref="Kind"/> = null means modpacks.</summary>
public sealed record ContentQuery(
    string Text,
    ContentKind? Kind,
    string? MinecraftVersion,
    GameLoader? Loader,
    ContentSort Sort = ContentSort.Relevance,
    int Offset = 0,
    int Limit = 20);

public sealed record ContentProject(
    ContentSource Source,
    string Id,
    string Slug,
    string Title,
    string Description,
    string? Author,
    string? IconUrl,
    long Downloads,
    string PageUrl,
    ContentKind? Kind,
    IReadOnlyList<string> Categories);

public sealed record ContentFile(string FileName, string Url, long Size, string? Sha1, string? Sha512, bool Primary);

public sealed record ContentDependency(string ProjectId, string? VersionId, bool Required);

public sealed record ContentVersion(
    ContentSource Source,
    string ProjectId,
    string VersionId,
    string Name,
    string VersionNumber,
    IReadOnlyList<string> GameVersions,
    IReadOnlyList<string> Loaders,
    DateTimeOffset Published,
    IReadOnlyList<ContentFile> Files,
    IReadOnlyList<ContentDependency> Dependencies,
    /// <summary>False when the author forbids downloads outside the catalog's own site (CurseForge).</summary>
    bool DownloadAllowed = true)
{
    public ContentFile? PrimaryFile => Files.FirstOrDefault(f => f.Primary) ?? Files.FirstOrDefault();
}

public sealed record SearchPage(IReadOnlyList<ContentProject> Items, int Total);

/// <summary>A content catalog (Modrinth, CurseForge).</summary>
public interface IContentProvider
{
    ContentSource Source { get; }

    /// <summary>False when the catalog cannot be used (e.g. CurseForge without an API key).</summary>
    bool IsAvailable { get; }

    Task<SearchPage> SearchAsync(ContentQuery query, CancellationToken ct = default);

    Task<ContentProject> GetProjectAsync(string projectId, CancellationToken ct = default);

    /// <summary>Versions compatible with the build, newest first.</summary>
    Task<IReadOnlyList<ContentVersion>> GetVersionsAsync(string projectId, ContentKind? kind, string? minecraftVersion, GameLoader? loader, CancellationToken ct = default);

    Task<ContentVersion> GetVersionAsync(string projectId, string versionId, CancellationToken ct = default);

    /// <summary>Catalog versions of local files by SHA-1 (files the catalog does not know are missing from the result).</summary>
    Task<IReadOnlyDictionary<string, ContentVersion>> IdentifyAsync(IReadOnlyList<string> sha1, CancellationToken ct = default);

    /// <summary>Newest compatible versions for installed files by SHA-1.</summary>
    Task<IReadOnlyDictionary<string, ContentVersion>> GetUpdatesAsync(IReadOnlyList<string> sha1, ContentKind kind, string minecraftVersion, GameLoader loader, CancellationToken ct = default);
}

public static class ContentLoaders
{
    /// <summary>Catalog loader names a build accepts for a kind of content.</summary>
    public static IReadOnlyList<string> For(ContentKind? kind, GameLoader loader) => kind switch
    {
        ContentKind.ResourcePack => ["minecraft"],
        ContentKind.Shader => ["iris", "optifine", "canvas"],
        _ => loader switch
        {
            GameLoader.Fabric => ["fabric"],
            GameLoader.NeoForge => ["neoforge"],
            _ => ["minecraft"],
        },
    };
}
