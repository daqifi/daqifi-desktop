namespace Daqifi.Desktop.WindowViewModelMapping;

/// <summary>
/// Descripes Window-ViewModel mappings used by the dialog service
/// </summary>
public interface IWindowViewModelMappings
{
    /// <summary>
    /// Gets the window type based on registered ViewModel type
    /// </summary>
    /// <param name="viewModelType"></param>
    /// <returns></returns>
    Type GetWindowTypeFromViewModelType(Type viewModelType);
}