using Lumora.Launcher.Core.Java;

namespace Lumora.Launcher.Tests;

public sealed class JavaLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lumora-java-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string FakeJdk(string name, string version)
    {
        var home = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java"), "");
        File.WriteAllLines(Path.Combine(home, "release"), [$"JAVA_VERSION=\"{version}\"", "IMPLEMENTOR=\"Test Vendor\""]);
        return home;
    }

    [Fact]
    public void Finds_fake_jdks_sorted_newest_first()
    {
        FakeJdk("jdk-17", "17.0.12");
        FakeJdk("jdk-21", "21.0.5");
        Directory.CreateDirectory(Path.Combine(_root, "not-a-jdk"));

        var found = JavaLocator.FindInstalled([_root]).Where(j => j.Home.StartsWith(_root, StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Equal([21, 17], found.Select(j => j.Major));
        Assert.Equal("Test Vendor", found[0].Vendor);
    }

    [Theory]
    [InlineData("1.8.0_402", 8)]
    [InlineData("17.0.12", 17)]
    [InlineData("21", 21)]
    public void Parses_major_version(string version, int major) =>
        Assert.Equal(major, new JavaInstallation("h", "e", version, null).Major);
}
