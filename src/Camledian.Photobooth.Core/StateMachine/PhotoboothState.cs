namespace Camledian.Photobooth.Core.StateMachine;

/// <summary>
/// Every screen/mode the kiosk workflow can be in. The UI renders strictly from this value instead
/// of juggling independent Visibility flags, per the spec's explicit requirement.
/// </summary>
public enum PhotoboothState
{
    Idle,
    SelectingBackground,
    Preview,
    Countdown,
    Capturing,

    /// <summary>Burst mode only: the guest picks which of the just-captured shots to keep. Skipped
    /// entirely when the configured burst count is 1.</summary>
    SelectingPhoto,

    Processing,
    Result,
    Printing,
    Admin,
    Error,
}
