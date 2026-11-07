using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Contracts;
using Lumora.Launcher.Core.Services;
using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.App.Services.Demo;

/// <summary>Simulates installing and running the game so the UI can be developed before CmlLib integration.</summary>
public sealed class DemoGameService : IGameService
{
    private readonly ConcurrentDictionary<Guid, InstanceState> _states = new()
    {
        [DemoLumoraApi.SurvivalBuild] = InstanceState.Ready,
        [DemoLumoraApi.TechBuild] = InstanceState.NotInstalled,
        [DemoLumoraApi.CreativeBuild] = InstanceState.UpdateAvailable,
    };

    public InstanceState GetState(LauncherBuildSummaryDto build) =>
        _states.GetValueOrDefault(build.Id, InstanceState.NotInstalled);

    public async Task InstallAsync(AccountSession session, LauncherBuildSummaryDto build, IProgress<InstallProgress> progress, CancellationToken ct = default)
    {
        (string Stage, int Steps)[] stages =
        [
            ($"Minecraft {build.MinecraftVersion}", 20),
            ("Java 21", 12),
            (build.Loader == LoaderType.Vanilla ? "Ресурсы игры" : $"NeoForge {build.LoaderVersion}", 15),
            ("Моды сборки", 30),
        ];
        var total = 0;
        foreach (var s in stages)
            total += s.Steps;

        var done = 0;
        foreach (var (stage, steps) in stages)
        {
            for (var i = 1; i <= steps; i++)
            {
                await Task.Delay(70, ct);
                done++;
                var speed = 8 + Random.Shared.NextDouble() * 6;
                progress.Report(new InstallProgress(stage, (double)done / total, $"{i}/{steps} · {speed:0.0} МБ/с"));
            }
        }
        _states[build.Id] = InstanceState.Ready;
    }

    public async Task<int> LaunchAsync(AccountSession session, LauncherServerDto server, GameSettings settings, Action<string> log, CancellationToken ct = default)
    {
        string[] lines =
        [
            $"[Lumora] Java: {settings.JavaPath ?? "встроенная Java 21"}",
            $"[Lumora] Память: {settings.MinMemoryMb}–{settings.MaxMemoryMb} МБ",
            "[Lumora] -javaagent:authlib-injector.jar=<auth>/api/yggdrasil",
            "[main/INFO] [cpw.mods.modlauncher.Launcher/MODLAUNCHER]: ModLauncher running",
            "[main/INFO] [net.neoforged.fml.loading.FMLLoader/CORE]: Loading NeoForge",
            "[main/INFO] [moe.yushi.authlibinjector]: Authentication server: Lumora",
            $"[Render thread/INFO] [minecraft/Minecraft]: Setting user: {session.PlayerName}",
            "[Render thread/INFO] [minecraft/Minecraft]: Backend library: LWJGL version 3.3.3",
            "[Render thread/WARN] [minecraft/ModelManager]: Missing textures in model jei:item",
            $"[Render thread/INFO] [minecraft/ConnectScreen]: Connecting to {server.Address}, {server.Port}",
            "[Render thread/INFO] [minecraft/Minecraft]: Stopping!",
        ];
        foreach (var line in lines)
        {
            await Task.Delay(450, ct);
            log(line);
        }
        return 0;
    }
}

public sealed class DemoServerStatusService : IServerStatusService
{
    public async Task<ServerStatus> GetStatusAsync(string address, int port, CancellationToken ct = default)
    {
        await Task.Delay(200 + Random.Shared.Next(300), ct);
        return address switch
        {
            "play.lumora.net" => new ServerStatus(true, 24, 50, 32, "Добро пожаловать на Lumora"),
            "tech.lumora.net" => new ServerStatus(true, 8, 30, 41, "Техно-сборка"),
            _ => ServerStatus.Offline,
        };
    }
}
