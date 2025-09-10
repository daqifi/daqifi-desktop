using Daqifi.Desktop.Device;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Daqifi.Desktop.ViewModels;

public partial class DuplicateDeviceDialogViewModel : ObservableObject
{
    #region Private Variables
    [ObservableProperty]
    private string _deviceSerialNumber;
    
    [ObservableProperty]
    private string _existingInterface;
    
    [ObservableProperty]
    private string _newInterface;
    
    [ObservableProperty]
    private string _message;
    
    [ObservableProperty]
    private string _keepExistingText;
    
    [ObservableProperty]
    private string _switchToNewText;
    #endregion

    #region Properties
    public IStreamingDevice ExistingDevice { get; }
    public IStreamingDevice NewDevice { get; }
    public bool SwitchToNewInterface { get; set; }
    #endregion

    #region Constructor
    public DuplicateDeviceDialogViewModel(IStreamingDevice existingDevice, IStreamingDevice newDevice, string existingInterface, string newInterface)
    {
        ExistingDevice = existingDevice;
        NewDevice = newDevice;
        DeviceSerialNumber = existingDevice.DeviceSerialNo;
        ExistingInterface = existingInterface;
        NewInterface = newInterface;
        
        Message = $"This device (S/N: {DeviceSerialNumber}) is already connected via {ExistingInterface}.\n\n" +
                 "Would you like to:";
        KeepExistingText = $"Keep {ExistingInterface} connection (recommended)";
        SwitchToNewText = $"Switch to {NewInterface} connection";
    }
    #endregion
}