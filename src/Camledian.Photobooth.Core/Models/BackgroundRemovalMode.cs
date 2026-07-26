namespace Camledian.Photobooth.Core.Models;

/// <summary>How the foreground subject is cut out from the live camera feed.</summary>
public enum BackgroundRemovalMode
{
    GreenScreen,
    Ai,
    Hybrid,

    /// <summary>Compares each frame against a one-time reference photo of the empty scene (no green
    /// screen needed — works for any static background, since the booth/camera don't move during an
    /// event). Falls back to Green Screen with a notice until a reference photo has been captured.</summary>
    BackgroundSubtraction,

    /// <summary>Background Subtraction combined with AI — the no-green-screen counterpart to
    /// <see cref="Hybrid"/>. Falls back to Green Screen with a notice if either the reference photo
    /// or the AI model is missing.</summary>
    BackgroundSubtractionHybrid,

    /// <summary>Reserved for a future heuristic that picks the best mode automatically. Not
    /// implemented yet — selecting it currently falls back to Hybrid.</summary>
    Auto,
}
