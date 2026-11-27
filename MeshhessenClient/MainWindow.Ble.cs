using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MeshhessenClient.Services;

namespace MeshhessenClient;

/// <summary>
/// Bluetooth bits of the main window: the 📶 signal label next to the connection bubble with the link counters
/// behind it, the gentle-mode setting (and the offer to switch to it after repeated drops), and the one rule for
/// pairings — the client never removes a Windows bond without asking.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _bleStatsPopupTimer;

    private BluetoothConnectionService? ActiveBle => _connectionService as BluetoothConnectionService;

    /// <summary>Setting "gentle Bluetooth mode": applies to the live link (if still in its init phase) and to every future one.</summary>
    private void ApplyBleGentleModeSetting(bool gentle)
    {
        BluetoothConnectionService.GentleModeDefault = gentle;
        ActiveBle?.SetGentleMode(gentle);
    }

    /// <summary>Called by both connect paths once config and node list are loaded: the fast connection interval is
    /// released, the firmware's own preference takes over.</summary>
    private void NotifyBleInitComplete()
    {
        ActiveBle?.InitComplete();
        Dispatcher.BeginInvoke(UpdateBleSignalHeader);
    }

    private void UpdateBleSignalHeader()
    {
        var ble = ActiveBle;
        if (ble == null || !ble.IsConnected)
        {
            BleSignalText.Visibility = Visibility.Collapsed;
            BleStatsPopup.IsOpen = false;
            return;
        }
        BleSignalText.Text = ble.ConnectRssiDbm is int rssi
            ? string.Format(Loc("StrBleSignalFmt"), rssi)
            : Loc("StrBleSignalUnknown");
        BleSignalText.Visibility = Visibility.Visible;
    }

    private string BuildBleStatsText()
    {
        var ble = ActiveBle;
        if (ble == null) return string.Empty;
        var s = ble.GetStats();

        string rssi = s.RssiDbm is int r
            ? $"{r} dBm ({s.AdvertisementsHeard} Adv.) — {(r >= -70 ? Loc("StrBleRssiGood") : r >= -85 ? Loc("StrBleRssiFair") : Loc("StrBleRssiWeakShort"))}"
            : Loc("StrBleRssiNone");
        string mode = s.Mode == BleLinkMode.Gentle ? Loc("StrBleModeGentle") : Loc("StrBleModeFast");
        string phase = s.Phase switch
        {
            BleLinkPhase.Steady => Loc("StrBlePhaseSteady"),
            BleLinkPhase.Init   => Loc("StrBlePhaseInit"),
            _                   => Loc("StrConnecting"),
        };
        string interval = s.IntervalMs is int i
            ? $"{i} ms" + (s.Latency is int l && l > 0 ? $", Latenz {l}" : "") + (s.LinkTimeoutMs is int t ? $", Timeout {t} ms" : "")
            : Loc("StrBleIntervalUnknown");
        string up = s.Uptime.TotalHours >= 1 ? s.Uptime.ToString(@"h\:mm\:ss") : s.Uptime.ToString(@"m\:ss");
        string notify = s.NotifySubscribed ? Loc("StrBleYes") : Loc("StrBleNo");

        return string.Format(Loc("StrBleStatsText"),
            rssi, mode, phase, interval, up,
            s.ReadsTotal, s.EmptyReadPercent, s.ReadsData, s.ReadFailures,
            s.Notifies, notify,
            s.DrainsNotify, s.DrainsWrite, s.DrainsPoll, s.PollInterval.TotalSeconds.ToString("0.#"),
            s.Writes, s.WriteFailures);
    }

    private void BleSignalText_ToolTipOpening(object sender, ToolTipEventArgs e)
        => BleSignalToolTipText.Text = BuildBleStatsText();

    private void BleSignalText_Click(object sender, MouseButtonEventArgs e)
    {
        if (BleStatsPopup.IsOpen)
        {
            BleStatsPopup.IsOpen = false;
            return;
        }
        BleStatsPopupText.Text = BuildBleStatsText();
        BleStatsPopup.IsOpen = true;
        if (_bleStatsPopupTimer == null)
        {
            _bleStatsPopupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _bleStatsPopupTimer.Tick += (_, _) =>
            {
                if (!BleStatsPopup.IsOpen) { _bleStatsPopupTimer.Stop(); return; }
                BleStatsPopupText.Text = BuildBleStatsText();
            };
        }
        _bleStatsPopupTimer.Start();
    }

    private void BleStatsCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = BuildBleStatsText();
            var logLine = ActiveBle?.GetStats().ToLogLine();
            Clipboard.SetText(string.IsNullOrEmpty(logLine) ? text : text + "\n\n" + logLine);
            UpdateStatusBar(Loc("StrBleStatsCopied"));
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[BLE] Copying the link stats failed: {ex.Message}");
        }
    }

    /// <summary>Drop storm (3 unexpected drops in 10 min): the usual-causes text, and — while the gentle mode is
    /// still off — the offer to switch to it. A "yes" is saved like a change on the settings tab and applies to the
    /// reconnect that is already running (it creates a fresh link).</summary>
    private void ShowDropStormHint(string text)
    {
        if (_currentSettings.BleGentleMode)
        {
            MessageBox.Show(this, text, Loc("StrBleDropStormTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var choice = MessageBox.Show(this, text + "\n\n" + Loc("StrBleGentleModeOffer"), Loc("StrBleDropStormTitle"),
                                     MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (choice != MessageBoxResult.Yes) return;

        _currentSettings = _currentSettings with { BleGentleMode = true };
        SettingsService.Save(_currentSettings);
        BleGentleModeCheckBox.IsChecked = true;
        ApplyBleGentleModeSetting(true);
        Services.Logger.WriteLine("[BLE] User switched to the gentle Bluetooth mode after repeated drops");
        UpdateStatusBar(Loc("StrBleGentleModeOn"));
    }

    /// <summary>Asks on the UI thread whether the Windows pairing may be removed. Safe to call from any thread;
    /// <paramref name="reconnectAttempt"/> selects the wording for the auto-reconnect loop.</summary>
    private async Task<bool> AskUnpairAsync(string evidence, int? reconnectAttempt)
    {
        return await Dispatcher.InvokeAsync(() =>
        {
            string text = reconnectAttempt is int n
                ? string.Format(Loc("StrBleStaleBondReconnectHint"), evidence, n)
                : string.Format(Loc("StrBleStaleBondHint"), evidence);
            var r = MessageBox.Show(this, text, Loc("StrBleStaleBondTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            Services.Logger.WriteLine($"[BLE] Stale-pairing dialog answered: {(r == MessageBoxResult.Yes ? "remove pairing" : "keep pairing")}");
            return r == MessageBoxResult.Yes;
        });
    }

    /// <summary>Removes the Windows bond after the user said yes, with status-bar feedback.</summary>
    private async Task<bool> UnpairWithStatusAsync(ulong address)
    {
        await Dispatcher.InvokeAsync(() => UpdateStatusBar(Loc("StrBleUnpairing")));
        bool ok = await Services.Firmware.BleGattLink.TryUnpairAsync(address, Windows.Devices.Bluetooth.BluetoothAddressType.Unspecified);
        Services.Logger.WriteLine(ok
            ? "[BLE] Pairing removed at the user's request — the next connect pairs afresh"
            : "[BLE] Removing the pairing failed — the user has to remove it in the Windows Bluetooth settings");
        await Dispatcher.InvokeAsync(() => UpdateStatusBar(Loc(ok ? "StrBleUnpaired" : "StrBleUnpairFailed")));
        return ok;
    }
}
