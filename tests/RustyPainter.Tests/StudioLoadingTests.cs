using Avalonia.Headless.XUnit;
using RustyPainter.Service;
using RustyPainter.ViewModels;

namespace RustyPainter.Tests;

/// <summary>Loading pictures for real, through Avalonia's decoder, in a headless application.</summary>
public class StudioLoadingTests : IDisposable
{
    // A 1x1 red PNG.
    private const string OnePixelPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rustypainter-tests-{Guid.NewGuid():N}");

    public StudioLoadingTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string WritePng(string name = "pixel.png")
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPng));
        return path;
    }

    /// <summary>A valid 24-bit BMP, two pixels wide and one tall.</summary>
    private string WriteBmp(string name = "two.bmp")
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream))
        {
            writer.Write((byte)'B'); writer.Write((byte)'M');
            writer.Write(62);                 // file size: 54 byte header + 8 bytes of pixels
            writer.Write(0);                  // reserved
            writer.Write(54);                 // where the pixels start
            writer.Write(40);                 // DIB header size
            writer.Write(2);                  // width
            writer.Write(1);                  // height
            writer.Write((short)1);           // planes
            writer.Write((short)24);          // bits per pixel
            writer.Write(0);                  // no compression
            writer.Write(8);                  // pixel data size
            writer.Write(2835); writer.Write(2835);
            writer.Write(0); writer.Write(0);
            writer.Write(new byte[] { 0, 0, 255, 0, 255, 0, 0, 0 });   // red, green, two bytes of row padding
        }

        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    [AvaloniaFact]
    public void Loads_a_png()
    {
        StudioViewModel studio = new(new SurfaceService());

        bool loaded = studio.TryLoad(WritePng());

        Assert.True(loaded);
        Assert.True(studio.HasImage);
        Assert.False(studio.NoImage);
        Assert.Equal("1 x 1", studio.ImageInfo);
        Assert.Equal("pixel.png", studio.ImageName);
        Assert.False(studio.HasError);
    }

    [AvaloniaFact]
    public void Loads_a_bmp()
    {
        StudioViewModel studio = new(new SurfaceService());

        Assert.True(studio.TryLoad(WriteBmp()));
        Assert.Equal("2 x 1", studio.ImageInfo);
    }

    [AvaloniaFact]
    public void Replacing_the_picture_updates_it()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.TryLoad(WritePng());

        studio.TryLoad(WriteBmp());

        Assert.Equal("2 x 1", studio.ImageInfo);
        Assert.Equal("two.bmp", studio.ImageName);
    }

    [AvaloniaFact]
    public void A_failed_replacement_keeps_the_current_picture()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.TryLoad(WritePng());

        bool loaded = studio.TryLoad(Path.Combine(_folder, "funny.gif"));

        Assert.False(loaded);
        Assert.True(studio.HasImage);
        Assert.Equal("pixel.png", studio.ImageName);
        Assert.True(studio.HasError);
    }

    [AvaloniaFact]
    public void A_successful_load_clears_an_earlier_error()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.TryLoad(Path.Combine(_folder, "funny.gif"));
        Assert.True(studio.HasError);

        studio.TryLoad(WritePng());

        Assert.False(studio.HasError);
    }

    [AvaloniaFact]
    public void Removing_the_picture_empties_the_page()
    {
        StudioViewModel studio = new(new SurfaceService());
        studio.TryLoad(WritePng());
        Assert.True(studio.ClearImageCommand.CanExecute(null));

        studio.ClearImageCommand.Execute(null);

        Assert.False(studio.HasImage);
        Assert.Equal("", studio.ImageName);
        Assert.Equal("", studio.ImageInfo);
        Assert.False(studio.ClearImageCommand.CanExecute(null));
    }
}
