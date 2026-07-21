using System.Windows.Media;

namespace Daqifi.Desktop.Channel;

public class ChannelColorManager
{
    #region Private Data

    private int _colorCount;

    #endregion

    #region Properties


    public List<System.Windows.Media.Brush> Brushes { get; } =
    [
        // 700-shade set — interleaved warm/cool for maximum perceptual separation
        FromHex(MaterialColors.Red700),
        FromHex(MaterialColors.Blue700),
        FromHex(MaterialColors.Green700),
        FromHex(MaterialColors.Orange700),
        FromHex(MaterialColors.Purple700),
        FromHex(MaterialColors.Teal700),
        FromHex(MaterialColors.DeepOranage700),
        FromHex(MaterialColors.Indigo700),
        FromHex(MaterialColors.Pink700),
        FromHex(MaterialColors.LightGreen700),
        FromHex(MaterialColors.Cyan700),
        FromHex(MaterialColors.Amber700),
        FromHex(MaterialColors.DeepPurple700),
        FromHex(MaterialColors.LightBlue700),
        FromHex(MaterialColors.Lime700),
        FromHex(MaterialColors.Yellow700),
        FromHex(MaterialColors.Brown700),
        FromHex(MaterialColors.BlueGrey700),
        FromHex(MaterialColors.Grey700),
        // 500-shade set — same interleaved order
        FromHex(MaterialColors.Red500),
        FromHex(MaterialColors.Blue500),
        FromHex(MaterialColors.Green500),
        FromHex(MaterialColors.Orange500),
        FromHex(MaterialColors.Purple500),
        FromHex(MaterialColors.Teal500),
        FromHex(MaterialColors.DeepOranage500),
        FromHex(MaterialColors.Indigo500),
        FromHex(MaterialColors.Pink500),
        FromHex(MaterialColors.LightGreen500),
        FromHex(MaterialColors.Cyan500),
        FromHex(MaterialColors.Amber500),
        FromHex(MaterialColors.DeepPurple500),
        FromHex(MaterialColors.LightBlue500),
        FromHex(MaterialColors.Lime500),
        FromHex(MaterialColors.Yellow500),
        FromHex(MaterialColors.Brown500),
        FromHex(MaterialColors.BlueGrey500),
        FromHex(MaterialColors.Grey500)
    ];

    #endregion

    #region Private Methods

    /// <summary>
    /// Creates a <see cref="SolidColorBrush"/> from a hex color string.
    /// </summary>
    /// <param name="hex">A hex color literal from <see cref="MaterialColors"/>.</param>
    private static SolidColorBrush FromHex(string hex)
    {
        // ColorConverter is declared to return object? because it accepts arbitrary input, but every
        // caller here passes a compile-time constant from MaterialColors, so the conversion cannot fail.
        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!);
    }

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

    /// <summary>
    /// Parses a <see cref="MaterialColors"/> hex string into a frozen-capable brush.
    /// </summary>
    /// <remarks>
    /// The inputs are compile-time constants that <see cref="BrushConverter"/> always parses,
    /// so the null-forgiving operator is safe here and keeps the palette free of 76 spurious
    /// nullability warnings.
    /// </remarks>
    private static SolidColorBrush FromHex(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;

    public System.Windows.Media.Brush NewColor()
    {
        var newColor = Brushes[_colorCount++ % Brushes.Count];
        return newColor;
    }
}