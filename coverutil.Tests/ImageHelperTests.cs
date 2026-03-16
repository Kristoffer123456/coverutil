using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace coverutil.Tests;

public class ImageHelperTests : IDisposable
{
    private readonly string _outputPath = Path.GetTempFileName();

    public void Dispose()
    {
        if (File.Exists(_outputPath)) File.Delete(_outputPath);
    }

    private static byte[] MakePng(int width, int height, Color color)
    {
        using var bmp = new Bitmap(width, height);
        using var g   = Graphics.FromImage(bmp);
        g.Clear(color);
        using var ms  = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Square_Input_OutputIs640x640()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 100, Color.Red), _outputPath);
        using var img = Image.FromFile(_outputPath);
        Assert.Equal(640, img.Width);
        Assert.Equal(640, img.Height);
    }

    [Fact]
    public void Landscape_Input_HasLetterboxBars()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(200, 100, Color.Red), _outputPath);
        using var bmp = new Bitmap(_outputPath);
        Assert.Equal(640, bmp.Width);
        Assert.Equal(640, bmp.Height);
        // Top-centre pixel should be black (letterbox bar above the image)
        var pixel = bmp.GetPixel(320, 0);
        Assert.True(pixel.R < 10 && pixel.G < 10 && pixel.B < 10,
            $"Expected black letterbox bar at (320,0), got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public void Portrait_Input_HasPillarboxBars()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 200, Color.Red), _outputPath);
        using var bmp = new Bitmap(_outputPath);
        Assert.Equal(640, bmp.Width);
        Assert.Equal(640, bmp.Height);
        // Left-centre pixel should be black (pillarbox bar left of the image)
        var pixel = bmp.GetPixel(0, 320);
        Assert.True(pixel.R < 10 && pixel.G < 10 && pixel.B < 10,
            $"Expected black pillarbox bar at (0,320), got R={pixel.R} G={pixel.G} B={pixel.B}");
    }

    [Fact]
    public void Output_IsValidJpeg()
    {
        ImageHelper.ResizeAndSaveAsJpeg(MakePng(100, 100, Color.Blue), _outputPath);
        using var img = Image.FromFile(_outputPath);
        Assert.NotNull(img);
    }
}
