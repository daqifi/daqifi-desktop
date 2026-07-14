using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class DaqifiViewModelStreamingFrequencyTests
{
    [TestMethod]
    public void SelectedStreamingFrequency_SeededFromDevice_WhenDeviceConnects()
    {
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.StreamingFrequency).Returns(1);

        viewModel.ConnectedDevices.Add(device.Object);

        Assert.AreEqual(1, viewModel.SelectedStreamingFrequency,
            "RATE chip should reflect the device's actual streaming frequency as soon as it connects, " +
            "not stay at 0 until the FREQUENCY slider is touched (issue #686).");
    }

    [TestMethod]
    public void SelectedStreamingFrequency_ClampedToOne_WhenDeviceReportsZero()
    {
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.StreamingFrequency).Returns(0);

        viewModel.ConnectedDevices.Add(device.Object);

        Assert.AreEqual(1, viewModel.SelectedStreamingFrequency,
            "Seeding must apply the same >=1 floor as the public setter, so an uninitialized/invalid " +
            "device value can't surface as 0 Hz on the chip.");
    }

    [TestMethod]
    public void SelectedStreamingFrequency_NotOverwritten_ByLaterConnectingDevice()
    {
        var viewModel = CreateViewModel();
        var firstDevice = new Mock<IStreamingDevice>();
        firstDevice.SetupGet(d => d.StreamingFrequency).Returns(2);
        viewModel.ConnectedDevices.Add(firstDevice.Object);

        var secondDevice = new Mock<IStreamingDevice>();
        secondDevice.SetupGet(d => d.StreamingFrequency).Returns(5);
        viewModel.ConnectedDevices.Add(secondDevice.Object);

        Assert.AreEqual(2, viewModel.SelectedStreamingFrequency,
            "Once seeded, a second connecting device should not clobber the already-established value.");
    }

    private static DaqifiViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new DaqifiViewModel(dialogService.Object);
    }
}
