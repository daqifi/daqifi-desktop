using System.Diagnostics.Contracts;

namespace Daqifi.Desktop.WindowViewModelMapping
{
    /// <summary>
    /// Descripes Window-ViewModel mappings used by the dialog service
    /// </summary>
    [ContractClass(typeof(IWindowViewModelMappingsContract))]
    public interface IWindowViewModelMappings
    {
        /// <summary>
        /// Gets the window type based on registered ViewModel type
        /// </summary>
        /// <param name="viewModelType"></param>
        /// <returns></returns>
        Type GetWindowTypeFromViewModelType(Type viewModelType);
    }
}
