using Daqifi.Desktop.Logger;

namespace Daqifi.Desktop.Test.Loggers;

[TestClass]
public class DeviceLegendGroupTests
{
    #region FormatFrequency
    [TestMethod]
    public void FormatFrequency_NullOrZero_ReturnsEmptyString()
    {
        Assert.AreEqual(string.Empty, DeviceLegendGroup.FormatFrequency(null));
        Assert.AreEqual(string.Empty, DeviceLegendGroup.FormatFrequency(0));
        Assert.AreEqual(string.Empty, DeviceLegendGroup.FormatFrequency(-5));
    }

    [TestMethod]
    public void FormatFrequency_LessThanOneKilohertz_ReturnsHz()
    {
        Assert.AreEqual("1 Hz", DeviceLegendGroup.FormatFrequency(1));
        Assert.AreEqual("100 Hz", DeviceLegendGroup.FormatFrequency(100));
        Assert.AreEqual("999 Hz", DeviceLegendGroup.FormatFrequency(999));
    }

    [TestMethod]
    public void FormatFrequency_ExactKilohertz_ReturnsWholeKHz()
    {
        Assert.AreEqual("1 kHz", DeviceLegendGroup.FormatFrequency(1_000));
        Assert.AreEqual("10 kHz", DeviceLegendGroup.FormatFrequency(10_000));
        Assert.AreEqual("100 kHz", DeviceLegendGroup.FormatFrequency(100_000));
    }

    [TestMethod]
    public void FormatFrequency_FractionalKilohertz_ReturnsFractionalKHz()
    {
        Assert.AreEqual("1.5 kHz", DeviceLegendGroup.FormatFrequency(1_500));
        Assert.AreEqual("2.25 kHz", DeviceLegendGroup.FormatFrequency(2_250));
    }

    [TestMethod]
    public void FormatFrequency_ExactMegahertz_ReturnsWholeMHz()
    {
        Assert.AreEqual("1 MHz", DeviceLegendGroup.FormatFrequency(1_000_000));
        Assert.AreEqual("2 MHz", DeviceLegendGroup.FormatFrequency(2_000_000));
    }

    [TestMethod]
    public void FormatFrequency_FractionalMegahertz_ReturnsFractionalMHz()
    {
        Assert.AreEqual("1.5 MHz", DeviceLegendGroup.FormatFrequency(1_500_000));
    }
    #endregion

    #region SamplingFrequencyHz property
    [TestMethod]
    public void SamplingFrequencyHz_Setter_UpdatesDisplayAndHasFrequency()
    {
        var group = new DeviceLegendGroup("DAQ-12345");

        Assert.IsFalse(group.HasSamplingFrequency);
        Assert.AreEqual(string.Empty, group.SamplingFrequencyDisplay);

        group.SamplingFrequencyHz = 1_000;

        Assert.IsTrue(group.HasSamplingFrequency);
        Assert.AreEqual("1 kHz", group.SamplingFrequencyDisplay);
    }

    [TestMethod]
    public void SamplingFrequencyHz_Setter_RaisesPropertyChangedForDependents()
    {
        var group = new DeviceLegendGroup("DAQ-12345");
        var changed = new HashSet<string>();
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) { changed.Add(e.PropertyName); }
        };

        group.SamplingFrequencyHz = 100;

        Assert.IsTrue(changed.Contains(nameof(DeviceLegendGroup.SamplingFrequencyHz)));
        Assert.IsTrue(changed.Contains(nameof(DeviceLegendGroup.SamplingFrequencyDisplay)));
        Assert.IsTrue(changed.Contains(nameof(DeviceLegendGroup.HasSamplingFrequency)));
    }
    #endregion

    #region TruncatedSerialNo
    [TestMethod]
    public void TruncatedSerialNo_LongSerial_ReturnsLastFourWithEllipsis()
    {
        var group = new DeviceLegendGroup("DAQ-1234567890");
        Assert.AreEqual("...7890", group.TruncatedSerialNo);
    }

    [TestMethod]
    public void TruncatedSerialNo_ShortSerial_ReturnsAsIs()
    {
        var group = new DeviceLegendGroup("ABCD");
        Assert.AreEqual("ABCD", group.TruncatedSerialNo);
    }

    [TestMethod]
    public void TruncatedSerialNo_NullSerial_ReturnsEmpty()
    {
        var group = new DeviceLegendGroup(null);
        Assert.AreEqual(string.Empty, group.TruncatedSerialNo);
    }
    #endregion
}
