using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Services;
using ModelNodeInfo = MeshhessenClient.Models.NodeInfo;

namespace MeshhessenClient;

public partial class RemoteAdminWindow : Window
{
    private readonly MeshtasticProtocolService _svc;
    private readonly ModelNodeInfo _targetNode;
    private int _timeoutMs;
    private bool _isFavorite;
    private bool _uiReady;

    // Loaded config state
    private User? _loadedOwner;
    private DeviceConfig? _loadedDevice;
    private PositionConfig? _loadedPosition;
    private LoRaConfig? _loadedLora;
    private BluetoothConfig? _loadedBt;
    private NetworkConfig? _loadedNetwork;
    private DisplayConfig? _loadedDisplay;
    private readonly Channel?[] _loadedChannels = new Channel?[8];

    public RemoteAdminWindow(
        MeshtasticProtocolService svc,
        ModelNodeInfo targetNode,
        int timeoutMs = 30000,
        bool isFavorite = false)
    {
        InitializeComponent();
        _svc = svc;
        _targetNode = targetNode;
        _timeoutMs = timeoutMs;
        _isFavorite = isFavorite;

        NodeNameLabel.Text = targetNode.Name;
        NodeIdLabel.Text = targetNode.Id;
        TimeoutBox.Text = (timeoutMs / 1000).ToString();
        _uiReady = true;
        UpdateFavoriteButton();

        Loaded += async (_, _) => await ConnectAndLoadAsync();
    }

    private void TimeoutBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady) return;
        if (int.TryParse(TimeoutBox.Text, out int secs))
            _timeoutMs = Math.Clamp(secs, 5, 300) * 1000;
    }

    private string Loc(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;

    private void UpdateFavoriteButton()
    {
        FavoriteButton.Foreground = _isFavorite
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07))
            : new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        FavoriteButton.ToolTip = _isFavorite
            ? Loc("StrUnfavorite")
            : Loc("StrFavorite");
    }

    private void SetStatus(string textKey, string dotColor = "#9E9E9E")
    {
        Dispatcher.Invoke(() =>
        {
            StatusLabel.Text = Loc(textKey);
            StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotColor));
        });
    }

    // ===== Connect + Load =====

    private async Task ConnectAndLoadAsync()
    {
        SetStatus("StrRemoteAdminConnecting");

        // Step 1: Request device metadata (checks connectivity and admin permission)
        var metaResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetDeviceMetadataRequest = true },
            _timeoutMs);

        if (metaResp == null)
        {
            SetStatus("StrRemoteAdminTimeout", "#F44336");
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Text = Loc("StrRemoteAdminTimeout");
                MessageBox.Show(
                    Loc("StrRemoteAdminTimeoutMsg"),
                    Loc("StrRemoteAdminTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            return;
        }

        if (metaResp.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetDeviceMetadataResponse)
        {
            var meta = metaResp.GetDeviceMetadataResponse;
            Dispatcher.Invoke(() =>
            {
                FirmwareLabel.Text = meta.FirmwareVersion ?? "-";
                HardwareLabel.Text = meta.HwModel != HardwareModel.Unset ? meta.HwModel.ToString() : "-";
                HopsLabel.Text = _targetNode.Snr != "-" ? _targetNode.Snr : "-";
            });
        }

        // Step 2: Get session key
        await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetConfigRequest = 8 }, // 8 = SESSIONKEY_CONFIG
            _timeoutMs);

        SetStatus("StrRemoteAdminLoading", "#2196F3");

        // Step 3: Load all configs in sequence
        await LoadAllConfigsAsync();

        SetStatus("StrRemoteAdminConnected", "#4CAF50");
        Dispatcher.Invoke(() =>
        {
            MainTabs.IsEnabled = true;
            SaveButton.IsEnabled = true;
            RefreshButton.IsEnabled = true;
        });
    }

    private async Task LoadAllConfigsAsync()
    {
        // Owner
        var ownerResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId, new AdminMessage { GetOwnerRequest = true }, _timeoutMs);
        if (ownerResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetOwnerResponse)
        {
            _loadedOwner = ownerResp.GetOwnerResponse;
            Dispatcher.Invoke(() => ApplyOwner(_loadedOwner));
        }

        await Task.Delay(200);

        // Device config
        var deviceResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetConfigRequest = 0 },
            _timeoutMs);
        if (deviceResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetConfigResponse &&
            deviceResp.GetConfigResponse.PayloadVariantCase == Config.PayloadVariantOneofCase.Device)
        {
            _loadedDevice = deviceResp.GetConfigResponse.Device;
            Dispatcher.Invoke(() => ApplyDeviceConfig(_loadedDevice));
        }

        await Task.Delay(200);

        // Position config
        var posResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetConfigRequest = 1 },
            _timeoutMs);
        if (posResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetConfigResponse &&
            posResp.GetConfigResponse.PayloadVariantCase == Config.PayloadVariantOneofCase.Position)
        {
            _loadedPosition = posResp.GetConfigResponse.Position;
            Dispatcher.Invoke(() => ApplyPositionConfig(_loadedPosition));
        }

        await Task.Delay(200);

        // LoRa config
        var loraResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetConfigRequest = 5 },
            _timeoutMs);
        if (loraResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetConfigResponse &&
            loraResp.GetConfigResponse.PayloadVariantCase == Config.PayloadVariantOneofCase.Lora)
        {
            _loadedLora = loraResp.GetConfigResponse.Lora;
            Dispatcher.Invoke(() => ApplyLoraConfig(_loadedLora));
        }

        await Task.Delay(200);

        // Bluetooth config
        var btResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetConfigRequest = 6 },
            _timeoutMs);
        if (btResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetConfigResponse &&
            btResp.GetConfigResponse.PayloadVariantCase == Config.PayloadVariantOneofCase.Bluetooth)
        {
            _loadedBt = btResp.GetConfigResponse.Bluetooth;
            Dispatcher.Invoke(() => ApplyBluetoothConfig(_loadedBt));
        }

        await Task.Delay(200);

        // Network config
        var netResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetConfigRequest = 3 },
            _timeoutMs);
        if (netResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetConfigResponse &&
            netResp.GetConfigResponse.PayloadVariantCase == Config.PayloadVariantOneofCase.Network)
        {
            _loadedNetwork = netResp.GetConfigResponse.Network;
            Dispatcher.Invoke(() => ApplyNetworkConfig(_loadedNetwork));
        }

        await Task.Delay(200);

        // Display config
        var dispResp = await _svc.SendRemoteAdminRequestAsync(
            _targetNode.NodeId,
            new AdminMessage { GetConfigRequest = 4 },
            _timeoutMs);
        if (dispResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetConfigResponse &&
            dispResp.GetConfigResponse.PayloadVariantCase == Config.PayloadVariantOneofCase.Display)
        {
            _loadedDisplay = dispResp.GetConfigResponse.Display;
            Dispatcher.Invoke(() => ApplyDisplayConfig(_loadedDisplay));
        }

        await Task.Delay(200);

        // Channels 0-7 — with one automatic retry per failed channel, plus per-row reload button
        Dispatcher.Invoke(() => ChannelsPanel.Children.Clear());
        for (int i = 0; i < 8; i++)
        {
            await LoadSingleChannelAsync(i);
            await Task.Delay(180);
        }
    }

    private async Task LoadSingleChannelAsync(int index)
    {
        // Try twice — remote channel gets can be lossy
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var chResp = await _svc.SendRemoteAdminRequestAsync(
                _targetNode.NodeId,
                new AdminMessage { GetChannelRequest = (uint)index },
                _timeoutMs);
            if (chResp?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.GetChannelResponse)
            {
                _loadedChannels[index] = chResp.GetChannelResponse;
                var ch = chResp.GetChannelResponse;
                Dispatcher.Invoke(() => AddOrUpdateChannelRow(index, ch));
                return;
            }
            if (attempt == 0) await Task.Delay(400);
        }
        // Still failed — insert placeholder row so user can retry
        Dispatcher.Invoke(() => AddOrUpdateChannelRow(index, null));
    }

    // ===== Apply config to UI =====

    private void ApplyOwner(User u)
    {
        OwnerShortName.Text = u.ShortName ?? "";
        OwnerLongName.Text  = u.LongName  ?? "";
        OwnerLicensed.IsChecked = u.IsLicensed;
    }

    private void ApplyDeviceConfig(DeviceConfig d)
    {
        SelectComboByTag(DeviceRoleCombo, (int)d.Role);
        DeviceNodeInfoInterval.Text = d.NodeInfoBroadcastSecs.ToString();
    }

    private void ApplyPositionConfig(PositionConfig p)
    {
        SelectComboByTag(PosGpsMode, (int)p.GpsMode);
        PosBroadcastInterval.Text = p.PositionBroadcastSecs.ToString();
        PosBroadcastSmart.IsChecked = p.PositionBroadcastSmartEnabled;
        PosFixedEnabled.IsChecked = p.FixedPosition;
        // Fixed position lat/lon/alt are stored separately — enter manually to override
    }

    private void ApplyLoraConfig(LoRaConfig l)
    {
        SelectComboByTag(LoraRegionCombo, (int)l.Region);
        SelectComboByTag(LoraPresetCombo, (int)l.ModemPreset);
        LoraHopLimit.Text  = l.HopLimit.ToString();
        LoraTxPower.Text   = l.TxPower.ToString();
        LoraTxEnabled.IsChecked = l.TxEnabled;
    }

    private void ApplyBluetoothConfig(BluetoothConfig b)
    {
        BtEnabled.IsChecked = b.Enabled;
        SelectComboByTag(BtModeCombo, (int)b.Mode);
        BtPin.Text = b.FixedPin.ToString();
    }

    private void ApplyNetworkConfig(NetworkConfig n)
    {
        WifiEnabled.IsChecked  = n.WifiEnabled;
        WifiSsid.Text          = n.WifiSsid ?? "";
        WifiPsk.Password       = n.WifiPsk  ?? "";
        EthEnabled.IsChecked   = n.EthEnabled;
        NtpServer.Text         = n.NtpServer ?? "";
    }

    private void ApplyDisplayConfig(DisplayConfig d)
    {
        DisplayTimeout.Text  = d.ScreenOnSecs.ToString();
        DisplayFlipScreen.IsChecked = d.FlipScreen;
        SelectComboByTag(DisplayUnitsCombo, (int)d.Units);
        SelectComboByTag(OledTypeCombo, (int)d.Oled);
    }

    private void AddOrUpdateChannelRow(int index, Channel? ch)
    {
        // Remove old row for this index if present
        var old = ChannelsPanel.Children.OfType<Border>()
            .FirstOrDefault(b => b.Tag is int t && t == index);
        if (old != null) ChannelsPanel.Children.Remove(old);

        var border = new Border
        {
            Tag = index,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 6, 0, 6),
            Margin  = new Thickness(0, 0, 0, 2)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });

        void AddCell(int col, string text, bool bold = false, Brush? fg = null)
        {
            var tb = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            };
            if (fg != null) tb.Foreground = fg;
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }

        if (ch == null)
        {
            var failBrush = new SolidColorBrush(Color.FromRgb(240, 76, 76));
            AddCell(0, index.ToString(), true, failBrush);
            AddCell(1, "—", fg: failBrush);
            AddCell(2, Loc("StrRemoteAdminChannelFailed"), fg: failBrush);
            AddCell(3, "", fg: failBrush);
        }
        else
        {
            var name = ch.Settings?.Name ?? "";
            var psk  = ch.Settings?.Psk != null ? Convert.ToBase64String(ch.Settings.Psk.ToByteArray()) : "";
            var role = ch.Role.ToString();
            AddCell(0, index.ToString(), true);
            AddCell(1, role);
            AddCell(2, name);
            AddCell(3, psk.Length > 0 ? $"PSK: {psk[..Math.Min(16, psk.Length)]}…" : "(no PSK)");
        }

        // Per-channel reload button
        var reloadBtn = new Button
        {
            Content = "⟳",
            Width = 24,
            Height = 22,
            FontSize = 13,
            Padding = new Thickness(0),
            ToolTip = Loc("StrRemoteAdminChannelReload"),
            Tag = index,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        reloadBtn.Click += async (_, _) => await ReloadSingleChannelAsync(index);
        Grid.SetColumn(reloadBtn, 4);
        grid.Children.Add(reloadBtn);

        border.Child = grid;
        ChannelsPanel.Children.Add(border);
    }

    private async Task ReloadSingleChannelAsync(int index)
    {
        SetStatus("StrRemoteAdminLoading", "#2196F3");
        await LoadSingleChannelAsync(index);
        SetStatus("StrRemoteAdminConnected", "#4CAF50");
    }

    // ===== Save =====

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        SetStatus("StrRemoteAdminSaving", "#FF9800");

        try
        {
            await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                new AdminMessage { BeginEditSettings = true });
            await Task.Delay(300);

            // Owner
            if (_loadedOwner != null)
            {
                var newOwner = _loadedOwner.Clone();
                newOwner.ShortName  = OwnerShortName.Text.Trim();
                newOwner.LongName   = OwnerLongName.Text.Trim();
                newOwner.IsLicensed = OwnerLicensed.IsChecked == true;
                await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                    new AdminMessage { SetOwner = newOwner });
                await Task.Delay(200);
            }

            // Device config
            if (_loadedDevice != null)
            {
                var d = _loadedDevice.Clone();
                d.Role = (Role)(GetComboTag(DeviceRoleCombo) ?? 0);
                if (uint.TryParse(DeviceNodeInfoInterval.Text, out var ni)) d.NodeInfoBroadcastSecs = ni;
                await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                    new AdminMessage { SetConfig = new Config { Device = d } });
                await Task.Delay(200);
            }

            // Position config
            if (_loadedPosition != null)
            {
                var p = _loadedPosition.Clone();
                p.GpsMode = (GpsMode)(GetComboTag(PosGpsMode) ?? 1);
                if (uint.TryParse(PosBroadcastInterval.Text, out var pi)) p.PositionBroadcastSecs = pi;
                p.PositionBroadcastSmartEnabled = PosBroadcastSmart.IsChecked == true;
                p.FixedPosition = PosFixedEnabled.IsChecked == true;
                await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                    new AdminMessage { SetConfig = new Config { Position = p } });
                await Task.Delay(200);

                // Fixed position coordinates sent via set_position
                if (p.FixedPosition &&
                    double.TryParse(PosFixedLat.Text, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(PosFixedLon.Text, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var lon) &&
                    int.TryParse(PosFixedAlt.Text, out var alt))
                {
                    await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                        new AdminMessage { SetPosition = new Position
                        {
                            LatitudeI  = (int)(lat * 1e7),
                            LongitudeI = (int)(lon * 1e7),
                            Altitude   = alt
                        }});
                    await Task.Delay(200);
                }
            }

            // LoRa config
            if (_loadedLora != null)
            {
                var l = _loadedLora.Clone();
                l.Region      = (Region)(GetComboTag(LoraRegionCombo) ?? 0);
                l.ModemPreset = (ModemPreset)(GetComboTag(LoraPresetCombo) ?? 0);
                if (uint.TryParse(LoraHopLimit.Text, out var hl)) l.HopLimit = hl;
                if (int.TryParse(LoraTxPower.Text, out var tp))   l.TxPower  = tp;
                l.TxEnabled = LoraTxEnabled.IsChecked == true;
                await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                    new AdminMessage { SetConfig = new Config { Lora = l } });
                await Task.Delay(200);
            }

            // Bluetooth config
            if (_loadedBt != null)
            {
                var b = _loadedBt.Clone();
                b.Enabled = BtEnabled.IsChecked == true;
                b.Mode    = (uint)(GetComboTag(BtModeCombo) ?? 0);
                if (uint.TryParse(BtPin.Text, out var pin)) b.FixedPin = pin;
                await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                    new AdminMessage { SetConfig = new Config { Bluetooth = b } });
                await Task.Delay(200);
            }

            // Network config
            if (_loadedNetwork != null)
            {
                var n = _loadedNetwork.Clone();
                n.WifiEnabled = WifiEnabled.IsChecked == true;
                n.WifiSsid    = WifiSsid.Text.Trim();
                n.WifiPsk     = WifiPsk.Password;
                n.EthEnabled  = EthEnabled.IsChecked == true;
                n.NtpServer   = NtpServer.Text.Trim();
                await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                    new AdminMessage { SetConfig = new Config { Network = n } });
                await Task.Delay(200);
            }

            // Display config
            if (_loadedDisplay != null)
            {
                var d = _loadedDisplay.Clone();
                if (uint.TryParse(DisplayTimeout.Text, out var dto)) d.ScreenOnSecs = dto;
                d.FlipScreen = DisplayFlipScreen.IsChecked == true;
                d.Units    = (uint)(GetComboTag(DisplayUnitsCombo) ?? 0);
                d.Oled     = (uint)(GetComboTag(OledTypeCombo) ?? 0);
                await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                    new AdminMessage { SetConfig = new Config { Display = d } });
                await Task.Delay(200);
            }

            await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
                new AdminMessage { CommitEditSettings = true });

            SetStatus("StrRemoteAdminSaved", "#4CAF50");
            Dispatcher.Invoke(() => MessageBox.Show(
                Loc("StrRemoteAdminSavedMsg"), Loc("StrRemoteAdminTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information));
        }
        catch (Exception ex)
        {
            SetStatus("StrRemoteAdminError", "#F44336");
            Dispatcher.Invoke(() => MessageBox.Show(
                $"{Loc("StrRemoteAdminErrorMsg")}\n{ex.Message}", Loc("StrRemoteAdminTitle"),
                MessageBoxButton.OK, MessageBoxImage.Error));
        }
        finally
        {
            Dispatcher.Invoke(() => SaveButton.IsEnabled = true);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        MainTabs.IsEnabled = false;
        _svc.ClearRemoteSessionKey(_targetNode.NodeId);
        await ConnectAndLoadAsync();
    }

    // ===== Control tab =====

    private async void RebootButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Loc("StrRemoteAdminRebootConfirm"), Loc("StrRemoteAdminTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
            new AdminMessage { RebootSeconds = 3 });
        SetStatus("StrRemoteAdminRebooting", "#FF9800");
    }

    private async void ShutdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Loc("StrRemoteAdminShutdownConfirm"), Loc("StrRemoteAdminTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
            new AdminMessage { ShutdownSeconds = 3 });
        SetStatus("StrRemoteAdminShutdown", "#9E9E9E");
    }

    private async void NodeDbResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Loc("StrRemoteAdminNodeDbResetConfirm"), Loc("StrRemoteAdminTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await _svc.SendRemoteAdminWriteAsync(_targetNode.NodeId,
            new AdminMessage { NodedbReset = true });
    }

    // ===== Favorite button =====

    private void FavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        _isFavorite = !_isFavorite;
        UpdateFavoriteButton();

        if (_isFavorite)
            _ = _svc.AddFavoriteNodeAsync(_targetNode.NodeId);
        else
            _ = _svc.RemoveFavoriteNodeAsync(_targetNode.NodeId);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // ===== Helpers =====

    private static void SelectComboByTag(ComboBox combo, int tagValue)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag is string ts && int.TryParse(ts, out int tv) && tv == tagValue)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static int? GetComboTag(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag is string ts &&
            int.TryParse(ts, out int v))
            return v;
        return null;
    }
}
