using System.Windows;
using MeshhessenClient.Services;

namespace MeshhessenClient;

/// <summary>Firmware-update wizard hook-up: the main window owns the connection, so it provides the
/// controlled disconnect (no auto-reconnect while the loader is driven) and the reconnect afterwards.</summary>
public partial class MainWindow : IFirmwareUpdateHost
{
    MeshtasticProtocolService? IFirmwareUpdateHost.Protocol => _protocolService;
    bool IFirmwareUpdateHost.IsConnected => _connectionService?.IsConnected == true;
    ConnectionParameters? IFirmwareUpdateHost.ConnectionParams => _lastConnectionParams;

    private void FirmwareUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        new FirmwareUpdateWindow(this) { Owner = this }.Show();
    }

    public async Task DisconnectForFirmwareUpdateAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            _intentionalDisconnect = true;
            _isReconnecting = false;
            ConnectButton.IsEnabled = false;
            SetConnectionStatus(ConnectionStatus.Disconnecting);
            UpdateStatusBar(Loc("StrFwStatusDisconnecting"));
        });

        var disconnectTask = Task.Run(() =>
        {
            try { _protocolService.Disconnect(); } catch (Exception ex) { Services.Logger.WriteLine($"[FW] protocol disconnect: {ex.Message}"); }
            System.Threading.Thread.Sleep(200);
            try { _connectionService?.Disconnect(); } catch (Exception ex) { Services.Logger.WriteLine($"[FW] connection disconnect: {ex.Message}"); }
        });
        if (await Task.WhenAny(disconnectTask, Task.Delay(8000)) != disconnectTask)
            Services.Logger.WriteLine("[FW] Disconnect took longer than 8s — continuing (teardown runs in background)");

        await Dispatcher.InvokeAsync(() =>
        {
            ConnectButton.Content = Loc("StrConnect");
            ConnectButton.IsEnabled = true;
            UpdateStatusBar(Loc("StrFwStatusUpdating"));
            SetConnectionStatus(ConnectionStatus.Disconnected);
            _myNodeId = 0;
            StationNameEditButton.Visibility = Visibility.Collapsed;
            RefreshActiveStationName();
        });
    }

    public void ReconnectAfterFirmwareUpdate()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_lastConnectionParams == null || _connectionService?.IsConnected == true) return;
            _intentionalDisconnect = false;
            _isReconnecting = true;
            ConnectButton.Content = Loc("StrDisconnect");
            ConnectButton.IsEnabled = false;
            SetConnectionStatus(ConnectionStatus.Connecting);
            UpdateStatusBar(Loc("StrFwStatusReconnecting"));
            _ = TryReconnectAsync();
        });
    }
}
