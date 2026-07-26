using System.ComponentModel;
using System.Windows;
using Camledian.Photobooth.App.ViewModels;

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
