using Daqifi.Desktop.Channel;
using Daqifi.Desktop.Commands;
using Daqifi.Desktop.Configuration;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace Daqifi.Desktop.ViewModels
{
    public class SelectColorDialogViewModel : ObservableObject
    {
        #region Private Variables
        private readonly IColorable _colorObject;
        #endregion

        #region Properties
        public ObservableCollection<Brush> AvailableColors
        {
            get
            {
                ObservableCollection<Brush> availableColors = new ObservableCollection<Brush>();
                foreach (Brush b in ChannelColorManager.Instance.Brushes) availableColors.Add(b);
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

        #region Command Delegatges
        public ICommand SelectColorCommand
        {
            get { return new DelegateCommand(OnSelectChannelExecute, OnSelectChannelCanExecute); }
        }

        private bool OnSelectChannelCanExecute(object selectedItems)
        {
            return true;
        }

        private void OnSelectChannelExecute(object selectedItems)
        {
            if (!((IEnumerable)selectedItems).Cast<Brush>().Any()) return;

            var selectedColor = ((IEnumerable)selectedItems).Cast<Brush>().ElementAt(0);
            _colorObject.SetColor(selectedColor);
        }
        #endregion
    }
}
