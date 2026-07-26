using System.Windows.Controls;
using Camledian.Photobooth.App.ViewModels;

namespace Camledian.Photobooth.App.Views;

public partial class AdminView : UserControl
{
    public AdminView()
    {
        InitializeComponent();
    }

    // PasswordBox.Password is intentionally not bindable in WPF (avoids leaking it into bindings/
    // clipboard tooling); wiring it through code-behind into the view model is the standard pattern.
    private void PinBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.PinInput = passwordBox.Password;
        }
    }
}
