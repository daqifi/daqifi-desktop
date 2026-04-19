using OxyPlot;
using OxyPlot.Axes;

namespace Daqifi.Desktop.Helpers;

/// <summary>
/// Dark color palette for OxyPlot models, mirroring /Resources/DesignTokens.xaml
/// so plots read as part of the same visual system as the rest of the app.
/// </summary>
public static class OxyPlotDarkTheme
{
    // Mirrors DesignTokens.xaml
    public static readonly OxyColor Surface = OxyColor.FromRgb(0x0D, 0x0F, 0x12);
    public static readonly OxyColor BorderDim = OxyColor.FromRgb(0x2A, 0x2F, 0x38);
    public static readonly OxyColor TextPrimary = OxyColor.FromRgb(0xF5, 0xF5, 0xF7);
    public static readonly OxyColor TextSecondary = OxyColor.FromRgb(0x8E, 0x91, 0x99);
    public static readonly OxyColor TextTertiary = OxyColor.FromRgb(0x5A, 0x5E, 0x66);
    public static readonly OxyColor Accent = OxyColor.FromRgb(0x11, 0x9E, 0xDA);

    // Gridlines: BorderDim at reduced alpha so they frame without competing with traces
    public static readonly OxyColor Gridline = OxyColor.FromArgb(0x66, 0x2A, 0x2F, 0x38);
    public static readonly OxyColor MinorGridline = OxyColor.FromArgb(0x33, 0x2A, 0x2F, 0x38);

    // Minimap dim overlay: translucent black over unselected regions
    public static readonly OxyColor MinimapDim = OxyColor.FromArgb(0xB0, 0x05, 0x07, 0x09);

    public static void ApplyTo(PlotModel model)
    {
        model.Background = Surface;
        model.PlotAreaBackground = Surface;
        model.PlotAreaBorderColor = BorderDim;
        model.TextColor = TextSecondary;
        model.TitleColor = TextPrimary;
        model.SubtitleColor = TextSecondary;
    }

    public static void ApplyTo(Axis axis)
    {
        axis.AxislineColor = BorderDim;
        axis.TicklineColor = BorderDim;
        axis.MinorTicklineColor = BorderDim;
        axis.MajorGridlineColor = Gridline;
        axis.MinorGridlineColor = MinorGridline;
        axis.TextColor = TextSecondary;
        axis.TitleColor = TextSecondary;
        axis.ExtraGridlineColor = Gridline;
    }
}
