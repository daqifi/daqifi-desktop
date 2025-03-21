using System.ComponentModel;

namespace Daqifi.Desktop.DataModel.Common;

public abstract class ObservableObject : INotifyPropertyChanged
{
    #region INotifyPropertyChanged Methods
    public event PropertyChangedEventHandler PropertyChanged;

    protected void NotifyPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}