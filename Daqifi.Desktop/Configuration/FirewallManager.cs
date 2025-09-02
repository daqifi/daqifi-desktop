using System.Diagnostics;
using System.Windows;
using WindowsFirewallHelper;
using Daqifi.Desktop.Services;

namespace Daqifi.Desktop.Configuration;

public static class FirewallConfiguration
{
    private const string RuleName = "DAQiFi Desktop";
    private static IFirewallHelper _firewallHelper;
    private static IMessageBoxService _messageBoxService;
    private static IAdminChecker _adminChecker;

    static FirewallConfiguration()
    {
        try
        {
            _firewallHelper = new WindowsFirewallWrapper();
        }
        catch (Exception)
        {
            // Try P/Invoke-based implementation as fallback
            try
            {
                _firewallHelper = new NetshFirewallHelper();
            }
            catch (Exception)
            {
                // Final fallback to no-op implementation
                _firewallHelper = new NoOpFirewallHelper();
            }
        }
        _messageBoxService = new WpfMessageBoxService();
        _adminChecker = new WindowsPrincipalAdminChecker();
    }

    // Added: Method to inject service for testing
    public static void SetAdminChecker(IAdminChecker checker)
    {
        _adminChecker = checker;
    }

    // Added: Method to inject service for testing
    public static void SetMessageBoxService(IMessageBoxService service)
    {
        _messageBoxService = service;
    }

    // Made public for test access
    public static void SetFirewallHelper(IFirewallHelper helper)
    {
        _firewallHelper = helper;
    }

    public static void InitializeFirewallRules()
    {
        try
        {
            // Check if running with admin privileges using the service
            if (!_adminChecker.IsCurrentUserAdmin())
            {
                _messageBoxService.Show(
                    "DAQiFi Desktop requires firewall permissions to discover devices on your network. " +
                    "Please run the application as administrator to automatically configure firewall rules, " +
                    "or manually add firewall rules for both private and public networks.",
                    "Firewall Configuration Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var appPath = Process.GetCurrentProcess().MainModule?.FileName;

            // Check if rule already exists
            if (_firewallHelper.RuleExists(RuleName))
            {
                return;
            }

            // Create new rule
            if (appPath != null)
            {
                _firewallHelper.CreateUdpRule(RuleName, appPath);
            }
        }
        catch (Exception ex)
        {
            _messageBoxService.Show(
                "Unable to configure firewall rules automatically. You may need to manually add firewall rules " +
                "for both private and public networks.\n\nError: " + ex.Message,
                "Firewall Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

public interface IFirewallHelper
{
    bool RuleExists(string ruleName);
    void CreateUdpRule(string ruleName, string applicationPath);
}

internal class WindowsFirewallWrapper : IFirewallHelper
{
    public bool RuleExists(string ruleName)
    {
        try
        {
            return FirewallManager.Instance.Rules.Any(r => r.Name == ruleName);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // COM interface not available, assume rule doesn't exist
            return false;
        }
    }

    public void CreateUdpRule(string ruleName, string applicationPath)
    {
        try
        {
            var rule = FirewallManager.Instance.CreateApplicationRule(
                ruleName,
                FirewallAction.Allow,
                applicationPath);

            rule.Direction = FirewallDirection.Inbound;
            rule.Protocol = FirewallProtocol.UDP;
                
            FirewallManager.Instance.Rules.Add(rule);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            throw new NotSupportedException(
                "Windows Firewall COM interface is not available in .NET 9. " +
                "Please configure firewall rules manually.", ex);
        }
    }
}

internal class NetshFirewallHelper : IFirewallHelper
{
    public bool RuleExists(string ruleName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall show rule name=\"{ruleName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;
            
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            
            return process.ExitCode == 0 && !output.Contains("No rules match");
        }
        catch
        {
            return false;
        }
    }

    public void CreateUdpRule(string ruleName, string applicationPath)
    {
        // Create inbound rule
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow program=\"{applicationPath}\" protocol=UDP",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            Verb = "runas" // Request admin privileges
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start netsh process");
        }
        
        process.WaitForExit();
        
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to create firewall rule: {error}");
        }
    }
}

internal class NoOpFirewallHelper : IFirewallHelper
{
    public bool RuleExists(string ruleName)
    {
        // Always return false to indicate rule doesn't exist, 
        // which will trigger manual configuration message
        return false;
    }

    public void CreateUdpRule(string ruleName, string applicationPath)
    {
        // No-op implementation - firewall rules cannot be created
        // This will cause the calling method to show appropriate error message
        throw new NotSupportedException("Firewall COM interface is not available. Please configure firewall rules manually.");
    }
}