using WoundBound.Game.Figures;

namespace WoundBound.Game.Tests;

/// <summary>Finding the grid drawn on a battlemap picture, so its squares can be laid on the board's.</summary>
public class GridFinderTests
{
    /// <summary>A picture of mottled ground with thin darker grid lines every <paramref name="pitch"/> pixels from (<paramref name="offX"/>, <paramref name="offY"/>).</summary>
    private static float[] Picture(int width, int height, double pitch, double offX, double offY, int seed = 3)
    {
        var random = new Random(seed);
        var pixels = new float[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var art = 0.55f + 0.2f * MathF.Sin(x * 0.013f + y * 0.007f) + (float)(random.NextDouble() - 0.5) * 0.25f;
                bool OnLine(double v, double off) => Math.Abs(((v - off) % pitch + pitch) % pitch) < 1;
                pixels[y * width + x] = OnLine(x, offX) || OnLine(y, offY) ? art - 0.12f : art;
            }
        }

        return pixels;
    }

    [Theory]
    [InlineData(25.0, 3.0, 9.0)]
    [InlineData(36.5, 17.0, 2.0)]
    [InlineData(20.5, 0.0, 11.0)]
    public void ADrawnGrid_IsFound_AtItsOwnSpacing_NotAMultiple(double pitch, double offX, double offY)
    {
        var grid = GridFinder.Find(Picture(600, 800, pitch, offX, offY), 600, 800);

        Assert.NotNull(grid);
        Assert.InRange(grid!.PixelsPerSquare, pitch - 0.3, pitch + 0.3);
        // An offset a whole square along is the same grid.
        double Apart(double a, double b) => Math.Min(Math.Abs(a - b) % pitch, pitch - Math.Abs(a - b) % pitch);
        Assert.InRange(Apart(grid.OffsetX, offX), 0, 1.5);
        Assert.InRange(Apart(grid.OffsetY, offY), 0, 1.5);
    }
}
