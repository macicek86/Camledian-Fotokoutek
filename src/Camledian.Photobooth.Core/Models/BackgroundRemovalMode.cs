namespace Camledian.Photobooth.Core.Models;

/// <summary>How the foreground subject is cut out from the live camera feed.</summary>
public enum BackgroundRemovalMode
{
    GreenScreen,
    Ai,
    Hybrid,

    /// <summary>Reserved for a future heuristic that picks GreenScreen/Ai/Hybrid automatically. Not
    /// implemented yet — selecting it currently falls back to Hybrid.</summary>
    Auto,
}
