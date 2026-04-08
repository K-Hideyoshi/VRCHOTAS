using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VRCHOTAS.Converters;

public sealed class BooleanToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Brushes.LimeGreen : Brushes.DimGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
