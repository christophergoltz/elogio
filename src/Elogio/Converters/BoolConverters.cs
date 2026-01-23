using System.Globalization;
using System.Windows.Data;

namespace Elogio.Converters;

/// <summary>
/// Inverts a boolean value (true becomes false, false becomes true).
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }
}
