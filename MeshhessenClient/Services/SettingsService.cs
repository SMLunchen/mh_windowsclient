using System.IO;

namespace MeshhessenClient.Services;

public record AppSettings(bool DarkMode, string StationName, bool ShowEncryptedMessages);

public static class SettingsService
{
    private static string IniFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "meshhessen-client.ini");

    public static AppSettings Load()
    {
        var defaults = new AppSettings(false, string.Empty, true);

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
                ShowEncryptedMessages: !values.TryGetValue("ShowEncryptedMessages", out var se) || !bool.TryParse(se, out var seBool) || seBool
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
            var lines = new[]
            {
                "[App]",
                $"DarkMode={settings.DarkMode}",
                $"StationName={settings.StationName}",
                $"ShowEncryptedMessages={settings.ShowEncryptedMessages}"
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
