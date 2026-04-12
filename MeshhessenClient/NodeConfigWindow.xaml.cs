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
    private User?                      _loadedOwner;
    private DeviceConfig?              _loadedDevice;
    private PositionConfig?            _loadedPosition;
    private LoRaConfig?                _loadedLora;
    private PowerConfig?               _loadedPower;
    private NetworkConfig?             _loadedNetwork;
    private DisplayConfig?             _loadedDisplay;
    private MQTTConfig?                _loadedMqtt;
    private TelemetryConfig?           _loadedTelemetry;
    private BluetoothConfig?           _loadedBluetooth;
    private NeighborInfoConfig?        _loadedNeighborInfo;
    private StoreForwardConfig?        _loadedStoreForward;
    private ExternalNotificationConfig? _loadedExtNotif;
    private CannedMessageConfig?       _loadedCannedMsg;
    private RangeTestConfig?           _loadedRangeTest;
    private SerialConfig?              _loadedSerial;

    private int _pendingRequests = 0;

    // Map Tag → content panel
    private Dictionary<string, FrameworkElement> _panels = new();

    public NodeConfigWindow(MeshtasticProtocolService protocolService)
    {
        InitializeComponent();
        _protocolService = protocolService;

        // Wire up events
        _protocolService.OwnerReceived                    += OnOwnerReceived;
        _protocolService.DeviceConfigReceived             += OnDeviceConfigReceived;
        _protocolService.PositionConfigReceived           += OnPositionConfigReceived;
        _protocolService.LoRaConfigReceived               += OnLoRaConfigReceived;
        _protocolService.PowerConfigReceived              += OnPowerConfigReceived;
        _protocolService.NetworkConfigReceived            += OnNetworkConfigReceived;
        _protocolService.DisplayConfigReceived            += OnDisplayConfigReceived;
        _protocolService.MqttConfigReceived               += OnMqttConfigReceived;
        _protocolService.TelemetryConfigReceived          += OnTelemetryConfigReceived;
        _protocolService.BluetoothConfigReceived          += OnBluetoothConfigReceived;
        _protocolService.NeighborInfoConfigReceived       += OnNeighborInfoConfigReceived;
        _protocolService.StoreForwardConfigReceived       += OnStoreForwardConfigReceived;
        _protocolService.ExternalNotificationConfigReceived += OnExtNotifConfigReceived;
        _protocolService.CannedMessageConfigReceived      += OnCannedMsgConfigReceived;
        _protocolService.RangeTestConfigReceived          += OnRangeTestConfigReceived;
        _protocolService.SerialConfigReceived             += OnSerialConfigReceived;

        Closed += (s, e) =>
        {
            _protocolService.OwnerReceived                    -= OnOwnerReceived;
            _protocolService.DeviceConfigReceived             -= OnDeviceConfigReceived;
            _protocolService.PositionConfigReceived           -= OnPositionConfigReceived;
            _protocolService.LoRaConfigReceived               -= OnLoRaConfigReceived;
            _protocolService.PowerConfigReceived              -= OnPowerConfigReceived;
            _protocolService.NetworkConfigReceived            -= OnNetworkConfigReceived;
            _protocolService.DisplayConfigReceived            -= OnDisplayConfigReceived;
            _protocolService.MqttConfigReceived               -= OnMqttConfigReceived;
            _protocolService.TelemetryConfigReceived          -= OnTelemetryConfigReceived;
            _protocolService.BluetoothConfigReceived          -= OnBluetoothConfigReceived;
            _protocolService.NeighborInfoConfigReceived       -= OnNeighborInfoConfigReceived;
            _protocolService.StoreForwardConfigReceived       -= OnStoreForwardConfigReceived;
            _protocolService.ExternalNotificationConfigReceived -= OnExtNotifConfigReceived;
            _protocolService.CannedMessageConfigReceived      -= OnCannedMsgConfigReceived;
            _protocolService.RangeTestConfigReceived          -= OnRangeTestConfigReceived;
            _protocolService.SerialConfigReceived             -= OnSerialConfigReceived;
        };

        Loaded += (s, e) =>
        {
            // Build panel map from NavList tags
            _panels = new Dictionary<string, FrameworkElement>
            {
                ["User"]        = PanelUser,
                ["Device"]      = PanelDevice,
                ["Lora"]        = PanelLora,
                ["Position"]    = PanelPosition,
                ["Power"]       = PanelPower,
                ["Network"]     = PanelNetwork,
                ["Display"]     = PanelDisplay,
                ["Bluetooth"]   = PanelBluetooth,
                ["Mqtt"]        = PanelMqtt,
                ["Telemetry"]   = PanelTelemetry,
                ["NeighborInfo"] = PanelNeighborInfo,
                ["StoreForward"] = PanelStoreForward,
                ["ExtNotif"]    = PanelExtNotif,
                ["CannedMsg"]   = PanelCannedMsg,
                ["RangeTest"]   = PanelRangeTest,
                ["Serial"]      = PanelSerial,
            };

            // Select first real item (User)
            foreach (ListBoxItem item in NavList.Items)
                if (item.Tag is string) { NavList.SelectedItem = item; break; }

            _ = RequestAllConfigsAsync();
        };
    }

    // ========== Navigation ==========

    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not ListBoxItem item || item.Tag is not string tag) return;

        foreach (var panel in _panels.Values)
            panel.Visibility = Visibility.Collapsed;

        if (_panels.TryGetValue(tag, out var selected))
            selected.Visibility = Visibility.Visible;
    }

    // ========== Config loading ==========

    private async System.Threading.Tasks.Task RequestAllConfigsAsync()
    {
        try
        {
            _pendingRequests = 16;
            UpdateStatus("Lade Konfiguration...");
            SaveButton.IsEnabled = false;

            await _protocolService.RequestOwnerAsync();
            await Delay();
            await _protocolService.RequestDeviceConfigAsync();
            await Delay();
            await _protocolService.RequestLoRaConfigAsync();
            await Delay();
            await _protocolService.RequestPositionConfigAsync();
            await Delay();
            await _protocolService.RequestPowerConfigAsync();
            await Delay();
            await _protocolService.RequestNetworkConfigAsync();
            await Delay();
            await _protocolService.RequestDisplayConfigAsync();
            await Delay();
            await _protocolService.RequestBluetoothConfigAsync();
            await Delay();
            await _protocolService.RequestMqttConfigAsync();
            await Delay();
            await _protocolService.RequestTelemetryConfigAsync();
            await Delay();
            await _protocolService.RequestNeighborInfoConfigAsync();
            await Delay();
            await _protocolService.RequestStoreForwardConfigAsync();
            await Delay();
            await _protocolService.RequestExternalNotificationConfigAsync();
            await Delay();
            await _protocolService.RequestCannedMessageConfigAsync();
            await Delay();
            await _protocolService.RequestRangeTestConfigAsync();
            await Delay();
            await _protocolService.RequestSerialConfigAsync();
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => UpdateStatus($"Fehler beim Laden: {ex.Message}"));
        }
    }

    private static System.Threading.Tasks.Task Delay() =>
        System.Threading.Tasks.Task.Delay(200);

    private void CheckAllLoaded()
    {
        _pendingRequests--;
        if (_pendingRequests <= 0)
        {
            var msg = (string?)Application.Current.TryFindResource("StrNcConfigLoaded")
                      ?? "Configuration loaded.";
            UpdateStatus(msg);
            SaveButton.IsEnabled = true;
        }
    }

    private void WifiPskToggle_Checked(object sender, RoutedEventArgs e)
    {
        // Reveal: hide mask, show real TextBox
        WifiPskMaskBox.Visibility = Visibility.Collapsed;
        WifiPskBox.Visibility     = Visibility.Visible;
        WifiPskBox.Focus();
        WifiPskToggle.ToolTip = Application.Current.TryFindResource("StrNcHidePassword");
    }

    private void WifiPskToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        // Hide: update mask to show bullets if password was typed, then swap
        WifiPskMaskBox.Text      = WifiPskBox.Text.Length > 0
            ? new string('●', Math.Min(WifiPskBox.Text.Length, 16))
            : (string?)Application.Current.TryFindResource("StrNcWifiPskHint") ?? "";
        WifiPskMaskBox.FontStyle = WifiPskBox.Text.Length > 0 ? FontStyles.Normal : FontStyles.Italic;
        WifiPskMaskBox.Foreground = WifiPskBox.Text.Length > 0
            ? SystemColors.ControlTextBrush
            : System.Windows.Media.Brushes.Gray;
        WifiPskBox.Visibility     = Visibility.Collapsed;
        WifiPskMaskBox.Visibility = Visibility.Visible;
        WifiPskToggle.ToolTip = Application.Current.TryFindResource("StrNcShowPassword");
    }

    private void UpdateStatus(string text) =>
        Dispatcher.BeginInvoke(() => StatusText.Text = text);

    // ========== Receive handlers ==========

    private void OnOwnerReceived(object? sender, User user)
    {
        _loadedOwner = user;
        Dispatcher.BeginInvoke(() =>
        {
            LongNameTextBox.Text  = user.LongName;
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
            NodeInfoIntervalTextBox.Text  = SecondsToHumanTime(config.NodeInfoBroadcastSecs);
            TzdefTextBox.Text             = config.Tzdef;
            LedHeartbeatCheckBox.IsChecked = config.LedHeartbeatDisabled;
            DoubleTapCheckBox.IsChecked   = config.DoubleTapAsButtonPress;
            CheckAllLoaded();
        });
    }

    private void OnPositionConfigReceived(object? sender, PositionConfig config)
    {
        _loadedPosition = config;
        Dispatcher.BeginInvoke(() =>
        {
            SelectComboBoxByTag(GpsModeComboBox, (int)config.GpsMode);
            PosBroadcastSecsTextBox.Text  = SecondsToHumanTime(config.PositionBroadcastSecs);
            SmartBroadcastCheckBox.IsChecked = config.PositionBroadcastSmartEnabled;
            MinDistanceTextBox.Text       = config.PositionBroadcastSmartMinimumDistance.ToString();
            MinSpeedTextBox.Text          = config.PositionBroadcastSmartMinimumSpeed.ToString();
            GpsUpdateIntervalTextBox.Text = config.GpsUpdateInterval.ToString();
            CheckAllLoaded();
        });
    }

    private void OnLoRaConfigReceived(object? sender, LoRaConfig config)
    {
        _loadedLora = config;
        Dispatcher.BeginInvoke(() =>
        {
            SelectComboBoxByTag(LoraRegionComboBox, (int)config.Region, keepIfNotFound: true);
            LoraUsePresetCheckBox.IsChecked = config.UsePreset;
            SelectComboBoxByTag(LoraPresetComboBox, (int)config.ModemPreset, keepIfNotFound: true);
            LoraPresetComboBox.IsEnabled   = config.UsePreset;
            HopLimitTextBox.Text           = config.HopLimit.ToString();
            TxEnabledCheckBox.IsChecked    = config.TxEnabled;
            TxPowerTextBox.Text            = ((int)config.TxPower).ToString();
            LoraChannelNumTextBox.Text     = config.ChannelNum.ToString();
            LoraOverrideDutyCycleCheckBox.IsChecked = config.OverrideDutyCycle;
            LoraSx126xRxBoostedCheckBox.IsChecked   = config.Sx126XRxBoostedGain;
            CheckAllLoaded();
        });
    }

    private void OnPowerConfigReceived(object? sender, PowerConfig config)
    {
        _loadedPower = config;
        Dispatcher.BeginInvoke(() =>
        {
            PowerSavingCheckBox.IsChecked   = config.IsPowerSaving;
            ShutdownAfterSecsTextBox.Text   = config.OnBatteryShutdownAfterSecs.ToString();
            WaitBtSecsTextBox.Text          = config.WaitBluetoothSecs.ToString();
            SdsSecsTextBox.Text             = config.SdsSecs.ToString();
            LsSecsTextBox.Text              = config.LsSecs.ToString();
            MinWakeSecsTextBox.Text         = config.MinWakeSecs.ToString();
            CheckAllLoaded();
        });
    }

    private void OnNetworkConfigReceived(object? sender, NetworkConfig config)
    {
        _loadedNetwork = config;
        Dispatcher.BeginInvoke(() =>
        {
            WifiEnabledCheckBox.IsChecked = config.WifiEnabled;
            WifiSsidTextBox.Text          = config.WifiSsid;
            WifiPskBox.Text               = config.WifiPsk;   // device always returns empty (security)
            NtpServerTextBox.Text         = config.NtpServer;
            CheckAllLoaded();
        });
    }

    private void OnDisplayConfigReceived(object? sender, DisplayConfig config)
    {
        _loadedDisplay = config;
        Dispatcher.BeginInvoke(() =>
        {
            ScreenOnSecsTextBox.Text          = SecondsToHumanTime(config.ScreenOnSecs);
            AutoCarouselSecsTextBox.Text      = config.AutoScreenCarouselSecs.ToString();
            CompassNorthTopCheckBox.IsChecked = config.CompassNorthTop;
            FlipScreenCheckBox.IsChecked      = config.FlipScreen;
            CheckAllLoaded();
        });
    }

    private void OnMqttConfigReceived(object? sender, MQTTConfig config)
    {
        _loadedMqtt = config;
        Dispatcher.BeginInvoke(() =>
        {
            MqttEnabledCheckBox.IsChecked    = config.Enabled;
            MqttAddressTextBox.Text          = config.Address;
            MqttUsernameTextBox.Text         = config.Username;
            MqttPasswordBox.Password         = config.Password;
            MqttEncryptionCheckBox.IsChecked = config.EncryptionEnabled;
            MqttJsonCheckBox.IsChecked       = config.JsonEnabled;
            MqttTlsCheckBox.IsChecked        = config.TlsEnabled;
            MqttRootTextBox.Text             = config.Root;
            MqttProxyCheckBox.IsChecked      = config.ProxyToClientEnabled;
            MqttMapReportingCheckBox.IsChecked = config.MapReportingEnabled;
            CheckAllLoaded();
        });
    }

    private void OnTelemetryConfigReceived(object? sender, TelemetryConfig config)
    {
        _loadedTelemetry = config;
        Dispatcher.BeginInvoke(() =>
        {
            DeviceTelemetryIntervalTextBox.Text  = SecondsToHumanTime(config.DeviceUpdateInterval);
            EnvironmentIntervalTextBox.Text      = SecondsToHumanTime(config.EnvironmentUpdateInterval);
            EnvironmentMeasurementCheckBox.IsChecked = config.EnvironmentMeasurementEnabled;
            FahrenheitCheckBox.IsChecked         = config.EnvironmentDisplayFahrenheit;
            AirQualityIntervalTextBox.Text       = SecondsToHumanTime(config.AirQualityInterval);
            PowerIntervalTextBox.Text            = SecondsToHumanTime(config.PowerUpdateInterval);
            PowerMeasurementCheckBox.IsChecked   = config.PowerMeasurementEnabled;
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
            BtFixedPinTextBox.Text      = config.FixedPin > 0 ? config.FixedPin.ToString() : string.Empty;
            BtFixedPinTextBox.IsEnabled = config.Mode == 1;
            CheckAllLoaded();
        });
    }

    private void OnNeighborInfoConfigReceived(object? sender, NeighborInfoConfig config)
    {
        _loadedNeighborInfo = config;
        Dispatcher.BeginInvoke(() =>
        {
            NiEnabledCheckBox.IsChecked = config.Enabled;
            NiIntervalTextBox.Text      = SecondsToHumanTime(config.UpdateInterval);
            CheckAllLoaded();
        });
    }

    private void OnStoreForwardConfigReceived(object? sender, StoreForwardConfig config)
    {
        _loadedStoreForward = config;
        Dispatcher.BeginInvoke(() =>
        {
            SfEnabledCheckBox.IsChecked    = config.Enabled;
            SfHeartbeatCheckBox.IsChecked  = config.Heartbeat;
            SfRecordsTextBox.Text          = config.Records.ToString();
            SfHistoryMaxTextBox.Text       = config.HistoryReturnMax.ToString();
            SfHistoryWindowTextBox.Text    = config.HistoryReturnWindow.ToString();
            CheckAllLoaded();
        });
    }

    private void OnExtNotifConfigReceived(object? sender, ExternalNotificationConfig config)
    {
        _loadedExtNotif = config;
        Dispatcher.BeginInvoke(() =>
        {
            EnEnabledCheckBox.IsChecked      = config.Enabled;
            EnOutputTextBox.Text             = config.Output.ToString();
            EnOutputMsTextBox.Text           = config.OutputMs.ToString();
            EnActiveCheckBox.IsChecked       = config.Active;
            EnAlertMessageCheckBox.IsChecked = config.AlertMessage;
            EnAlertBellCheckBox.IsChecked    = config.AlertBell;
            CheckAllLoaded();
        });
    }

    private void OnCannedMsgConfigReceived(object? sender, CannedMessageConfig config)
    {
        _loadedCannedMsg = config;
        Dispatcher.BeginInvoke(() =>
        {
            CmEnabledCheckBox.IsChecked        = config.Enabled;
            CmSendBellCheckBox.IsChecked       = config.SendBell;
            CmAllowInputSourceTextBox.Text     = config.AllowInputSource;
            CmRotary1CheckBox.IsChecked        = config.Rotary1Enabled;
            CmUpdown1CheckBox.IsChecked        = config.Updown1Enabled;
            CheckAllLoaded();
        });
    }

    private void OnRangeTestConfigReceived(object? sender, RangeTestConfig config)
    {
        _loadedRangeTest = config;
        Dispatcher.BeginInvoke(() =>
        {
            RtEnabledCheckBox.IsChecked = config.Enabled;
            RtSenderTextBox.Text        = config.Sender.ToString();
            RtSaveCheckBox.IsChecked    = config.Save;
            CheckAllLoaded();
        });
    }

    private void OnSerialConfigReceived(object? sender, SerialConfig config)
    {
        _loadedSerial = config;
        Dispatcher.BeginInvoke(() =>
        {
            SerEnabledCheckBox.IsChecked = config.Enabled;
            SerEchoCheckBox.IsChecked    = config.Echo;
            SerRxdTextBox.Text           = config.Rxd.ToString();
            SerTxdTextBox.Text           = config.Txd.ToString();
            SelectComboBoxByTag(SerBaudComboBox, (int)config.Baud);
            SerTimeoutTextBox.Text       = config.Timeout.ToString();
            SelectComboBoxByTag(SerModeComboBox, (int)config.Mode);
            CheckAllLoaded();
        });
    }

    // ========== Interval helpers ==========

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

    private static uint HumanTimeToSeconds(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return 0;
        if (uint.TryParse(input.Trim(), out var plain)) return plain;
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

    // ========== ComboBox helpers ==========

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
        if (!keepIfNotFound && cb.Items.Count > 0)
            cb.SelectedIndex = 0;
    }

    private static int GetComboBoxTag(ComboBox cb)
    {
        if (cb.SelectedItem is ComboBoxItem item && item.Tag is string s && int.TryParse(s, out int v))
            return v;
        return 0;
    }

    // ========== UI events ==========

    private void LoraUsePresetCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        LoraPresetComboBox.IsEnabled = LoraUsePresetCheckBox.IsChecked == true;
    }

    private void BtModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BtFixedPinTextBox != null)
            BtFixedPinTextBox.IsEnabled = GetComboBoxTag(BtModeComboBox) == 1;
    }

    private void TzFromWindows_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TzdefTextBox.Text = WindowsToPosixTz(TimeZoneInfo.Local);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string WindowsToPosixTz(TimeZoneInfo tz)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["W. Europe Standard Time"]        = "CET-1CEST,M3.5.0,M10.5.0/3",
            ["Central Europe Standard Time"]   = "CET-1CEST,M3.5.0,M10.5.0/3",
            ["Central European Standard Time"] = "CET-1CEST,M3.5.0,M10.5.0/3",
            ["Romance Standard Time"]          = "CET-1CEST,M3.5.0,M10.5.0/3",
            ["GTB Standard Time"]              = "EET-2EEST,M3.5.0/3,M10.5.0/4",
            ["FLE Standard Time"]              = "EET-2EEST,M3.5.0/3,M10.5.0/4",
            ["E. Europe Standard Time"]        = "EET-2EEST,M3.5.0/3,M10.5.0/4",
            ["GMT Standard Time"]              = "GMT0BST,M3.5.0/1,M10.5.0",
            ["UTC"]                            = "UTC0",
            ["Eastern Standard Time"]          = "EST5EDT,M3.2.0,M11.1.0",
            ["Central Standard Time"]          = "CST6CDT,M3.2.0,M11.1.0",
            ["Mountain Standard Time"]         = "MST7MDT,M3.2.0,M11.1.0",
            ["Pacific Standard Time"]          = "PST8PDT,M3.2.0,M11.1.0",
            ["Tokyo Standard Time"]            = "JST-9",
            ["China Standard Time"]            = "CST-8",
            ["AUS Eastern Standard Time"]      = "AEST-10AEDT,M10.1.0,M4.1.0/3",
        };
        if (map.TryGetValue(tz.Id, out var posix)) return posix;
        var offsetHours = -(int)tz.BaseUtcOffset.TotalHours;
        var offsetStr = offsetHours == 0 ? "0" : $"{offsetHours:+0;-0}";
        return tz.SupportsDaylightSavingTime ? $"STD{offsetStr}DST" : $"STD{offsetStr}";
    }

    // ========== NodeDB reset ==========

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
            Logger.WriteLine("NodeDB reset sent");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ========== Save ==========

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            (string)Application.Current.TryFindResource("StrNcSaveRebootWarning")
                ?? "Konfiguration speichern und Gerät neu starten?",
            (string)Application.Current.TryFindResource("StrNcSaveRebootTitle")
                ?? "Gerät neu starten?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            SaveButton.IsEnabled = false;
            UpdateStatus("Speichern...");

            // Begin batch edit so device applies all at once and reboots once
            await _protocolService.BeginEditSettingsAsync();
            await Delay();

            // Owner
            var newOwner = _loadedOwner != null ? _loadedOwner.Clone() : new User();
            newOwner.LongName  = LongNameTextBox.Text.Trim();
            newOwner.ShortName = ShortNameTextBox.Text.Trim();
            await _protocolService.SetOwnerAsync(newOwner);
            await Delay();

            // Device Config
            var newDevice = _loadedDevice != null ? _loadedDevice.Clone() : new DeviceConfig();
            newDevice.Role                   = (Role)GetComboBoxTag(RoleComboBox);
            newDevice.RebroadcastMode        = (RebroadcastMode)GetComboBoxTag(RebroadcastComboBox);
            newDevice.NodeInfoBroadcastSecs  = HumanTimeToSeconds(NodeInfoIntervalTextBox.Text);
            newDevice.Tzdef                  = TzdefTextBox.Text.Trim();
            newDevice.LedHeartbeatDisabled   = LedHeartbeatCheckBox.IsChecked == true;
            newDevice.DoubleTapAsButtonPress = DoubleTapCheckBox.IsChecked == true;
            await _protocolService.SetDeviceConfigAsync(newDevice);
            await Delay();

            // LoRa Config
            if (_loadedLora != null)
            {
                var newLora = _loadedLora.Clone();
                newLora.Region            = (Meshtastic.Protobufs.Region)GetComboBoxTag(LoraRegionComboBox);
                newLora.UsePreset         = LoraUsePresetCheckBox.IsChecked == true;
                newLora.ModemPreset       = (Meshtastic.Protobufs.ModemPreset)GetComboBoxTag(LoraPresetComboBox);
                newLora.HopLimit          = uint.TryParse(HopLimitTextBox.Text, out var hl) ? hl : 3;
                newLora.TxEnabled         = TxEnabledCheckBox.IsChecked == true;
                newLora.TxPower           = int.TryParse(TxPowerTextBox.Text, out var tp) ? tp : 0;
                newLora.ChannelNum        = uint.TryParse(LoraChannelNumTextBox.Text, out var cn) ? cn : 0;
                newLora.OverrideDutyCycle = LoraOverrideDutyCycleCheckBox.IsChecked == true;
                newLora.Sx126XRxBoostedGain = LoraSx126xRxBoostedCheckBox.IsChecked == true;
                await _protocolService.SetLoRaConfigAsync(newLora);
                await Delay();
            }

            // Position Config
            var newPosition = _loadedPosition != null ? _loadedPosition.Clone() : new PositionConfig();
            newPosition.GpsMode                             = (GpsMode)GetComboBoxTag(GpsModeComboBox);
            newPosition.PositionBroadcastSecs               = HumanTimeToSeconds(PosBroadcastSecsTextBox.Text);
            newPosition.PositionBroadcastSmartEnabled       = SmartBroadcastCheckBox.IsChecked == true;
            newPosition.PositionBroadcastSmartMinimumDistance = uint.TryParse(MinDistanceTextBox.Text, out var md) ? md : 0;
            newPosition.PositionBroadcastSmartMinimumSpeed  = uint.TryParse(MinSpeedTextBox.Text, out var ms) ? ms : 0;
            newPosition.GpsUpdateInterval                   = uint.TryParse(GpsUpdateIntervalTextBox.Text, out var gu) ? gu : 0;
            await _protocolService.SetPositionConfigAsync(newPosition);
            await Delay();

            // Power Config
            if (_loadedPower != null)
            {
                var newPower = _loadedPower.Clone();
                newPower.IsPowerSaving              = PowerSavingCheckBox.IsChecked == true;
                newPower.OnBatteryShutdownAfterSecs = uint.TryParse(ShutdownAfterSecsTextBox.Text, out var sa) ? sa : 0;
                newPower.WaitBluetoothSecs          = uint.TryParse(WaitBtSecsTextBox.Text, out var wb) ? wb : 0;
                newPower.SdsSecs                    = uint.TryParse(SdsSecsTextBox.Text, out var sds) ? sds : 0;
                newPower.LsSecs                     = uint.TryParse(LsSecsTextBox.Text, out var ls) ? ls : 0;
                newPower.MinWakeSecs                = uint.TryParse(MinWakeSecsTextBox.Text, out var mw) ? mw : 0;
                await _protocolService.SetPowerConfigAsync(newPower);
                await Delay();
            }

            // Network Config
            if (_loadedNetwork != null)
            {
                var newNetwork = _loadedNetwork.Clone();
                newNetwork.WifiEnabled = WifiEnabledCheckBox.IsChecked == true;
                newNetwork.WifiSsid    = WifiSsidTextBox.Text.Trim();
                newNetwork.WifiPsk     = WifiPskBox.Text.Trim();
                newNetwork.NtpServer   = NtpServerTextBox.Text.Trim();
                await _protocolService.SetNetworkConfigAsync(newNetwork);
                await Delay();
            }

            // Display Config
            if (_loadedDisplay != null)
            {
                var newDisplay = _loadedDisplay.Clone();
                newDisplay.ScreenOnSecs           = HumanTimeToSeconds(ScreenOnSecsTextBox.Text);
                newDisplay.AutoScreenCarouselSecs = uint.TryParse(AutoCarouselSecsTextBox.Text, out var carousel) ? carousel : 0;
                newDisplay.CompassNorthTop        = CompassNorthTopCheckBox.IsChecked == true;
                newDisplay.FlipScreen             = FlipScreenCheckBox.IsChecked == true;
                await _protocolService.SetDisplayConfigAsync(newDisplay);
                await Delay();
            }

            // Bluetooth Config
            var newBluetooth = _loadedBluetooth != null ? _loadedBluetooth.Clone() : new BluetoothConfig();
            newBluetooth.Enabled  = BtEnabledCheckBox.IsChecked == true;
            newBluetooth.Mode     = (uint)GetComboBoxTag(BtModeComboBox);
            newBluetooth.FixedPin = uint.TryParse(BtFixedPinTextBox.Text.Trim(), out var pin) ? pin : 0;
            await _protocolService.SetBluetoothConfigAsync(newBluetooth);
            await Delay();

            // MQTT Config
            var newMqtt = _loadedMqtt != null ? _loadedMqtt.Clone() : new MQTTConfig();
            newMqtt.Enabled             = MqttEnabledCheckBox.IsChecked == true;
            newMqtt.Address             = MqttAddressTextBox.Text.Trim();
            newMqtt.Username            = MqttUsernameTextBox.Text.Trim();
            newMqtt.Password            = MqttPasswordBox.Password;
            newMqtt.EncryptionEnabled   = MqttEncryptionCheckBox.IsChecked == true;
            newMqtt.JsonEnabled         = MqttJsonCheckBox.IsChecked == true;
            newMqtt.TlsEnabled          = MqttTlsCheckBox.IsChecked == true;
            newMqtt.Root                = MqttRootTextBox.Text.Trim();
            newMqtt.ProxyToClientEnabled = MqttProxyCheckBox.IsChecked == true;
            newMqtt.MapReportingEnabled = MqttMapReportingCheckBox.IsChecked == true;
            await _protocolService.SetMqttConfigAsync(newMqtt);
            await Delay();

            // Telemetry Config
            var newTelemetry = _loadedTelemetry != null ? _loadedTelemetry.Clone() : new TelemetryConfig();
            newTelemetry.DeviceUpdateInterval         = HumanTimeToSeconds(DeviceTelemetryIntervalTextBox.Text);
            newTelemetry.EnvironmentUpdateInterval    = HumanTimeToSeconds(EnvironmentIntervalTextBox.Text);
            newTelemetry.EnvironmentMeasurementEnabled = EnvironmentMeasurementCheckBox.IsChecked == true;
            newTelemetry.EnvironmentDisplayFahrenheit = FahrenheitCheckBox.IsChecked == true;
            newTelemetry.AirQualityInterval           = HumanTimeToSeconds(AirQualityIntervalTextBox.Text);
            newTelemetry.PowerUpdateInterval          = HumanTimeToSeconds(PowerIntervalTextBox.Text);
            newTelemetry.PowerMeasurementEnabled      = PowerMeasurementCheckBox.IsChecked == true;
            await _protocolService.SetTelemetryConfigAsync(newTelemetry);
            await Delay();

            // Neighbor Info Config
            if (_loadedNeighborInfo != null)
            {
                var newNi = _loadedNeighborInfo.Clone();
                newNi.Enabled        = NiEnabledCheckBox.IsChecked == true;
                newNi.UpdateInterval = HumanTimeToSeconds(NiIntervalTextBox.Text);
                await _protocolService.SetNeighborInfoConfigAsync(newNi);
                await Delay();
            }

            // Store & Forward Config
            if (_loadedStoreForward != null)
            {
                var newSf = _loadedStoreForward.Clone();
                newSf.Enabled           = SfEnabledCheckBox.IsChecked == true;
                newSf.Heartbeat         = SfHeartbeatCheckBox.IsChecked == true;
                newSf.Records           = uint.TryParse(SfRecordsTextBox.Text, out var sr) ? sr : 0;
                newSf.HistoryReturnMax  = uint.TryParse(SfHistoryMaxTextBox.Text, out var shm) ? shm : 0;
                newSf.HistoryReturnWindow = uint.TryParse(SfHistoryWindowTextBox.Text, out var shw) ? shw : 0;
                await _protocolService.SetStoreForwardConfigAsync(newSf);
                await Delay();
            }

            // External Notification Config
            if (_loadedExtNotif != null)
            {
                var newEn = _loadedExtNotif.Clone();
                newEn.Enabled       = EnEnabledCheckBox.IsChecked == true;
                newEn.Output        = uint.TryParse(EnOutputTextBox.Text, out var eo) ? eo : 0;
                newEn.OutputMs      = uint.TryParse(EnOutputMsTextBox.Text, out var ems) ? ems : 1000;
                newEn.Active        = EnActiveCheckBox.IsChecked == true;
                newEn.AlertMessage  = EnAlertMessageCheckBox.IsChecked == true;
                newEn.AlertBell     = EnAlertBellCheckBox.IsChecked == true;
                await _protocolService.SetExternalNotificationConfigAsync(newEn);
                await Delay();
            }

            // Canned Message Config
            if (_loadedCannedMsg != null)
            {
                var newCm = _loadedCannedMsg.Clone();
                newCm.Enabled          = CmEnabledCheckBox.IsChecked == true;
                newCm.SendBell         = CmSendBellCheckBox.IsChecked == true;
                newCm.AllowInputSource = CmAllowInputSourceTextBox.Text.Trim();
                newCm.Rotary1Enabled   = CmRotary1CheckBox.IsChecked == true;
                newCm.Updown1Enabled   = CmUpdown1CheckBox.IsChecked == true;
                await _protocolService.SetCannedMessageConfigAsync(newCm);
                await Delay();
            }

            // Range Test Config
            if (_loadedRangeTest != null)
            {
                var newRt = _loadedRangeTest.Clone();
                newRt.Enabled = RtEnabledCheckBox.IsChecked == true;
                newRt.Sender  = uint.TryParse(RtSenderTextBox.Text, out var rts) ? rts : 0;
                newRt.Save    = RtSaveCheckBox.IsChecked == true;
                await _protocolService.SetRangeTestConfigAsync(newRt);
                await Delay();
            }

            // Serial Config
            if (_loadedSerial != null)
            {
                var newSer = _loadedSerial.Clone();
                newSer.Enabled  = SerEnabledCheckBox.IsChecked == true;
                newSer.Echo     = SerEchoCheckBox.IsChecked == true;
                newSer.Rxd      = uint.TryParse(SerRxdTextBox.Text, out var srx) ? srx : 0;
                newSer.Txd      = uint.TryParse(SerTxdTextBox.Text, out var stx) ? stx : 0;
                newSer.Baud     = (uint)GetComboBoxTag(SerBaudComboBox);
                newSer.Timeout  = uint.TryParse(SerTimeoutTextBox.Text, out var sto) ? sto : 0;
                newSer.Mode     = (uint)GetComboBoxTag(SerModeComboBox);
                await _protocolService.SetSerialConfigAsync(newSer);
                await Delay();
            }

            // Commit — device applies all changes and reboots
            await _protocolService.CommitEditSettingsAsync();

            UpdateStatus("Gespeichert! Gerät wendet Änderungen an und startet neu...");
            Logger.WriteLine("NodeConfigWindow: All configs saved (commit sent)");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Fehler beim Speichern: {ex.Message}");
            Logger.WriteLine($"NodeConfigWindow save error: {ex.Message}");
            MessageBox.Show($"Fehler beim Speichern: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
