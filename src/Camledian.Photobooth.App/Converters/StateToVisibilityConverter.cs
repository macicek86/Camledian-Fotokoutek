using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Camledian.Photobooth.Core.StateMachine;

namespace Camledian.Photobooth.App.Converters;

/// <summary>
/// The single, authoritative mapping from <see cref="PhotoboothStateMachine.Current"/> to which
/// screen is visible. Every screen's Visibility binds through this one converter (parameterized with
/// the state(s) it represents, comma-separated) instead of each screen inventing its own independent
/// boolean — per spec §18, the state machine is the only thing deciding what's on screen.
/// </summary>
public class StateToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PhotoboothState state || parameter is not string statesCsv)
        {
            return Visibility.Collapsed;
        }

        var matches = statesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(s => Enum.TryParse<PhotoboothState>(s, out var candidate) && candidate == state);

        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
