using Camledian.Photobooth.Camera.Providers;
using Camledian.Photobooth.Core.Models;
using Microsoft.Extensions.Logging;

namespace Camledian.Photobooth.Camera;

/// <summary>
/// Chooses between the real webcam and the mock provider (spec §7: "pokud fyzická kamera není
/// dostupná, vytvoř také MockCameraProvider aby bylo možné pipeline testovat"). Real hardware is
/// preferred whenever it can actually be opened; the mock is the deliberate, explicit fallback.
/// </summary>
public class CameraProviderFactory(ILogger<CameraProviderFactory> logger)
{
    public ICameraProvider Create(CameraSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            logger.LogInformation(
                "Not running on Windows; using MockCameraProvider (WebcamCameraProvider requires a real Windows camera backend).");
            return new MockCameraProvider();
        }

        try
        {
            var webcam = new WebcamCameraProvider();
            if (webcam.ListDevices().Count > 0)
            {
                return webcam;
            }

            logger.LogWarning("No webcam devices detected.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to probe webcam devices.");
        }

        if (!settings.UseMockIfUnavailable)
        {
            throw new InvalidOperationException(
                "No camera available and Camera.UseMockIfUnavailable is false.");
        }

        logger.LogInformation("Falling back to MockCameraProvider.");
        return new MockCameraProvider();
    }
}
