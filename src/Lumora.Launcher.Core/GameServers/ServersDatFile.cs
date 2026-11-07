using fNbt;

namespace Lumora.Launcher.Core.GameServers;

/// <summary>
/// Reads and updates Minecraft's servers.dat (uncompressed NBT) so that servers
/// managed by Lumora appear at the top of the multiplayer list while
/// entries added by the player are preserved.
/// </summary>
public static class ServersDatFile
{
    public const string FileName = "servers.dat";

    public static IReadOnlyList<ServerListEntry> Read(string path)
    {
        if (!File.Exists(path))
            return [];

        var file = new NbtFile();
        file.LoadFromFile(path, NbtCompression.None, null);
        if (file.RootTag.Get<NbtList>("servers") is not { } servers)
            return [];

        return servers.OfType<NbtCompound>()
            .Select(s => new ServerListEntry(
                s.Get<NbtString>("name")?.Value ?? "",
                s.Get<NbtString>("ip")?.Value ?? "",
                s.Get<NbtString>("icon")?.Value))
            .Where(s => s.Address.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Puts <paramref name="managed"/> first (in order), drops stale copies of them and
    /// entries listed in <paramref name="previouslyManagedAddresses"/> (servers removed by the admin),
    /// and keeps every other user entry after them, including their unknown NBT fields.
    /// </summary>
    public static void Update(
        string path,
        IReadOnlyList<ServerListEntry> managed,
        IEnumerable<string>? previouslyManagedAddresses = null)
    {
        var removeAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in managed)
            removeAddresses.Add(Normalize(s.Address));
        foreach (var a in previouslyManagedAddresses ?? [])
            removeAddresses.Add(Normalize(a));

        var userEntries = new List<NbtCompound>();
        if (File.Exists(path))
        {
            var existing = new NbtFile();
            existing.LoadFromFile(path, NbtCompression.None, null);
            if (existing.RootTag.Get<NbtList>("servers") is { } list)
            {
                foreach (var entry in list.OfType<NbtCompound>())
                {
                    var ip = entry.Get<NbtString>("ip")?.Value;
                    if (ip is null || removeAddresses.Contains(Normalize(ip)))
                        continue;
                    userEntries.Add((NbtCompound)entry.Clone());
                }
            }
        }

        var servers = new NbtList("servers", NbtTagType.Compound);
        foreach (var s in managed)
            servers.Add(ToNbt(s));
        foreach (var e in userEntries)
            servers.Add(e);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tmp = fullPath + ".tmp";
        new NbtFile(new NbtCompound("") { servers }).SaveToFile(tmp, NbtCompression.None);
        File.Move(tmp, fullPath, overwrite: true);
    }

    private static NbtCompound ToNbt(ServerListEntry s)
    {
        var tag = new NbtCompound
        {
            new NbtString("name", s.Name),
            new NbtString("ip", s.Address),
        };
        if (!string.IsNullOrEmpty(s.IconBase64))
            tag.Add(new NbtString("icon", s.IconBase64));
        return tag;
    }

    /// <summary>"host" and "host:25565" refer to the same server.</summary>
    private static string Normalize(string address)
    {
        var a = address.Trim();
        return a.EndsWith(":25565", StringComparison.Ordinal) ? a[..^6] : a;
    }
}
