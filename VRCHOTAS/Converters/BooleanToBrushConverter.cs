using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;

namespace VRCHOTAS.Converters;

public sealed class BooleanToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var boolValue = value is true;
        if (parameter is string text && text.Equals("RedGreen", StringComparison.OrdinalIgnoreCase))
        {
            return boolValue ? MediaBrushes.LimeGreen : MediaBrushes.IndianRed;
        }

        return boolValue ? MediaBrushes.LimeGreen : MediaBrushes.DimGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
