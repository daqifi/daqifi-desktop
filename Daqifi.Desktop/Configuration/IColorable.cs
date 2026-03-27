using Brush = System.Windows.Media.Brush;

namespace Daqifi.Desktop.Configuration;

public interface IColorable
{
    void SetColor(Brush color);
}