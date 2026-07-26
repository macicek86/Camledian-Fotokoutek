namespace Camledian.Photobooth.Core.Utilities;

/// <summary>
/// A single-slot "latest value wins" mailbox. Publishing while a value is already pending disposes
/// the stale one and replaces it — exactly the semantics spec §9 asks for: the live preview pipeline
/// must never block on a backlog of old camera frames, it always renders the newest one and drops
/// anything older. Generic so the same primitive backs both the camera preview loop and the AI
/// preview inference loop.
/// </summary>
public sealed class LatestFrameBox<T> : IDisposable
    where T : class, IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private T? _pending;
    private bool _disposed;

    public void Publish(T frame)
    {
        T? toDispose;
        lock (_gate)
        {
            if (_disposed)
            {
                frame.Dispose();
                return;
            }

            toDispose = _pending;
            _pending = frame;
        }

        toDispose?.Dispose();

        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A frame is already waiting to be consumed; the one we just stored replaces it above.
        }
        catch (ObjectDisposedException)
        {
            // Disposed concurrently with Publish; the frame we stored will simply never be read.
        }
    }

    /// <summary>Waits for the next available frame, skipping any backlog. Returns null if disposed
    /// or cancelled while waiting.</summary>
    public async Task<T?> WaitNextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }

        lock (_gate)
        {
            var frame = _pending;
            _pending = null;
            return frame;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending?.Dispose();
            _pending = null;
        }

        _signal.Dispose();
    }
}
