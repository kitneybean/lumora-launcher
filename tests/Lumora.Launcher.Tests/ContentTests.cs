using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lumora.Launcher.Core.Content;
using Lumora.Launcher.Core.Custom;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.Tests;

/// <summary>Answers requests from a URL → response table (substring match).</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    public readonly List<(string Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> Routes = [];
    public readonly List<string> Requests = [];

    public FakeHttp On(string match, string json) =>
        On(match, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    public FakeHttp OnBytes(string match, byte[] bytes) =>
        On(match, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

    public FakeHttp On(string match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        Routes.Add((match, respond));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add($"{request.Method} {url}");
        foreach (var (match, respond) in Routes)
            if (url.Contains(match, StringComparison.Ordinal))
                return Task.FromResult(respond(request));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public sealed class ContentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lumora-content").FullName;
    private static readonly byte[] JarA = Encoding.UTF8.GetBytes("jar of mod A");
    private static readonly byte[] JarApi = Encoding.UTF8.GetBytes("jar of the API");

    private static string Sha1(byte[] b) => Convert.ToHexStringLower(SHA1.HashData(b));

    private static string Version(string project, string id, string file, byte[] bytes, string deps = "[]", string host = "cdn.modrinth.com") => $$$"""
        {"id":"{{{id}}}","project_id":"{{{project}}}","name":"{{{id}}}","version_number":"1.0.{{{id}}}","game_versions":["1.21.1"],"loaders":["fabric"],
         "date_published":"2024-08-10T00:00:00Z","dependencies":{{{deps}}},
         "files":[{"filename":"{{{file}}}","url":"https://{{{host}}}/data/{{{project}}}/{{{file}}}","size":{{{bytes.Length}}},"primary":true,
                   "hashes":{"sha1":"{{{Sha1(bytes)}}}"}}]}
        """;

    private (CustomInstanceStore Store, CustomInstance Instance, ContentInstaller Installer, FakeHttp Http) Setup(Action<FakeHttp> routes)
    {
        var settings = new LauncherSettings { DataDirectory = _root };
        var store = new CustomInstanceStore(new LauncherPaths(_root), () => settings);
        var instance = store.Create("Test", "1.21.1", GameLoader.Fabric, "0.16.9");
        var fake = new FakeHttp();
        routes(fake);
        var http = new HttpClient(fake);
        var installer = new ContentInstaller(http, store, [new ModrinthProvider(http, "https://api.modrinth.com/v2")]);
        return (store, instance, installer, fake);
    }

    private static string Project(string id, string title) =>
        $$"""{"id":"{{id}}","slug":"{{id.ToLower()}}","title":"{{title}}","description":"d","project_type":"mod","icon_url":null,"downloads":5,"categories":["fabric"]}""";

    [Fact]
    public async Task Install_brings_required_dependencies_and_toggles_files()
    {
        var (store, instance, installer, _) = Setup(h => h
            .On("/project/MODA/version", "[" + Version("MODA", "va1", "mod-a.jar", JarA, """[{"project_id":"API","dependency_type":"required"},{"project_id":"OPT","dependency_type":"optional"}]""") + "]")
            .On("/project/API/version", "[" + Version("API", "vapi", "api.jar", JarApi) + "]")
            .On("/project/MODA", Project("MODA", "Mod A"))
            .On("/project/API", Project("API", "Fabric API"))
            .OnBytes("/data/MODA/mod-a.jar", JarA)
            .OnBytes("/data/API/api.jar", JarApi));

        var result = await installer.InstallAsync(instance, ContentSource.Modrinth, "MODA", ContentKind.Mod);
        Assert.Equal(["Mod A", "Fabric API"], result.Added.Select(e => e.Title).ToArray());
        Assert.True(result.Added[1].IsDependency);
        var mods = Path.Combine(store.GameDir(instance.Id), "mods");
        Assert.True(File.Exists(Path.Combine(mods, "mod-a.jar")));
        Assert.True(File.Exists(Path.Combine(mods, "api.jar")));

        var entry = instance.Content.First(e => e.Title == "Mod A");
        installer.SetEnabled(instance, entry, false);
        Assert.True(File.Exists(Path.Combine(mods, "mod-a.jar.disabled")));
        installer.SetEnabled(instance, entry, true);
        installer.Remove(instance, entry);
        Assert.False(File.Exists(Path.Combine(mods, "mod-a.jar")));
        Assert.Single(store.Get(instance.Id)!.Content);
    }

    [Fact]
    public async Task Untrusted_hosts_and_wrong_hashes_are_refused()
    {
        var (_, instance, installer, _) = Setup(h => h
            .On("/project/EVIL/version", "[" + Version("EVIL", "v1", "evil.jar", JarA, host: "evil.example.com") + "]")
            .On("/project/BAD/version", "[" + Version("BAD", "v2", "bad.jar", JarA) + "]")
            .On("/project/EVIL", Project("EVIL", "Evil"))
            .On("/project/BAD", Project("BAD", "Bad"))
            .OnBytes("/data/BAD/bad.jar", JarApi)); // different bytes than the hash says

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(instance, ContentSource.Modrinth, "EVIL", ContentKind.Mod));
        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(instance, ContentSource.Modrinth, "BAD", ContentKind.Mod));
        Assert.Empty(instance.Content);
    }

    [Fact]
    public async Task Hand_dropped_files_are_recognised_and_updates_found()
    {
        var (store, instance, installer, http) = Setup(h => h
            .On("/version_files/update", $$"""{"{{Sha1(JarA)}}":{{Version("MODA", "va2", "mod-a-2.jar", JarApi)}}}""")
            .On("/version_files", $$"""{"{{Sha1(JarA)}}":{{Version("MODA", "va1", "mod-a.jar", JarA)}}}""")
            .On("/project/MODA", Project("MODA", "Mod A")));
        var mods = Path.Combine(store.GameDir(instance.Id), "mods");
        Directory.CreateDirectory(mods);
        File.WriteAllBytes(Path.Combine(mods, "mod-a.jar"), JarA);
        File.WriteAllBytes(Path.Combine(mods, "unknown.jar.disabled"), JarApi);
        File.WriteAllText(Path.Combine(mods, "notes.txt"), "not a mod");

        Assert.Equal(2, await installer.ScanAsync(instance));
        var known = instance.Content.Single(e => e.FileName == "mod-a.jar");
        Assert.Equal(ContentSource.Modrinth, known.Source);
        Assert.Equal("Mod A", known.Title);
        var unknown = instance.Content.Single(e => e.FileName == "unknown.jar");
        Assert.Equal(ContentSource.Local, unknown.Source);
        Assert.False(unknown.Enabled);

        var updates = await installer.CheckUpdatesAsync(instance);
        Assert.Equal("va2", Assert.Single(updates).Version.VersionId);

        File.Delete(Path.Combine(mods, "unknown.jar.disabled"));
        await installer.ScanAsync(instance);
        Assert.DoesNotContain(instance.Content, e => e.FileName == "unknown.jar");
    }

    [Fact]
    public void Modrinth_search_hit_is_parsed()
    {
        var hit = System.Text.Json.Nodes.JsonNode.Parse("""
            {"project_id":"AANobbMI","slug":"sodium","title":"Sodium","description":"Fast","author":"jellysquid3",
             "icon_url":"https://cdn.modrinth.com/i.png","downloads":1000,"project_type":"mod","display_categories":["optimization"]}
            """)!;
        var p = ModrinthProvider.ParseHit(hit);
        Assert.Equal("https://modrinth.com/mod/sodium", p.PageUrl);
        Assert.Equal(ContentKind.Mod, p.Kind);
        Assert.Equal("jellysquid3", p.Author);
    }

    [Theory]
    [InlineData("../evil.jar", "evil.jar")]
    [InlineData("mods\\ok.jar", "ok.jar")]
    public void File_names_are_kept_inside_the_folder(string name, string expected) =>
        Assert.Equal(expected, ContentInstaller.SafeFileName(name));

    public void Dispose() => Directory.Delete(_root, true);
}
