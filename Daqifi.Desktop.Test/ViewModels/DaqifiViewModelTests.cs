using System.Collections.ObjectModel;
using Daqifi.Desktop.ViewModels;
using Daqifi.Desktop.Models;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using Daqifi.Desktop.View;
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
            var viewModel = new DaqifiViewModel(dialogServiceMock.Object);
            // Clear and add to the read-only ConnectedDevices collection
            viewModel.ConnectedDevices.Clear();
            foreach (var d in connectedDevices) viewModel.ConnectedDevices.Add(d);
            // Create and add the profile to the read-only profiles collection
            var profile = new Profile { Devices = profileDevices };
            viewModel.profiles.Clear();
            viewModel.profiles.Add(profile);
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
            viewModel.ActivateProfileCommand.Execute(viewModel.SelectedProfile);

            // Assert
            dialogServiceMock.Verify(ds => ds.ShowDialog<ErrorDialog>(viewModel, It.IsAny<ErrorDialogViewModel>()), Times.Once);
        }

        [TestMethod]
        public void ActivateProfile_MissingDevice_ShowsWarning()
        {
            // Arrange
            var connectedDevices = new ObservableCollection<IStreamingDevice>
            {
                Mock.Of<IStreamingDevice>(d => d.DeviceSerialNo == "123" && d.Name == "DeviceA")
            };
            var profileDevices = new ObservableCollection<ProfileDevice>
            {
                new ProfileDevice { DeviceSerialNo = "123", DeviceName = "DeviceA" },
                new ProfileDevice { DeviceSerialNo = "456", DeviceName = "DeviceB" }
            };
            var viewModel = CreateViewModelWithDevices(connectedDevices, profileDevices, out var dialogServiceMock);

            // Act
            viewModel.ActivateProfileCommand.Execute(viewModel.SelectedProfile);

            // Assert
            dialogServiceMock.Verify(ds => ds.ShowDialog<ErrorDialog>(viewModel, It.Is<ErrorDialogViewModel>(vm => vm.ErrorMessage.Contains("not currently connected"))), Times.Once);
        }

        [TestMethod]
        public void ActivateProfile_AllDevicesConnected_NoWarningOrError()
        {
            // Arrange
            var connectedDevices = new ObservableCollection<IStreamingDevice>
            {
                Mock.Of<IStreamingDevice>(d => d.DeviceSerialNo == "123" && d.Name == "DeviceA"),
                Mock.Of<IStreamingDevice>(d => d.DeviceSerialNo == "456" && d.Name == "DeviceB")
            };
            var profileDevices = new ObservableCollection<ProfileDevice>
            {
                new ProfileDevice { DeviceSerialNo = "123", DeviceName = "DeviceA" },
                new ProfileDevice { DeviceSerialNo = "456", DeviceName = "DeviceB" }
            };
            var viewModel = CreateViewModelWithDevices(connectedDevices, profileDevices, out var dialogServiceMock);

            // Act
            viewModel.ActivateProfileCommand.Execute(viewModel.SelectedProfile);

            // Assert
            dialogServiceMock.Verify(ds => ds.ShowDialog<ErrorDialog>(viewModel, It.IsAny<ErrorDialogViewModel>()), Times.Never);
        }
    }
}
