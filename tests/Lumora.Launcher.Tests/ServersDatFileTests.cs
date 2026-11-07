using fNbt;
using Lumora.Launcher.Core.GameServers;

namespace Lumora.Launcher.Tests;

public sealed class ServersDatFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lumora-tests-" + Guid.NewGuid().ToString("N"));
    private string DatPath => Path.Combine(_dir, ServersDatFile.FileName);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Creates_file_when_missing()
    {
        ServersDatFile.Update(DatPath, [new("Lumora", "play.lumora.net")]);

        var entries = ServersDatFile.Read(DatPath);
        Assert.Equal([new ServerListEntry("Lumora", "play.lumora.net")], entries);
    }

    [Fact]
    public void Managed_servers_go_first_and_user_servers_are_kept()
    {
        ServersDatFile.Update(DatPath, [new("Old name", "play.lumora.net"), new("Friend", "friend.example:25570")]);

        ServersDatFile.Update(DatPath, [new("Lumora", "play.lumora.net:25565", "aWNvbg==")]);

        var entries = ServersDatFile.Read(DatPath);
        Assert.Equal(2, entries.Count);
        Assert.Equal(new ServerListEntry("Lumora", "play.lumora.net:25565", "aWNvbg=="), entries[0]);
        Assert.Equal("friend.example:25570", entries[1].Address);
    }

    [Fact]
    public void Servers_removed_by_admin_are_dropped()
    {
        ServersDatFile.Update(DatPath, [new("A", "a.lumora.net"), new("B", "b.lumora.net")]);

        ServersDatFile.Update(DatPath, [new("A", "a.lumora.net")], previouslyManagedAddresses: ["a.lumora.net", "b.lumora.net"]);

        Assert.Equal(["a.lumora.net"], ServersDatFile.Read(DatPath).Select(e => e.Address));
    }

    [Fact]
    public void Unknown_fields_of_user_entries_are_preserved()
    {
        Directory.CreateDirectory(_dir);
        var user = new NbtCompound
        {
            new NbtString("name", "Mine"),
            new NbtString("ip", "mine.example"),
            new NbtByte("acceptTextures", 1),
        };
        new NbtFile(new NbtCompound("") { new NbtList("servers", NbtTagType.Compound) { user } })
            .SaveToFile(DatPath, NbtCompression.None);

        ServersDatFile.Update(DatPath, [new("Lumora", "play.lumora.net")]);

        var file = new NbtFile();
        file.LoadFromFile(DatPath, NbtCompression.None, null);
        var kept = file.RootTag.Get<NbtList>("servers")!.OfType<NbtCompound>().Last();
        Assert.Equal(1, kept.Get<NbtByte>("acceptTextures")!.Value);
    }
}
