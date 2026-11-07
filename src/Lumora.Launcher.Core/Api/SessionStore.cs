using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumora.Launcher.Core.Services;

namespace Lumora.Launcher.Core.Api;

/// <summary>
/// Remembers the logged-in session ("Запомнить меня"). Only tokens are stored, never the password.
/// Windows: encrypted with DPAPI for the current user. Other systems: file readable only by the user.
/// </summary>
public sealed class SessionStore(string path)
{
    private static readonly byte[] Entropy = "Lumora.Launcher.Session"u8.ToArray();

    public AccountSession? Load()
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var data = File.ReadAllBytes(path);
            if (OperatingSystem.IsWindows())
                data = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AccountSession>(Encoding.UTF8.GetString(data));
        }
        catch (Exception e) when (e is IOException or CryptographicException or JsonException or UnauthorizedAccessException)
        {
            Clear();
            return null;
        }
    }

    public void Save(AccountSession session)
    {
        var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session));
        if (OperatingSystem.IsWindows())
            data = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, []);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
