using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Camledian.Photobooth.App.Wpf;

/// <summary>Converts ImageSharp frames to frozen WPF BitmapSources. Freezing lets the conversion run
/// on a background thread — only the (cheap) Image.Source assignment needs to happen on the UI
/// thread, which is what keeps the preview pipeline from blocking it (spec §9).</summary>
public static class ImageSharpWpfInterop
{
    public static BitmapSource ToBitmapSource(Image<Rgba32> image)
    {
        using var bgra = image.CloneAs<Bgra32>();
        var stride = bgra.Width * 4;
        var bytes = new byte[stride * bgra.Height];
        bgra.CopyPixelDataTo(bytes);

        var bitmap = BitmapSource.Create(
            bgra.Width, bgra.Height, 96, 96, PixelFormats.Bgra32, null, bytes, stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Loads an encoded image (e.g. the PNG bytes from QrCodeService) into a frozen
    /// BitmapSource, safe to hand to the UI thread from a background sync poll.</summary>
    public static BitmapSource FromEncodedBytes(byte[] encodedImageBytes)
    {
        using var stream = new MemoryStream(encodedImageBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
