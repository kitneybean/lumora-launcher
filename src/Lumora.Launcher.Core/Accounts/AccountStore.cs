using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumora.Launcher.Core.Accounts;

[JsonConverter(typeof(JsonStringEnumConverter<AccountKind>))]
public enum AccountKind
{
    /// <summary>The Lumora account the player is signed in with (kept by SessionStore, not stored here).</summary>
    Lumora,
    Microsoft,
    Offline,
}

/// <summary>A Microsoft or offline account for the player's own builds.</summary>
public sealed class GameAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AccountKind Kind { get; set; }
    /// <summary>Minecraft name (for Microsoft: the profile name at the last sign-in).</summary>
    public required string Name { get; set; }
    public string? Uuid { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// accounts.json next to settings.json: Microsoft and offline accounts plus which one plays the player's builds.
/// Secrets are not here: Microsoft tokens live in the MSAL cache (encrypted by the OS) and CmlLib's account file.
/// </summary>
public sealed class AccountStore(string file)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private sealed class Data
    {
        public List<GameAccount> Accounts { get; set; } = [];
        /// <summary>Active account id; Guid.Empty means the Lumora account.</summary>
        public Guid ActiveId { get; set; }
    }

    private Data _data = Load(file);

    public IReadOnlyList<GameAccount> Accounts => _data.Accounts;

    /// <summary>Account used to play the player's own builds; null = the Lumora account.</summary>
    public GameAccount? Active => _data.Accounts.FirstOrDefault(a => a.Id == _data.ActiveId);

    public void SetActive(Guid? id)
    {
        _data.ActiveId = id is { } value && _data.Accounts.Any(a => a.Id == value) ? value : Guid.Empty;
        Save();
    }

    public GameAccount AddOffline(string name)
    {
        if (!OfflineNames.IsValid(name))
            throw new ArgumentException("Ник: от 3 до 16 символов, латинские буквы, цифры и «_».");
        var existing = _data.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Offline && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;
        var account = new GameAccount { Kind = AccountKind.Offline, Name = name, Uuid = OfflineNames.Uuid(name) };
        _data.Accounts.Add(account);
        Save();
        return account;
    }

    /// <summary>Adds or refreshes a Microsoft account (matched by UUID).</summary>
    public GameAccount UpsertMicrosoft(string name, string uuid)
    {
        var account = _data.Accounts.FirstOrDefault(a => a.Kind == AccountKind.Microsoft && a.Uuid == uuid);
        if (account is null)
        {
            account = new GameAccount { Kind = AccountKind.Microsoft, Name = name, Uuid = uuid };
            _data.Accounts.Add(account);
        }
        account.Name = name;
        Save();
        return account;
    }

    public void Remove(Guid id)
    {
        _data.Accounts.RemoveAll(a => a.Id == id);
        if (_data.ActiveId == id)
            _data.ActiveId = Guid.Empty;
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
        File.Move(tmp, file, overwrite: true);
    }

    private static Data Load(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(file), Json) ?? new Data() : new Data();
        }
        catch (JsonException)
        {
            return new Data();
        }
    }
}
