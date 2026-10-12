using RustyPainter.Imaging;
using Xunit;

namespace RustyPainter.Tests;

public class SurfaceBackdropTests
{
    [Theory]
    [InlineData("wood")]
    [InlineData("canvas")]
    [InlineData("frame")]
    [InlineData("neon")]
    [InlineData("pumpkin")]
    [InlineData("stone")]
    [InlineData(null)]
    [InlineData("nonsense")]
    public void Every_kind_makes_an_opaque_picture_of_the_asked_size(string? kind)
    {
        PixelBuffer backdrop = SurfaceBackdrop.Create(kind, 40, 30);

        Assert.Equal(40, backdrop.Width);
        Assert.Equal(30, backdrop.Height);
        for (int i = 3; i < backdrop.Data.Length; i += 4)
            Assert.Equal(255, backdrop.Data[i]);
    }

    [Fact]
    public void The_same_size_always_gives_the_same_picture()
        => Assert.Equal(SurfaceBackdrop.Create("wood", 50, 50).Data, SurfaceBackdrop.Create("wood", 50, 50).Data);

    [Fact]
    public void The_kinds_look_different()
    {
        double Mean(string kind)
        {
            PixelBuffer b = SurfaceBackdrop.Create(kind, 64, 64);
            double sum = 0;
            for (int i = 0; i < b.Data.Length; i += 4)
                sum += b.Data[i] + b.Data[i + 1] + b.Data[i + 2];
            return sum / (b.Data.Length / 4);
        }

        Assert.True(Mean("canvas") > Mean("wood"));
        Assert.True(Mean("wood") > Mean("neon"));
    }
}
