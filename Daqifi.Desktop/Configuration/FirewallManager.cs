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
    private static INetFwPolicy2 GetPolicy()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                   ?? throw new InvalidOperationException("HNetCfg.FwPolicy2 COM type not available");
        return (INetFwPolicy2)Activator.CreateInstance(type)!;
    }

    public bool RuleExists(string ruleName)
    {
        try
        {
            var policy = GetPolicy();
            try
            {
                var _ = policy.Rules.Item(ruleName);
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
        var policy = GetPolicy();

        var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule")
                    ?? throw new InvalidOperationException("HNetCfg.FWRule COM type not available");
        var rule = (INetFwRule)Activator.CreateInstance(ruleType)!;

        rule.Name = ruleName;
        rule.ApplicationName = applicationPath;
        rule.Protocol = (int)NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_UDP;
        rule.Direction = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_IN;
        rule.Action = NET_FW_ACTION_.NET_FW_ACTION_ALLOW;
        rule.Enabled = true;
        // Apply to all profiles: Domain(1) | Private(2) | Public(4) = 7
        rule.Profiles = 7;

        policy.Rules.Add(rule);
    }
}

// Minimal COM interop definitions for Windows Firewall API
[ComImport, Guid("98325047-C671-4174-8D81-DEFCD3F03186"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetFwPolicy2
{
    int CurrentProfileTypes { get; }
    INetFwRules Rules { get; }
}

[ComImport, Guid("9C4C6277-5027-441E-AFAE-CA1F542DA009"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetFwRules
{
    int Count { get; }
    void Add([In] INetFwRule rule);
    void Remove([MarshalAs(UnmanagedType.BStr)] string name);
    INetFwRule Item([MarshalAs(UnmanagedType.BStr)] string name);
}

[ComImport, Guid("AF230D27-BABA-4E42-ACED-F524F22CFCE2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface INetFwRule
{
    string Name { get; set; }
    string Description { get; set; }
    string ApplicationName { get; set; }
    string ServiceName { get; set; }
    int Protocol { get; set; }
    string LocalPorts { get; set; }
    string RemotePorts { get; set; }
    string LocalAddresses { get; set; }
    string RemoteAddresses { get; set; }
    NET_FW_RULE_DIRECTION_ Direction { get; set; }
    object Interfaces { get; set; }
    string InterfaceTypes { get; set; }
    bool Enabled { get; set; }
    string Grouping { get; set; }
    int Profiles { get; set; }
    bool EdgeTraversal { get; set; }
    NET_FW_ACTION_ Action { get; set; }
}

internal enum NET_FW_IP_PROTOCOL_
{
    NET_FW_IP_PROTOCOL_TCP = 6,
    NET_FW_IP_PROTOCOL_UDP = 17,
}

internal enum NET_FW_RULE_DIRECTION_
{
    NET_FW_RULE_DIR_IN = 1,
    NET_FW_RULE_DIR_OUT = 2,
}

internal enum NET_FW_ACTION_
{
    NET_FW_ACTION_BLOCK = 0,
    NET_FW_ACTION_ALLOW = 1,
}
