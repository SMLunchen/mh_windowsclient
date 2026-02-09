using System.Globalization;
using System.Windows.Data;

namespace MeshhessenClient.Converters;

public class MqttIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isViaMqtt && isViaMqtt)
        {
            return "🌐"; // Globe icon for MQTT
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class MqttTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isViaMqtt && isViaMqtt)
        {
            return "Via MQTT";
        }
        return "Via LoRa";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
