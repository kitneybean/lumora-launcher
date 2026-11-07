namespace Lumora.Launcher.Core.GameServers;

/// <summary>One entry of the in-game multiplayer server list (servers.dat).</summary>
/// <param name="Name">Display name.</param>
/// <param name="Address">"host" or "host:port".</param>
/// <param name="IconBase64">Optional 64x64 PNG icon, base64 without data: prefix.</param>
public sealed record ServerListEntry(string Name, string Address, string? IconBase64 = null);
