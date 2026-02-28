using System.Security.Principal;
using System.Runtime.Versioning;

namespace Membran.MultiTool.Core.Windows;

[SupportedOSPlatform("windows")]
public static class AdminContext
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
