﻿using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Device;
using Daqifi.Desktop.DialogService;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

namespace Daqifi.Desktop.ViewModels;

public class AddProfileConfirmationDialogViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    #region Private Variables
    private IStreamingDevice _selectedDevice;
    private int _selectedStreamingFrequency;
    private readonly IDialogService _dialogService;
    #endregion

    #region Properties
    private readonly DaqifiViewModel _daqifiViewModel;
    public ObservableCollection<IStreamingDevice> AvailableDevices { get; } = new ObservableCollection<IStreamingDevice>();
    public ObservableCollection<IChannel> AvailableChannels { get; } = new ObservableCollection<IChannel>();


    public IStreamingDevice SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            _selectedDevice = value;
            OnPropertyChanged();
        }
    }
    private Visibility _saveProfileExisting = Visibility.Collapsed;
    public Visibility SaveProfileExisting
    {
        get => _saveProfileExisting;
        set
        {
            _saveProfileExisting = value;
            OnPropertyChanged();
        }
    }
    public int SelectedStreamingFrequency
    {
        get => _selectedStreamingFrequency;
        set
        {
            if (value < 1) { return; }
            _selectedStreamingFrequency = value;
            OnPropertyChanged();
        }
    }
    #endregion

    #region Constructor
    public AddProfileConfirmationDialogViewModel() : this(ServiceLocator.Resolve<IDialogService>()) { }

    public AddProfileConfirmationDialogViewModel(IDialogService dialogService)
    {
        _dialogService=dialogService;
           
        if (_dialogService != null && _dialogService.Views.Count > 0 && _dialogService.Views[0] is Daqifi.Desktop.MainWindow mainWindow)
        {
            if (mainWindow.DataContext is DaqifiViewModel daqifiViewModel)
            {

                _daqifiViewModel = daqifiViewModel;
                if (_daqifiViewModel != null)
                {
                    if (_daqifiViewModel.ActiveChannels != null && _daqifiViewModel.ActiveChannels.Count > 0)
                    { 
                        SaveProfileExisting = Visibility.Visible;
                    }
                    else 
                    { 
                        SaveProfileExisting = Visibility.Collapsed;
                    }
                }
            }
        }
    }
    #endregion

    #region Command Delegatges
    public ICommand AddNewProfileCommand => new DelegateCommand(AddNewProfileExecute, OnSelectedProfileCanExecute);
    public ICommand ExistingProfileCommand => new DelegateCommand(SaveExistingProfileExecute, OnSelectedProfileCanExecute);

    private bool OnSelectedProfileCanExecute(object o)
    {
        return true;
            
    }

    private void AddNewProfileExecute(object o)
    {
           
        _daqifiViewModel.ShowAddProfileDialog(null);
            

    }
    private void SaveExistingProfileExecute(object o)
    {
          
        _daqifiViewModel.SaveExistingSetting();
            
    }

    #endregion

}