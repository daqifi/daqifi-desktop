using System.Diagnostics.Contracts;
using System.Windows;

namespace Daqifi.Desktop.WindowViewModelMapping
{
    [ContractClassFor(typeof(IWindowViewModelMappings))]
    abstract class IWindowViewModelMappingsContract : IWindowViewModelMappings
    {
        public Type GetWindowTypeFromViewModelType(Type viewModelType)
        {
            Contract.Ensures(Contract.Result<Type>().IsSubclassOf(typeof(Window)));

            return default(Type);
        }
    }
}
