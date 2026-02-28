using Membran.MultiTool.Core.Security;

namespace Membran.MultiTool.Tests;

public sealed class PathGuardTests
{
    [Fact]
    public void IsAllowed_ReturnsTrue_ForUserAppDataPath()
    {
        var guard = new PathGuard();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidate = Path.Combine(appData, "Membran.MultiTool.Tests");

        var allowed = guard.IsAllowed(candidate);

        Assert.True(allowed);
    }

    [Fact]
    public void IsAllowed_ReturnsFalse_ForRootSystemPath()
    {
        var guard = new PathGuard();
        var candidate = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Windows", "System32");

        var allowed = guard.IsAllowed(candidate);

        Assert.False(allowed);
    }
}
