using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lumora.Contracts;
using Lumora.Launcher.Core.Services;

namespace Lumora.Launcher.App.Services.Demo;

/// <summary>
/// Offline stand-in for Lumora Auth until the real API exists.
/// Any login works; the password "wrong" simulates a failed login.
/// </summary>
public sealed class DemoLumoraApi : ILumoraApi
{
    public static readonly Guid SurvivalBuild = Guid.Parse("6f1c0f0a-5d2e-4c55-9a59-2a8f0b3d1a01");
    public static readonly Guid TechBuild = Guid.Parse("6f1c0f0a-5d2e-4c55-9a59-2a8f0b3d1a02");
    public static readonly Guid CreativeBuild = Guid.Parse("6f1c0f0a-5d2e-4c55-9a59-2a8f0b3d1a03");

    public async Task<AccountSession> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        await Task.Delay(700, ct);
        if (password == "wrong")
            throw new AuthenticationFailedException("Неверный логин или пароль.");

        var name = login.Contains('@') ? login[..login.IndexOf('@')] : login;
        return new AccountSession("demo-token", "demo-client", Guid.NewGuid(), name, null, SlimModel: false);
    }

    public Task LogoutAsync(AccountSession session, CancellationToken ct = default) => Task.CompletedTask;

    public async Task<IReadOnlyList<LauncherServerDto>> GetServersAsync(AccountSession session, CancellationToken ct = default)
    {
        await Task.Delay(400, ct);
        return
        [
            new(Guid.NewGuid(), "Lumora Survival", "Выживание с экономикой, приватами и ивентами по выходным.",
                "play.lumora.net", 25565, null, true, false,
                new(SurvivalBuild, "Survival", 14, "1.21.1", LoaderType.NeoForge, "21.1.77")),
            new(Guid.NewGuid(), "Lumora Tech", "Индустриальная сборка: Create, Mekanism, AE2 и автоматизация.",
                "tech.lumora.net", 25565, null, true, false,
                new(TechBuild, "Tech", 3, "1.21.1", LoaderType.NeoForge, "21.1.77")),
            new(Guid.NewGuid(), "Lumora Creative", "Творческий режим с WorldEdit для строителей.",
                "creative.lumora.net", 25566, null, true, false,
                new(CreativeBuild, "Creative", 7, "1.21.1", LoaderType.Vanilla, null)),
        ];
    }

    public async Task<BuildManifestDto> GetManifestAsync(AccountSession session, Guid buildId, CancellationToken ct = default)
    {
        await Task.Delay(150, ct);
        var files = new List<BuildFileDto>();
        void Add(string name, FileInstallMode mode, string? display = null) =>
            files.Add(new($"mods/{name}.jar", "0", 1, "", mode, display));

        Add("jei", FileInstallMode.Required);
        Add("appleskin", FileInstallMode.Required);
        Add("jade", FileInstallMode.Required);
        Add("journeymap", FileInstallMode.OptionalOn, "JourneyMap — миникарта");
        Add("iris", FileInstallMode.OptionalOff, "Iris — шейдеры");
        Add("sound-physics", FileInstallMode.OptionalOff, "Sound Physics — реалистичный звук");
        Add("mouse-tweaks", FileInstallMode.OptionalOn, "Mouse Tweaks — удобный инвентарь");

        var count = buildId == TechBuild ? 118 : buildId == CreativeBuild ? 6 : 42;
        for (var i = files.Count; i < count; i++)
            Add($"library-{i}", FileInstallMode.Required);

        return new BuildManifestDto(buildId, "Demo", 1, "1.21.1", LoaderType.NeoForge, "21.1.77", 21,
            buildId == TechBuild ? 6144 : 4096, [], files,
            [new("mods", SyncPolicy.Strict), new("config", SyncPolicy.AddOrUpdate)], []);
    }

    public Task<byte[]?> GetSkinAsync(AccountSession session, CancellationToken ct = default) =>
        Task.FromResult<byte[]?>(DemoSkin.Generate());
}

/// <summary>Draws a simple 64x64 skin in the standard layout so the skin preview has something to show.</summary>
internal static class DemoSkin
{
    public static byte[] Generate()
    {
        using var bitmap = new WriteableBitmap(new PixelSize(64, 64), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            var pixels = new uint[64 * 64];
            void Fill(int x, int y, int w, int h, uint argb)
            {
                for (var yy = y; yy < y + h; yy++)
                    for (var xx = x; xx < x + w; xx++)
                        pixels[yy * 64 + xx] = argb;
            }

            const uint skin = 0xFFC69C7B, hair = 0xFF3B2A1E, shirt = 0xFF2F9E6B, pants = 0xFF2D3A63, shoes = 0xFF2A2A2A, eye = 0xFF1E3A8A;
            Fill(0, 0, 32, 16, skin);         // head (all faces)
            Fill(8, 0, 16, 8, hair);          // head top/bottom
            Fill(0, 8, 32, 2, hair);          // hair line around
            Fill(9, 12, 2, 1, 0xFFFFFFFF);    // eyes
            Fill(13, 12, 2, 1, 0xFFFFFFFF);
            Fill(10, 12, 1, 1, eye);
            Fill(13, 12, 1, 1, eye);
            Fill(11, 14, 2, 1, 0xFF8A5A44);   // mouth
            Fill(16, 16, 24, 16, shirt);      // body
            Fill(40, 16, 16, 16, shirt);      // right arm
            Fill(44, 26, 4, 6, skin);         // right hand
            Fill(32, 48, 16, 16, shirt);      // left arm
            Fill(36, 58, 4, 6, skin);
            Fill(0, 16, 16, 16, pants);       // right leg
            Fill(4, 29, 4, 3, shoes);
            Fill(16, 48, 16, 16, pants);      // left leg
            Fill(20, 61, 4, 3, shoes);

            unsafe
            {
                fixed (uint* src = pixels)
                    Buffer.MemoryCopy(src, (void*)fb.Address, fb.RowBytes * 64, pixels.Length * 4);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
