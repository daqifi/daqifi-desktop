using System.Collections.ObjectModel;
using Daqifi.Desktop.ViewModels;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.Device;
using Moq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daqifi.Desktop.Test.ViewModels
{
    [TestClass]
    public class DaqifiViewModelTests
    {
        private DaqifiViewModel CreateViewModelWithDevices(
            ObservableCollection<IStreamingDevice> connectedDevices,
            ObservableCollection<ProfileDevice> profileDevices,
            out Mock<IDialogService> dialogServiceMock)
        {
            dialogServiceMock = new Mock<IDialogService>();
            var viewModel = new DaqifiViewModel(dialogServiceMock.Object)
            {
                ConnectedDevices = connectedDevices,
                profiles = new ObservableCollection<Profile>()
            };
            var profile = new Profile
            {
                Devices = profileDevices
            };
            viewModel.SelectedProfile = profile;
            return viewModel;
        }

        [TestMethod]
        public void ActivateProfile_NoDevices_ShowsError()
        {
            // Arrange
            var connectedDevices = new ObservableCollection<IStreamingDevice>();
            var profileDevices = new ObservableCollection<ProfileDevice>
            {
                new ProfileDevice { DeviceSerialNo = "123", DeviceName = "DeviceA" }
            };
            var viewModel = CreateViewModelWithDevices(connectedDevices, profileDevices, out var dialogServiceMock);

            // Act
            viewModel.ActivateProfile(viewModel.SelectedProfile);

            // Assert
            dialogServiceMock.Verify(ds => ds.ShowDialog<ErrorDialog>(viewModel, It.IsAny<ErrorDialogViewModel>()), Times.Once);
        }

        [TestMethod]
        public void ActivateProfile_MissingDevice_ShowsWarning()
        {
            // Arrange
            var connectedDevices = new ObservableCollection<IStreamingDevice>
            {
                Mock.Of<IStreamingDevice>(d => d.DeviceSerialNo == "123" && d.DeviceName == "DeviceA")
            };
            var profileDevices = new ObservableCollection<ProfileDevice>
            {
                new ProfileDevice { DeviceSerialNo = "123", DeviceName = "DeviceA" },
                new ProfileDevice { DeviceSerialNo = "456", DeviceName = "DeviceB" }
            };
            var viewModel = CreateViewModelWithDevices(connectedDevices, profileDevices, out var dialogServiceMock);

            // Act
            viewModel.ActivateProfile(viewModel.SelectedProfile);

            // Assert
            dialogServiceMock.Verify(ds => ds.ShowDialog<ErrorDialog>(viewModel, It.Is<ErrorDialogViewModel>(vm => vm.Message.Contains("not currently connected"))), Times.Once);
        }

        [TestMethod]
        public void ActivateProfile_AllDevicesConnected_NoWarningOrError()
        {
            // Arrange
            var connectedDevices = new ObservableCollection<IStreamingDevice>
            {
                Mock.Of<IStreamingDevice>(d => d.DeviceSerialNo == "123" && d.DeviceName == "DeviceA"),
                Mock.Of<IStreamingDevice>(d => d.DeviceSerialNo == "456" && d.DeviceName == "DeviceB")
            };
            var profileDevices = new ObservableCollection<ProfileDevice>
            {
                new ProfileDevice { DeviceSerialNo = "123", DeviceName = "DeviceA" },
                new ProfileDevice { DeviceSerialNo = "456", DeviceName = "DeviceB" }
            };
            var viewModel = CreateViewModelWithDevices(connectedDevices, profileDevices, out var dialogServiceMock);

            // Act
            viewModel.ActivateProfile(viewModel.SelectedProfile);

            // Assert
            dialogServiceMock.Verify(ds => ds.ShowDialog<ErrorDialog>(viewModel, It.IsAny<ErrorDialogViewModel>()), Times.Never);
        }
    }
}
