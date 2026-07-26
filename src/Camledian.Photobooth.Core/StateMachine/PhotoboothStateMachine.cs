namespace Camledian.Photobooth.Core.StateMachine;

public sealed class StateChangedEventArgs : EventArgs
{
    public StateChangedEventArgs(PhotoboothState from, PhotoboothState to)
    {
        From = from;
        To = to;
    }

    public PhotoboothState From { get; }
    public PhotoboothState To { get; }
}

/// <summary>
/// Explicit finite-state machine driving the kiosk workflow. The UI layer subscribes to
/// <see cref="StateChanged"/> and renders the screen for <see cref="Current"/> — it never decides
/// transitions itself. Keeping the transition table here (rather than scattered UI event handlers)
/// is what makes "Vyfotit -> 3 -> 2 -> 1 -> capture -> processing -> result" reliable and testable.
/// </summary>
public class PhotoboothStateMachine
{
    private static readonly Dictionary<PhotoboothState, PhotoboothState[]> Transitions = new()
    {
        [PhotoboothState.Idle] = [PhotoboothState.SelectingBackground, PhotoboothState.Admin],
        [PhotoboothState.SelectingBackground] = [PhotoboothState.Preview, PhotoboothState.Idle, PhotoboothState.Admin],
        [PhotoboothState.Preview] = [PhotoboothState.Countdown, PhotoboothState.SelectingBackground, PhotoboothState.Idle, PhotoboothState.Admin],
        [PhotoboothState.Countdown] = [PhotoboothState.Capturing, PhotoboothState.Preview],
        [PhotoboothState.Capturing] = [PhotoboothState.Processing, PhotoboothState.Error],
        [PhotoboothState.Processing] = [PhotoboothState.Result, PhotoboothState.Error],
        [PhotoboothState.Result] = [PhotoboothState.Printing, PhotoboothState.SelectingBackground, PhotoboothState.Idle],
        [PhotoboothState.Printing] = [PhotoboothState.Result, PhotoboothState.Idle],
        [PhotoboothState.Admin] = [PhotoboothState.Idle],
        [PhotoboothState.Error] = [PhotoboothState.Idle],
    };

    private readonly Lock _gate = new();

    public PhotoboothStateMachine(PhotoboothState initial = PhotoboothState.Idle)
    {
        Current = initial;
    }

    public PhotoboothState Current { get; private set; }

    public event EventHandler<StateChangedEventArgs>? StateChanged;

    public bool CanFire(PhotoboothState to) =>
        to == PhotoboothState.Error || (Transitions.TryGetValue(Current, out var allowed) && allowed.Contains(to));

    /// <summary>Attempts the transition. Returns false instead of throwing when it is not allowed.</summary>
    public bool TryFire(PhotoboothState to)
    {
        lock (_gate)
        {
            if (!CanFire(to))
            {
                return false;
            }

            var from = Current;
            Current = to;
            StateChanged?.Invoke(this, new StateChangedEventArgs(from, to));
            return true;
        }
    }

    /// <summary>Throwing variant for call sites that treat an illegal transition as a programming error.</summary>
    public void Fire(PhotoboothState to)
    {
        if (!TryFire(to))
        {
            throw new InvalidOperationException($"Cannot transition from {Current} to {to}.");
        }
    }

    /// <summary>Unconditional reset back to Idle, used by result-screen timeouts and error recovery.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            var from = Current;
            Current = PhotoboothState.Idle;
            if (from != PhotoboothState.Idle)
            {
                StateChanged?.Invoke(this, new StateChangedEventArgs(from, PhotoboothState.Idle));
            }
        }
    }
}
