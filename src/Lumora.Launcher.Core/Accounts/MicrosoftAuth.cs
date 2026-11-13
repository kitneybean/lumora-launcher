using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using Microsoft.Identity.Client;
using XboxAuthNet.Game.Msal;

namespace Lumora.Launcher.Core.Accounts;

/// <summary>
/// Microsoft (licensed Minecraft) sign-in: MSAL in the system browser → Xbox Live → Minecraft services.
/// Needs an Azure application id approved for the Minecraft API (see README); without it the feature is hidden.
/// </summary>
public sealed class MicrosoftAuth(string clientId, string dataDir)
{
    private IPublicClientApplication? _app;
    private JELoginHandler? _handler;

    public static bool IsConfigured(string? clientId) => !string.IsNullOrWhiteSpace(clientId);

    /// <summary>Opens the browser for the Microsoft sign-in; returns the Minecraft identity.</summary>
    public async Task<LaunchIdentity> SignInAsync(CancellationToken ct = default)
    {
        var (app, handler) = await InitAsync();
        var authenticator = handler.CreateAuthenticatorWithNewAccount(ct);
        authenticator.AddMsalOAuth(app, msal => msal.Interactive());
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddJEAuthenticator();
        return ToIdentity(await Run(authenticator.ExecuteForLauncherAsync()));
    }

    /// <summary>Fresh Minecraft token for a saved account without asking the player (MSAL refresh token).</summary>
    public async Task<LaunchIdentity> RefreshAsync(string playerName, CancellationToken ct = default)
    {
        var (app, handler) = await InitAsync();
        var account = handler.AccountManager.GetAccounts().GetJEAccountByUsername(playerName)
            ?? throw new MicrosoftAuthException("Аккаунт Microsoft не найден на этом компьютере — войдите заново.");
        var authenticator = handler.CreateAuthenticator(account, ct);
        authenticator.AddMsalOAuth(app, msal => msal.Silent());
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddJEAuthenticator();
        return ToIdentity(await Run(authenticator.ExecuteForLauncherAsync()));
    }

    public async Task SignOutAsync(string playerName, CancellationToken ct = default)
    {
        var (app, handler) = await InitAsync();
        if (handler.AccountManager.GetAccounts().GetJEAccountByUsername(playerName) is { } account)
            await handler.Signout(account, ct);
        foreach (var a in await app.GetAccountsAsync())
            await app.RemoveAsync(a);
    }

    private async Task<(IPublicClientApplication App, JELoginHandler Handler)> InitAsync()
    {
        Directory.CreateDirectory(dataDir);
        _app ??= await MsalClientHelper.BuildApplicationWithCache(clientId, new MsalCacheSettings
        {
            CacheDir = dataDir,
            CacheFileName = "microsoft-msal.cache",
        });
        _handler ??= new JELoginHandlerBuilder()
            .WithAccountManager(Path.Combine(dataDir, "microsoft-accounts.json"))
            .Build();
        return (_app, _handler);
    }

    private static LaunchIdentity ToIdentity(MSession session) =>
        new(session.Username!, session.UUID!, session.AccessToken!, "msa", null, session.Xuid, false);

    /// <summary>Turns library errors into messages for the player.</summary>
    private static async Task<MSession> Run(Task<MSession> task)
    {
        try
        {
            return await task;
        }
        catch (MsalException e) when (e.ErrorCode is "authentication_canceled" or "access_denied")
        {
            throw new MicrosoftAuthException("Вход через Microsoft отменён.");
        }
        catch (Exception e) when (e.Message.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("403"))
        {
            throw new MicrosoftAuthException("Microsoft не разрешил этому лаунчеру вход в Minecraft (приложение ещё не одобрено Mojang).");
        }
        catch (Exception e) when (e.Message.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("profile", StringComparison.OrdinalIgnoreCase))
        {
            throw new MicrosoftAuthException("На этом аккаунте Microsoft нет купленного Minecraft: Java Edition.");
        }
    }
}

public sealed class MicrosoftAuthException(string message) : Exception(message);
