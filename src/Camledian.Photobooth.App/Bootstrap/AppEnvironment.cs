namespace Camledian.Photobooth.App.Bootstrap;

/// <summary>Development mode shows a bordered window and debug affordances; Kiosk mode runs
/// fullscreen with minimal chrome (spec §20).</summary>
public enum AppEnvironment
{
    Development,
    Kiosk,
}
