namespace Camledian.Photobooth.Core.Models;

/// <summary>Normalized (0-1) placement rectangle within the output canvas.</summary>
public readonly record struct PlacementRect(double X, double Y, double Width, double Height)
{
    public static PlacementRect FullFrame => new(0, 0, 1, 1);
}

/// <summary>
/// Decouples the final photo's pixel dimensions from the camera's capture resolution, per spec
/// section 29. A template says how big the output canvas is and where background/foreground/overlay
/// land within it (all normalized so any camera resolution maps cleanly onto any output size).
/// </summary>
public class OutputTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }
    public PlacementRect BackgroundPlacement { get; init; } = PlacementRect.FullFrame;
    public PlacementRect ForegroundPlacement { get; init; } = PlacementRect.FullFrame;
    public PlacementRect OverlayPlacement { get; init; } = PlacementRect.FullFrame;

    public static OutputTemplate DigitalLandscape => new()
    {
        Id = "digital-landscape",
        Name = "Digital 16:9",
        WidthPx = 1920,
        HeightPx = 1080,
    };

    public static OutputTemplate Photo10x15Portrait => new()
    {
        Id = "photo-10x15-portrait",
        Name = "Photo 10x15 Portrait",
        WidthPx = 1181, // 10cm x 15cm @ ~300dpi
        HeightPx = 1772,
    };

    public static OutputTemplate Photo10x15Landscape => new()
    {
        Id = "photo-10x15-landscape",
        Name = "Photo 10x15 Landscape",
        WidthPx = 1772,
        HeightPx = 1181,
    };

    public static IReadOnlyList<OutputTemplate> BuiltIn { get; } =
    [
        DigitalLandscape,
        Photo10x15Portrait,
        Photo10x15Landscape,
    ];
}
