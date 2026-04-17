using System.Collections.ObjectModel;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using UserControl = System.Windows.Controls.UserControl;

namespace Daqifi.Desktop.View.Prototype;

public partial class ChannelsPanePrototype : UserControl
{
    private static readonly Brush AnalogAccent = MakeBrush("#4A9EFF");
    private static readonly Brush DigitalInAccent = MakeBrush("#4ADE80");
    private static readonly Brush DigitalOutAccent = MakeBrush("#F59E0B");
    private static readonly Brush SurfaceRaised = MakeBrush("#171A20");
    private static readonly Brush SurfaceActive = MakeBrush("#1E2530");
    private static readonly Brush BorderDim = MakeBrush("#2A2F38");

    public ObservableCollection<ChannelTileVm> AnalogInputs { get; } = [];
    public ObservableCollection<ChannelTileVm> DigitalInputs { get; } = [];
    public ObservableCollection<ChannelTileVm> DigitalOutputs { get; } = [];

    public int ActiveAnalogCount { get; private set; }
    public int TotalAnalogCount => AnalogInputs.Count;
    public int ActiveDigitalInCount { get; private set; }
    public int TotalDigitalInCount => DigitalInputs.Count;
    public int ActiveDigitalOutCount { get; private set; }
    public int TotalDigitalOutCount => DigitalOutputs.Count;
    public int TotalActive => ActiveAnalogCount + ActiveDigitalInCount + ActiveDigitalOutCount;

    public ChannelsPanePrototype()
    {
        InitializeComponent();
        SeedMockData();
        DataContext = this;
    }

    private void SeedMockData()
    {
        double[] mockVoltages = [3.241, 1.804, 4.998, 0.412];
        for (var i = 0; i < 16; i++)
        {
            var active = i < mockVoltages.Length;
            AnalogInputs.Add(Tile(
                name: $"AI{i}",
                typeLabel: "ANALOG IN",
                active: active,
                value: active ? $"{mockVoltages[i]:F3} V" : null,
                stripe: AnalogAccent));
        }
        ActiveAnalogCount = mockVoltages.Length;

        for (var i = 0; i < 16; i++)
        {
            var active = i < 2;
            DigitalInputs.Add(Tile(
                name: $"DIO{i}",
                typeLabel: "DIGITAL IN",
                active: active,
                value: active ? (i % 2 == 0 ? "HIGH" : "LOW") : null,
                stripe: DigitalInAccent));
        }
        ActiveDigitalInCount = 2;

        for (var i = 0; i < 8; i++)
        {
            DigitalOutputs.Add(Tile(
                name: $"DO{i}",
                typeLabel: "DIGITAL OUT",
                active: false,
                value: null,
                stripe: DigitalOutAccent));
        }
    }

    private static ChannelTileVm Tile(string name, string typeLabel, bool active, string? value, Brush stripe)
    {
        return new ChannelTileVm
        {
            Name = name,
            TypeLabel = typeLabel,
            IsActive = active,
            Value = value,
            StripeBrush = stripe,
            TileBackground = active ? SurfaceActive : SurfaceRaised,
            TileBorderBrush = active ? stripe : BorderDim,
        };
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

public class ChannelTileVm
{
    public string Name { get; set; } = "";
    public string TypeLabel { get; set; } = "";
    public bool IsActive { get; set; }
    public string? Value { get; set; }
    public Brush StripeBrush { get; set; } = Brushes.Gray;
    public Brush TileBackground { get; set; } = Brushes.Transparent;
    public Brush TileBorderBrush { get; set; } = Brushes.Gray;
}
