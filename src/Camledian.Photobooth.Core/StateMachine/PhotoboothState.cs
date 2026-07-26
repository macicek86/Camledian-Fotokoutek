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
    Processing,
    Result,
    Printing,
    Admin,
    Error,
}
