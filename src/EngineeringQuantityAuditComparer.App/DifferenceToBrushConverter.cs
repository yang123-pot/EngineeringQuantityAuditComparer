using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace EngineeringQuantityAuditComparer.App;

public sealed class DifferenceToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PositiveBrush = CreateBrush(Color.FromRgb(0xC0, 0x00, 0x00));
    private static readonly SolidColorBrush NegativeBrush = CreateBrush(Color.FromRgb(0x00, 0x61, 0x00));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (!TryGetDecimal(value, out var number) || number == 0m)
        {
            return DependencyProperty.UnsetValue;
        }

        return number > 0m ? PositiveBrush : NegativeBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool TryGetDecimal(object value, out decimal number)
    {
        try
        {
            number = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            number = 0m;
            return false;
        }
    }
}
