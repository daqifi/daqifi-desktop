using System.Security.Principal;

namespace Daqifi.Desktop.Services;

public interface IAdminChecker
{
    bool IsCurrentUserAdmin();
}

public class WindowsPrincipalAdminChecker : IAdminChecker
{
    public bool IsCurrentUserAdmin()
    {
        // This requires a reference to System.Security.Principal
        // and potentially platform-specific handling if ported.
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            // Handle cases where identity might not be available (rare)
            return false;
        }
    }
}