using Lumora.Launcher.Core.Accounts;
using Lumora.Launcher.Core.Custom;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.Tests;

public class OfflineAccountTests
{
    [Fact]
    public void Offline_uuid_matches_the_vanilla_server()
    {
        // The well-known offline UUID of "Notch" (UUID v3 of "OfflinePlayer:Notch").
        Assert.Equal("b50ad385829d3141a2167e7d7539ba7f", OfflineNames.Uuid("Notch"));
        var identity = LaunchIdentity.Offline("Steve_1");
        Assert.Equal("legacy", identity.UserType);
        Assert.False(identity.UseLumoraAuth);
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("Steve", true)]
    [InlineData("Стив", false)]
    [InlineData("a_very_long_name_17", false)]
    public void Offline_names_follow_minecraft_rules(string name, bool valid) =>
        Assert.Equal(valid, OfflineNames.IsValid(name));
}

public class VersionCatalogTests
{
    [Fact]
    public void Mojang_manifest_keeps_releases_and_recent_snapshots()
    {
        const string json = """
            {"latest":{"release":"1.21.1"},"versions":[
              {"id":"24w40a","type":"snapshot","releaseTime":"2024-10-02T10:00:00+00:00"},
              {"id":"1.21.1","type":"release","releaseTime":"2024-08-08T12:24:45+00:00"},
              {"id":"b1.7.3","type":"old_beta","releaseTime":"2011-07-07T22:00:00+00:00"},
              {"id":"1.7.10","type":"release","releaseTime":"2014-05-14T17:29:23+00:00"}]}
            """;
        var versions = VersionCatalog.ParseMojang(json);
        Assert.Equal(["24w40a", "1.21.1", "1.7.10"], versions.Select(v => v.Id).ToArray());
        Assert.False(versions[0].IsRelease);
    }

    [Fact]
    public void NeoForge_versions_are_matched_to_minecraft()
    {
        const string xml = """
            <metadata><versioning><versions>
              <version>20.4.80-beta</version><version>21.0.167</version>
              <version>21.1.1-beta</version><version>21.1.77</version><version>21.1.265</version>
            </versions></versioning></metadata>
            """;
        var all = VersionCatalog.ParseNeoForge(xml);
        var for1211 = VersionCatalog.ForMinecraft(all, "1.21.1");
        Assert.Equal(["21.1.265", "21.1.77", "21.1.1-beta"], for1211.Select(v => v.Version).ToArray());
        Assert.Equal("21.1.265", VersionCatalog.Recommended(for1211)!.Version);
        Assert.Equal(["21.0.167"], VersionCatalog.ForMinecraft(all, "1.21").Select(v => v.Version).ToArray());
        Assert.Empty(VersionCatalog.ForMinecraft(all, "24w40a"));
    }

    [Fact]
    public void Fabric_loader_versions_and_profile_id()
    {
        const string loaders = """[{"loader":{"version":"0.16.10","stable":false}},{"loader":{"version":"0.16.9","stable":true}}]""";
        var versions = VersionCatalog.ParseFabric(loaders);
        Assert.Equal("0.16.9", VersionCatalog.Recommended(versions)!.Version);

        Assert.Equal("fabric-loader-0.16.9-1.21.1",
            FabricProfile.VersionId("""{"id":"fabric-loader-0.16.9-1.21.1","inheritsFrom":"1.21.1","libraries":[]}"""));
        Assert.Throws<InvalidDataException>(() => FabricProfile.VersionId("""{"id":"../evil"}"""));
    }
}

public sealed class CustomInstanceStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lumora-custom").FullName;

    [Fact]
    public void Create_save_duplicate_and_delete()
    {
        var settings = new LauncherSettings { DataDirectory = _root };
        var store = new CustomInstanceStore(new LauncherPaths(_root), () => settings);

        var a = store.Create("  Моя\u0007 сборка  ", "1.21.1", GameLoader.Fabric, "0.16.9");
        Assert.Equal("Моя сборка", a.Name);
        Assert.True(a.NeedsInstall);
        a.InstalledVersionName = "fabric-loader-0.16.9-1.21.1";
        a.InstalledFor = a.InstallKey;
        a.Content.Add(new ContentEntry { Kind = ContentKind.Mod, Source = ContentSource.Modrinth, ProjectId = "AANobbMI", FileName = "sodium.jar" });
        store.Save(a);
        File.WriteAllText(Path.Combine(store.GameDir(a.Id), "options.txt"), "lang:ru_ru");

        var loaded = store.Get(a.Id)!;
        Assert.False(loaded.NeedsInstall);
        Assert.Equal(GameLoader.Fabric, loaded.Loader);
        Assert.Single(loaded.Content);

        // Changing the loader version means installing again.
        loaded.LoaderVersion = "0.16.10";
        Assert.True(loaded.NeedsInstall);

        var copy = store.Duplicate(a.Id);
        Assert.Equal("Моя сборка (копия)", copy.Name);
        Assert.True(File.Exists(Path.Combine(store.GameDir(copy.Id), "options.txt")));
        Assert.Equal(2, store.List().Count);

        store.Delete(a.Id);
        Assert.Single(store.List());
    }

    public void Dispose() => Directory.Delete(_root, true);
}

public sealed class AccountStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lumora-accounts").FullName;

    [Fact]
    public void Offline_and_microsoft_accounts_persist_with_the_active_one()
    {
        var file = Path.Combine(_dir, "accounts.json");
        var store = new AccountStore(file);
        var steve = store.AddOffline("Steve");
        Assert.Same(steve, store.AddOffline("steve")); // no duplicates
        Assert.Throws<ArgumentException>(() => store.AddOffline("x"));
        var ms = store.UpsertMicrosoft("Notch", "069a79f444e94726a5befca90e38aaf5");
        store.SetActive(ms.Id);

        var reloaded = new AccountStore(file);
        Assert.Equal(2, reloaded.Accounts.Count);
        Assert.Equal("Notch", reloaded.Active!.Name);
        reloaded.Remove(ms.Id);
        Assert.Null(reloaded.Active); // back to the Lumora account
    }

    public void Dispose() => Directory.Delete(_dir, true);
}

public class Log4jXmlReaderTests
{
    [Fact]
    public void Xml_events_become_plain_lines()
    {
        var lines = new List<string>();
        var reader = new Lumora.Launcher.Core.Game.Log4jXmlReader(lines.Add);
        reader.Line("plain line");
        reader.Line("""<log4j:Event logger="Iris" timestamp="1794623418103" level="INFO" thread="Render thread">""");
        reader.Line("""  <log4j:Message><![CDATA[Loading 16 mods:""");
        reader.Line("""  - sodium 0.8.13]]></log4j:Message>""");
        reader.Line("""</log4j:Event>""");
        Assert.Equal("plain line", lines[0]);
        Assert.Matches(@"^\[\d\d:\d\d:\d\d\] \[Render thread/INFO\] \[Iris\]: Loading 16 mods:$", lines[1]);
        Assert.Equal("  - sodium 0.8.13", lines[2]);
    }
}
