using System.IO;

namespace MeshhessenClient.Services;

public record AppSettings(
    bool DarkMode,
    string StationName,
    bool ShowEncryptedMessages,
    double MyLatitude,
    double MyLongitude);

public static class SettingsService
{
    private static string IniFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meshhessen-client.ini");

    public static AppSettings Load()
    {
        var defaults = new AppSettings(false, string.Empty, true, 50.9, 9.5);

        try
        {
            if (!File.Exists(IniFilePath))
                return defaults;

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(IniFilePath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[") || trimmed.StartsWith(";") || string.IsNullOrEmpty(trimmed))
                    continue;

                var eq = trimmed.IndexOf('=');
                if (eq > 0)
                    values[trimmed[..eq].Trim()] = trimmed[(eq + 1)..].Trim();
            }

            return new AppSettings(
                DarkMode: values.TryGetValue("DarkMode", out var dm) && bool.TryParse(dm, out var dmBool) ? dmBool : defaults.DarkMode,
                StationName: values.TryGetValue("StationName", out var sn) ? sn : defaults.StationName,
                ShowEncryptedMessages: !values.TryGetValue("ShowEncryptedMessages", out var se) || !bool.TryParse(se, out var seBool) || seBool,
                MyLatitude: values.TryGetValue("MyLatitude", out var lat) && double.TryParse(lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var latVal) ? latVal : defaults.MyLatitude,
                MyLongitude: values.TryGetValue("MyLongitude", out var lon) && double.TryParse(lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lonVal) ? lonVal : defaults.MyLongitude
            );
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR loading settings: {ex.Message}");
            return defaults;
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new[]
            {
                "[App]",
                $"DarkMode={settings.DarkMode}",
                $"StationName={settings.StationName}",
                $"ShowEncryptedMessages={settings.ShowEncryptedMessages}",
                $"MyLatitude={settings.MyLatitude.ToString("F7", ci)}",
                $"MyLongitude={settings.MyLongitude.ToString("F7", ci)}"
            };
            File.WriteAllLines(IniFilePath, lines);
            Logger.WriteLine($"Settings saved to {IniFilePath}");
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"ERROR saving settings: {ex.Message}");
        }
    }
}
