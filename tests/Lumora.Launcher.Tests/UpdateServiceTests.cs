using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lumora.Launcher.Core.Updates;

namespace Lumora.Launcher.Tests;

public class UpdateServiceTests
{
    private sealed class FakeGitHub(Dictionary<string, Func<HttpResponseMessage>> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(routes.TryGetValue(request.RequestUri!.ToString(), out var r) ? r() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open()))
                    w.Write(content);
        return ms.ToArray();
    }

    private static (UpdateService Service, byte[] Archive) Setup(string tag, string? checksumOverride = null, bool prerelease = false)
    {
        var archive = Zip(("Lumora.exe", "new launcher"), ("README.txt", "hi"));
        var sha = checksumOverride ?? Convert.ToHexStringLower(SHA256.HashData(archive));
        var version = tag.TrimStart('v');
        var json = $$"""
            {"tag_name":"{{tag}}","html_url":"https://github.com/o/r/releases/{{tag}}","body":"Что нового","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},
             "assets":[
               {"name":"Lumora-{{version}}-win-x64.zip","browser_download_url":"https://dl/win.zip","size":{{archive.Length}}},
               {"name":"Lumora-{{version}}-linux-x64.zip","browser_download_url":"https://dl/linux.zip","size":1},
               {"name":"checksums.txt","browser_download_url":"https://dl/checksums.txt","size":10}]}
            """;
        var routes = new Dictionary<string, Func<HttpResponseMessage>>
        {
            ["https://api.github.com/repos/o/r/releases/latest"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) },
            ["https://dl/checksums.txt"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{sha}  Lumora-{version}-win-x64.zip\nabc  Lumora-{version}-linux-x64.zip\n") },
            ["https://dl/win.zip"] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) },
        };
        return (new UpdateService(new HttpClient(new FakeGitHub(routes)), () => "o/r"), archive);
    }

    [Fact]
    public async Task Finds_newer_release_for_this_platform_with_checksum()
    {
        var (service, archive) = Setup("v0.3.0");

        var release = await service.CheckAsync(new Version(0, 2, 0), "win-x64");

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 3, 0), release.Version);
        Assert.Equal("Lumora-0.3.0-win-x64.zip", release.AssetName);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(archive)), release.Sha256);
    }

    [Theory]
    [InlineData("v0.3.0", "0.3.0", "win-x64")]   // same version
    [InlineData("v0.3.0", "0.4.0", "win-x64")]   // older
    [InlineData("v0.3.0", "0.2.0", "osx-arm64")] // no archive for the platform
    public async Task Ignores_releases_that_do_not_apply(string tag, string current, string rid)
    {
        var (service, _) = Setup(tag);
        Assert.Null(await service.CheckAsync(Version.Parse(current), rid));
    }

    [Fact]
    public async Task Ignores_prereleases() =>
        Assert.Null(await Setup("v9.0.0", prerelease: true).Service.CheckAsync(new Version(0, 1, 0), "win-x64"));

    [Fact]
    public async Task Downloads_and_unpacks_or_rejects_a_corrupted_archive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lumora-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (good, _) = Setup("v0.3.0");
            var release = (await good.CheckAsync(new Version(0, 1, 0), "win-x64"))!;
            var staging = await good.DownloadAsync(release, dir, null);
            Assert.Equal("new launcher", File.ReadAllText(Path.Combine(staging, "Lumora.exe")));

            var (bad, _) = Setup("v0.3.0", checksumOverride: new string('0', 64));
            var badRelease = (await bad.CheckAsync(new Version(0, 1, 0), "win-x64"))!;
            await Assert.ThrowsAsync<InvalidDataException>(() => bad.DownloadAsync(badRelease, dir, null));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("v0.2.0-beta.1", "0.2.0")]
    [InlineData("0.1.0+abc123", "0.1.0")]
    public void Parses_versions(string text, string expected) => Assert.Equal(Version.Parse(expected), UpdateService.ParseVersion(text));

    [Fact]
    public async Task Missing_repository_is_reported() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UpdateService(new HttpClient(), () => null).CheckAsync());

    /// <summary>Runs the real update script: waits for the process, swaps files, starts the "launcher" again.</summary>
    [Fact]
    public async Task Windows_script_swaps_files_and_restarts()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var root = Path.Combine(Path.GetTempPath(), "lumora-apply-" + Guid.NewGuid().ToString("N"));
        var install = Directory.CreateDirectory(Path.Combine(root, "install")).FullName;
        var staging = Directory.CreateDirectory(Path.Combine(root, "staging")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(install, "launcher.cmd"), "@echo old");
            File.WriteAllText(Path.Combine(install, "user-file.txt"), "keep me");
            // The new "launcher" proves it was started by writing a marker.
            // "exit" closes the console that "start" opens for a .cmd file (a real Lumora.exe has no console).
            File.WriteAllText(Path.Combine(staging, "launcher.cmd"), $"@echo restarted> \"{Path.Combine(root, "marker.txt")}\"\r\n@exit\r\n");

            using var running = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "ping -n 3 127.0.0.1 >nul"]) { CreateNoWindow = true })!;
            UpdateService.ApplyAndRestart(staging, install, "launcher.cmd", running.Id);

            var marker = Path.Combine(root, "marker.txt");
            for (var i = 0; i < 60 && !File.Exists(marker); i++)
                await Task.Delay(250);

            Assert.True(File.Exists(marker), "the updated launcher was not started");
            Assert.Contains("restarted", File.ReadAllText(Path.Combine(install, "launcher.cmd")));
            Assert.Equal("keep me", File.ReadAllText(Path.Combine(install, "user-file.txt")));
        }
        finally
        {
            await Task.Delay(500);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
