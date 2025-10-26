using Daqifi.Desktop.DataModel.Channel;
using System.Windows.Media;
using ChannelDirection = Daqifi.Core.Channel.ChannelDirection;
using ChannelType = Daqifi.Core.Channel.ChannelType;

namespace Daqifi.Desktop.Channel;

public class ChannelColorManager
{
    #region Private Data

    private int _colorCount;

    #endregion

    #region Properties


    public List<System.Windows.Media.Brush> Brushes { get; } =
    [
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Red700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Red700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Pink700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Purple700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.DeepPurple700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Indigo700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Blue700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.LightBlue700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Cyan700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Teal700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Green700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.LightGreen700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Lime700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Yellow700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Amber700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Orange700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.DeepOranage700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Brown700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Grey700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.BlueGrey700),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Red500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Pink500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Purple500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.DeepPurple500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Indigo500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Blue500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.LightBlue500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Cyan500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Teal500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Green500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.LightGreen500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Lime500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Yellow500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Amber500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Orange500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.DeepOranage500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Brown500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.Grey500),
        (SolidColorBrush)new BrushConverter().ConvertFrom(MaterialColors.BlueGrey500)
    ];

    #endregion

    #region Singleton Constructor / Initalization
    private static readonly ChannelColorManager _instance = new();

    private ChannelColorManager()
    {
        foreach (var brush in Brushes)
        {
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }
        }
    }

    public static ChannelColorManager Instance => _instance;

    #endregion

    public System.Windows.Media.Brush NewColor()
    {
        var newColor = Brushes[_colorCount++ % Brushes.Count];
        return newColor;
    }
}