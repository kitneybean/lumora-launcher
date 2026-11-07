using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumora.Contracts;
using Lumora.Launcher.Core.Services;

namespace Lumora.Launcher.Core.Api;

/// <summary>
/// HTTP client for Lumora Auth: Yggdrasil login (<c>/api/yggdrasil</c>) and the launcher API (<c>/api/launcher</c>).
/// </summary>
public sealed class LumoraApiClient : ILumoraApi, IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<string> _serverUrl;
    private readonly bool _ownsClient;

    /// <param name="serverUrl">Read on every call so changing it in settings applies immediately.</param>
    public LumoraApiClient(Func<string> serverUrl, HttpClient? http = null)
    {
        _serverUrl = serverUrl;
        _ownsClient = http is null;
        _http = http ?? CreateHttpClient(null);
    }

    public static HttpClient CreateHttpClient(string? proxyUrl)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            handler.Proxy = new WebProxy(proxyUrl);
            handler.UseProxy = true;
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"LumoraLauncher/{typeof(LumoraApiClient).Assembly.GetName().Version?.ToString(3) ?? "0"}");
        return http;
    }

    public string ServerUrl => _serverUrl().TrimEnd('/');

    public string YggdrasilUrl => ServerUrl + "/api/yggdrasil";

    // ------------------------------------------------------------------ auth

    public async Task<AccountSession> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        var response = await PostYggdrasilAsync("/authserver/authenticate", new
        {
            username = login,
            password,
            requestUser = false,
            agent = new { name = "Minecraft", version = 1 },
        }, ct);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, ct) ?? throw Unexpected();
        return await ToSessionAsync(auth.AccessToken, auth.ClientToken, auth.SelectedProfile, ct);
    }

    public async Task<AccountSession> RefreshAsync(AccountSession session, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await PostYggdrasilAsync("/authserver/refresh", new
            {
                accessToken = session.AccessToken,
                clientToken = session.ClientToken,
                requestUser = false,
            }, ct);
        }
        catch (AuthenticationFailedException e)
        {
            throw new SessionExpiredException(e.Message);
        }
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json, ct) ?? throw Unexpected();
        return await ToSessionAsync(auth.AccessToken, auth.ClientToken, auth.SelectedProfile, ct);
    }

    public async Task LogoutAsync(AccountSession session, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(YggdrasilUrl + "/authserver/invalidate",
            new { accessToken = session.AccessToken, clientToken = session.ClientToken }, ct);
    }

    private async Task<AccountSession> ToSessionAsync(string accessToken, string clientToken, ProfileRef? profile, CancellationToken ct)
    {
        if (profile is null)
            throw new AuthenticationFailedException("У аккаунта нет игрового профиля. Придумайте ник на сайте, в разделе «Мой аккаунт».");
        var id = Guid.ParseExact(profile.Id, "N");
        var (skinUrl, slim) = await GetTexturesAsync(id, ct);
        return new AccountSession(accessToken, clientToken, id, profile.Name, skinUrl, slim);
    }

    // ------------------------------------------------------------------ skins

    public async Task<byte[]?> GetSkinAsync(AccountSession session, CancellationToken ct = default)
    {
        var (url, _) = await GetTexturesAsync(session.ProfileId, ct);
        return url is null ? null : await _http.GetByteArrayAsync(url, ct);
    }

    public async Task<AccountSession> UploadSkinAsync(AccountSession session, byte[] png, bool slim, CancellationToken ct = default)
    {
        var file = new ByteArrayContent(png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(slim ? "slim" : ""), "model" },
            { file, "file", "skin.png" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{YggdrasilUrl}/api/user/profile/{session.ProfileId:N}/skin") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _http.SendAsync(request, ct);
        await EnsureYggdrasilSuccessAsync(response, ct);
        var (url, isSlim) = await GetTexturesAsync(session.ProfileId, ct);
        return session with { SkinUrl = url, SlimModel = isSlim };
    }

    private async Task<(string? Url, bool Slim)> GetTexturesAsync(Guid profileId, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"{YggdrasilUrl}/sessionserver/session/minecraft/profile/{profileId:N}", ct);
        if (response.StatusCode != HttpStatusCode.OK)
            return (null, false);
        var profile = await response.Content.ReadFromJsonAsync<GameProfile>(Json, ct);
        var textures = profile?.Properties.FirstOrDefault(p => p.Name == "textures")?.Value;
        if (textures is null)
            return (null, false);

        using var doc = JsonDocument.Parse(Convert.FromBase64String(textures));
        if (!doc.RootElement.TryGetProperty("textures", out var t) || !t.TryGetProperty("SKIN", out var skin))
            return (null, false);
        var slim = skin.TryGetProperty("metadata", out var meta) && meta.TryGetProperty("model", out var model) && model.GetString() == "slim";
        return (skin.GetProperty("url").GetString(), slim);
    }

    // ------------------------------------------------------------------ launcher API

    public async Task<IReadOnlyList<LauncherServerDto>> GetServersAsync(AccountSession session, CancellationToken ct = default) =>
        await GetLauncherAsync<List<LauncherServerDto>>(session, "/servers", ct);

    public async Task<BuildManifestDto> GetManifestAsync(AccountSession session, Guid buildId, CancellationToken ct = default) =>
        await GetLauncherAsync<BuildManifestDto>(session, $"/builds/{buildId}/manifest", ct);

    public async Task<AuthlibInjectorDto> GetAuthlibInjectorAsync(CancellationToken ct = default) =>
        await _http.GetFromJsonAsync<AuthlibInjectorDto>(ServerUrl + "/api/launcher/authlib-injector", LumoraJson.Options, ct)
        ?? throw Unexpected();

    /// <summary>Raw Yggdrasil metadata JSON (passed to authlib-injector as prefetched data).</summary>
    public async Task<string> GetYggdrasilMetadataAsync(CancellationToken ct = default) =>
        await _http.GetStringAsync(YggdrasilUrl + "/", ct);

    /// <summary>Downloads a file to <paramref name="target"/>; launcher files need the access token.</summary>
    public async Task DownloadAsync(string url, string target, AccountSession? session, IProgress<long>? bytes, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (session is not null && url.StartsWith(ServerUrl, StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SessionExpiredException("Сессия истекла. Войдите заново.");
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var tmp = target + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var file = File.Create(tmp))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                bytes?.Report(read);
            }
        }
        File.Move(tmp, target, overwrite: true);
    }

    private async Task<T> GetLauncherAsync<T>(AccountSession session, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ServerUrl + "/api/launcher" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new SessionExpiredException("Сессия истекла. Войдите заново.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(LumoraJson.Options, ct) ?? throw Unexpected();
    }

    // ------------------------------------------------------------------ helpers

    private async Task<HttpResponseMessage> PostYggdrasilAsync(string path, object body, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(YggdrasilUrl + path, body, ct);
        }
        catch (HttpRequestException e)
        {
            throw new HttpRequestException($"Сервер {ServerUrl} недоступен. Проверьте интернет и адрес в настройках.", e);
        }
        await EnsureYggdrasilSuccessAsync(response, ct);
        return response;
    }

    private static async Task EnsureYggdrasilSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        ErrorBody? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ErrorBody>(Json, ct);
        }
        catch (JsonException)
        {
        }
        var message = error?.ErrorMessage switch
        {
            null or "" => $"Сервер ответил ошибкой {(int)response.StatusCode}.",
            var m when m.StartsWith("Invalid credentials", StringComparison.Ordinal) => "Неверный логин или пароль.",
            "Invalid token." => "Сессия истекла. Войдите заново.",
            var m => m,
        };
        response.Dispose();
        throw new AuthenticationFailedException(message);
    }

    private static InvalidOperationException Unexpected() => new("Сервер вернул неожиданный ответ.");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record ProfileRef(string Id, string Name);

    private sealed record AuthResponse(string AccessToken, string ClientToken, ProfileRef? SelectedProfile);

    private sealed record Property(string Name, string Value);

    private sealed record GameProfile(string Id, string Name, List<Property> Properties);

    private sealed record ErrorBody([property: JsonPropertyName("error")] string? Error, [property: JsonPropertyName("errorMessage")] string? ErrorMessage);

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }
}
