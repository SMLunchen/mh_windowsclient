using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Meshtastic.Protobufs;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class NodeConfigWindow : Window
{
    private readonly MeshtasticProtocolService _protocolService;

    // Loaded configs (for clone-and-update pattern)
    private User? _loadedOwner;
    private DeviceConfig? _loadedDevice;
    private PositionConfig? _loadedPosition;
    private LoRaConfig? _loadedLora;
    private MQTTConfig? _loadedMqtt;
    private TelemetryConfig? _loadedTelemetry;
    private BluetoothConfig? _loadedBluetooth;
    private int _pendingRequests = 0;

    public NodeConfigWindow(MeshtasticProtocolService protocolService)
    {
        InitializeComponent();
        _protocolService = protocolService;

        _protocolService.OwnerReceived += OnOwnerReceived;
        _protocolService.DeviceConfigReceived += OnDeviceConfigReceived;
        _protocolService.PositionConfigReceived += OnPositionConfigReceived;
        _protocolService.LoRaConfigReceived += OnLoRaConfigReceived;
        _protocolService.MqttConfigReceived += OnMqttConfigReceived;
        _protocolService.TelemetryConfigReceived += OnTelemetryConfigReceived;
        _protocolService.BluetoothConfigReceived += OnBluetoothConfigReceived;

        Closed += (s, e) =>
        {
            _protocolService.OwnerReceived -= OnOwnerReceived;
            _protocolService.DeviceConfigReceived -= OnDeviceConfigReceived;
            _protocolService.PositionConfigReceived -= OnPositionConfigReceived;
            _protocolService.LoRaConfigReceived -= OnLoRaConfigReceived;
            _protocolService.MqttConfigReceived -= OnMqttConfigReceived;
            _protocolService.TelemetryConfigReceived -= OnTelemetryConfigReceived;
            _protocolService.BluetoothConfigReceived -= OnBluetoothConfigReceived;
        };

        Loaded += async (s, e) => await RequestAllConfigsAsync();
    }

    private async System.Threading.Tasks.Task RequestAllConfigsAsync()
    {
        try
        {
            _pendingRequests = 7;
            UpdateStatus("Lade Konfiguration...");
            SaveButton.IsEnabled = false;

            await _protocolService.RequestOwnerAsync();
            await System.Threading.Tasks.Task.Delay(200);
            await _protocolService.RequestDeviceConfigAsync();
            await System.Threading.Tasks.Task.Delay(200);
            await _protocolService.RequestPositionConfigAsync();
            await System.Threading.Tasks.Task.Delay(200);
            await _protocolService.RequestLoRaConfigAsync();
            await System.Threading.Tasks.Task.Delay(200);
            await _protocolService.RequestMqttConfigAsync();
            await System.Threading.Tasks.Task.Delay(200);
            await _protocolService.RequestTelemetryConfigAsync();
            await System.Threading.Tasks.Task.Delay(200);
            await _protocolService.RequestBluetoothConfigAsync();
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => UpdateStatus($"Fehler beim Laden: {ex.Message}"));
        }
    }

    private void CheckAllLoaded()
    {
        _pendingRequests--;
        if (_pendingRequests <= 0)
        {
            UpdateStatus("Konfiguration geladen.");
            SaveButton.IsEnabled = true;
        }
    }

    private void UpdateStatus(string text)
    {
        Dispatcher.BeginInvoke(() => StatusText.Text = text);
    }

    // ========== Event handlers — populate UI ==========

    private void OnOwnerReceived(object? sender, User user)
    {
        _loadedOwner = user;
        Dispatcher.BeginInvoke(() =>
        {
            LongNameTextBox.Text = user.LongName;
            ShortNameTextBox.Text = user.ShortName;
            CheckAllLoaded();
        });
    }

    private void OnDeviceConfigReceived(object? sender, DeviceConfig config)
    {
        _loadedDevice = config;
        Dispatcher.BeginInvoke(() =>
        {
            SelectComboBoxByTag(RoleComboBox, (int)config.Role);
            SelectComboBoxByTag(RebroadcastComboBox, (int)config.RebroadcastMode);
            NodeInfoIntervalTextBox.Text = SecondsToHumanTime(config.NodeInfoBroadcastSecs);
            TzdefTextBox.Text = config.Tzdef;
            LedHeartbeatCheckBox.IsChecked = config.LedHeartbeatDisabled;
            CheckAllLoaded();
        });
    }

    private void OnPositionConfigReceived(object? sender, PositionConfig config)
    {
        _loadedPosition = config;
        Dispatcher.BeginInvoke(() =>
        {
            SelectComboBoxByTag(GpsModeComboBox, (int)config.GpsMode);
            PosBroadcastSecsTextBox.Text = SecondsToHumanTime(config.PositionBroadcastSecs);
            SmartBroadcastCheckBox.IsChecked = config.PositionBroadcastSmartEnabled;
            MinDistanceTextBox.Text = config.PositionBroadcastSmartMinimumDistance.ToString();
            MinSpeedTextBox.Text = config.PositionBroadcastSmartMinimumSpeed.ToString();
            CheckAllLoaded();
        });
    }

    private void OnLoRaConfigReceived(object? sender, LoRaConfig config)
    {
        _loadedLora = config;
        Dispatcher.BeginInvoke(() =>
        {
            // Use -1 fallback to preserve original value if not in list
            SelectComboBoxByTag(LoraRegionComboBox, (int)config.Region, keepIfNotFound: true);
            LoraUsePresetCheckBox.IsChecked = config.UsePreset;
            SelectComboBoxByTag(LoraPresetComboBox, (int)config.ModemPreset, keepIfNotFound: true);
            LoraPresetComboBox.IsEnabled = config.UsePreset;
            HopLimitTextBox.Text = config.HopLimit.ToString();
            TxEnabledCheckBox.IsChecked = config.TxEnabled;
            TxPowerTextBox.Text = config.TxPower.ToString();
            CheckAllLoaded();
        });
    }

    private void OnMqttConfigReceived(object? sender, MQTTConfig config)
    {
        _loadedMqtt = config;
        Dispatcher.BeginInvoke(() =>
        {
            MqttEnabledCheckBox.IsChecked = config.Enabled;
            MqttAddressTextBox.Text = config.Address;
            MqttUsernameTextBox.Text = config.Username;
            MqttPasswordBox.Password = config.Password;
            MqttEncryptionCheckBox.IsChecked = config.EncryptionEnabled;
            MqttJsonCheckBox.IsChecked = config.JsonEnabled;
            MqttTlsCheckBox.IsChecked = config.TlsEnabled;
            MqttRootTextBox.Text = config.Root;
            MqttProxyCheckBox.IsChecked = config.ProxyToClientEnabled;
            MqttMapReportingCheckBox.IsChecked = config.MapReportingEnabled;
            CheckAllLoaded();
        });
    }

    private void OnTelemetryConfigReceived(object? sender, TelemetryConfig config)
    {
        _loadedTelemetry = config;
        Dispatcher.BeginInvoke(() =>
        {
            DeviceTelemetryIntervalTextBox.Text = SecondsToHumanTime(config.DeviceUpdateInterval);
            EnvironmentIntervalTextBox.Text = SecondsToHumanTime(config.EnvironmentUpdateInterval);
            FahrenheitCheckBox.IsChecked = config.EnvironmentDisplayFahrenheit;
            EnvironmentMeasurementCheckBox.IsChecked = config.EnvironmentMeasurementEnabled;
            AirQualityIntervalTextBox.Text = SecondsToHumanTime(config.AirQualityInterval);
            PowerIntervalTextBox.Text = SecondsToHumanTime(config.PowerUpdateInterval);
            CheckAllLoaded();
        });
    }

    private void OnBluetoothConfigReceived(object? sender, BluetoothConfig config)
    {
        _loadedBluetooth = config;
        Dispatcher.BeginInvoke(() =>
        {
            BtEnabledCheckBox.IsChecked = config.Enabled;
            SelectComboBoxByTag(BtModeComboBox, (int)config.Mode);
            BtFixedPinTextBox.Text = config.FixedPin > 0 ? config.FixedPin.ToString() : string.Empty;
            BtFixedPinTextBox.IsEnabled = config.Mode == 1; // 1 = FIXED_PIN
            CheckAllLoaded();
        });
    }

    // ========== Interval format helpers ==========

    /// <summary>Converts seconds to human-readable format, e.g. 3600 → "1h", 5400 → "1h 30m"</summary>
    private static string SecondsToHumanTime(uint seconds)
    {
        if (seconds == 0) return "0";
        var parts = new List<string>();
        uint rem = seconds;
        if (rem >= 86400) { parts.Add($"{rem / 86400}d"); rem %= 86400; }
        if (rem >= 3600)  { parts.Add($"{rem / 3600}h");  rem %= 3600; }
        if (rem >= 60)    { parts.Add($"{rem / 60}m");    rem %= 60; }
        if (rem > 0)      { parts.Add($"{rem}s"); }
        return string.Join(" ", parts);
    }

    /// <summary>Parses human-readable time string to seconds, e.g. "1h 30m" → 5400. Plain number = seconds.</summary>
    private static uint HumanTimeToSeconds(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        // Try plain number first
        if (uint.TryParse(input.Trim(), out var plain)) return plain;
        // Parse tokens like 1d, 2h, 30m, 45s
        uint total = 0;
        var matches = Regex.Matches(input, @"(\d+)\s*([dhms])", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            var val = uint.Parse(m.Groups[1].Value);
            total += m.Groups[2].Value.ToLower() switch
            {
                "d" => val * 86400u,
                "h" => val * 3600u,
                "m" => val * 60u,
                _   => val
            };
        }
        return total;
    }

    // ========== Helpers ==========

    private static void SelectComboBoxByTag(ComboBox cb, int tag, bool keepIfNotFound = false)
    {
        foreach (ComboBoxItem item in cb.Items)
        {
            if (item.Tag is string s && int.TryParse(s, out int t) && t == tag)
            {
                cb.SelectedItem = item;
                return;
            }
        }
        // If not found: either keep current selection or fall back to first item
        if (!keepIfNotFound && cb.Items.Count > 0)
            cb.SelectedIndex = 0;
        // keepIfNotFound=true → don't change selection (avoids accidental Unset)
    }

    private static int GetComboBoxTag(ComboBox cb)
    {
        if (cb.SelectedItem is ComboBoxItem item && item.Tag is string s && int.TryParse(s, out int v))
            return v;
        return 0;
    }

    // ========== UI Events ==========

    private void LoraUsePresetCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        LoraPresetComboBox.IsEnabled = LoraUsePresetCheckBox.IsChecked == true;
    }

    private void TzFromWindows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var tz = TimeZoneInfo.Local;
            var posix = WindowsToPosixTz(tz);
            TzdefTextBox.Text = posix;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Lesen der Windows-Zeitzone: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Converts Windows TimeZoneInfo to a POSIX TZ string for Meshtastic.</summary>
    private static string WindowsToPosixTz(TimeZoneInfo tz)
    {
        // Lookup table for common Windows TZ IDs → POSIX
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["W. Europe Standard Time"]       = "CET-1CEST,M3.5.0,M10.5.0/3",
            ["Central Europe Standard Time"]  = "CET-1CEST,M3.5.0,M10.5.0/3",
            ["Central European Standard Time"]= "CET-1CEST,M3.5.0,M10.5.0/3",
            ["Romance Standard Time"]         = "CET-1CEST,M3.5.0,M10.5.0/3",
            ["GTB Standard Time"]             = "EET-2EEST,M3.5.0/3,M10.5.0/4",
            ["FLE Standard Time"]             = "EET-2EEST,M3.5.0/3,M10.5.0/4",
            ["E. Europe Standard Time"]       = "EET-2EEST,M3.5.0/3,M10.5.0/4",
            ["GMT Standard Time"]             = "GMT0BST,M3.5.0/1,M10.5.0",
            ["UTC"]                           = "UTC0",
            ["Eastern Standard Time"]         = "EST5EDT,M3.2.0,M11.1.0",
            ["Central Standard Time"]         = "CST6CDT,M3.2.0,M11.1.0",
            ["Mountain Standard Time"]        = "MST7MDT,M3.2.0,M11.1.0",
            ["Pacific Standard Time"]         = "PST8PDT,M3.2.0,M11.1.0",
            ["Tokyo Standard Time"]           = "JST-9",
            ["China Standard Time"]           = "CST-8",
            ["AUS Eastern Standard Time"]     = "AEST-10AEDT,M10.1.0,M4.1.0/3",
        };

        if (map.TryGetValue(tz.Id, out var posix))
            return posix;

        // Fallback: generate minimal POSIX string from offset (no DST info)
        var offsetHours = -(int)tz.BaseUtcOffset.TotalHours;
        var offsetStr = offsetHours == 0 ? "0" : $"{offsetHours:+0;-0}";
        return tz.SupportsDaylightSavingTime
            ? $"STD{offsetStr}DST"
            : $"STD{offsetStr}";
    }

    private async void NodeDbReset_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "NodeDB zurücksetzen?\n\nDas löscht alle bekannten Nodes auf dem Gerät.\nDie eigene Konfiguration bleibt erhalten.\n\nDas Gerät wird danach neu starten.",
            "NodeDB zurücksetzen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _protocolService.ResetNodeDbAsync();
            UpdateStatus("NodeDB-Reset gesendet. Gerät startet neu...");
            Services.Logger.WriteLine("NodeDB reset sent");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveButton.IsEnabled = false;
            UpdateStatus("Speichern...");

            // Owner
            var newOwner = new User
            {
                LongName = LongNameTextBox.Text.Trim(),
                ShortName = ShortNameTextBox.Text.Trim()
            };
            if (_loadedOwner != null)
                newOwner.Id = _loadedOwner.Id;
            await _protocolService.SetOwnerAsync(newOwner);
            await System.Threading.Tasks.Task.Delay(300);

            // Device Config
            var newDevice = new DeviceConfig
            {
                Role = (Role)GetComboBoxTag(RoleComboBox),
                RebroadcastMode = (RebroadcastMode)GetComboBoxTag(RebroadcastComboBox),
                NodeInfoBroadcastSecs = HumanTimeToSeconds(NodeInfoIntervalTextBox.Text),
                Tzdef = TzdefTextBox.Text.Trim(),
                LedHeartbeatDisabled = LedHeartbeatCheckBox.IsChecked == true
            };
            await _protocolService.SetDeviceConfigAsync(newDevice);
            await System.Threading.Tasks.Task.Delay(300);

            // Position Config
            var newPosition = new PositionConfig
            {
                GpsMode = (GpsMode)GetComboBoxTag(GpsModeComboBox),
                PositionBroadcastSecs = HumanTimeToSeconds(PosBroadcastSecsTextBox.Text),
                PositionBroadcastSmartEnabled = SmartBroadcastCheckBox.IsChecked == true,
                PositionBroadcastSmartMinimumDistance = uint.TryParse(MinDistanceTextBox.Text, out var md) ? md : 0,
                PositionBroadcastSmartMinimumSpeed = uint.TryParse(MinSpeedTextBox.Text, out var ms) ? ms : 0
            };
            await _protocolService.SetPositionConfigAsync(newPosition);
            await System.Threading.Tasks.Task.Delay(300);

            // LoRa Config — clone to preserve all fields, only update what we show
            if (_loadedLora != null)
            {
                var newLora = _loadedLora.Clone();
                // Only update region if a selection was made (keepIfNotFound=true means original stays if unrecognized)
                var regionTag = GetComboBoxTag(LoraRegionComboBox);
                newLora.Region = (Meshtastic.Protobufs.Region)regionTag;
                newLora.UsePreset = LoraUsePresetCheckBox.IsChecked == true;
                newLora.ModemPreset = (Meshtastic.Protobufs.ModemPreset)GetComboBoxTag(LoraPresetComboBox);
                newLora.HopLimit = uint.TryParse(HopLimitTextBox.Text, out var hl) ? hl : 3;
                newLora.TxEnabled = TxEnabledCheckBox.IsChecked == true;
                newLora.TxPower = int.TryParse(TxPowerTextBox.Text, out var tp) ? tp : 0;
                await _protocolService.SetLoRaConfigAsync(newLora);
                await System.Threading.Tasks.Task.Delay(300);
            }

            // MQTT Config
            var newMqtt = new MQTTConfig
            {
                Enabled = MqttEnabledCheckBox.IsChecked == true,
                Address = MqttAddressTextBox.Text.Trim(),
                Username = MqttUsernameTextBox.Text.Trim(),
                Password = MqttPasswordBox.Password,
                EncryptionEnabled = MqttEncryptionCheckBox.IsChecked == true,
                JsonEnabled = MqttJsonCheckBox.IsChecked == true,
                TlsEnabled = MqttTlsCheckBox.IsChecked == true,
                Root = MqttRootTextBox.Text.Trim(),
                ProxyToClientEnabled = MqttProxyCheckBox.IsChecked == true,
                MapReportingEnabled = MqttMapReportingCheckBox.IsChecked == true
            };
            await _protocolService.SetMqttConfigAsync(newMqtt);
            await System.Threading.Tasks.Task.Delay(300);

            // Telemetry Config
            var newTelemetry = new TelemetryConfig
            {
                DeviceUpdateInterval = HumanTimeToSeconds(DeviceTelemetryIntervalTextBox.Text),
                EnvironmentUpdateInterval = HumanTimeToSeconds(EnvironmentIntervalTextBox.Text),
                EnvironmentDisplayFahrenheit = FahrenheitCheckBox.IsChecked == true,
                EnvironmentMeasurementEnabled = EnvironmentMeasurementCheckBox.IsChecked == true,
                AirQualityInterval = HumanTimeToSeconds(AirQualityIntervalTextBox.Text),
                PowerUpdateInterval = HumanTimeToSeconds(PowerIntervalTextBox.Text)
            };
            await _protocolService.SetTelemetryConfigAsync(newTelemetry);
            await System.Threading.Tasks.Task.Delay(300);

            // Bluetooth Config
            var newBluetooth = new BluetoothConfig
            {
                Enabled = BtEnabledCheckBox.IsChecked == true,
                Mode = (uint)GetComboBoxTag(BtModeComboBox),
                FixedPin = uint.TryParse(BtFixedPinTextBox.Text.Trim(), out var pin) ? pin : 0
            };
            await _protocolService.SetBluetoothConfigAsync(newBluetooth);

            UpdateStatus("Gespeichert! Gerät wendet Änderungen an...");
            Services.Logger.WriteLine("NodeConfigWindow: All configs saved");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Fehler beim Speichern: {ex.Message}");
            Services.Logger.WriteLine($"NodeConfigWindow save error: {ex.Message}");
            MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void BtModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Enable fixed-PIN field only when mode 1 (FIXED_PIN) is selected
        if (BtFixedPinTextBox != null)
            BtFixedPinTextBox.IsEnabled = GetComboBoxTag(BtModeComboBox) == 1;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
