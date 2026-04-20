using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;

namespace VRCHOTAS.Converters;

/// <summary>
/// Maps (IsTemporarilyDisabled, IsSourceDeviceConnected) to indicator brush: gray when disabled, green/red otherwise.
/// </summary>
public sealed class MappingSourceIndicatorConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not bool disabled || values[1] is not bool connected)
        {
            return MediaBrushes.DimGray;
        }

        if (disabled)
        {
            return MediaBrushes.Gray;
        }

        return connected ? MediaBrushes.LimeGreen : MediaBrushes.IndianRed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
