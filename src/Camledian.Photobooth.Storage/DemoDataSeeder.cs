using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage.Repositories;

namespace Camledian.Photobooth.Storage;

/// <summary>Seeds a "Demo Event" the first time the database is empty (spec §30), so a clean checkout
/// can be tried immediately without any manual admin configuration.</summary>
public class DemoDataSeeder(EventRepository eventRepository, AssetRepository assetRepository)
{
    public async Task SeedIfEmptyAsync(
        IReadOnlyList<AssetRecord> backgrounds,
        IReadOnlyList<AssetRecord> overlays,
        CancellationToken cancellationToken = default)
    {
        var existingEvents = await eventRepository.ListAsync(cancellationToken).ConfigureAwait(false);
        if (existingEvents.Count > 0)
        {
            return;
        }

        foreach (var background in backgrounds)
        {
            await assetRepository.UpsertAsync(background, cancellationToken).ConfigureAwait(false);
        }

        foreach (var overlay in overlays)
        {
            await assetRepository.UpsertAsync(overlay, cancellationToken).ConfigureAwait(false);
        }

        var demoEvent = new EventDefinition
        {
            Id = "demo-event",
            Name = "Demo Event",
            BackgroundAssetIds = backgrounds.Select(b => b.Id).ToList(),
            OverlayAssetIds = overlays.Select(o => o.Id).ToList(),
            OutputTemplateId = OutputTemplate.DigitalLandscape.Id,
            IsActive = true,
        };
        await eventRepository.UpsertAsync(demoEvent, cancellationToken).ConfigureAwait(false);
    }
}
