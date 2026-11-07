namespace Lumora.Launcher.Core.Java;

/// <summary>A Java installation found on this machine.</summary>
/// <param name="Home">JDK/JRE home folder.</param>
/// <param name="Executable">Path to java(w) executable used to start the game.</param>
/// <param name="Version">Full version, e.g. "21.0.12".</param>
/// <param name="Vendor">Implementor from the release file, if known.</param>
public sealed record JavaInstallation(string Home, string Executable, string Version, string? Vendor)
{
    /// <summary>Major version: "1.8.0_402" gives 8, "21.0.2" gives 21.</summary>
    public int Major
    {
        get
        {
            var parts = Version.Split('.', '_', '-', '+');
            if (parts.Length == 0 || !int.TryParse(parts[0], out var first))
                return 0;
            return first == 1 && parts.Length > 1 && int.TryParse(parts[1], out var second) ? second : first;
        }
    }
}

/// <summary>
/// Finds installed Java runtimes without starting any process: reads the "release" file
/// that every modern JDK/JRE ships in its home folder.
/// </summary>
public static class JavaLocator
{
    public static IReadOnlyList<JavaInstallation> FindInstalled(IEnumerable<string>? extraRoots = null)
    {
        var homes = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var root in CandidateRoots().Concat(extraRoots ?? []))
        {
            if (!Directory.Exists(root))
                continue;
            if (IsJavaHome(root))
                homes.Add(Path.GetFullPath(root));
            foreach (var dir in SafeSubdirectories(root))
            {
                var home = OperatingSystem.IsMacOS() && Directory.Exists(Path.Combine(dir, "Contents", "Home"))
                    ? Path.Combine(dir, "Contents", "Home")
                    : dir;
                if (IsJavaHome(home))
                    homes.Add(Path.GetFullPath(home));
            }
        }

        foreach (var home in HomesFromEnvironment())
            homes.Add(home);

        return homes
            .Select(TryRead)
            .OfType<JavaInstallation>()
            .OrderByDescending(j => j.Major)
            .ThenByDescending(j => j.Version, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads a Java home folder; null if it is not a usable Java installation.</summary>
    public static JavaInstallation? TryRead(string home)
    {
        var exe = ExecutablePath(home);
        if (exe is null)
            return null;

        string? version = null, vendor = null;
        var release = Path.Combine(home, "release");
        if (File.Exists(release))
        {
            foreach (var line in File.ReadLines(release))
            {
                if (line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal))
                    version = Unquote(line["JAVA_VERSION=".Length..]);
                else if (line.StartsWith("IMPLEMENTOR=", StringComparison.Ordinal))
                    vendor = Unquote(line["IMPLEMENTOR=".Length..]);
            }
        }

        return version is null ? null : new JavaInstallation(home, exe, version, vendor);
    }

    private static string? ExecutablePath(string home)
    {
        var bin = Path.Combine(home, "bin");
        string[] names = OperatingSystem.IsWindows() ? ["javaw.exe", "java.exe"] : ["java"];
        return names.Select(n => Path.Combine(bin, n)).FirstOrDefault(File.Exists);
    }

    private static bool IsJavaHome(string dir) => ExecutablePath(dir) is not null;

    private static IEnumerable<string> CandidateRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            string[] vendors =
            [
                "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Zulu", "BellSoft",
                "Amazon Corretto", "Semeru", "OpenJDK", "GraalVM",
            ];
            foreach (var pf in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     }.Where(p => p.Length > 0).Distinct())
            {
                foreach (var v in vendors)
                    yield return Path.Combine(pf, v);
            }
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Library/Java/JavaVirtualMachines";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Java/JavaVirtualMachines");
        }
        else
        {
            yield return "/usr/lib/jvm";
            yield return "/usr/java";
            yield return "/opt/java";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sdkman/candidates/java");
        }
    }

    private static IEnumerable<string> HomesFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome && IsJavaHome(javaHome))
            yield return Path.GetFullPath(javaHome);

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // PATH points at <home>/bin; resolve symlinks like /usr/bin/java -> /usr/lib/jvm/.../bin/java.
            var exe = Path.Combine(entry, OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (!File.Exists(exe))
                continue;
            var target = SafeResolve(exe) ?? exe;
            var home = Path.GetDirectoryName(Path.GetDirectoryName(target));
            if (home is not null && IsJavaHome(home) && File.Exists(Path.Combine(home, "release")))
                yield return Path.GetFullPath(home);
        }
    }

    private static string? SafeResolve(string file)
    {
        try
        {
            return new FileInfo(file).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string root)
    {
        try
        {
            return Directory.GetDirectories(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Unquote(string value) => value.Trim().Trim('"');
}
