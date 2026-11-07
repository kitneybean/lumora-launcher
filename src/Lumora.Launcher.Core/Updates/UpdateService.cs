using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumora.Launcher.Core.Updates;

/// <summary>A newer launcher release found on GitHub.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, string? Notes, string HtmlUrl, string AssetName, string AssetUrl, long Size, string? Sha256);

/// <summary>
/// Self-update from GitHub Releases. A release (tag <c>v1.2.3</c>) carries one archive per platform,
/// <c>Lumora-1.2.3-win-x64.zip</c>, and <c>checksums.txt</c> with SHA-256 sums (see .github/workflows/release.yml).
/// The update is unpacked next to the launcher and swapped in by a small script after the launcher exits.
/// </summary>
public sealed class UpdateService(HttpClient http, Func<string?> repository)
{
    public const string ChecksumsAsset = "checksums.txt";

    /// <summary>Version of the running launcher (the entry assembly, e.g. Lumora.exe).</summary>
    public static Version CurrentVersion { get; } = ParseVersion(
        (Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly).GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0") ?? new Version(0, 0, 0);

    /// <summary>Repository baked into release builds (-p:LumoraUpdateRepo=owner/name).</summary>
    public static string? BuiltInRepository { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly).GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "LumoraUpdateRepo")?.Value is { Length: > 0 } repo ? repo : null;

    /// <summary>win-x64, linux-x64, osx-arm64 …</summary>
    public static string CurrentRid
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64",
            };
            return $"{os}-{arch}";
        }
    }

    public string? Repository => repository() is { Length: > 0 } r ? r.Trim() : null;

    /// <summary>Latest release newer than <paramref name="current"/> with an archive for this platform, or null.</summary>
    public async Task<ReleaseInfo?> CheckAsync(Version? current = null, string? rid = null, CancellationToken ct = default)
    {
        var repo = Repository ?? throw new InvalidOperationException("Не задан репозиторий обновлений.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.UserAgent.ParseAdd($"LumoraLauncher/{CurrentVersion}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // no releases yet
        response.EnsureSuccessStatusCode();
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct)
            ?? throw new InvalidDataException("Пустой ответ GitHub.");
        return await SelectAsync(release, current ?? CurrentVersion, rid ?? CurrentRid, ct);
    }

    internal async Task<ReleaseInfo?> SelectAsync(GitHubRelease release, Version current, string rid, CancellationToken ct)
    {
        if (release.Draft || release.Prerelease || ParseVersion(release.TagName) is not { } version || version <= current)
            return null;
        var asset = release.Assets.FirstOrDefault(a => a.Name.EndsWith($"-{rid}.zip", StringComparison.OrdinalIgnoreCase));
        if (asset is null)
            return null;

        string? sha256 = null;
        if (release.Assets.FirstOrDefault(a => a.Name == ChecksumsAsset) is { } sums)
            sha256 = ParseChecksums(await http.GetStringAsync(sums.DownloadUrl, ct)).GetValueOrDefault(asset.Name);
        return new ReleaseInfo(version, release.TagName, release.Body, release.HtmlUrl, asset.Name, asset.DownloadUrl, asset.Size, sha256);
    }

    /// <summary>Downloads and unpacks the release next to the launcher; returns the staging folder.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string workDir, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(workDir);
        var zipPath = Path.Combine(workDir, release.AssetName);
        using (var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? release.Size;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var file = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                    progress?.Report((double)done / total);
            }
        }

        if (release.Sha256 is { } expected)
        {
            string actual;
            await using (var stream = File.OpenRead(zipPath))
                actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                throw new InvalidDataException("Обновление скачано с ошибкой (контрольная сумма не совпала). Попробуйте ещё раз.");
            }
        }

        var staging = Path.Combine(workDir, "staging-" + release.Version);
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, staging);
        File.Delete(zipPath);
        return staging;
    }

    /// <summary>True if the launcher can replace its own files (portable install in a writable folder).</summary>
    public static bool CanWrite(string installDir)
    {
        try
        {
            var probe = Path.Combine(installDir, ".lumora-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts a detached script that waits for <paramref name="processId"/> to exit, copies the staged files over
    /// <paramref name="installDir"/> and starts <paramref name="executable"/> again. The caller must exit right after.
    /// </summary>
    public static void ApplyAndRestart(string staging, string installDir, string executable, int processId)
    {
        var scriptDir = Path.GetDirectoryName(staging)!;
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(scriptDir, "apply-update.cmd");
            File.WriteAllText(script, WindowsScript(staging, installDir, executable, processId), Encoding.ASCII);
            start = new ProcessStartInfo("cmd.exe", ["/c", script]) { CreateNoWindow = true, UseShellExecute = false };
        }
        else
        {
            var script = Path.Combine(scriptDir, "apply-update.sh");
            File.WriteAllText(script, UnixScript(staging, installDir, executable, processId));
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            start = new ProcessStartInfo("/bin/sh", [script]) { UseShellExecute = false };
        }
        start.WorkingDirectory = scriptDir;
        Process.Start(start);
    }

    public static string WindowsScript(string staging, string installDir, string executable, int pid) => $"""
        @echo off
        rem Lumora launcher update: wait for the launcher to exit, copy new files, start it again.
        :wait
        tasklist /FI "PID eq {pid}" 2>nul | find " {pid} " >nul && (timeout /t 1 /nobreak >nul & goto wait)
        robocopy "{staging}" "{installDir}" /E /MOVE /R:5 /W:1 /NFL /NDL /NJH /NJS /NP >nul
        if %ERRORLEVEL% GEQ 8 exit /b 1
        start "" "{Path.Combine(installDir, executable)}"
        (goto) 2>nul & del "%~f0"
        """;

    public static string UnixScript(string staging, string installDir, string executable, int pid) => $"""
        #!/bin/sh
        # Lumora launcher update: wait for the launcher to exit, copy new files, start it again.
        while kill -0 {pid} 2>/dev/null; do sleep 1; done
        cp -R "{staging}/." "{installDir}/" || exit 1
        rm -rf "{staging}"
        chmod +x "{Path.Combine(installDir, executable)}"
        nohup "{Path.Combine(installDir, executable)}" >/dev/null 2>&1 &
        rm -f "$0"
        """;

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = text.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['-', '+']);
        if (cut >= 0)
            s = s[..cut];
        return Version.TryParse(s, out var v) ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : null;
    }

    /// <summary>Parses "sha256  filename" lines (sha256sum format).</summary>
    public static Dictionary<string, string> ParseChecksums(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[1].TrimStart('*'))
            .ToDictionary(g => g.Key, g => g.First()[0].ToLowerInvariant());

    internal sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    internal sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
        [property: JsonPropertyName("size")] long Size);
}
