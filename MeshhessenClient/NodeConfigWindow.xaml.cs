using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class NodeConfigWindow : Window
{
    private readonly MeshtasticProtocolService _protocolService;
    private Func<(double lat, double lon)?>? _mapCenterProvider;
    private Func<(double lat, double lon)?>? _myPosProvider;
    private Func<Mapsui.Tiling.Layers.TileLayer>? _tileLayerFactory;

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
    private SecurityConfig?            _loadedSecurity;
    private readonly ChannelInfo?[] _loadedChannels = new ChannelInfo?[8];

    // Loading progress tracking
    private readonly HashSet<string> _expectedConfigs = new();
    private readonly HashSet<string> _receivedConfigs = new();
    private System.Threading.Timer? _loadingTimeoutTimer;
    private bool _loadingDone;

    // Map Tag → content panel
    private Dictionary<string, FrameworkElement> _panels = new();

    public NodeConfigWindow(MeshtasticProtocolService protocolService, Func<(double lat, double lon)?>? mapCenterProvider = null, Func<(double lat, double lon)?>? myPosProvider = null, Func<Mapsui.Tiling.Layers.TileLayer>? tileLayerFactory = null)
    {
        InitializeComponent();
        _protocolService = protocolService;
        _mapCenterProvider = mapCenterProvider;
        _myPosProvider = myPosProvider;
        _tileLayerFactory = tileLayerFactory;

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
        _protocolService.SecurityConfigReceived           += OnSecurityConfigReceived;
        _protocolService.ChannelInfoReceived              += OnChannelInfoReceived;

        Closed += (s, e) =>
        {
            _loadingTimeoutTimer?.Dispose();
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
            _protocolService.SecurityConfigReceived           -= OnSecurityConfigReceived;
            _protocolService.ChannelInfoReceived              -= OnChannelInfoReceived;
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
                ["Security"]    = PanelSecurity,
                ["Channels"]    = PanelChannels,
            };

            InitPrecisionComboBoxes();

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
            _loadingDone = false;
            _expectedConfigs.Clear();
            _receivedConfigs.Clear();

            // Register all configs we expect to receive
            // Channels are NOT included — disabled channels never fire, so they'd block completion
            _expectedConfigs.UnionWith(new[]
            {
                "owner", "device", "lora", "position", "power", "network",
                "display", "bluetooth", "mqtt", "telemetry", "neighborinfo",
                "storeforward", "extnotif", "cannedmsg", "rangetest", "serial",
                "security"
            });

            UpdateStatus(Loc("StrNcLoadingProgress", "0", _expectedConfigs.Count.ToString()));
            SaveButton.IsEnabled = false;

            // Start 30-second timeout
            _loadingTimeoutTimer?.Dispose();
            _loadingTimeoutTimer = new System.Threading.Timer(_ =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (_loadingDone) return;
                    _loadingDone = true;
                    var missing = _expectedConfigs.Except(_receivedConfigs).ToList();
                    UpdateStatus(Loc("StrNcLoadingTimeout", string.Join(", ", missing)));
                    SaveButton.IsEnabled = true;
                });
            }, null, 30000, System.Threading.Timeout.Infinite);

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
            await Delay();
            await _protocolService.RequestSecurityConfigAsync();
            await Delay();
            // Request channels 0-7
            for (int i = 0; i < 8; i++)
            {
                await _protocolService.RequestChannelConfigAsync(i);
                await Delay();
            }
        }
        catch (Exception ex)
        {
            Dispatcher.BeginInvoke(() => UpdateStatus($"Fehler beim Laden: {ex.Message}"));
        }
    }

    private static System.Threading.Tasks.Task Delay() =>
        System.Threading.Tasks.Task.Delay(200);

    private string Loc(string key, params string[] args)
    {
        var raw = (string?)Application.Current.TryFindResource(key) ?? key;
        for (int i = 0; i < args.Length; i++)
            raw = raw.Replace($"{{{i}}}", args[i]);
        return raw;
    }

    private void MarkReceived(string key)
    {
        _receivedConfigs.Add(key);
        if (_loadingDone) return;

        var rcv = _receivedConfigs.Count;
        var exp = _expectedConfigs.Count;
        UpdateStatus(Loc("StrNcLoadingProgress", rcv.ToString(), exp.ToString()));

        if (_receivedConfigs.IsSupersetOf(_expectedConfigs))
        {
            _loadingDone = true;
            _loadingTimeoutTimer?.Dispose();
            UpdateStatus(Loc("StrNcConfigLoaded") != "StrNcConfigLoaded"
                ? Loc("StrNcConfigLoaded")
                : "Konfiguration geladen.");
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
            IsLicensedCheckBox.IsChecked     = user.IsLicensed;
            IsUnmessagableCheckBox.IsChecked = user.IsUnmessagable;
            MarkReceived("owner");
        });
    }

    private void OnDeviceConfigReceived(object? sender, DeviceConfig config)
    {
        _loadedDevice = config;
        Dispatcher.BeginInvoke(() =>
        {
            SelectComboBoxByTag(RoleComboBox, (int)config.Role);
            SelectComboBoxByTag(RebroadcastComboBox, (int)config.RebroadcastMode);
            NodeInfoIntervalTextBox.Text    = SecondsToHumanTime(config.NodeInfoBroadcastSecs);
            TzdefTextBox.Text               = config.Tzdef;
            LedHeartbeatCheckBox.IsChecked  = config.LedHeartbeatDisabled;
            DoubleTapCheckBox.IsChecked     = config.DoubleTapAsButtonPress;
            DisableTripleClickCheckBox.IsChecked = config.DisableTripleClick;
            ButtonGpioTextBox.Text          = config.ButtonGpio > 0 ? config.ButtonGpio.ToString() : string.Empty;
            BuzzerGpioTextBox.Text          = config.BuzzerGpio > 0 ? config.BuzzerGpio.ToString() : string.Empty;
            MarkReceived("device");
        });
    }

    private void OnPositionConfigReceived(object? sender, PositionConfig config)
    {
        _loadedPosition = config;
        Dispatcher.BeginInvoke(() =>
        {
            SelectComboBoxByTag(GpsModeComboBox, (int)config.GpsMode);
            FixedPositionCheckBox.IsChecked  = config.FixedPosition;
            FixedPositionPanel.Visibility    = config.FixedPosition ? Visibility.Visible : Visibility.Collapsed;
            PosBroadcastSecsTextBox.Text     = SecondsToHumanTime(config.PositionBroadcastSecs);
            SmartBroadcastCheckBox.IsChecked = config.PositionBroadcastSmartEnabled;
            MinDistanceTextBox.Text          = config.BroadcastSmartMinimumDistance.ToString();
            MinIntervalSecsTextBox.Text      = config.BroadcastSmartMinimumIntervalSecs.ToString();
            GpsUpdateIntervalTextBox.Text    = config.GpsUpdateInterval.ToString();
            GpsRxGpioTextBox.Text            = config.RxGpio > 0 ? config.RxGpio.ToString() : string.Empty;
            GpsTxGpioTextBox.Text            = config.TxGpio > 0 ? config.TxGpio.ToString() : string.Empty;
            GpsEnGpioTextBox.Text            = config.GpsEnGpio > 0 ? config.GpsEnGpio.ToString() : string.Empty;
            MarkReceived("position");
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
            LoraPresetComboBox.IsEnabled    = config.UsePreset;
            HopLimitTextBox.Text            = config.HopLimit.ToString();
            TxEnabledCheckBox.IsChecked     = config.TxEnabled;
            TxPowerTextBox.Text             = config.TxPower.ToString();
            LoraChannelNumTextBox.Text      = config.ChannelNum.ToString();
            LoraOverrideDutyCycleCheckBox.IsChecked = config.OverrideDutyCycle;
            LoraSx126xRxBoostedCheckBox.IsChecked   = config.Sx126XRxBoostedGain;
            LoraIgnoreMqttCheckBox.IsChecked = config.IgnoreMqtt;
            LoraOkToMqttCheckBox.IsChecked   = config.ConfigOkToMqtt;
            LoraOverrideFreqTextBox.Text     = config.OverrideFrequency > 0 ? config.OverrideFrequency.ToString("F3") : string.Empty;
            MarkReceived("lora");
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
            AdcMultTextBox.Text             = config.AdcMultiplierOverride > 0 ? config.AdcMultiplierOverride.ToString("F2") : string.Empty;
            BatteryInaAddrTextBox.Text      = config.DeviceBatteryInaAddress > 0 ? $"0x{config.DeviceBatteryInaAddress:X2}" : string.Empty;
            MarkReceived("power");
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
            EthEnabledCheckBox.IsChecked  = config.EthEnabled;
            SelectComboBoxByTag(AddressModeComboBox, (int)config.AddressMode);
            RsyslogServerTextBox.Text     = config.RsyslogServer;
            MarkReceived("network");
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
            HeadingBoldCheckBox.IsChecked     = config.HeadingBold;
            WakeOnTapCheckBox.IsChecked       = config.WakeOnTapOrMotion;
            Use12hClockCheckBox.IsChecked     = config.Use12HClock;
            SelectComboBoxByTag(DisplayUnitsComboBox, (int)config.Units);
            SelectComboBoxByTag(DisplayModeComboBox, (int)config.Displaymode);
            SelectComboBoxByTag(OledTypeComboBox, (int)config.Oled);
            SelectComboBoxByTag(CompassOrientationComboBox, (int)config.CompassOrientation);
            MarkReceived("display");
        });
    }

    private void OnMqttConfigReceived(object? sender, MQTTConfig config)
    {
        _loadedMqtt = config;
        Dispatcher.BeginInvoke(() =>
        {
            MqttEnabledCheckBox.IsChecked      = config.Enabled;
            MqttAddressTextBox.Text            = config.Address;
            MqttUsernameTextBox.Text           = config.Username;
            MqttPasswordBox.Text               = config.Password;
            MqttEncryptionCheckBox.IsChecked   = config.EncryptionEnabled;
            MqttJsonCheckBox.IsChecked         = config.JsonEnabled;
            MqttTlsCheckBox.IsChecked          = config.TlsEnabled;
            MqttRootTextBox.Text               = config.Root;
            MqttProxyCheckBox.IsChecked        = config.ProxyToClientEnabled;
            MqttMapReportingCheckBox.IsChecked = config.MapReportingEnabled;
            SelectComboBoxByTag(MqttMapReportPrecisionComboBox, (int)config.MapReportPrecision);
            MarkReceived("mqtt");
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
            MarkReceived("telemetry");
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
            MarkReceived("bluetooth");
        });
    }

    private void OnNeighborInfoConfigReceived(object? sender, NeighborInfoConfig config)
    {
        _loadedNeighborInfo = config;
        Dispatcher.BeginInvoke(() =>
        {
            NiEnabledCheckBox.IsChecked = config.Enabled;
            NiIntervalTextBox.Text      = SecondsToHumanTime(config.UpdateInterval);
            MarkReceived("neighborinfo");
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
            MarkReceived("storeforward");
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
            MarkReceived("extnotif");
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
            MarkReceived("cannedmsg");
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
            MarkReceived("rangetest");
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
            MarkReceived("serial");
        });
    }

    private void OnSecurityConfigReceived(object? sender, SecurityConfig config)
    {
        _loadedSecurity = config;
        Dispatcher.BeginInvoke(() =>
        {
            // Public key: hex display (read-only)
            SecPublicKeyTextBox.Text = config.PublicKey != null && config.PublicKey.Length > 0
                ? BitConverter.ToString(config.PublicKey.ToByteArray()).Replace("-", "").ToLowerInvariant()
                : string.Empty;

            // Private key: stored in box but masked; toggle reveals it
            SecPrivKeyBox.Text = config.PrivateKey != null && config.PrivateKey.Length > 0
                ? BitConverter.ToString(config.PrivateKey.ToByteArray()).Replace("-", "").ToLowerInvariant()
                : string.Empty;
            // Ensure masked view is visible
            SecPrivKeyToggle.IsChecked = false;
            SecPrivKeyMaskBox.Visibility = Visibility.Visible;
            SecPrivKeyBox.Visibility     = Visibility.Collapsed;

            // Admin keys: separate fields
            var keys = config.AdminKey.ToList();
            SecAdminKey1TextBox.Text = keys.Count > 0 ? Convert.ToBase64String(keys[0].ToByteArray()) : string.Empty;
            SecAdminKey2TextBox.Text = keys.Count > 1 ? Convert.ToBase64String(keys[1].ToByteArray()) : string.Empty;
            SecAdminKey3TextBox.Text = keys.Count > 2 ? Convert.ToBase64String(keys[2].ToByteArray()) : string.Empty;

            SecSerialEnabledCheckBox.IsChecked = config.SerialEnabled;
            SecDebugLogCheckBox.IsChecked      = config.DebugLogApiEnabled;
            SecIsManagedCheckBox.IsChecked     = config.IsManaged;
            SecAdminChannelCheckBox.IsChecked  = config.AdminChannelEnabled;
            MarkReceived("security");
        });
    }

    private void SecPrivKeyToggle_Checked(object sender, RoutedEventArgs e)
    {
        SecPrivKeyMaskBox.Visibility = Visibility.Collapsed;
        SecPrivKeyBox.Visibility     = Visibility.Visible;
        SecPrivKeyBox.Focus();
        SecPrivKeyToggle.ToolTip = Application.Current.TryFindResource("StrNcHidePassword");
    }

    private void SecPrivKeyToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        SecPrivKeyBox.Visibility     = Visibility.Collapsed;
        SecPrivKeyMaskBox.Visibility = Visibility.Visible;
        SecPrivKeyToggle.ToolTip = Application.Current.TryFindResource("StrNcShowPassword");
    }

    private void OnChannelInfoReceived(object? sender, ChannelInfo info)
    {
        if (info.Index < 0 || info.Index >= 8) return;
        _loadedChannels[info.Index] = info;

        Dispatcher.BeginInvoke(() =>
        {
            bool isActive = !string.Equals(info.Role, "Disabled", StringComparison.OrdinalIgnoreCase);
            var name = string.IsNullOrEmpty(info.Name) ? (isActive ? $"Ch {info.Index}" : "—") : info.Name;

            (TextBlock role, TextBlock nameTb, CheckBox up, CheckBox dn, ComboBox prec, Grid row) = info.Index switch
            {
                0 => (Ch0Role, Ch0Name, Ch0Uplink, Ch0Downlink, Ch0Precision, ChRow0),
                1 => (Ch1Role, Ch1Name, Ch1Uplink, Ch1Downlink, Ch1Precision, ChRow1),
                2 => (Ch2Role, Ch2Name, Ch2Uplink, Ch2Downlink, Ch2Precision, ChRow2),
                3 => (Ch3Role, Ch3Name, Ch3Uplink, Ch3Downlink, Ch3Precision, ChRow3),
                4 => (Ch4Role, Ch4Name, Ch4Uplink, Ch4Downlink, Ch4Precision, ChRow4),
                5 => (Ch5Role, Ch5Name, Ch5Uplink, Ch5Downlink, Ch5Precision, ChRow5),
                6 => (Ch6Role, Ch6Name, Ch6Uplink, Ch6Downlink, Ch6Precision, ChRow6),
                _ => (Ch7Role, Ch7Name, Ch7Uplink, Ch7Downlink, Ch7Precision, ChRow7),
            };
            role.Text = info.Role;
            nameTb.Text = name;
            up.IsChecked = info.Uplink;
            dn.IsChecked = info.Downlink;
            SelectComboBoxByTag(prec, (int)info.PositionPrecision);
            // Dim disabled channels visually but keep all controls interactive
            row.Opacity = isActive ? 1.0 : 0.45;
        });
    }

    private static bool IsValidHex(string s) =>
        s.Length % 2 == 0 && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));

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

    // ========== Precision ComboBox helpers ==========

    private static readonly (string LabelKey, string Distance, uint Value)[] PrecisionOptions =
    {
        ("StrNcPrecNone", "",       0),
        ("",              "~23km",  10),
        ("",              "~11km",  11),
        ("",              "~5.7km", 12),
        ("",              "~2.9km", 13),
        ("",              "~1.4km", 14),
        ("",              "~720m",  15),
        ("",              "~360m",  16),
        ("",              "~180m",  17),
        ("",              "~90m",   18),
        ("",              "~45m",   19),
        ("StrNcPrecExact","",       32),
    };

    private string PrecisionLabel(int index)
    {
        var (key, dist, _) = PrecisionOptions[index];
        if (!string.IsNullOrEmpty(key)) return Loc(key);
        return dist;
    }

    private void InitPrecisionComboBoxes()
    {
        foreach (var cb in new[] { Ch0Precision, Ch1Precision, Ch2Precision, Ch3Precision,
                                   Ch4Precision, Ch5Precision, Ch6Precision, Ch7Precision,
                                   MqttMapReportPrecisionComboBox })
        {
            if (cb == null) continue;
            cb.Items.Clear();
            for (int i = 0; i < PrecisionOptions.Length; i++)
            {
                var (_, _, value) = PrecisionOptions[i];
                cb.Items.Add(new ComboBoxItem { Content = PrecisionLabel(i), Tag = value.ToString() });
            }
            cb.SelectedIndex = 0;
        }
    }


    // ========== UI events ==========

    private void LoraUsePresetCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        LoraPresetComboBox.IsEnabled = LoraUsePresetCheckBox.IsChecked == true;
    }

    private void FixedPositionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (FixedPositionPanel != null)
        {
            FixedPositionPanel.Visibility = FixedPositionCheckBox.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
            if (FixedPositionCheckBox.IsChecked == true)
                RefreshMapCenterHint();
        }
    }

    private void RefreshMapCenterHint()
    {
        if (_mapCenterProvider == null || MapCenterCoordRun == null) return;
        var pos = _mapCenterProvider();
        MapCenterCoordRun.Text = pos.HasValue
            ? $"{pos.Value.lat:F5}, {pos.Value.lon:F5}"
            : "–";
    }

    private void PickFromMap_Click(object sender, RoutedEventArgs e)
    {
        // Get initial center: prefer current fixed-pos values, fall back to mapCenterProvider
        double initLat = 50.5, initLon = 9.0;
        if (double.TryParse(FixedLatTextBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsedLat) &&
            double.TryParse(FixedLonTextBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsedLon) &&
            parsedLat != 0 && parsedLon != 0)
        {
            initLat = parsedLat;
            initLon = parsedLon;
        }
        else if (_mapCenterProvider != null)
        {
            var center = _mapCenterProvider();
            if (center.HasValue) { initLat = center.Value.lat; initLon = center.Value.lon; }
        }

        var picker = new MapPickerWindow(initLat, initLon, _tileLayerFactory) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPosition.HasValue)
        {
            FixedLatTextBox.Text = picker.SelectedPosition.Value.lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            FixedLonTextBox.Text = picker.SelectedPosition.Value.lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            RefreshMapCenterHint();
        }
    }

    private void UseCurrentPosition_Click(object sender, RoutedEventArgs e)
    {
        if (_myPosProvider == null)
        {
            MessageBox.Show(Loc("StrHint"), Loc("StrHint"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var pos = _myPosProvider();
        if (pos == null || (pos.Value.lat == 0 && pos.Value.lon == 0))
        {
            MessageBox.Show(Loc("StrHint"), Loc("StrHint"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        FixedLatTextBox.Text = pos.Value.lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        FixedLonTextBox.Text = pos.Value.lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        RefreshMapCenterHint();
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
            newOwner.LongName        = LongNameTextBox.Text.Trim();
            newOwner.ShortName       = ShortNameTextBox.Text.Trim();
            newOwner.IsLicensed      = IsLicensedCheckBox.IsChecked == true;
            newOwner.IsUnmessagable  = IsUnmessagableCheckBox.IsChecked == true;
            await _protocolService.SetOwnerAsync(newOwner);
            await Delay();

            // Device Config
            if (_loadedDevice != null)
            {
                var newDevice = _loadedDevice.Clone();
                newDevice.Role                   = (Role)GetComboBoxTag(RoleComboBox);
                newDevice.RebroadcastMode        = (RebroadcastMode)GetComboBoxTag(RebroadcastComboBox);
                newDevice.NodeInfoBroadcastSecs  = HumanTimeToSeconds(NodeInfoIntervalTextBox.Text);
                newDevice.Tzdef                  = TzdefTextBox.Text.Trim();
                newDevice.LedHeartbeatDisabled   = LedHeartbeatCheckBox.IsChecked == true;
                newDevice.DoubleTapAsButtonPress = DoubleTapCheckBox.IsChecked == true;
                newDevice.DisableTripleClick     = DisableTripleClickCheckBox.IsChecked == true;
                newDevice.ButtonGpio             = uint.TryParse(ButtonGpioTextBox.Text, out var bgp) ? bgp : 0;
                newDevice.BuzzerGpio             = uint.TryParse(BuzzerGpioTextBox.Text, out var bzg) ? bzg : 0;
                await _protocolService.SetDeviceConfigAsync(newDevice);
                await Delay();
            }
            if (_loadedLora != null)
            {
                var newLora = _loadedLora.Clone();
                newLora.Region              = (Meshtastic.Protobufs.Region)GetComboBoxTag(LoraRegionComboBox);
                newLora.UsePreset           = LoraUsePresetCheckBox.IsChecked == true;
                newLora.ModemPreset         = (Meshtastic.Protobufs.ModemPreset)GetComboBoxTag(LoraPresetComboBox);
                newLora.HopLimit            = uint.TryParse(HopLimitTextBox.Text, out var hl) ? hl : 3;
                newLora.TxEnabled           = TxEnabledCheckBox.IsChecked == true;
                newLora.TxPower             = int.TryParse(TxPowerTextBox.Text, out var tp) ? tp : 0;
                newLora.ChannelNum          = uint.TryParse(LoraChannelNumTextBox.Text, out var cn) ? cn : 0;
                newLora.OverrideDutyCycle   = LoraOverrideDutyCycleCheckBox.IsChecked == true;
                newLora.Sx126XRxBoostedGain = LoraSx126xRxBoostedCheckBox.IsChecked == true;
                newLora.IgnoreMqtt          = LoraIgnoreMqttCheckBox.IsChecked == true;
                newLora.ConfigOkToMqtt      = LoraOkToMqttCheckBox.IsChecked == true;
                newLora.OverrideFrequency   = float.TryParse(LoraOverrideFreqTextBox.Text, out var ovf) ? ovf : 0f;
                await _protocolService.SetLoRaConfigAsync(newLora);
                await Delay();
            }

            // Position Config
            if (_loadedPosition != null)
            {
                var newPosition = _loadedPosition.Clone();
                newPosition.GpsMode                           = (GpsMode)GetComboBoxTag(GpsModeComboBox);
                newPosition.FixedPosition                     = FixedPositionCheckBox.IsChecked == true;
                newPosition.PositionBroadcastSecs             = HumanTimeToSeconds(PosBroadcastSecsTextBox.Text);
                newPosition.PositionBroadcastSmartEnabled     = SmartBroadcastCheckBox.IsChecked == true;
                newPosition.BroadcastSmartMinimumDistance     = uint.TryParse(MinDistanceTextBox.Text, out var md) ? md : 0;
                newPosition.BroadcastSmartMinimumIntervalSecs = uint.TryParse(MinIntervalSecsTextBox.Text, out var mis) ? mis : 0;
                newPosition.GpsUpdateInterval                 = uint.TryParse(GpsUpdateIntervalTextBox.Text, out var gu) ? gu : 0;
                newPosition.RxGpio                            = uint.TryParse(GpsRxGpioTextBox.Text, out var rxg) ? rxg : 0;
                newPosition.TxGpio                            = uint.TryParse(GpsTxGpioTextBox.Text, out var txg) ? txg : 0;
                newPosition.GpsEnGpio                         = uint.TryParse(GpsEnGpioTextBox.Text, out var eng) ? eng : 0;
                await _protocolService.SetPositionConfigAsync(newPosition);
                await Delay();

                // Feste Position übertragen wenn aktiviert und Koordinaten eingegeben
                if (newPosition.FixedPosition &&
                    double.TryParse(FixedLatTextBox.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double lat) &&
                    double.TryParse(FixedLonTextBox.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double lon))
                {
                    int alt = int.TryParse(FixedAltTextBox.Text, out var a) ? a : 0;
                    await _protocolService.SetFixedPositionAsync(lat, lon, alt);
                    await Delay();
                }
            }

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
                newPower.AdcMultiplierOverride       = float.TryParse(AdcMultTextBox.Text, out var adc) ? adc : 0f;
                // Battery INA address: accept hex (0x..) or decimal
                var inaText = BatteryInaAddrTextBox.Text.Trim();
                newPower.DeviceBatteryInaAddress = inaText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? (uint.TryParse(inaText[2..], System.Globalization.NumberStyles.HexNumber, null, out var inaH) ? inaH : 0)
                    : (uint.TryParse(inaText, out var inaD) ? inaD : 0);
                await _protocolService.SetPowerConfigAsync(newPower);
                await Delay();
            }

            // Network Config
            if (_loadedNetwork != null)
            {
                var newNetwork = _loadedNetwork.Clone();
                newNetwork.WifiEnabled  = WifiEnabledCheckBox.IsChecked == true;
                newNetwork.WifiSsid     = WifiSsidTextBox.Text.Trim();
                newNetwork.WifiPsk      = WifiPskBox.Text.Trim();
                newNetwork.NtpServer    = NtpServerTextBox.Text.Trim();
                newNetwork.EthEnabled   = EthEnabledCheckBox.IsChecked == true;
                newNetwork.AddressMode  = (uint)GetComboBoxTag(AddressModeComboBox);
                newNetwork.RsyslogServer = RsyslogServerTextBox.Text.Trim();
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
                newDisplay.HeadingBold            = HeadingBoldCheckBox.IsChecked == true;
                newDisplay.WakeOnTapOrMotion      = WakeOnTapCheckBox.IsChecked == true;
                newDisplay.Use12HClock            = Use12hClockCheckBox.IsChecked == true;
                newDisplay.Units                  = (uint)GetComboBoxTag(DisplayUnitsComboBox);
                newDisplay.Displaymode            = (uint)GetComboBoxTag(DisplayModeComboBox);
                newDisplay.Oled                   = (uint)GetComboBoxTag(OledTypeComboBox);
                newDisplay.CompassOrientation     = (uint)GetComboBoxTag(CompassOrientationComboBox);
                await _protocolService.SetDisplayConfigAsync(newDisplay);
                await Delay();
            }

            // Bluetooth Config
            if (_loadedBluetooth != null)
            {
                var newBluetooth = _loadedBluetooth.Clone();
                newBluetooth.Enabled  = BtEnabledCheckBox.IsChecked == true;
                newBluetooth.Mode     = (uint)GetComboBoxTag(BtModeComboBox);
                newBluetooth.FixedPin = uint.TryParse(BtFixedPinTextBox.Text.Trim(), out var pin) ? pin : 0;
                await _protocolService.SetBluetoothConfigAsync(newBluetooth);
                await Delay();
            }

            // MQTT Config
            var newMqtt = _loadedMqtt != null ? _loadedMqtt.Clone() : new MQTTConfig();
            newMqtt.Enabled              = MqttEnabledCheckBox.IsChecked == true;
            newMqtt.Address              = MqttAddressTextBox.Text.Trim();
            newMqtt.Username             = MqttUsernameTextBox.Text.Trim();
            newMqtt.Password             = MqttPasswordBox.Text.Trim();
            newMqtt.EncryptionEnabled    = MqttEncryptionCheckBox.IsChecked == true;
            newMqtt.JsonEnabled          = MqttJsonCheckBox.IsChecked == true;
            newMqtt.TlsEnabled           = MqttTlsCheckBox.IsChecked == true;
            newMqtt.Root                 = MqttRootTextBox.Text.Trim();
            newMqtt.ProxyToClientEnabled = MqttProxyCheckBox.IsChecked == true;
            newMqtt.MapReportingEnabled  = MqttMapReportingCheckBox.IsChecked == true;
            newMqtt.MapReportPrecision   = (uint)GetComboBoxTag(MqttMapReportPrecisionComboBox);
            await _protocolService.SetMqttConfigAsync(newMqtt);
            await Delay();

            // Telemetry Config
            if (_loadedTelemetry != null)
            {
                var newTelemetry = _loadedTelemetry.Clone();
                newTelemetry.DeviceUpdateInterval          = HumanTimeToSeconds(DeviceTelemetryIntervalTextBox.Text);
                newTelemetry.EnvironmentUpdateInterval     = HumanTimeToSeconds(EnvironmentIntervalTextBox.Text);
                newTelemetry.EnvironmentMeasurementEnabled = EnvironmentMeasurementCheckBox.IsChecked == true;
                newTelemetry.EnvironmentDisplayFahrenheit  = FahrenheitCheckBox.IsChecked == true;
                newTelemetry.AirQualityInterval            = HumanTimeToSeconds(AirQualityIntervalTextBox.Text);
                newTelemetry.PowerUpdateInterval           = HumanTimeToSeconds(PowerIntervalTextBox.Text);
                newTelemetry.PowerMeasurementEnabled       = PowerMeasurementCheckBox.IsChecked == true;
                await _protocolService.SetTelemetryConfigAsync(newTelemetry);
                await Delay();
            }

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

            // Security Config
            if (_loadedSecurity != null)
            {
                var newSec = _loadedSecurity.Clone();
                newSec.SerialEnabled       = SecSerialEnabledCheckBox.IsChecked == true;
                newSec.DebugLogApiEnabled  = SecDebugLogCheckBox.IsChecked == true;
                newSec.IsManaged           = SecIsManagedCheckBox.IsChecked == true;
                newSec.AdminChannelEnabled = SecAdminChannelCheckBox.IsChecked == true;

                // Private key: only overwrite if user typed a new hex key (different from loaded)
                var privHex = SecPrivKeyBox.Text.Trim().Replace(" ", "");
                var loadedHex = _loadedSecurity.PrivateKey != null && _loadedSecurity.PrivateKey.Length > 0
                    ? BitConverter.ToString(_loadedSecurity.PrivateKey.ToByteArray()).Replace("-", "").ToLowerInvariant()
                    : string.Empty;
                if (!string.Equals(privHex, loadedHex, StringComparison.OrdinalIgnoreCase)
                    && privHex.Length == 64
                    && IsValidHex(privHex))
                {
                    newSec.PrivateKey = ByteString.CopyFrom(Convert.FromHexString(privHex));
                }

                // Admin keys: rebuild repeated field from 3 text boxes
                newSec.AdminKey.Clear();
                foreach (var box in new[] { SecAdminKey1TextBox.Text.Trim(), SecAdminKey2TextBox.Text.Trim(), SecAdminKey3TextBox.Text.Trim() })
                {
                    if (string.IsNullOrEmpty(box)) continue;
                    try { newSec.AdminKey.Add(ByteString.CopyFrom(Convert.FromBase64String(box))); }
                    catch { /* skip invalid */ }
                }

                await _protocolService.SetSecurityConfigAsync(newSec);
                await Delay();
            }

            // Channel settings: uplink/downlink/precision (only for active channels)
            var chControls = new (CheckBox Up, CheckBox Dn, ComboBox Prec)[]
            {
                (Ch0Uplink, Ch0Downlink, Ch0Precision),
                (Ch1Uplink, Ch1Downlink, Ch1Precision),
                (Ch2Uplink, Ch2Downlink, Ch2Precision),
                (Ch3Uplink, Ch3Downlink, Ch3Precision),
                (Ch4Uplink, Ch4Downlink, Ch4Precision),
                (Ch5Uplink, Ch5Downlink, Ch5Precision),
                (Ch6Uplink, Ch6Downlink, Ch6Precision),
                (Ch7Uplink, Ch7Downlink, Ch7Precision),
            };
            for (int i = 0; i < 8; i++)
            {
                var info = _loadedChannels[i];
                if (info == null) continue;

                // Only save active channels (Primary/Secondary)
                var loadedRole = info.Role.ToUpperInvariant() switch
                {
                    "PRIMARY"   => ChannelRole.Primary,
                    "SECONDARY" => ChannelRole.Secondary,
                    _           => ChannelRole.Disabled
                };
                if (loadedRole == ChannelRole.Disabled) continue;

                bool newUplink    = chControls[i].Up.IsChecked == true;
                bool newDownlink  = chControls[i].Dn.IsChecked == true;
                uint newPrecision = (uint)GetComboBoxTag(chControls[i].Prec);

                if (newUplink == info.Uplink && newDownlink == info.Downlink && newPrecision == info.PositionPrecision) continue;

                var ch = new Channel { Index = i, Role = loadedRole };
                var settings = new ChannelSettings { UplinkEnabled = newUplink, DownlinkEnabled = newDownlink };
                if (!string.IsNullOrEmpty(info.Psk))
                    settings.Psk = ByteString.CopyFrom(Convert.FromBase64String(info.Psk));
                settings.ModuleSettings = new ModuleSettings { PositionPrecision = newPrecision };
                ch.Settings = settings;
                await _protocolService.UpdateChannelUplinkDownlinkAsync(ch);
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
