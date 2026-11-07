using Lumora.Launcher.Core.Settings;

namespace Lumora.Launcher.Tests;

public class SettingsResolverTests
{
    [Fact]
    public void Uses_defaults_when_nothing_is_set()
    {
        var effective = SettingsResolver.Resolve(new LauncherSettings(), null);

        Assert.Equal(4096, effective.MaxMemoryMb);
        Assert.False(effective.Fullscreen);
    }

    [Fact]
    public void Instance_override_wins_only_when_enabled()
    {
        var global = new LauncherSettings { Game = { MaxMemoryMb = 6144, Fullscreen = true } };
        var instance = new InstanceSettings { Game = { MaxMemoryMb = 8192 } };

        Assert.Equal(6144, SettingsResolver.Resolve(global, instance).MaxMemoryMb);

        instance.OverrideGlobal = true;
        var effective = SettingsResolver.Resolve(global, instance);
        Assert.Equal(8192, effective.MaxMemoryMb);
        Assert.True(effective.Fullscreen); // not overridden -> inherited from global
    }
}
