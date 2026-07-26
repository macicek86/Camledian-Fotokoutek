using Camledian.Photobooth.Core.StateMachine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Camledian.Photobooth.App.ViewModels;

/// <summary>
/// Physical shutter trigger support (spec §57): most photobooth remotes (Bluetooth shutter buttons,
/// USB footswitches, presentation clickers) emulate a keyboard keypress, so a single configurable
/// key — checked here, actually captured by MainWindow's PreviewKeyDown handler — covers the
/// overwhelming majority of hardware without any device-specific drivers.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty]
    private bool _isLearningTriggerKey;

    /// <summary>Called by MainWindow's key handler for every key press that isn't consumed by the
    /// "learn key" flow. Mirrors what a single physical button should do at each stage: start the
    /// session from Idle, or start the countdown from Preview. A no-op everywhere else (Admin,
    /// Countdown already running, etc.) so an accidental extra press can't do anything surprising.</summary>
    public void OnTriggerKeyPressed()
    {
        switch (State)
        {
            case PhotoboothState.Idle:
                StartSession();
                break;
            case PhotoboothState.Preview:
                _ = StartCountdownAsync();
                break;
        }
    }

    [RelayCommand]
    private void BeginLearnTriggerKey() => IsLearningTriggerKey = true;

    /// <summary>Called by MainWindow when a key arrives while <see cref="IsLearningTriggerKey"/> is
    /// true — saves it as the new trigger immediately (spec-inspired UX: no separate "save" step,
    /// matches the one-shot feel of "press the button you want to use").</summary>
    public async Task LearnTriggerKeyAsync(string keyName)
    {
        IsLearningTriggerKey = false;
        _settingsService.Current.Ui.PhotoTriggerKey = keyName;
        await _settingsService.SaveSectionAsync(nameof(Core.Models.AppSettings.Ui), _settingsService.Current.Ui).ConfigureAwait(true);
        OnPropertyChanged(nameof(Settings));
    }

    [RelayCommand]
    private async Task ClearTriggerKeyAsync()
    {
        IsLearningTriggerKey = false;
        _settingsService.Current.Ui.PhotoTriggerKey = null;
        await _settingsService.SaveSectionAsync(nameof(Core.Models.AppSettings.Ui), _settingsService.Current.Ui).ConfigureAwait(true);
        OnPropertyChanged(nameof(Settings));
    }
}
