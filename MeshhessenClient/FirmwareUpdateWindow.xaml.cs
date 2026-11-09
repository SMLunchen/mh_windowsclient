using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meshtastic.Protobufs;
using MeshhessenClient.Services;
using MeshhessenClient.Services.Firmware;
using MeshhessenClient.Services.Firmware.Dfu;

namespace MeshhessenClient;

/// <summary>What the firmware wizard needs from the main window: the live protocol service, the
/// connection it runs on, and the ability to release / re-establish that connection around the flash.</summary>
public interface IFirmwareUpdateHost
{
    MeshtasticProtocolService? Protocol { get; }
    bool IsConnected { get; }
    ConnectionParameters? ConnectionParams { get; }
    /// <summary>Intentional disconnect — must not trigger the auto-reconnect while the loader/bootloader is being driven.</summary>
    Task DisconnectForFirmwareUpdateAsync();
    /// <summary>Re-run the normal reconnect logic with the last connection parameters.</summary>
    void ReconnectAfterFirmwareUpdate();
}

/// <summary>
/// Guided firmware update (Tools tab): device & catalog → transport → firmware → pre-flight → transfer → done.
/// The wizard never writes anything to a device before the pre-flight page shows no red item.
/// </summary>
public partial class FirmwareUpdateWindow : Window
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly Version MinOtaRequestFirmware = new(2, 7, 17);
    private const int StepCount = 6;

    private readonly IFirmwareUpdateHost _host;
    private readonly FirmwareCatalogService _catalogSvc;
    private readonly FirmwareRetriever _retriever;

    private FirmwareCatalog? _catalog;
    private DeviceHardware? _hw;
    private bool _hwFromCatalog;
    private string? _currentFirmware;
    private string _pioEnv = "";
    private int _hwModelValue;
    private string _hwModelName = "";
    private ConnectionType? _connType;
    private ulong? _bleAddress;
    private string? _tcpHost;
    private bool _connected;
    private bool _wifiConfigured;
    private BleAdapterInfo? _adapter;

    private int _step = 1;
    private FirmwareUpdateMethod _method = FirmwareUpdateMethod.None;
    private FirmwareRelease? _release;
    private string? _localFile;
    private FirmwareArtifact? _artifact;
    private FileCheckResult? _fileCheck;
    private bool _preflightOk;
    private CancellationTokenSource? _cts;
    private bool _running;
    private bool _succeeded;
    private MeshtasticProtocolService? _protocolBeforeUpdate;

    private static string Loc(string key) => Application.Current.TryFindResource(key) as string ?? key;

    private static string Loc(string key, params object?[] args)
    {
        var fmt = Loc(key);
        try { return args.Length == 0 ? fmt : string.Format(fmt, args); }
        catch (FormatException) { return fmt; }
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MeshhessenClient-FirmwareWizard/1.0");
        return c;
    }

    public FirmwareUpdateWindow(IFirmwareUpdateHost host)
    {
        InitializeComponent();
        ModernWpf.Controls.Primitives.WindowHelper.SetUseModernWindowStyle(this, true);
        _host = host;
        _catalogSvc = new FirmwareCatalogService(Http);
        _retriever = new FirmwareRetriever(Http, _catalogSvc.CacheDir);
        Loaded += async (_, _) => await InitAsync();
        Closing += (_, e) =>
        {
            if (_running)
            {
                var r = MessageBox.Show(this, Loc("StrFwCancelConfirm"), Loc("StrFwTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
                _cts?.Cancel();
            }
            // Win32 quirk (same as TelemetryWindow/RemoteAdminWindow): activate the owner before the
            // active owned window is destroyed, or the main window can minimize to the taskbar.
            try { Owner?.Activate(); } catch { }
        };
    }

    // ── Step 1: device & catalog ───────────────────────────────────────────

    private async Task InitAsync()
    {
        ShowStep(1);
        ReadDeviceState();
        RenderDeviceInfo();
        _adapter = await BleAdapterQuery.GetAsync();
        RenderAdapterInfo();
        await LoadCatalogAsync();
    }

    private void ReadDeviceState()
    {
        var proto = _host.Protocol;
        _connected = _host.IsConnected && proto != null;
        _protocolBeforeUpdate = proto;
        var p = _host.ConnectionParams;
        _connType = p?.Type;
        _bleAddress = (p as BluetoothConnectionParameters)?.DeviceAddress;
        _tcpHost = (p as TcpConnectionParameters)?.Hostname;

        if (_connected && proto != null)
        {
            _currentFirmware = proto.GetFirmwareVersion();
            _pioEnv = proto.GetMyPioEnv();
            var hwEnum = proto.GetMyHardwareModel();
            _hwModelValue = (int)hwEnum;
            _hwModelName = hwEnum == HardwareModel.Unset ? "" : hwEnum.ToString();
            var net = proto.GetSnapshotConfig(Config.PayloadVariantOneofCase.Network)?.Network;
            _wifiConfigured = _connType == ConnectionType.Tcp || (net?.WifiEnabled == true && !string.IsNullOrEmpty(net.WifiSsid));
        }
        NotConnectedBox.Visibility = _connected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderDeviceInfo()
    {
        var modelTag = string.IsNullOrEmpty(_hwModelName)
            ? (_hwModelValue > 0 ? $"#{_hwModelValue}" : "")
            : $"{_hwModelName} #{_hwModelValue}";
        DevNameText.Text = _hw != null
            ? (modelTag.Length > 0 && !string.Equals(_hw.DisplayName, _hwModelName, StringComparison.OrdinalIgnoreCase)
                ? $"{_hw.DisplayName} ({modelTag})" : _hw.DisplayName)
            : modelTag.Length > 0 ? modelTag : "–";
        DevArchText.Text = _hw?.Architecture ?? (string.IsNullOrEmpty(_hwModelName) ? "–" : FirmwareCatalogService.GuessArchitecture(_hwModelName) ?? "?");
        DevFirmwareText.Text = string.IsNullOrEmpty(_currentFirmware) ? "–" : _currentFirmware;
        DevPioEnvText.Text = string.IsNullOrEmpty(_pioEnv) ? "–" : _pioEnv;
        DevConnectionText.Text = _connType switch
        {
            ConnectionType.Serial => $"USB / {((SerialConnectionParameters)_host.ConnectionParams!).PortName}",
            ConnectionType.Bluetooth => $"Bluetooth · {((BluetoothConnectionParameters)_host.ConnectionParams!).DeviceName} ({BleAddress.Format(_bleAddress ?? 0)})",
            ConnectionType.Tcp => $"TCP · {_tcpHost}:{((TcpConnectionParameters)_host.ConnectionParams!).Port}",
            _ => "–"
        } + (_connected ? "" : $" ({Loc("StrFwDisconnectedSuffix")})");

        HwUnknownBox.Visibility = _connected && _catalog is { IsEmpty: false } && !_hwFromCatalog ? Visibility.Visible : Visibility.Collapsed;
        HwUnknownText.Text = Loc("StrFwHwUnknown", string.IsNullOrEmpty(_hwModelName) ? "?" : _hwModelName, _hwModelValue);
        NotSupportedBox.Visibility = _hw is { ActivelySupported: false } ? Visibility.Visible : Visibility.Collapsed;
        BootloaderWarnBox.Visibility = _hw?.RequiresBootloaderUpgradeForOta == true && _connType == ConnectionType.Bluetooth
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderAdapterInfo()
    {
        if (_adapter == null)
        {
            BtAdapterText.Text = Loc("StrFwBtNone");
            BtMultiBox.Visibility = Visibility.Collapsed;
            return;
        }
        BtAdapterText.Text = Loc("StrFwBtActive", _adapter.RadioName ?? "?", BleAddress.Format(_adapter.Address))
            + (_adapter.LowEnergySupported ? "" : $" — {Loc("StrFwBtNoLe")}");
        if (_adapter.BluetoothRadioCount > 1)
        {
            BtMultiText.Text = Loc("StrFwBtMultiHint", _adapter.BluetoothRadioCount);
            BtMultiBox.Visibility = Visibility.Visible;
        }
        else BtMultiBox.Visibility = Visibility.Collapsed;
    }

    private async Task LoadCatalogAsync(bool force = false)
    {
        CatalogStatusText.Text = Loc("StrFwCatalogLoading");
        RefreshCatalogBtn.IsEnabled = false;
        try
        {
            _catalog = await _catalogSvc.LoadAsync(TimeSpan.FromSeconds(force ? 60 : 25), CancellationToken.None);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[FW] Catalog load crashed: {ex.Message}");
            _catalog = _catalogSvc.LoadFromCache(ex.Message);
        }
        RefreshCatalogBtn.IsEnabled = true;

        if (_catalog.IsEmpty)
        {
            CatalogStatusText.Text = Loc("StrFwCatalogNone");
            CatalogStatusText.Foreground = Brushes.OrangeRed;
            CatalogNoneBox.Visibility = Visibility.Visible;
        }
        else
        {
            CatalogStatusText.Text = _catalog.FromCache
                ? Loc("StrFwCatalogCache", _catalog.CacheTimestampUtc?.ToLocalTime().ToString("g") ?? "?")
                : Loc("StrFwCatalogOnline");
            CatalogStatusText.Foreground = _catalog.FromCache ? Brushes.DarkOrange : Brushes.Green;
            CatalogNoneBox.Visibility = Visibility.Collapsed;
        }

        _hw = _catalog.IsEmpty ? null : FirmwareCatalogService.ResolveHardware(_catalog.Hardware, _hwModelValue, _pioEnv);
        _hwFromCatalog = _hw != null;
        if (_hw == null && (!string.IsNullOrEmpty(_hwModelName) || !string.IsNullOrEmpty(_pioEnv)))
            _hw = DeviceHardware.Unknown(_hwModelValue, string.IsNullOrEmpty(_hwModelName) ? _pioEnv : _hwModelName, _pioEnv,
                FirmwareCatalogService.GuessArchitecture(_hwModelName.Length > 0 ? _hwModelName : _pioEnv) ?? "");
        RenderDeviceInfo();
        FooterStatusText.Text = "";
        UpdateNav();
    }

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e) => await LoadCatalogAsync(force: true);

    private void BootloaderLink_Click(object sender, RoutedEventArgs e) =>
        OpenUrl("https://meshtastic.org/docs/getting-started/flashing-firmware/nrf52/");

    // ── Step 2: method ─────────────────────────────────────────────────────

    private void RenderMethodPage()
    {
        bool esp = _hw?.IsEsp32 ?? false;
        bool nrf = _hw?.IsNrf52 ?? false;
        bool rp = _hw?.IsRp2040 ?? false;
        bool archKnown = _hw != null && !string.IsNullOrEmpty(_hw.Architecture);
        bool ble = _connType == ConnectionType.Bluetooth;
        var fw = FirmwareCaps.Parse(_currentFirmware);
        bool fwOk = fw == null || fw >= MinOtaRequestFirmware;

        RecoveryBox.Visibility = _connected ? Visibility.Collapsed : Visibility.Visible;

        // Wi-Fi OTA (ESP32)
        string? wifiReason = null;
        if (!_connected) wifiReason = Loc("StrFwReasonNotConnected");
        else if (archKnown && !esp) wifiReason = Loc("StrFwReasonNotEsp32");
        else if (!fwOk) wifiReason = Loc("StrFwReasonFwTooOld");
        else if (!_wifiConfigured) wifiReason = Loc("StrFwReasonNeedWifi");
        SetMethod(MethodWifiRadio, MethodWifiReason, wifiReason);

        // BLE OTA (ESP32)
        string? bleOtaReason = null;
        if (!_connected) bleOtaReason = Loc("StrFwReasonNotConnected");
        else if (archKnown && !esp) bleOtaReason = Loc("StrFwReasonNotEsp32");
        else if (!ble) bleOtaReason = Loc("StrFwReasonNeedBle");
        else if (!fwOk) bleOtaReason = Loc("StrFwReasonFwTooOld");
        SetMethod(MethodBleOtaRadio, MethodBleOtaReason, bleOtaReason);

        // BLE DFU (nRF52) — also offered as recovery when the last connection was BLE
        string? dfuReason = null;
        if (_connected && archKnown && !nrf) dfuReason = Loc("StrFwReasonNotNrf52");
        else if (_bleAddress == null) dfuReason = Loc("StrFwReasonNeedBle");
        SetMethod(MethodBleDfuRadio, MethodBleDfuReason, dfuReason);

        // UF2 (nRF52 / RP2040) — always possible when the device can show a bootloader drive
        string? uf2Reason = null;
        if (_connected && archKnown && !(nrf || rp)) uf2Reason = Loc("StrFwReasonNotUf2Arch");
        SetMethod(MethodUf2Radio, MethodUf2Reason, uf2Reason);

        Esp32SerialBox.Visibility = _connected && esp && _connType == ConnectionType.Serial && !_wifiConfigured
            ? Visibility.Visible : Visibility.Collapsed;

        // Sensible default: the first enabled option in the order Wi-Fi, BLE OTA, BLE DFU, UF2.
        if (_method == FirmwareUpdateMethod.None || !RadioFor(_method).IsEnabled)
        {
            var first = new[] { MethodWifiRadio, MethodBleOtaRadio, MethodBleDfuRadio, MethodUf2Radio }.FirstOrDefault(r => r.IsEnabled);
            if (first != null) first.IsChecked = true;
            else _method = FirmwareUpdateMethod.None;
        }
        UpdateNav();
    }

    private static void SetMethod(RadioButton radio, TextBlock reason, string? disabledReason)
    {
        radio.IsEnabled = disabledReason == null;
        reason.Text = disabledReason ?? "";
        reason.Visibility = disabledReason == null ? Visibility.Collapsed : Visibility.Visible;
        if (disabledReason != null) radio.IsChecked = false;
    }

    private RadioButton RadioFor(FirmwareUpdateMethod m) => m switch
    {
        FirmwareUpdateMethod.Esp32Wifi => MethodWifiRadio,
        FirmwareUpdateMethod.Esp32Ble => MethodBleOtaRadio,
        FirmwareUpdateMethod.Nrf52BleDfu => MethodBleDfuRadio,
        _ => MethodUf2Radio
    };

    private void Method_Checked(object sender, RoutedEventArgs e)
    {
        _method = ReferenceEquals(sender, MethodWifiRadio) ? FirmwareUpdateMethod.Esp32Wifi
            : ReferenceEquals(sender, MethodBleOtaRadio) ? FirmwareUpdateMethod.Esp32Ble
            : ReferenceEquals(sender, MethodBleDfuRadio) ? FirmwareUpdateMethod.Nrf52BleDfu
            : FirmwareUpdateMethod.Uf2Usb;
        // A method change invalidates a previously chosen local file (different container type).
        if (_localFile != null && _fileCheck != null) { _localFile = null; _fileCheck = null; LocalFileBox.Text = ""; LocalCheckList.Items.Clear(); }
        UpdateNav();
    }

    private void OpenFlasher_Click(object sender, RoutedEventArgs e) => OpenUrl(FirmwareCatalogService.WebFlasherUrl);

    // ── Step 3: firmware ───────────────────────────────────────────────────

    private void RenderFirmwarePage()
    {
        bool haveReleases = _catalog is { Releases.Count: > 0 };
        ChannelStableRadio.IsEnabled = haveReleases;
        ChannelAlphaRadio.IsEnabled = haveReleases;
        if (!haveReleases) ChannelLocalRadio.IsChecked = true;
        Channel_Checked(ChannelLocalRadio.IsChecked == true ? ChannelLocalRadio : ChannelAlphaRadio.IsChecked == true ? ChannelAlphaRadio : ChannelStableRadio, new RoutedEventArgs());
    }

    private void Channel_Checked(object sender, RoutedEventArgs e)
    {
        if (ReleaseCombo == null) return; // during InitializeComponent
        bool local = ChannelLocalRadio.IsChecked == true;
        ReleasePanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        LocalPanel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        NotesPanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        AlphaWarnBox.Visibility = ChannelAlphaRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (local)
        {
            _release = null;
            VersionLineText.Text = "";
            TargetLineText.Text = "";
            UpdateNav();
            return;
        }

        var list = (ChannelAlphaRadio.IsChecked == true ? _catalog?.Alpha : _catalog?.Stable)?.ToList() ?? new List<FirmwareRelease>();
        ReleaseCombo.ItemsSource = list;
        ReleaseCombo.DisplayMemberPath = nameof(FirmwareRelease.Title);
        NoReleasesText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReleaseCombo.SelectedIndex = list.Count > 0 ? 0 : -1;
        if (list.Count == 0) { _release = null; UpdateNav(); }
    }

    private async void ReleaseCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _release = ReleaseCombo.SelectedItem as FirmwareRelease;
        _artifact = null;
        ReleaseNotesBox.Text = _release?.ReleaseNotes ?? "";
        RenderVersionLine(_release?.Version);
        UpdateNav();
        if (_release == null) { TargetLineText.Text = ""; return; }

        var target = _hw?.EffectiveTarget ?? "";
        if (string.IsNullOrEmpty(target)) { TargetLineText.Text = Loc("StrFwTargetUnknown"); return; }
        TargetLineText.Text = Loc("StrFwTargetChecking", target);
        var rel = _release;
        try
        {
            var targets = await _retriever.GetReleaseTargetsAsync(rel, CancellationToken.None);
            if (!ReferenceEquals(rel, _release)) return;
            if (targets == null) { TargetLineText.Text = Loc("StrFwTargetUnknown"); TargetLineText.Foreground = Brushes.Gray; }
            else if (targets.Contains(target)) { TargetLineText.Text = Loc("StrFwTargetOk", target); TargetLineText.Foreground = Brushes.Green; }
            else { TargetLineText.Text = Loc("StrFwTargetMissing", target); TargetLineText.Foreground = Brushes.OrangeRed; }
        }
        catch (Exception ex)
        {
            TargetLineText.Text = Loc("StrFwTargetUnknown");
            Services.Logger.WriteLine($"[FW] Target check failed: {ex.Message}");
        }
    }

    private void RenderVersionLine(string? selectedVersion)
    {
        if (selectedVersion == null) { VersionLineText.Text = ""; return; }
        var cur = FirmwareCaps.Parse(_currentFirmware);
        var sel = FirmwareCaps.Parse(selectedVersion);
        string verdict = cur == null || sel == null ? Loc("StrFwVersionUnknown")
            : sel > cur ? Loc("StrFwVersionUpgrade")
            : sel == cur ? Loc("StrFwVersionSame")
            : Loc("StrFwVersionDowngrade");
        VersionLineText.Text = Loc("StrFwVersionLine", _currentFirmware ?? "?", selectedVersion) + $" — {verdict}";
        VersionLineText.Foreground = sel != null && cur != null && sel < cur ? Brushes.OrangeRed : Brushes.Black;
    }

    private void ReleasePage_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_release?.PageUrl)) OpenUrl(_release.PageUrl);
    }

    private void BrowseLocal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc("StrFwChannelLocal"),
            Filter = _method switch
            {
                FirmwareUpdateMethod.Esp32Wifi or FirmwareUpdateMethod.Esp32Ble => "Meshtastic ESP32 firmware (*.bin)|*.bin|All files (*.*)|*.*",
                FirmwareUpdateMethod.Nrf52BleDfu => "Nordic DFU package (*-ota.zip)|*-ota.zip;*.zip|All files (*.*)|*.*",
                FirmwareUpdateMethod.Uf2Usb => "UF2 image (*.uf2)|*.uf2|All files (*.*)|*.*",
                _ => "All files (*.*)|*.*"
            },
            InitialDirectory = Directory.Exists(_catalogSvc.CacheDir) ? _catalogSvc.CacheDir : null
        };
        if (dlg.ShowDialog(this) != true) return;
        _localFile = dlg.FileName;
        _artifact = null;
        LocalFileBox.Text = _localFile;
        LocalCheckList.Items.Clear();
        if (_hw != null)
        {
            _fileCheck = FirmwareFileInspector.Inspect(_localFile, _hw, _method);
            foreach (var item in _fileCheck.Items) LocalCheckList.Items.Add(CheckLine(item.Level, Loc(item.Key, item.Args)));
            RenderVersionLine(_fileCheck.EmbeddedVersion ?? FirmwareFileInspector.VersionFromFileName(_localFile));
        }
        else
        {
            _fileCheck = null;
            LocalCheckList.Items.Add(CheckLine(FileCheckLevel.Warning, Loc("StrFwCheckNoTarget")));
            RenderVersionLine(FirmwareFileInspector.VersionFromFileName(_localFile));
        }
        TargetLineText.Text = "";
        UpdateNav();
    }

    private void OpenCacheDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_catalogSvc.CacheDir);
            Process.Start(new ProcessStartInfo("explorer.exe", _catalogSvc.CacheDir) { UseShellExecute = true });
        }
        catch (Exception ex) { Services.Logger.WriteLine($"[FW] Open cache dir failed: {ex.Message}"); }
    }

    // ── Step 4: pre-flight ─────────────────────────────────────────────────

    private async Task RunPreflightAsync()
    {
        PreflightList.Items.Clear();
        StartBtn.IsEnabled = false;
        _preflightOk = false;
        bool hasError = false;

        void Add(FileCheckLevel level, string text)
        {
            PreflightList.Items.Add(CheckLine(level, text));
            if (level == FileCheckLevel.Error) hasError = true;
        }

        // Catalog & device
        if (_catalog == null || _catalog.IsEmpty) Add(FileCheckLevel.Warning, Loc("StrFwCheckCatalogNone"));
        else Add(FileCheckLevel.Info, _catalog.FromCache ? Loc("StrFwCatalogCache", _catalog.CacheTimestampUtc?.ToLocalTime().ToString("g") ?? "?") : Loc("StrFwCatalogOnline"));
        if (_hw is { ActivelySupported: false } && _catalog is { IsEmpty: false }) Add(FileCheckLevel.Warning, Loc("StrFwNotSupported"));
        if (_hw?.RequiresBootloaderUpgradeForOta == true && _method == FirmwareUpdateMethod.Nrf52BleDfu) Add(FileCheckLevel.Warning, Loc("StrFwBootloaderWarn"));

        // Firmware file
        if (_release != null)
        {
            PreflightProgress.Visibility = Visibility.Visible;
            PreflightProgress.IsIndeterminate = false;
            PreflightProgress.Value = 0;
            var line = CheckLine(FileCheckLevel.Info, Loc("StrFwCheckDownloading", "0 %"));
            PreflightList.Items.Add(line);
            var progress = new Progress<double>(p =>
            {
                PreflightProgress.Value = p * 100;
                line.Text = "⏳ " + Loc("StrFwCheckDownloading", $"{p * 100:0} %");
            });
            try
            {
                if (_hw == null) throw new FirmwareUpdateException("StrFwCheckNoTarget");
                _artifact = _method switch
                {
                    FirmwareUpdateMethod.Esp32Wifi or FirmwareUpdateMethod.Esp32Ble => await _retriever.GetEsp32OtaImageAsync(_release, _hw, progress, CancellationToken.None),
                    FirmwareUpdateMethod.Nrf52BleDfu => await _retriever.GetNrf52DfuPackageAsync(_release, _hw, progress, CancellationToken.None),
                    FirmwareUpdateMethod.Uf2Usb => await _retriever.GetUf2Async(_release, _hw, progress, CancellationToken.None),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"[FW] Download failed: {ex.Message}");
                _artifact = null;
            }
            PreflightProgress.Visibility = Visibility.Collapsed;
            PreflightList.Items.Remove(line);
            if (_artifact == null)
            {
                Add(FileCheckLevel.Error, Loc("StrFwCheckDownloadFailed", _hw?.EffectiveTarget ?? "?"));
            }
            else
            {
                Add(FileCheckLevel.Info, Loc("StrFwCheckDownloaded", _artifact.FileName, _artifact.Size / 1024, _artifact.Sha256Hex[..12]));
                if (_hw != null)
                    foreach (var item in FirmwareFileInspector.Inspect(_artifact.Path, _hw, _method).Items)
                        Add(item.Level, Loc(item.Key, item.Args));
            }
        }
        else if (_localFile != null)
        {
            try
            {
                _artifact = await FirmwareRetriever.DescribeAsync(_localFile, CancellationToken.None);
                Add(FileCheckLevel.Info, Loc("StrFwCheckDownloaded", _artifact.FileName, _artifact.Size / 1024, _artifact.Sha256Hex[..12]));
            }
            catch (Exception ex)
            {
                Add(FileCheckLevel.Error, Loc("StrFwCheckUnreadable", ex.Message));
            }
            if (_fileCheck != null)
                foreach (var item in _fileCheck.Items) Add(item.Level, Loc(item.Key, item.Args));
            else
                Add(FileCheckLevel.Warning, Loc("StrFwCheckNoTarget"));
        }
        else
        {
            Add(FileCheckLevel.Error, Loc("StrFwNoReleases"));
        }

        // Power
        var proto = _host.Protocol;
        if (_connected && proto != null && proto.GetKnownNodes().TryGetValue(proto.GetMyNodeId(), out var me))
        {
            if (me.IsPowered) Add(FileCheckLevel.Info, Loc("StrFwCheckBatteryOk", Loc("StrFwPowerExternal")));
            else if (me.BatteryValue is { } b && b < 30) Add(FileCheckLevel.Warning, Loc("StrFwCheckBattery", (int)b));
            else if (me.BatteryValue is { } b2) Add(FileCheckLevel.Info, Loc("StrFwCheckBatteryOk", $"{(int)b2} %"));
        }

        // Transport hints
        switch (_method)
        {
            case FirmwareUpdateMethod.Esp32Wifi: Add(FileCheckLevel.Info, Loc("StrFwCheckWifiSameNet")); break;
            case FirmwareUpdateMethod.Esp32Ble:
            case FirmwareUpdateMethod.Nrf52BleDfu:
                Add(FileCheckLevel.Info, Loc("StrFwCheckBleHint"));
                if (_adapter is { BluetoothRadioCount: > 1 }) Add(FileCheckLevel.Warning, Loc("StrFwBtMultiHint", _adapter.BluetoothRadioCount));
                break;
            case FirmwareUpdateMethod.Uf2Usb: Add(FileCheckLevel.Info, Loc("StrFwCheckUf2Hint")); break;
        }
        if (!_connected) Add(FileCheckLevel.Warning, Loc("StrFwRecoveryHint"));

        BackupCheck.IsEnabled = _connected;
        if (!_connected) BackupCheck.IsChecked = false;
        _preflightOk = !hasError && _artifact != null;
        StartBtn.IsEnabled = _preflightOk;
    }

    // ── Step 5: transfer ───────────────────────────────────────────────────

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!_preflightOk || _artifact == null || _running) return;
        _running = true;
        _succeeded = false;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        ShowStep(5);
        LogBox.Clear();
        TransferProgress.IsIndeterminate = true;
        TransferProgress.Value = 0;
        DetailText.Text = "";
        var progress = new Progress<FirmwareProgress>(OnProgress);
        var proto = _host.Protocol;
        string? backupPath = null;

        try
        {
            if (BackupCheck.IsChecked == true && _connected && proto != null)
            {
                OnProgress(new FirmwareProgress("StrFwPhaseBackup"));
                backupPath = WriteBackup(proto);
                Log(Loc("StrFwBackupSaved", backupPath));
            }

            switch (_method)
            {
                case FirmwareUpdateMethod.Esp32Wifi:
                case FirmwareUpdateMethod.Esp32Ble:
                {
                    if (proto == null) throw new FirmwareUpdateException("StrFwErrNotConnected");
                    var bytes = await File.ReadAllBytesAsync(_artifact.Path, ct);
                    var updater = new Esp32OtaUpdater(proto, _host.DisconnectForFirmwareUpdateAsync, progress);
                    if (_method == FirmwareUpdateMethod.Esp32Wifi) await updater.RunWifiAsync(bytes, _tcpHost, ct);
                    else await updater.RunBleAsync(bytes, _bleAddress ?? throw new FirmwareUpdateException("StrFwReasonNeedBle"), ct);
                    break;
                }
                case FirmwareUpdateMethod.Nrf52BleDfu:
                {
                    var pkg = DfuPackage.FromFile(_artifact.Path);
                    var updater = new NordicDfuUpdater(_bleAddress ?? throw new FirmwareUpdateException("StrFwReasonNeedBle"),
                        _host.DisconnectForFirmwareUpdateAsync, progress);
                    await updater.RunAsync(pkg, alreadyInBootloader: !_connected, ct);
                    break;
                }
                case FirmwareUpdateMethod.Uf2Usb:
                    await RunUf2Async(proto, progress, ct);
                    break;
                default:
                    throw new FirmwareUpdateException("StrFwErrNoMethod");
            }

            _succeeded = true;
            ShowDone(Loc(_method == FirmwareUpdateMethod.Uf2Usb ? "StrFwDoneUf2" : "StrFwDoneOk"), backupPath);
        }
        catch (OperationCanceledException)
        {
            Log(Loc("StrFwErrCancelled"));
            ShowDone(Loc("StrFwDoneFail", Loc("StrFwErrCancelled")), backupPath);
        }
        catch (FirmwareUpdateException ex)
        {
            Services.Logger.WriteLine($"[FW] Update failed: {ex.Key} {ex.InnerException?.Message}");
            var text = Loc(ex.Key, ex.Args);
            Log("✗ " + text);
            ShowDone(Loc("StrFwDoneFail", text), backupPath);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[FW] Update crashed: {ex}");
            Log("✗ " + ex.Message);
            ShowDone(Loc("StrFwDoneFail", ex.Message), backupPath);
        }
        finally
        {
            _running = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task RunUf2Async(MeshtasticProtocolService? proto, IProgress<FirmwareProgress> progress, CancellationToken ct)
    {
        var known = Uf2UsbFlasher.FindDrives();
        Uf2Drive? drive = null;
        if (_connected && proto != null)
        {
            progress.Report(new FirmwareProgress("StrFwPhaseEnablingDfu"));
            await proto.EnterDfuModeAsync();
            await Task.Delay(800, ct);
            progress.Report(new FirmwareProgress("StrFwPhaseDisconnecting"));
            await _host.DisconnectForFirmwareUpdateAsync();
            progress.Report(new FirmwareProgress("StrFwPhaseWaitingDrive"));
            drive = await Uf2UsbFlasher.WaitForNewDriveAsync(known.Select(d => d.Root).ToHashSet(StringComparer.OrdinalIgnoreCase),
                TimeSpan.FromSeconds(90), ct);
        }
        else
        {
            // Recovery: the device is already in bootloader mode, or will be after a manual double-reset.
            progress.Report(new FirmwareProgress("StrFwPhaseWaitingDrive"));
            drive = known.FirstOrDefault() ?? await Uf2UsbFlasher.WaitForNewDriveAsync(new HashSet<string>(), TimeSpan.FromSeconds(120), ct);
        }
        if (drive == null) throw new FirmwareUpdateException("StrFwErrUf2NoDrive");
        Log($"UF2: {drive.Root} Board-ID={drive.BoardId ?? "?"} Bootloader={drive.BootloaderVersion ?? "?"} {drive.SoftDevice ?? ""}");

        progress.Report(new FirmwareProgress("StrFwPhaseCopying", 0));
        var copyProgress = new Progress<double>(p => progress.Report(new FirmwareProgress("StrFwPhaseCopying", p, $"{p * 100:0} %")));
        var consumed = await Uf2UsbFlasher.CopyAndWaitAsync(drive, _artifact!.Path, copyProgress, TimeSpan.FromSeconds(60), ct);
        progress.Report(new FirmwareProgress("StrFwPhaseFlashing", 1.0));
        if (!consumed) throw new FirmwareUpdateException("StrFwErrUf2NotConsumed", drive.Root);
    }

    private string WriteBackup(MeshtasticProtocolService proto)
    {
        var dir = Path.Combine(_catalogSvc.CacheDir, "backups");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"node-{proto.GetMyNodeId():x8}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, proto.BuildConfigBackupJson());
        return path;
    }

    private void OnProgress(FirmwareProgress p)
    {
        var text = Loc(p.Key, p.Args ?? Array.Empty<object>());
        if (p.Detail == "slow") text += " — " + Loc("StrFwPhaseSlowHint");
        if (PhaseText.Text != text)
        {
            PhaseText.Text = text;
            Log(text);
        }
        if (p.Fraction is { } f)
        {
            TransferProgress.IsIndeterminate = false;
            TransferProgress.Value = Math.Clamp(f * 100, 0, 100);
        }
        else TransferProgress.IsIndeterminate = true;
        DetailText.Text = p.Detail == "slow" ? "" : p.Detail ?? "";
    }

    private void Log(string line)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        LogBox.ScrollToEnd();
    }

    // ── Step 6: done ───────────────────────────────────────────────────────

    private void ShowDone(string text, string? backupPath)
    {
        ShowStep(6);
        ResultText.Text = text;
        ResultText.Foreground = _succeeded ? Brushes.Green : Brushes.OrangeRed;
        ResultDetailText.Text = backupPath != null ? Loc("StrFwBackupSaved", backupPath) : "";
        ReconnectBtn.IsEnabled = _host.ConnectionParams != null;
        ReconnectStatusText.Text = "";
    }

    private async void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        ReconnectBtn.IsEnabled = false;
        ReconnectStatusText.Text = Loc("StrFwReconnecting");
        var before = _protocolBeforeUpdate;
        _host.ReconnectAfterFirmwareUpdate();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(1000);
            var p = _host.Protocol;
            if (p != null && !ReferenceEquals(p, before) && _host.IsConnected)
            {
                var v = p.GetFirmwareVersion();
                if (!string.IsNullOrEmpty(v))
                {
                    ReconnectStatusText.Text = Loc("StrFwReconnectedVersion", v);
                    ReconnectStatusText.Foreground = Brushes.Green;
                    return;
                }
            }
        }
        ReconnectStatusText.Text = Loc("StrFwReconnectTimeout");
        ReconnectBtn.IsEnabled = true;
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private void ShowStep(int step)
    {
        _step = step;
        Page1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Page4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Page5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        Page6.Visibility = step == 6 ? Visibility.Visible : Visibility.Collapsed;
        StepTitleText.Text = Loc($"StrFwStep{step}");
        StepIndicatorText.Text = Loc("StrFwStepOf", step, StepCount);
        UpdateNav();
    }

    private void UpdateNav()
    {
        BackBtn.Visibility = _step is > 1 and < 5 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Visibility = _step < 4 ? Visibility.Visible : Visibility.Collapsed;
        CancelBtn.Content = Loc(_step == 6 ? "StrFwCloseBtn" : "StrFwCancel");
        NextBtn.IsEnabled = _step switch
        {
            1 => _catalog != null && (_connected || _bleAddress != null || Uf2UsbFlasher.FindDrives().Count > 0),
            2 => _method != FirmwareUpdateMethod.None,
            3 => _release != null || (_localFile != null && !(_fileCheck?.HasErrors ?? false)),
            _ => false
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 1) ShowStep(_step - 1);
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 1: ShowStep(2); RenderMethodPage(); break;
            case 2: ShowStep(3); RenderFirmwarePage(); break;
            case 3: ShowStep(4); await RunPreflightAsync(); break;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            var r = MessageBox.Show(this, Loc("StrFwCancelConfirm"), Loc("StrFwTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) _cts?.Cancel();
            return;
        }
        Close();
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static TextBlock CheckLine(FileCheckLevel level, string text)
    {
        var (icon, brush) = level switch
        {
            FileCheckLevel.Error => ("✗ ", Brushes.OrangeRed),
            FileCheckLevel.Warning => ("⚠ ", Brushes.DarkOrange),
            _ => (text.StartsWith('✓') || text.StartsWith('✗') || text.StartsWith('?') || text.StartsWith('ℹ') ? "" : "✓ ", Brushes.Black)
        };
        return new TextBlock { Text = icon + text, Foreground = brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Services.Logger.WriteLine($"[FW] Open URL failed: {ex.Message}"); }
    }
}
