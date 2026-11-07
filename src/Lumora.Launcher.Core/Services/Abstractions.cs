using Lumora.Contracts;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.Core.Services;

/// <summary>Logged-in player.</summary>
public sealed record AccountSession(
    string AccessToken,
    string ClientToken,
    Guid ProfileId,
    string PlayerName,
    string? SkinUrl,
    bool SlimModel);

/// <summary>Thrown when login or password is wrong, or the account is banned.</summary>
public sealed class AuthenticationFailedException(string message) : Exception(message);

/// <summary>Lumora Auth server: Yggdrasil login + launcher API.</summary>
public interface ILumoraApi
{
    Task<AccountSession> LoginAsync(string login, string password, CancellationToken ct = default);

    Task LogoutAsync(AccountSession session, CancellationToken ct = default);

    Task<IReadOnlyList<LauncherServerDto>> GetServersAsync(AccountSession session, CancellationToken ct = default);

    Task<BuildManifestDto> GetManifestAsync(AccountSession session, Guid buildId, CancellationToken ct = default);

    /// <summary>PNG bytes of the player's skin, or null for the default skin.</summary>
    Task<byte[]?> GetSkinAsync(AccountSession session, CancellationToken ct = default);
}

/// <summary>Live status from the Minecraft server list ping.</summary>
public sealed record ServerStatus(bool IsOnline, int Players, int MaxPlayers, int PingMs, string? Motd)
{
    public static ServerStatus Offline { get; } = new(false, 0, 0, 0, null);
}

public interface IServerStatusService
{
    Task<ServerStatus> GetStatusAsync(string address, int port, CancellationToken ct = default);
}

public enum InstanceState
{
    NotInstalled,
    UpdateAvailable,
    Ready,
}

/// <summary>Install progress: a human readable stage plus 0..1 fraction (null = indeterminate).</summary>
public sealed record InstallProgress(string Stage, double? Fraction, string? Detail = null);

/// <summary>Installs builds and starts the game.</summary>
public interface IGameService
{
    InstanceState GetState(LauncherBuildSummaryDto build);

    Task InstallAsync(AccountSession session, LauncherBuildSummaryDto build, IProgress<InstallProgress> progress, CancellationToken ct = default);

    /// <summary>Starts the game and returns when it exits. Output lines go to <paramref name="log"/>.</summary>
    Task<int> LaunchAsync(
        AccountSession session,
        LauncherServerDto server,
        GameSettings settings,
        Action<string> log,
        CancellationToken ct = default);
}
