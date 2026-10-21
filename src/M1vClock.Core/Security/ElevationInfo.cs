using System.Security.Principal;

namespace M1vClock.Core.Security;

public sealed record ElevationInfo(bool IsElevated, string UserSid, string UserName)
{
    public static ElevationInfo Current()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return new ElevationInfo(
            principal.IsInRole(WindowsBuiltInRole.Administrator),
            identity.User?.Value ?? "S-1-0-0",
            identity.Name);
    }
}
