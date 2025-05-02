using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Configuration;
using System.Collections;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Brush = System.Windows.Media.Brush;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daqifi.Desktop.ViewModels;

public partial class SelectColorDialogViewModel : ObservableObject
{
    #region Private Variables
    private readonly IColorable _colorObject;
    #endregion

    #region Properties
    public ObservableCollection<Brush> AvailableColors
    {
        get
        {
            var availableColors = new ObservableCollection<Brush>();
            foreach (var b in ChannelColorManager.Instance.Brushes) availableColors.Add(b);
            return availableColors;
        }
    }
    #endregion

    #region Constructor
    public SelectColorDialogViewModel(IColorable colorObject)
    {
        _colorObject = colorObject;
    }

    #endregion

    #region Commands
    [RelayCommand]
    private void SelectColor(object selectedItems)
    {
        if (!((IEnumerable)selectedItems).Cast<Brush>().Any()) { return; }

        var selectedColor = ((IEnumerable)selectedItems).Cast<Brush>().ElementAt(0);
        _colorObject.SetColor(selectedColor);
    }
    #endregion
}