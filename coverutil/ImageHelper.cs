using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace coverutil;

public static class ImageHelper
{
    /// <summary>
    /// Resize image bytes to size×size and save as JPEG at the given path.
    /// Non-square inputs are letterboxed/pillarboxed with black bars.
    /// </summary>
    public static void ResizeAndSaveAsJpeg(byte[] imageBytes, string outputPath, int size = 640)
    {
        using var ms     = new MemoryStream(imageBytes);
        using var source = Image.FromStream(ms);
        using var dest   = new Bitmap(size, size);
        using var g      = Graphics.FromImage(dest);

        g.InterpolationMode    = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode        = SmoothingMode.HighQuality;
        g.PixelOffsetMode      = PixelOffsetMode.HighQuality;
        g.CompositingQuality   = CompositingQuality.HighQuality;

        float srcRatio = (float)source.Width / source.Height;
        Rectangle destRect;
        if (srcRatio > 1f)
        {
            int h = (int)(size / srcRatio);
            destRect = new Rectangle(0, (size - h) / 2, size, h);
        }
        else if (srcRatio < 1f)
        {
            int w = (int)(size * srcRatio);
            destRect = new Rectangle((size - w) / 2, 0, w, size);
        }
        else
        {
            destRect = new Rectangle(0, 0, size, size);
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
