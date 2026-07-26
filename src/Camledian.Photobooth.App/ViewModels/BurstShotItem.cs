using System.Windows.Media.Imaging;

namespace Camledian.Photobooth.App.ViewModels;

/// <summary>One shot of a burst on the selection screen: its index into the raw captured frames
/// (owned by MainViewModel) plus a small frozen thumbnail for display.</summary>
public sealed record BurstShotItem(int Index, BitmapSource Thumbnail)
{
    public string Label => $"{Index + 1}";
}
