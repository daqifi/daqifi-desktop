using Daqifi.Desktop.Helpers;
using Color = System.Windows.Media.Color;

namespace Daqifi.Desktop.Test.Helpers;

[TestClass]
public class TileBrushesTests
{
    [TestMethod]
    public void Frozen_ParsesHexColor()
    {
        // Arrange & Act
        var brush = TileBrushes.Frozen("#171A20");

        // Assert
        Assert.AreEqual(Color.FromRgb(0x17, 0x1A, 0x20), brush.Color);
    }

    [TestMethod]
    public void Frozen_ReturnsFrozenBrush()
    {
        // Arrange & Act
        var brush = TileBrushes.Frozen("#4A9EFF");

        // Assert
        Assert.IsTrue(brush.IsFrozen);
    }

    [TestMethod]
    public void SharedSurfaceBrushes_AreFrozenWithExpectedColors()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(TileBrushes.SurfaceRaised.IsFrozen);
        Assert.IsTrue(TileBrushes.SurfaceActive.IsFrozen);
        Assert.IsTrue(TileBrushes.BorderDim.IsFrozen);

        Assert.AreEqual(Color.FromRgb(0x17, 0x1A, 0x20), TileBrushes.SurfaceRaised.Color);
        Assert.AreEqual(Color.FromRgb(0x1E, 0x25, 0x30), TileBrushes.SurfaceActive.Color);
        Assert.AreEqual(Color.FromRgb(0x2A, 0x2F, 0x38), TileBrushes.BorderDim.Color);
    }
}
