using Lumora.Contracts;
using Lumora.Shared.Sync;

namespace Lumora.Launcher.Tests;

public class FileSyncPlannerTests
{
    private static readonly SyncRuleDto[] Rules =
    [
        new("mods", SyncPolicy.Strict),
        new("config", SyncPolicy.AddOrUpdate),
        new("options.txt", SyncPolicy.CreateIfMissing),
    ];

    private static BuildFileDto File(string path, string sha1, FileInstallMode mode = FileInstallMode.Required) =>
        new(path, sha1, 1, "https://x/" + sha1, mode, null);

    [Fact]
    public void Downloads_missing_and_changed_files_only()
    {
        var files = new[] { File("mods/a.jar", "aaa"), File("mods/b.jar", "bbb"), File("config/c.toml", "ccc") };
        var local = new Dictionary<string, string> { ["mods/a.jar"] = "aaa", ["mods/b.jar"] = "old" };

        var plan = FileSyncPlanner.Plan(files, Rules, new Dictionary<string, bool>(), p => local.GetValueOrDefault(p), local.Keys);

        Assert.Equal(["mods/b.jar", "config/c.toml"], plan.Downloads.Select(f => f.Path).ToArray());
        Assert.Empty(plan.Deletions);
    }

    [Fact]
    public void Strict_folder_loses_unknown_files_but_other_folders_keep_them()
    {
        var files = new[] { File("mods/a.jar", "aaa"), File("config/c.toml", "ccc") };
        var local = new Dictionary<string, string>
        {
            ["mods/a.jar"] = "aaa",
            ["mods/players-own.jar"] = "zzz",
            ["mods/sub/nested.jar"] = "yyy",
            ["config/c.toml"] = "edited-by-player",
            ["config/players.toml"] = "ppp",
        };

        var plan = FileSyncPlanner.Plan(files, Rules, new Dictionary<string, bool>(), p => local.GetValueOrDefault(p),
            local.Keys.Where(k => k.StartsWith("mods/")));

        Assert.Equal(["mods/players-own.jar", "mods/sub/nested.jar"], plan.Deletions);
        // AddOrUpdate: the build's version of c.toml wins, the player's own config stays.
        Assert.Equal(["config/c.toml"], plan.Downloads.Select(f => f.Path).ToArray());
    }

    [Fact]
    public void Create_if_missing_never_overwrites()
    {
        var files = new[] { File("options.txt", "defaults") };

        var existing = FileSyncPlanner.Plan(files, Rules, new Dictionary<string, bool>(), _ => "players-options", []);
        var missing = FileSyncPlanner.Plan(files, Rules, new Dictionary<string, bool>(), _ => null, []);

        Assert.True(existing.IsEmpty);
        Assert.Single(missing.Downloads);
    }

    [Fact]
    public void Optional_mods_follow_player_choice_and_default_mode()
    {
        var files = new[]
        {
            File("mods/map.jar", "map", FileInstallMode.OptionalOn),
            File("mods/shaders.jar", "sh", FileInstallMode.OptionalOff),
            File("mods/sound.jar", "snd", FileInstallMode.OptionalOn),
        };
        var local = new Dictionary<string, string> { ["mods/map.jar"] = "map", ["mods/sound.jar"] = "snd" };
        var choices = new Dictionary<string, bool> { ["mods/sound.jar"] = false, ["mods/shaders.jar"] = true };

        var plan = FileSyncPlanner.Plan(files, Rules, choices, p => local.GetValueOrDefault(p), local.Keys);

        Assert.Equal(["mods/shaders.jar"], plan.Downloads.Select(f => f.Path).ToArray());
        Assert.Equal(["mods/sound.jar"], plan.Deletions);
    }

    [Fact]
    public void Disabled_optional_file_in_shared_folder_is_removed_only_if_unchanged()
    {
        var files = new[] { File("shaderpacks/pack.zip", "orig", FileInstallMode.OptionalOff) };

        var ours = FileSyncPlanner.Plan(files, Rules, new Dictionary<string, bool>(), _ => "orig", []);
        var theirs = FileSyncPlanner.Plan(files, Rules, new Dictionary<string, bool>(), _ => "modified", []);

        Assert.Equal(["shaderpacks/pack.zip"], ours.Deletions);
        Assert.Empty(theirs.Deletions);
    }

    [Theory]
    [InlineData("mods/a.jar", SyncPolicy.Strict)]
    [InlineData("MODS/A.JAR", SyncPolicy.Strict)]
    [InlineData("config/x/y.toml", SyncPolicy.AddOrUpdate)]
    [InlineData("options.txt", SyncPolicy.CreateIfMissing)]
    [InlineData("modsextra/a.jar", SyncPolicy.AddOrUpdate)]
    [InlineData("kubejs/server.js", SyncPolicy.AddOrUpdate)]
    public void Most_specific_rule_wins(string path, SyncPolicy expected)
    {
        SyncRuleDto[] rules = [.. Rules, new("config/x", SyncPolicy.AddOrUpdate)];
        Assert.Equal(expected, FileSyncPlanner.PolicyFor(path, rules));
    }
}
