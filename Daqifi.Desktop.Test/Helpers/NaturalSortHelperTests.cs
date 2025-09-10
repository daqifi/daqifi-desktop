using Daqifi.Desktop.Helpers;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class NaturalSortHelperTests
{
    #region NaturalCompare Tests

    [TestMethod]
    public void NaturalCompare_ChannelNames_SortsCorrectly()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI0", "AI1") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI1", "AI2") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI2", "AI10") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI9", "AI10") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI10", "AI11") < 0);
    }

    [TestMethod]
    public void NaturalCompare_MixedChannelTypes_SortsCorrectly()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI0", "DI0") < 0); // AI comes before DI alphabetically
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("DI1", "DI10") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI10", "DI1") < 0);
    }

    [TestMethod]
    public void NaturalCompare_EqualStrings_ReturnsZero()
    {
        // Arrange & Act & Assert
        Assert.AreEqual(0, NaturalSortHelper.NaturalCompare("AI5", "AI5"));
        Assert.AreEqual(0, NaturalSortHelper.NaturalCompare("DI10", "DI10"));
        Assert.AreEqual(0, NaturalSortHelper.NaturalCompare("test", "test"));
    }

    [TestMethod]
    public void NaturalCompare_NullValues_HandlesCorrectly()
    {
        // Arrange & Act & Assert
        Assert.AreEqual(0, NaturalSortHelper.NaturalCompare(null, null));
        Assert.IsTrue(NaturalSortHelper.NaturalCompare(null, "AI0") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI0", null) > 0);
    }

    [TestMethod]
    public void NaturalCompare_PurelyAlphabetic_SortsAlphabetically()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("apple", "banana") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("zebra", "apple") > 0);
    }

    [TestMethod]
    public void NaturalCompare_PurelyNumeric_SortsNumerically()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("1", "2") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("9", "10") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("100", "99") > 0);
    }

    [TestMethod]
    public void NaturalCompare_DifferentLengths_HandlesCorrectly()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI", "AI0") < 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("AI0", "AI") > 0);
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("A1B", "A1B2") < 0);
    }

    [TestMethod]
    public void NaturalCompare_CaseInsensitive_HandlesCorrectly()
    {
        // Arrange & Act & Assert
        Assert.AreEqual(0, NaturalSortHelper.NaturalCompare("ai0", "AI0"));
        Assert.IsTrue(NaturalSortHelper.NaturalCompare("ai1", "AI10") < 0);
    }

    #endregion

    #region NaturalOrderBy Tests

    [TestMethod]
    public void NaturalOrderBy_ChannelNames_SortsCorrectly()
    {
        // Arrange
        var channelNames = new[] { "AI10", "AI2", "AI1", "AI11", "AI0", "AI3", "AI15", "AI14" };
        var expected = new[] { "AI0", "AI1", "AI2", "AI3", "AI10", "AI11", "AI14", "AI15" };

        // Act
        var sorted = channelNames.NaturalOrderBy(name => name).ToArray();

        // Assert
        CollectionAssert.AreEqual(expected, sorted);
    }

    [TestMethod]
    public void NaturalOrderBy_MixedChannelTypes_SortsCorrectly()
    {
        // Arrange
        var channelNames = new[] { "DI10", "AI2", "DI1", "AI11", "DI0", "AI0", "DI2", "AI1" };
        var expected = new[] { "AI0", "AI1", "AI2", "AI11", "DI0", "DI1", "DI2", "DI10" };

        // Act
        var sorted = channelNames.NaturalOrderBy(name => name).ToArray();

        // Assert
        CollectionAssert.AreEqual(expected, sorted);
    }

    [TestMethod]
    public void NaturalOrderBy_ObjectsWithNameProperty_SortsCorrectly()
    {
        // Arrange
        var channels = new[]
        {
            new TestChannel { Name = "AI10" },
            new TestChannel { Name = "AI2" },
            new TestChannel { Name = "AI1" },
            new TestChannel { Name = "AI11" },
            new TestChannel { Name = "AI0" },
            new TestChannel { Name = "AI3" }
        };
        var expectedOrder = new[] { "AI0", "AI1", "AI2", "AI3", "AI10", "AI11" };

        // Act
        var sorted = channels.NaturalOrderBy(c => c.Name).ToArray();
        var actualOrder = sorted.Select(c => c.Name).ToArray();

        // Assert
        CollectionAssert.AreEqual(expectedOrder, actualOrder);
    }

    [TestMethod]
    public void NaturalOrderBy_EmptyCollection_ReturnsEmpty()
    {
        // Arrange
        var empty = new string[0];

        // Act
        var sorted = empty.NaturalOrderBy(name => name).ToArray();

        // Assert
        Assert.AreEqual(0, sorted.Length);
    }

    [TestMethod]
    public void NaturalOrderBy_SingleItem_ReturnsSingleItem()
    {
        // Arrange
        var single = new[] { "AI5" };

        // Act
        var sorted = single.NaturalOrderBy(name => name).ToArray();

        // Assert
        Assert.AreEqual(1, sorted.Length);
        Assert.AreEqual("AI5", sorted[0]);
    }

    [TestMethod]
    public void NaturalOrderBy_RealWorldChannelScenario_SortsCorrectly()
    {
        // Arrange - Simulate a real device with 16 analog inputs
        var channels = new[]
        {
            "AI15", "AI7", "AI3", "AI11", "AI1", "AI13", "AI5", "AI9",
            "AI0", "AI8", "AI4", "AI12", "AI2", "AI14", "AI6", "AI10"
        };
        var expected = new[]
        {
            "AI0", "AI1", "AI2", "AI3", "AI4", "AI5", "AI6", "AI7",
            "AI8", "AI9", "AI10", "AI11", "AI12", "AI13", "AI14", "AI15"
        };

        // Act
        var sorted = channels.NaturalOrderBy(name => name).ToArray();

        // Assert
        CollectionAssert.AreEqual(expected, sorted);
    }

    #endregion

    #region CreateNaturalComparer Tests

    [TestMethod]
    public void CreateNaturalComparer_WithKeySelector_SortsCorrectly()
    {
        // Arrange
        var channels = new[]
        {
            new TestChannel { Name = "AI10" },
            new TestChannel { Name = "AI1" },
            new TestChannel { Name = "AI2" }
        };

        // Act
        var comparer = NaturalSortHelper.CreateNaturalComparer<TestChannel>(c => c.Name);
        Array.Sort(channels, comparer);

        // Assert
        Assert.AreEqual("AI1", channels[0].Name);
        Assert.AreEqual("AI2", channels[1].Name);
        Assert.AreEqual("AI10", channels[2].Name);
    }

    #endregion

    #region Helper Classes

    private class TestChannel
    {
        public string Name { get; set; } = string.Empty;
    }

    #endregion
}