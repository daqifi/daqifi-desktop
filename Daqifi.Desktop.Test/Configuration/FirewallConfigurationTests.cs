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

    [TestInitialize]
    public void Initialize()
    {
        _mockFirewallHelper = new Mock<IFirewallHelper>();
        FirewallConfiguration.SetFirewallHelper(_mockFirewallHelper.Object);

        _mockMessageBoxService = new Mock<IMessageBoxService>();
        FirewallConfiguration.SetMessageBoxService(_mockMessageBoxService.Object);
    }

    [TestMethod]
    public void InitializeFirewallRules_WhenRuleExists_DoesNotCreateNewRule()
    {
        // Arrange
        _mockFirewallHelper.Setup(f => f.RuleExists("DAQiFi Desktop")).Returns(true);
            
        // Act
        FirewallConfiguration.InitializeFirewallRules();

        // Assert
        _mockFirewallHelper.Verify(f => f.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _mockMessageBoxService.Verify(m => m.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButton>(), It.IsAny<MessageBoxImage>()), Times.Never);
    }

    [TestMethod]
    public void InitializeFirewallRules_WhenRuleDoesNotExist_CreatesNewRule()
    {
        // Arrange
        _mockFirewallHelper.Setup(f => f.RuleExists("DAQiFi Desktop")).Returns(false);
            
        // Act
        FirewallConfiguration.InitializeFirewallRules();

        // Assert
        _mockFirewallHelper.Verify(f => f.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        _mockMessageBoxService.Verify(m => m.Show(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageBoxButton>(), It.IsAny<MessageBoxImage>()), Times.Never);
    }

    [TestMethod]
    public void InitializeFirewallRules_WhenNotAdmin_ShowsWarningAndDoesNotCheckRule()
    {
        // Arrange
        // Note: We can't easily mock the admin check (WindowsPrincipal), so we rely on the test runner NOT being elevated.
        // The key is verifying the message box IS shown and the firewall helper is NOT called.

        // Act
        FirewallConfiguration.InitializeFirewallRules();

        // Verify
        _mockMessageBoxService.Verify(m => m.Show(
            It.Is<string>(s => s.Contains("requires firewall permissions")),
            "Firewall Configuration Required",
            MessageBoxButton.OK,
            MessageBoxImage.Warning), Times.Once);

        _mockFirewallHelper.Verify(h => h.RuleExists(It.IsAny<string>()), Times.Never);
        _mockFirewallHelper.Verify(h => h.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}