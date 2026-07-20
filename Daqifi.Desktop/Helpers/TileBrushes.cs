using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Daqifi.Desktop.Helpers;

/// <summary>
/// Single source of truth for the frozen brushes used by the device and channel tiles.
/// Centralizes the byte-identical "parse hex, freeze" idiom and the shared surface/border
/// palette that both tile view-models previously duplicated.
/// </summary>
internal static class TileBrushes
{
    /// <summary>
    /// Creates a <see cref="SolidColorBrush"/> from a hex color string and freezes it so the
    /// brush can be shared across threads and never triggers change tracking.
    /// </summary>
    /// <param name="hex">A hex color string (e.g. <c>"#171A20"</c>).</param>
    /// <returns>A frozen <see cref="SolidColorBrush"/> for the parsed color.</returns>
    public static SolidColorBrush Frozen(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Raised tile surface (default background).</summary>
    public static readonly SolidColorBrush SurfaceRaised = Frozen("#171A20");

    /// <summary>Active tile surface (background while a channel is streaming).</summary>
    public static readonly SolidColorBrush SurfaceActive = Frozen("#1E2530");

    /// <summary>Dim border color used when a tile is inactive/disconnected.</summary>
    public static readonly SolidColorBrush BorderDim = Frozen("#2A2F38");
}
