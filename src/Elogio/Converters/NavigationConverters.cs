using System.Globalization;
using System.Windows.Data;

namespace Elogio.Converters;

/// <summary>
/// Compares current page type with a target page type for navigation highlighting.
/// Returns true if the current page matches the target (from ConverterParameter).
/// </summary>
public class PageTypeToIsActiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Type currentPage && parameter is Type targetPage)
        {
            return currentPage == targetPage;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
