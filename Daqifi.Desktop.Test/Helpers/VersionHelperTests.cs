using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class VersionHelperTests
{
    #region TryParseVersionInfo Tests

    [TestMethod]
    public void TryParseVersionInfo_ValidFullVersion_ReturnsTrue()
    {
        // Arrange & Act
        var result = VersionHelper.TryParseVersionInfo("1.2.3", out var version);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(1, version.Major);
        Assert.AreEqual(2, version.Minor);
        Assert.AreEqual(3, version.Patch);
        Assert.IsFalse(version.IsPreRelease);
    }

    [TestMethod]
    public void TryParseVersionInfo_VersionWithVPrefix_ReturnsTrue()
    {
        // Arrange & Act
        var result = VersionHelper.TryParseVersionInfo("v2.1.0", out var version);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(2, version.Major);
        Assert.AreEqual(1, version.Minor);
        Assert.AreEqual(0, version.Patch);
    }

    [TestMethod]
    public void TryParseVersionInfo_VersionWithBetaSuffix_ReturnsTrue()
    {
        // Arrange & Act
        var result = VersionHelper.TryParseVersionInfo("1.2.3beta2", out var version);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(1, version.Major);
        Assert.AreEqual(2, version.Minor);
        Assert.AreEqual(3, version.Patch);
        Assert.IsTrue(version.IsPreRelease);
        Assert.AreEqual("beta", version.PreLabel);
        Assert.AreEqual(2, version.PreNumber);
    }

    [TestMethod]
    public void TryParseVersionInfo_VersionWithRcSuffix_ReturnsTrue()
    {
        // Arrange & Act
        var result = VersionHelper.TryParseVersionInfo("2.0.0rc1", out var version);

        // Assert
        Assert.IsTrue(result);
        Assert.AreEqual(2, version.Major);
        Assert.AreEqual(0, version.Minor);
        Assert.AreEqual(0, version.Patch);
        Assert.IsTrue(version.IsPreRelease);
        Assert.AreEqual("rc", version.PreLabel);
        Assert.AreEqual(1, version.PreNumber);
    }

    [TestMethod]
    public void TryParseVersionInfo_EmptyOrNullString_ReturnsFalse()
    {
        // Act & Assert
        Assert.IsFalse(VersionHelper.TryParseVersionInfo("", out _));
        Assert.IsFalse(VersionHelper.TryParseVersionInfo(null, out _));
        Assert.IsFalse(VersionHelper.TryParseVersionInfo("   ", out _));
    }

    [TestMethod]
    public void TryParseVersionInfo_InvalidFormat_ReturnsFalse()
    {
        // Act & Assert
        Assert.IsFalse(VersionHelper.TryParseVersionInfo("abc", out _));
        Assert.IsFalse(VersionHelper.TryParseVersionInfo("1.x.3", out _));
        Assert.IsFalse(VersionHelper.TryParseVersionInfo("1.2.3.4.5", out _));
    }

    #endregion

    #region Compare Tests

    [TestMethod]
    public void Compare_SameVersions_ReturnsZero()
    {
        // Act
        var result = VersionHelper.Compare("1.2.3", "1.2.3");

        // Assert
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void Compare_LeftVersionHigher_ReturnsPositive()
    {
        // Act
        var result = VersionHelper.Compare("2.0.0", "1.9.9");

        // Assert
        Assert.IsTrue(result > 0);
    }

    [TestMethod]
    public void Compare_RightVersionHigher_ReturnsNegative()
    {
        // Act
        var result = VersionHelper.Compare("1.2.3", "1.2.4");

        // Assert
        Assert.IsTrue(result < 0);
    }

    [TestMethod]
    public void Compare_ReleaseVsBeta_ReleaseHigher()
    {
        // Act
        var result = VersionHelper.Compare("1.2.3", "1.2.3beta1");

        // Assert
        Assert.IsTrue(result > 0);
    }

    [TestMethod]
    public void Compare_RcVsBeta_RcHigher()
    {
        // Act
        var result = VersionHelper.Compare("1.2.3rc1", "1.2.3beta1");

        // Assert
        Assert.IsTrue(result > 0);
    }

    [TestMethod]
    public void Compare_BothInvalid_ReturnsZero()
    {
        // Act
        var result = VersionHelper.Compare("invalid", "also-invalid");

        // Assert
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public void Compare_OneInvalid_ValidVersionWins()
    {
        // Act & Assert
        Assert.IsTrue(VersionHelper.Compare("1.0.0", "invalid") > 0);
        Assert.IsTrue(VersionHelper.Compare("invalid", "1.0.0") < 0);
    }

    #endregion

    #region NormalizeVersionString Tests

    [TestMethod]
    public void NormalizeVersionString_ValidVersion_ReturnsNormalized()
    {
        // Act
        var result = VersionHelper.NormalizeVersionString("v1.2.3");

        // Assert
        Assert.AreEqual("1.2.3", result);
    }

    [TestMethod]
    public void NormalizeVersionString_PreReleaseVersion_ReturnsNormalizedWithSuffix()
    {
        // Act
        var result = VersionHelper.NormalizeVersionString("1.2.3beta2");

        // Assert
        Assert.AreEqual("1.2.3beta2", result);
    }

    [TestMethod]
    public void NormalizeVersionString_InvalidVersion_ReturnsNull()
    {
        // Act
        var result = VersionHelper.NormalizeVersionString("invalid");

        // Assert
        Assert.IsNull(result);
    }

    #endregion

    #region VersionInfo Comparison Tests

    [TestMethod]
    public void VersionInfo_CompareTo_MajorVersionDifference()
    {
        // Arrange
        var version1 = VersionHelper.TryParseVersionInfo("2.0.0", out var v1);
        var version2 = VersionHelper.TryParseVersionInfo("1.9.9", out var v2);

        // Act & Assert
        Assert.IsTrue(version1);
        Assert.IsTrue(version2);
        Assert.IsGreaterThan(0, v1.CompareTo(v2));
        Assert.IsLessThan(0, v2.CompareTo(v1));
    }

    [TestMethod]
    public void VersionInfo_CompareTo_MinorVersionDifference()
    {
        // Arrange
        var version1 =VersionHelper.TryParseVersionInfo("1.2.0", out var v1);
        var version2 =VersionHelper.TryParseVersionInfo("1.1.9", out var v2);

        // Act & Assert
        Assert.IsTrue(version1);
        Assert.IsTrue(version2);
        Assert.IsGreaterThan(0, v1.CompareTo(v2));
    }

    [TestMethod]
    public void VersionInfo_CompareTo_PatchVersionDifference()
    {
        // Arrange
        var version1 = VersionHelper.TryParseVersionInfo("1.2.3", out var v1);
        var version2 = VersionHelper.TryParseVersionInfo("1.2.2", out var v2);

        // Act & Assert
        Assert.IsTrue(version1);
        Assert.IsTrue(version2);
        Assert.IsGreaterThan(0, v1.CompareTo(v2));
    }

    [TestMethod]
    public void VersionInfo_CompareTo_PreReleasePrecedence()
    {
        // Arrange
        var version1 = VersionHelper.TryParseVersionInfo("1.0.0alpha1", out var alpha);
        var version2 = VersionHelper.TryParseVersionInfo("1.0.0beta1", out var beta);
        var version3 = VersionHelper.TryParseVersionInfo("1.0.0rc1", out var rc);
        var version4 = VersionHelper.TryParseVersionInfo("1.0.0", out var release);

        // Act & Assert - Release should be highest
        Assert.IsTrue(version1);
        Assert.IsTrue(version2);
        Assert.IsTrue(version3);
        Assert.IsTrue(version4);
        Assert.IsGreaterThan(0, release.CompareTo(rc));
        Assert.IsGreaterThan(0, rc.CompareTo(beta));
        Assert.IsGreaterThan(0, beta.CompareTo(alpha));
    }

    [TestMethod]
    public void VersionInfo_ToString_FormatsCorrectly()
    {
        // Arrange
        var version1 = VersionHelper.TryParseVersionInfo("1.2.3", out var release);
        var version2 = VersionHelper.TryParseVersionInfo("1.2.3beta2", out var preRelease);

        // Act & Assert
        Assert.IsTrue(version1);
        Assert.IsTrue(version2);
        Assert.AreEqual("1.2.3", release.ToString());
        Assert.AreEqual("1.2.3beta2", preRelease.ToString());
    }

    #endregion
}