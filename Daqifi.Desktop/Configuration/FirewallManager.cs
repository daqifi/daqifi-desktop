using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Runtime.InteropServices;
using System.IO;
using Daqifi.Desktop.Services;
using Daqifi.Desktop.Common.Loggers;

namespace Daqifi.Desktop.Configuration;

public static class FirewallConfiguration
{
    private static readonly IAppLogger Logger = AppLogger.Instance;
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

            // Validate application path
            if (!IsValidApplicationPath(appPath))
            {
                throw new InvalidOperationException("Invalid or missing application path for firewall rule creation.");
            }

            // Check if rule already exists
            if (_firewallHelper.RuleExists(RuleName))
            {
                return;
            }

            // Create new rule with specific UDP port (21234 is DAQiFi's discovery port)
            _firewallHelper.CreateUdpRule(RuleName, appPath, 21234);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to initialize firewall rules for DAQiFi Desktop");
            _messageBoxService.Show(
                "Unable to configure firewall rules automatically. You may need to manually add firewall rules " +
                "for both private and public networks.\n\nError: " + ex.Message,
                "Firewall Configuration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool IsValidApplicationPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            // Check if file exists and has .exe extension
            return File.Exists(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

public interface IFirewallHelper
{
    bool RuleExists(string ruleName);
    void CreateUdpRule(string ruleName, string applicationPath, int port = 0);
}

public class WindowsFirewallWrapper : IFirewallHelper
{
    private static readonly IAppLogger Logger = AppLogger.Instance;

    private static object GetPolicy()
    {
        var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                   ?? throw new InvalidOperationException("Windows Firewall COM interface not available on this system");
        return Activator.CreateInstance(type)!;
    }

    public bool RuleExists(string ruleName)
    {
        if (!IsValidRuleName(ruleName))
            return false;

        object? policy = null;
        try
        {
            policy = GetPolicy();
            dynamic rules = policy.GetType().InvokeMember("Rules",
                System.Reflection.BindingFlags.GetProperty, null, policy, null);

            try
            {
                _ = rules.GetType().InvokeMember("Item",
                    System.Reflection.BindingFlags.InvokeMethod, null, rules, new object[] { ruleName });
                return true;
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) // ERROR_FILE_NOT_FOUND
            {
                return false;
            }
            catch (System.Reflection.TargetInvocationException ex) when (
                (ex.InnerException is COMException comEx && comEx.HResult == unchecked((int)0x80070002)) ||
                ex.InnerException is FileNotFoundException)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to check firewall rule existence for rule: {ruleName}");
            throw new InvalidOperationException($"Failed to check firewall rule existence: {ex.Message}", ex);
        }
        finally
        {
            if (policy != null && Marshal.IsComObject(policy))
            {
                Marshal.ReleaseComObject(policy);
            }
        }
    }

    public void CreateUdpRule(string ruleName, string applicationPath, int port = 0)
    {
        ValidateInputs(ruleName, applicationPath);

        object? policy = null;
        object? rule = null;

        try
        {
            policy = GetPolicy();
            var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule")
                        ?? throw new InvalidOperationException("Windows Firewall Rule COM interface not available");
            rule = Activator.CreateInstance(ruleType)!;

            // Set rule properties using reflection for better error handling
            SetRuleProperty(rule, "Name", ruleName);
            SetRuleProperty(rule, "ApplicationName", applicationPath);
            SetRuleProperty(rule, "Protocol", 17); // UDP
            SetRuleProperty(rule, "Direction", 1); // Inbound
            SetRuleProperty(rule, "Action", 1);    // Allow
            SetRuleProperty(rule, "Enabled", true);
            SetRuleProperty(rule, "Profiles", 7);  // Domain|Private|Public

            // Set specific port if provided
            if (port is > 0 and <= 65535)
            {
                SetRuleProperty(rule, "LocalPorts", port.ToString(CultureInfo.InvariantCulture));
            }

            // Add rule to policy
            var rules = policy.GetType().InvokeMember("Rules",
                System.Reflection.BindingFlags.GetProperty, null, policy, null);
            rules.GetType().InvokeMember("Add",
                System.Reflection.BindingFlags.InvokeMethod, null, rules, [rule]);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to create firewall rule '{ruleName}' for application: {applicationPath}");
            throw new InvalidOperationException($"Failed to create firewall rule '{ruleName}': {ex.Message}", ex);
        }
        finally
        {
            if (rule != null && Marshal.IsComObject(rule))
            {
                Marshal.ReleaseComObject(rule);
            }
            if (policy != null && Marshal.IsComObject(policy))
            {
                Marshal.ReleaseComObject(policy);
            }
        }
    }

    private static void SetRuleProperty(object rule, string propertyName, object value)
    {
        try
        {
            rule.GetType().InvokeMember(propertyName,
                System.Reflection.BindingFlags.SetProperty, null, rule, [value]);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set firewall rule property '{propertyName}': {ex.Message}", ex);
        }
    }

    private static bool IsValidRuleName(string ruleName)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            return false;

        // Check for potentially dangerous characters that could be used for injection
        var invalidChars = new char[] { '<', '>', '|', '&', ';', '$', '`', '\0', '\r', '\n' };
        return !ruleName.Any(c => invalidChars.Contains(c)) && ruleName.Length <= 255;
    }

    private static void ValidateInputs(string ruleName, string applicationPath)
    {
        if (!IsValidRuleName(ruleName))
            throw new ArgumentException("Invalid firewall rule name. Rule names must be non-empty, under 256 characters, and not contain special characters.", nameof(ruleName));

        if (string.IsNullOrWhiteSpace(applicationPath))
            throw new ArgumentException("Application path cannot be null or empty.", nameof(applicationPath));

        if (!File.Exists(applicationPath))
            throw new ArgumentException($"Application file not found: {applicationPath}", nameof(applicationPath));

        if (!Path.GetExtension(applicationPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Application path must point to an executable (.exe) file.", nameof(applicationPath));
    }
}
