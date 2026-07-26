using Camledian.Photobooth.Core.Models;
using Camledian.Photobooth.Storage.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Camledian.Photobooth.App.Services;

/// <summary>
/// Live, admin-editable settings (spec §10: "Administrátor musí hodnoty vidět a měnit za běhu").
/// Every read goes through <see cref="Current"/> so a change takes effect on the very next preview
/// frame — nothing here is captured once at startup and frozen.
/// </summary>
public partial class SettingsService(SettingsRepository repository) : ObservableObject
{
    [ObservableProperty]
    private AppSettings _current = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Current = await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveAllAsync(CancellationToken cancellationToken = default) =>
        repository.SaveAllAsync(Current, cancellationToken);

    public Task SaveSectionAsync(string sectionKey, object sectionValue, CancellationToken cancellationToken = default) =>
        repository.SaveSectionAsync(sectionKey, sectionValue, cancellationToken);
}
