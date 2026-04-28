using System.ComponentModel;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.ViewModels;
using Moq;

namespace Daqifi.Desktop.Test.ViewModels;

[TestClass]
public class DaqifiViewModelLoggingStateTests
{
    [TestMethod]
    public void IsLogging_ReturnsTrue_WhenConnectedDeviceReportsSdCardLogging()
    {
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.IsLoggingToSdCard).Returns(true);

        viewModel.ConnectedDevices.Add(device.Object);

        Assert.IsTrue(viewModel.IsLogging,
            "IsLogging should reflect device-reported SD logging even when the toggle was never flipped.");
    }

    [TestMethod]
    public void IsSdCardLoggingActive_FollowsDevicePropertyChanged()
    {
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        var isLoggingToSd = false;
        device.SetupGet(d => d.IsLoggingToSdCard).Returns(() => isLoggingToSd);

        viewModel.ConnectedDevices.Add(device.Object);
        Assert.IsFalse(viewModel.IsSdCardLoggingActive);

        var raisedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => raisedProperties.Add(args.PropertyName);

        isLoggingToSd = true;
        device.Raise(d => d.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(IStreamingDevice.IsLoggingToSdCard)));

        Assert.IsTrue(viewModel.IsSdCardLoggingActive,
            "IsSdCardLoggingActive should turn true once the device raises PropertyChanged for IsLoggingToSdCard.");
        CollectionAssert.Contains(raisedProperties, nameof(DaqifiViewModel.IsSdCardLoggingActive));
        CollectionAssert.Contains(raisedProperties, nameof(DaqifiViewModel.IsLogging));
    }

    [TestMethod]
    public void SdLoggingElapsed_ResetsToZero_WhenSdLoggingBegins()
    {
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        var isLoggingToSd = false;
        device.SetupGet(d => d.IsLoggingToSdCard).Returns(() => isLoggingToSd);

        viewModel.ConnectedDevices.Add(device.Object);

        isLoggingToSd = true;
        device.Raise(d => d.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(IStreamingDevice.IsLoggingToSdCard)));

        Assert.AreEqual("00:00:00", viewModel.SdLoggingElapsed,
            "Elapsed time should reset to 00:00:00 the moment SD logging begins.");
    }

    [TestMethod]
    public void RemovingTheLastLoggingDevice_ClearsSdCardLoggingActive()
    {
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.IsLoggingToSdCard).Returns(true);

        viewModel.ConnectedDevices.Add(device.Object);
        Assert.IsTrue(viewModel.IsSdCardLoggingActive);

        viewModel.ConnectedDevices.Remove(device.Object);

        Assert.IsFalse(viewModel.IsSdCardLoggingActive,
            "Once no devices are reporting SD logging, the active flag should clear.");
    }

    [TestMethod]
    public void DevicePropertyChangedForUnrelatedProperty_DoesNotRaiseLoggingState()
    {
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.IsLoggingToSdCard).Returns(false);

        viewModel.ConnectedDevices.Add(device.Object);

        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        device.Raise(d => d.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(IStreamingDevice.MacAddress)));

        CollectionAssert.DoesNotContain(raised, nameof(DaqifiViewModel.IsLogging));
        CollectionAssert.DoesNotContain(raised, nameof(DaqifiViewModel.IsSdCardLoggingActive));
    }

    [TestMethod]
    public void Clear_UnsubscribesPropertyChanged_AndDoesNotLeak()
    {
        // ObservableCollection<T>.Clear() raises a Reset event with OldItems == null;
        // a naive subscribe/unsubscribe handler would leak the original subscription.
        var viewModel = CreateViewModel();
        var device = new Mock<IStreamingDevice>();
        device.SetupGet(d => d.IsLoggingToSdCard).Returns(false);

        viewModel.ConnectedDevices.Add(device.Object);
        viewModel.ConnectedDevices.Clear();

        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        // After Clear(), the device should be unsubscribed — its PropertyChanged
        // events must not bubble up to the view model.
        device.Raise(d => d.PropertyChanged += null,
            new PropertyChangedEventArgs(nameof(IStreamingDevice.IsLoggingToSdCard)));

        CollectionAssert.DoesNotContain(raised, nameof(DaqifiViewModel.IsLogging));
        CollectionAssert.DoesNotContain(raised, nameof(DaqifiViewModel.IsSdCardLoggingActive));
    }

    private static DaqifiViewModel CreateViewModel()
    {
        var dialogService = new Mock<IDialogService>();
        return new DaqifiViewModel(dialogService.Object);
    }
}
