namespace Camledian.Photobooth.Imaging;

/// <summary>
/// What a single background-removal pass is for. This used to be one <c>bool highQuality</c>, which
/// silently conflated two independent decisions — "spend the heavy model on this" and "this frame
/// must get its own freshly computed mask". The burst picker needs the second without the first, and
/// asking for <c>highQuality: true</c> to get it meant every thumbnail paid for the 176 MB final
/// model. Only the AI provider reads either flag; chroma key and background subtraction recompute
/// from scratch every call regardless.
/// </summary>
/// <param name="UseFinalQualityModel">Use the larger, slower final-render model where one is
/// configured, instead of the small model the live loop runs on.</param>
/// <param name="ForceFreshMask">Bypass the preview inference throttle. Without it, calls that land
/// inside the throttle window reuse the previous mask — correct for consecutive frames of a live
/// camera feed, badly wrong for a set of distinct captured stills.</param>
public readonly record struct BackgroundRemovalOptions(bool UseFinalQualityModel, bool ForceFreshMask)
{
    /// <summary>The live camera loop: cheapest possible, and reusing a mask a few frames old is
    /// exactly the intended behaviour (spec §24 — camera at 30 FPS, AI at ~10-15 FPS).</summary>
    public static BackgroundRemovalOptions LivePreview => new(UseFinalQualityModel: false, ForceFreshMask: false);

    /// <summary>The one-shot render of the photo that actually gets saved (spec §25).</summary>
    public static BackgroundRemovalOptions FinalRender => new(UseFinalQualityModel: true, ForceFreshMask: true);

    /// <summary>A preview of one already-captured still — the burst picker's thumbnails. Needs its
    /// own mask per shot, but not the heavy model: these are shown at a few hundred pixels wide.</summary>
    public static BackgroundRemovalOptions StillPreview => new(UseFinalQualityModel: false, ForceFreshMask: true);
}
