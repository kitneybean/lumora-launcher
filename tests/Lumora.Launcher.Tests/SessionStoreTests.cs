using Lumora.Launcher.Core.Api;
using Lumora.Launcher.Core.Services;

namespace Lumora.Launcher.Tests;

public class SessionStoreTests
{
    [Fact]
    public void Round_trips_and_does_not_store_plain_token_on_windows()
    {
        var path = Path.Combine(Path.GetTempPath(), "lumora-session-" + Guid.NewGuid().ToString("N"), "session.dat");
        var store = new SessionStore(path);
        var session = new AccountSession("secret-access-token", "client", Guid.NewGuid(), "Steve", "https://x/skin", true);

        store.Save(session);

        Assert.Equal(session, store.Load());
        if (OperatingSystem.IsWindows())
            Assert.DoesNotContain("secret-access-token", File.ReadAllText(path));

        store.Clear();
        Assert.Null(store.Load());
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void Broken_file_is_ignored()
    {
        var path = Path.Combine(Path.GetTempPath(), "lumora-session-" + Guid.NewGuid().ToString("N") + ".dat");
        File.WriteAllText(path, "garbage");

        Assert.Null(new SessionStore(path).Load());
        Assert.False(File.Exists(path));
    }
}
