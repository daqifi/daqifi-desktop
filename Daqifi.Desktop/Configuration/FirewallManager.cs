using System.Diagnostics;
using System.Windows;
using System.Runtime.InteropServices;
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
    private static object GetPolicy()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                   ?? throw new InvalidOperationException("HNetCfg.FwPolicy2 COM type not available");
        return Activator.CreateInstance(type)!;
    }

    public bool RuleExists(string ruleName)
    {
        try
        {
            dynamic policy = GetPolicy();
            dynamic rules = policy.Rules;
            try
            {
                var _ = rules.Item(ruleName);
                return true;
            }
            catch (COMException)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public void CreateUdpRule(string ruleName, string applicationPath)
    {
        dynamic policy = GetPolicy();
        var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule")
                    ?? throw new InvalidOperationException("HNetCfg.FWRule COM type not available");
        dynamic rule = Activator.CreateInstance(ruleType)!;

        rule.Name = ruleName;
        rule.ApplicationName = applicationPath;
        rule.Protocol = 17; // UDP
        rule.Direction = 1; // Inbound
        rule.Action = 1;    // Allow
        rule.Enabled = true;
        rule.Profiles = 7;  // Domain|Private|Public

        policy.Rules.Add(rule);
    }
}
