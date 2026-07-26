using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Core.Utilities;
using Camledian.Photobooth.Storage.Repositories;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.Cloud.Services;

public enum PairingOutcome
{
    Confirmed,
    Expired,
    TimedOut,
    Cancelled,
}

public sealed record PairingResult(PairingOutcome Outcome, string? DeviceId);

/// <summary>
/// Device pairing flow (spec §36): generate a short human-typeable code locally, register it with
/// the backend, show it on screen, then poll until an admin confirms it and hands back a
/// deviceId/deviceToken — which is stored via <see cref="DeviceRepository"/>, never in plain
/// settings JSON.
/// </summary>
public class DevicePairingService(CloudApiClient apiClient, DeviceRepository deviceRepository, ILogger<DevicePairingService> logger)
{
    /// <summary>Starts pairing and returns the code to display (e.g. as text/QR) immediately; the
    /// actual polling loop is a separate call so the caller can show the code before awaiting.</summary>
    public async Task<string> BeginAsync(CancellationToken cancellationToken = default)
    {
        var code = TokenGenerator.CreatePairingCode();
        await apiClient.PairStartAsync(code, cancellationToken).ConfigureAwait(false);
        return code;
    }

    /// <summary>Polls pair/status until confirmed, expired, or <paramref name="timeout"/> elapses.
    /// On confirmation, persists the device identity locally.</summary>
    public async Task<PairingResult> WaitForConfirmationAsync(string code, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (true)
            {
                var status = await apiClient.PairStatusAsync(code, linkedCts.Token).ConfigureAwait(false);
                switch (status.Status)
                {
                    case "confirmed" when status.DeviceId is not null && status.DeviceToken is not null:
                        var device = new DeviceRecord { DeviceId = status.DeviceId, DeviceToken = status.DeviceToken };
                        await deviceRepository.SaveAsync(device, cancellationToken).ConfigureAwait(false);
                        logger.LogInformation("Device paired successfully: {DeviceId}", status.DeviceId);
                        return new PairingResult(PairingOutcome.Confirmed, status.DeviceId);

                    case "expired":
                        return new PairingResult(PairingOutcome.Expired, null);

                    default:
                        await Task.Delay(TimeSpan.FromSeconds(4), linkedCts.Token).ConfigureAwait(false);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            return new PairingResult(PairingOutcome.TimedOut, null);
        }
        catch (OperationCanceledException)
        {
            return new PairingResult(PairingOutcome.Cancelled, null);
        }
    }
}
