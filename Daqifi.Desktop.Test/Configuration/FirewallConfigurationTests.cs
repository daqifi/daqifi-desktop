using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Daqifi.Desktop.Configuration;

namespace Daqifi.Desktop.Test.Configuration
{
    [TestClass]
    public class FirewallConfigurationTests
    {
        [TestMethod]
        public void InitializeFirewallRules_WhenRuleExists_DoesNotCreateNewRule()
        {
            // Arrange
            var mockFirewall = new Mock<IFirewallHelper>();
            mockFirewall.Setup(f => f.RuleExists("DAQiFi Desktop")).Returns(true);
            
            FirewallConfiguration.SetFirewallHelper(mockFirewall.Object);

            // Act
            FirewallConfiguration.InitializeFirewallRules();

            // Assert
            mockFirewall.Verify(f => f.CreateUdpRule(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [TestMethod]
        public void InitializeFirewallRules_WhenRuleDoesNotExist_CreatesNewRule()
        {
            // Arrange
            var mockFirewall = new Mock<IFirewallHelper>();
            mockFirewall.Setup(f => f.RuleExists("DAQiFi Desktop")).Returns(false);
            
            FirewallConfiguration.SetFirewallHelper(mockFirewall.Object);

            // Act
            FirewallConfiguration.InitializeFirewallRules();

            // Assert
            mockFirewall.Verify(
                f => f.CreateUdpRule("DAQiFi Desktop", It.IsAny<string>()), 
                Times.Once);
        }
    }
}
