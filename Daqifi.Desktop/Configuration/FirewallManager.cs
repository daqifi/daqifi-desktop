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
        _firewallHelper = new WindowsFirewallWrapper();
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
        return FirewallManager.Instance.Rules.Any(r => r.Name == ruleName);
    }

    public void CreateUdpRule(string ruleName, string applicationPath)
    {
        var rule = FirewallManager.Instance.CreateApplicationRule(
            ruleName,
            FirewallAction.Allow,
            applicationPath);

        rule.Direction = FirewallDirection.Inbound;
        rule.Protocol = FirewallProtocol.UDP;
            
        FirewallManager.Instance.Rules.Add(rule);
    }
}