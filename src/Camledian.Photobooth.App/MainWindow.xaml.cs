using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Camledian.Photobooth.App.ViewModels;
using Camledian.Photobooth.Core.StateMachine;

namespace Camledian.Photobooth.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyWindowMode(viewModel.IsKioskMode);
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Physical shutter trigger (spec §57). Handled at the Window level via standard WPF key
    /// routing — sufficient for a kiosk that owns the whole screen and is always focused, so a
    /// low-level global keyboard hook (with its extra complexity/security surface) isn't warranted.
    /// </summary>
    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.IsLearningTriggerKey)
        {
            await _viewModel.LearnTriggerKeyAsync(e.Key.ToString());
            e.Handled = true;
            return;
        }

        var triggerKey = _viewModel.Settings.Ui.PhotoTriggerKey;
        if (string.IsNullOrEmpty(triggerKey) || e.Key.ToString() != triggerKey)
        {
            return;
        }

        // Only swallow the key when it would actually do something (spec §57's button should feel
        // like a physical button, not like a keyboard shortcut that can eat a space bar someone
        // meant to type into an Admin text field). PIN entry / text boxes always keep normal input.
        if (_viewModel.State is PhotoboothState.Idle or PhotoboothState.Preview)
        {
            _viewModel.OnTriggerKeyPressed();
            e.Handled = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsKioskMode))
        {
            ApplyWindowMode(_viewModel.IsKioskMode);
        }
    }

    private void ApplyWindowMode(bool kiosk)
    {
        if (kiosk)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Topmost = true;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
            Topmost = false;
            Width = 1280;
            Height = 800;
        }
    }
}
