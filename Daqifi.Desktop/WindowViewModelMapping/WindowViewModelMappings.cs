using System;
using System.Collections.Generic;
using Daqifi.Desktop.View;
using Daqifi.Desktop.ViewModels;

namespace Daqifi.Desktop.WindowViewModelMapping
{
    /// <summary>
    /// Class describing the Window-ViewModel _mappings used by the dialog service.
    /// </summary>
    public class WindowViewModelMappings : IWindowViewModelMappings
    {
        private readonly IDictionary<Type, Type> _mappings;


        /// <summary>
        /// Initializes a new instance of the <see cref="WindowViewModelMappings"/> class.
        /// </summary>
        public WindowViewModelMappings()
        {
            _mappings = new Dictionary<Type, Type>
			{
				{ typeof(DaqifiViewModel), typeof(string) },
                { typeof(ConnectionDialogViewModel), typeof(ConnectionDialog) }
            };
        }

        /// <summary>
        /// Gets the window type based on registered ViewModel type.
        /// </summary>
        /// <param name="viewModelType">The type of the ViewModel.</param>
        /// <returns>The window type based on registered ViewModel type.</returns>
        public Type GetWindowTypeFromViewModelType(Type viewModelType)
        {
            return _mappings[viewModelType];
        }
    }
}
