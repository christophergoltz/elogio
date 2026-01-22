using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Elogio.Resources;

namespace Elogio.Converters;

/// <summary>
/// Converts a boolean to a background brush (true = light gray, false = transparent).
/// Useful for highlighting weekend days.
/// </summary>
public class BoolToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true
            ? AppColors.WeekendBackgroundBrush
            : Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string (e.g., "#FF0000") to a SolidColorBrush with reduced opacity.
/// Returns Transparent if the value is null or empty.
/// </summary>
public class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hexColor && !string.IsNullOrEmpty(hexColor))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                // Use 50% opacity for a subtle but visible border
                return new SolidColorBrush(Color.FromArgb(128, color.R, color.G, color.B));
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
