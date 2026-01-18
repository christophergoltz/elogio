using System.Globalization;
using System.Windows.Data;

namespace Elogio.Converters;

/// <summary>
/// Returns true if the value is negative (works with TimeSpan and numeric types).
/// </summary>
public class IsNegativeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            TimeSpan ts => ts < TimeSpan.Zero,
            double d => d < 0,
            int i => i < 0,
            long l => l < 0,
            decimal m => m < 0,
            _ => false
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns true if the value is positive (greater than zero, works with TimeSpan and numeric types).
/// </summary>
public class IsPositiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            TimeSpan ts => ts > TimeSpan.Zero,
            double d => d > 0,
            int i => i > 0,
            long l => l > 0,
            decimal m => m > 0,
            _ => false
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
