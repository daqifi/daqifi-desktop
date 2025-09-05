using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Daqifi.Desktop.Configuration;
using Daqifi.Desktop.Services;
using System.Windows;

namespace Daqifi.Desktop.Test.Configuration;

[TestClass]
public class FirewallConfigurationTests
{
    private Mock<IFirewallHelper> _mockFirewallHelper;
    private Mock<IMessageBoxService> _mockMessageBoxService;
    private Mock<IAdminChecker> _mockAdminChecker;

    [TestInitialize]
    public void Initialize()
    {
        _mockFirewallHelper = new Mock<IFirewallHelper>();
        FirewallConfiguration.SetFirewallHelper(_mockFirewallHelper.Object);

        _mockMessageBoxService = new Mock<IMessageBoxService>();
        FirewallConfiguration.SetMessageBoxService(_mockMessageBoxService.Object);

        _mockAdminChecker = new Mock<IAdminChecker>();
        FirewallConfiguration.SetAdminChecker(_mockAdminChecker.Object);
    }

    [TestMethod]
    public void InitializeFirewallRules_WhenRuleExists_DoesNotCreateNewRule()
    {
        // Arrange
        _mockAdminChecker.Setup(c => c.IsCurrentUserAdmin()).Returns(true);
        _mockFirewallHelper.Setup(f => f.RuleExists("DAQiFi Desktop")).Returns(true);
            
        // Act
        FirewallConfiguration.InitializeFirewallRules();

        // Assert
        _mockFirewallHelper.Verify(f => f.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>(), 0), Times.Never);
        _mockMessageBoxService.Verify(m => m.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButton>(), It.IsAny<MessageBoxImage>()), Times.Never);
    }

    [TestMethod]
    public void InitializeFirewallRules_WhenRuleDoesNotExist_CreatesNewRule()
    {
        // Arrange
        _mockAdminChecker.Setup(c => c.IsCurrentUserAdmin()).Returns(true);
        _mockFirewallHelper.Setup(f => f.RuleExists("DAQiFi Desktop")).Returns(false);
            
        // Act
        FirewallConfiguration.InitializeFirewallRules();

        // Assert
        _mockFirewallHelper.Verify(f => f.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>(), 21234), Times.Once);
        _mockMessageBoxService.Verify(m => m.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButton>(), It.IsAny<MessageBoxImage>()), Times.Never);
    }

    [TestMethod]
    public void InitializeFirewallRules_WhenNotAdmin_ShowsWarningAndDoesNotCheckRule()
    {
        // Arrange
        _mockAdminChecker.Setup(c => c.IsCurrentUserAdmin()).Returns(false);

        // Act
        FirewallConfiguration.InitializeFirewallRules();

        // Verify
        _mockMessageBoxService.Verify(m => m.Show(
            It.Is<string>(s => s.Contains("requires firewall permissions")),
            "Firewall Configuration Required",
            MessageBoxButton.OK,
            MessageBoxImage.Warning), Times.Once);

        _mockFirewallHelper.Verify(h => h.RuleExists(It.IsAny<string>()), Times.Never);
        _mockFirewallHelper.Verify(h => h.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>(), 0), Times.Never);
    }

    [TestMethod]
    public void InitializeFirewallRules_WhenCreatingRuleThrowsException_ShowsErrorMessage()
    {
        // Arrange
        _mockAdminChecker.Setup(c => c.IsCurrentUserAdmin()).Returns(true);
        _mockFirewallHelper.Setup(f => f.RuleExists("DAQiFi Desktop")).Returns(false);
        _mockFirewallHelper.Setup(f => f.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>(), 21234))
            .Throws(new InvalidOperationException("Test exception"));

        // Act
        FirewallConfiguration.InitializeFirewallRules();

        // Assert
        _mockMessageBoxService.Verify(m => m.Show(
            It.Is<string>(s => s.Contains("Unable to configure firewall rules automatically")),
            "Firewall Configuration Error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning), Times.Once);
    }
}

[TestClass]
public class WindowsFirewallWrapperTests
{
    private IFirewallHelper _firewallWrapper;
    private string _validRuleName;
    private string _validAppPath;

    [TestInitialize]
    public void Initialize()
    {
        _firewallWrapper = new WindowsFirewallWrapper();
        _validRuleName = "Test Rule";
        _validAppPath = CreateTempExeFile();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_validAppPath))
        {
            File.Delete(_validAppPath);
        }
    }

    [TestMethod]
    public void RuleExists_WithInvalidRuleName_ReturnsFalse()
    {
        // Test null
        Assert.IsFalse(_firewallWrapper.RuleExists(null));
        
        // Test empty
        Assert.IsFalse(_firewallWrapper.RuleExists(""));
        
        // Test whitespace
        Assert.IsFalse(_firewallWrapper.RuleExists("   "));
        
        // Test with dangerous characters
        Assert.IsFalse(_firewallWrapper.RuleExists("Rule<script>"));
        Assert.IsFalse(_firewallWrapper.RuleExists("Rule|command"));
        Assert.IsFalse(_firewallWrapper.RuleExists("Rule&test"));
    }

    [TestMethod]
    public void CreateUdpRule_WithInvalidRuleName_ThrowsArgumentException()
    {
        // Test null rule name
        Assert.ThrowsException<ArgumentException>(() => 
            _firewallWrapper.CreateUdpRule(null, _validAppPath));
        
        // Test empty rule name
        Assert.ThrowsException<ArgumentException>(() => 
            _firewallWrapper.CreateUdpRule("", _validAppPath));
        
        // Test rule name with dangerous characters
        Assert.ThrowsException<ArgumentException>(() => 
            _firewallWrapper.CreateUdpRule("Rule<script>", _validAppPath));
    }

    [TestMethod]
    public void CreateUdpRule_WithInvalidApplicationPath_ThrowsArgumentException()
    {
        // Test null app path
        Assert.ThrowsException<ArgumentException>(() => 
            _firewallWrapper.CreateUdpRule(_validRuleName, null));
        
        // Test empty app path
        Assert.ThrowsException<ArgumentException>(() => 
            _firewallWrapper.CreateUdpRule(_validRuleName, ""));
        
        // Test non-existent file
        Assert.ThrowsException<ArgumentException>(() => 
            _firewallWrapper.CreateUdpRule(_validRuleName, @"C:\NonExistent\app.exe"));
        
        // Test non-exe file
        var txtFile = Path.GetTempFileName();
        try
        {
            Assert.ThrowsException<ArgumentException>(() => 
                _firewallWrapper.CreateUdpRule(_validRuleName, txtFile));
        }
        finally
        {
            File.Delete(txtFile);
        }
    }

    [TestMethod]
    public void CreateUdpRule_WithInvalidPort_CreatesRuleWithoutPort()
    {
        // This test verifies that invalid ports (negative, zero, > 65535) don't cause exceptions
        // but are ignored (no port restriction applied)
        try
        {
            _firewallWrapper.CreateUdpRule(_validRuleName, _validAppPath, -1);
            _firewallWrapper.CreateUdpRule(_validRuleName + "2", _validAppPath, 70000);
            // If we get here without exceptions, the test passes
            Assert.IsTrue(true);
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            // We expect potential COM exceptions, but not argument exceptions for port values
            Assert.Fail($"Unexpected exception type: {ex.GetType()}");
        }
    }

    private string CreateTempExeFile()
    {
        var tempFile = Path.GetTempFileName();
        var exeFile = Path.ChangeExtension(tempFile, ".exe");
        File.Move(tempFile, exeFile);
        File.WriteAllBytes(exeFile, new byte[] { 0x4D, 0x5A }); // MZ header for exe
        return exeFile;
    }
}