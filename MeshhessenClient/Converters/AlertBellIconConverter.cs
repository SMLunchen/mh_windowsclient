using System.Globalization;
using System.Windows.Data;

namespace MeshhessenClient.Converters;

public class AlertBellIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool hasAlertBell && hasAlertBell)
        {
            return "🔔"; // Bell icon for alert
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
