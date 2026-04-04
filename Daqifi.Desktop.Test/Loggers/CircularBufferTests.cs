using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Test.Loggers;

[TestClass]
public class CircularBufferTests
{
    #region Constructor Tests
    [TestMethod]
    public void Constructor_ZeroCapacity_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CircularBuffer<int>(0));
    }

    [TestMethod]
    public void Constructor_ValidCapacity_CreatesEmptyBuffer()
    {
        var buffer = new CircularBuffer<int>(10);
        Assert.AreEqual(0, buffer.Count);
        Assert.AreEqual(10, buffer.Capacity);
    }
    #endregion

    #region Add and Count Tests
    [TestMethod]
    public void Add_BelowCapacity_IncrementsCount()
    {
        var buffer = new CircularBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        Assert.AreEqual(3, buffer.Count);
    }

    [TestMethod]
    public void Add_AtCapacity_CountStaysAtCapacity()
    {
        var buffer = new CircularBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);
        Assert.AreEqual(3, buffer.Count);
    }
    #endregion

    #region Indexer Tests
    [TestMethod]
    public void Indexer_BelowCapacity_ReturnsCorrectItems()
    {
        var buffer = new CircularBuffer<int>(5);
        buffer.Add(10);
        buffer.Add(20);
        buffer.Add(30);
        Assert.AreEqual(10, buffer[0]);
        Assert.AreEqual(20, buffer[1]);
        Assert.AreEqual(30, buffer[2]);
    }

    [TestMethod]
    public void Indexer_AfterWraparound_ReturnsOldestFirst()
    {
        var buffer = new CircularBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4); // Overwrites 1
        buffer.Add(5); // Overwrites 2

        Assert.AreEqual(3, buffer[0]); // Oldest surviving
        Assert.AreEqual(4, buffer[1]);
        Assert.AreEqual(5, buffer[2]); // Newest
    }

    [TestMethod]
    public void Indexer_OutOfRange_Throws()
    {
        var buffer = new CircularBuffer<int>(5);
        buffer.Add(1);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => buffer[1]);
    }
    #endregion

    #region ToList Tests
    [TestMethod]
    public void ToList_BelowCapacity_ReturnsAllItems()
    {
        var buffer = new CircularBuffer<int>(5);
        buffer.Add(10);
        buffer.Add(20);
        var list = buffer.ToList();
        CollectionAssert.AreEqual(new[] { 10, 20 }, list);
    }

    [TestMethod]
    public void ToList_AfterWraparound_ReturnsInInsertionOrder()
    {
        var buffer = new CircularBuffer<int>(3);
        for (var i = 1; i <= 7; i++)
        {
            buffer.Add(i);
        }

        var list = buffer.ToList();
        CollectionAssert.AreEqual(new[] { 5, 6, 7 }, list);
    }

    [TestMethod]
    public void ToList_Empty_ReturnsEmptyList()
    {
        var buffer = new CircularBuffer<int>(5);
        var list = buffer.ToList();
        Assert.AreEqual(0, list.Count);
    }
    #endregion

    #region Clear Tests
    [TestMethod]
    public void Clear_ResetsCountToZero()
    {
        var buffer = new CircularBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Clear();
        Assert.AreEqual(0, buffer.Count);
    }

    [TestMethod]
    public void Clear_AllowsReuse()
    {
        var buffer = new CircularBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Clear();
        buffer.Add(10);
        Assert.AreEqual(1, buffer.Count);
        Assert.AreEqual(10, buffer[0]);
    }
    #endregion
}
