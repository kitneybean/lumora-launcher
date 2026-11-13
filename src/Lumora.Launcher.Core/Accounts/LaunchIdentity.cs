using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Lumora.Launcher.Core.Accounts;

/// <summary>Who the game is started as.</summary>
public sealed record LaunchIdentity(
    string PlayerName,
    string Uuid,
    string AccessToken,
    string UserType,
    string? ClientToken,
    string? Xuid,
    /// <summary>Lumora accounts: start with authlib-injector so Lumora servers and skins work.</summary>
    bool UseLumoraAuth)
{
    /// <summary>
    /// Offline ("pirate") play: works in single player and on servers with online-mode=false.
    /// The UUID follows Minecraft's own rule (name-based UUID v3 of "OfflinePlayer:&lt;name&gt;"), so a server
    /// recognises the same player every time.
    /// </summary>
    public static LaunchIdentity Offline(string name)
    {
        if (!OfflineNames.IsValid(name))
            throw new ArgumentException("Ник: от 3 до 16 символов, латинские буквы, цифры и «_».", nameof(name));
        return new LaunchIdentity(name, OfflineNames.Uuid(name), "0", "legacy", null, null, false);
    }
}

public static partial class OfflineNames
{
    [GeneratedRegex("^[A-Za-z0-9_]{3,16}$")]
    private static partial Regex NameRegex();

    public static bool IsValid(string? name) => name is not null && NameRegex().IsMatch(name);

    /// <summary>UUID the vanilla server gives an offline player, without dashes.</summary>
    public static string Uuid(string name)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80); // IETF variant
        return Convert.ToHexStringLower(hash);
    }
}
