using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Camledian.Photobooth.App.Converters;

/// <summary>Thumbnail/logo loader for anything shown from a plain file path — plain WPF BitmapImage
/// is simplest here since no ImageSharp processing is needed, just "show this file". Decode width
/// defaults to 320 (background/overlay picker grids); pass a ConverterParameter (e.g. "600") to
/// decode a larger image, such as a hero logo, without it looking blurry.</summary>
public class FilePathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        var decodePixelWidth = parameter is string widthText && int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            ? width
            : 320;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = decodePixelWidth;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
