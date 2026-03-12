using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace coverutil;

public static class ImageHelper
{
    private const int TargetSize = 640;

    /// <summary>
    /// Resize image bytes to 640×640 and save as JPEG at the given path.
    /// </summary>
    public static void ResizeAndSaveAsJpeg(byte[] imageBytes, string outputPath)
    {
        using var ms = new MemoryStream(imageBytes);
        using var source = Image.FromStream(ms);
        using var dest = new Bitmap(TargetSize, TargetSize);
        using var g = Graphics.FromImage(dest);

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;

        // If not square, center with black bars
        float srcRatio = (float)source.Width / source.Height;
        Rectangle destRect;
        if (srcRatio > 1f)
        {
            int h = (int)(TargetSize / srcRatio);
            destRect = new Rectangle(0, (TargetSize - h) / 2, TargetSize, h);
        }
        else if (srcRatio < 1f)
        {
            int w = (int)(TargetSize * srcRatio);
            destRect = new Rectangle((TargetSize - w) / 2, 0, w, TargetSize);
        }
        else
        {
            destRect = new Rectangle(0, 0, TargetSize, TargetSize);
        }

        g.Clear(Color.Black);
        g.DrawImage(source, destRect);

        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);

        dest.Save(outputPath, encoder, encoderParams);
    }
}
