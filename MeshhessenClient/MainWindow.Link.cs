using System.Windows;

namespace MeshhessenClient;

/// <summary>Reaction to a dead device link that the transport itself did not notice (port/socket still
/// open, device silent): drop the connection so the normal auto-reconnect path brings it back.</summary>
public partial class MainWindow
{
    private void OnLinkStale(object? sender, string why)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_intentionalDisconnect || _isReconnecting || _connectionService?.IsConnected != true) return;
            Services.Logger.WriteLine($"[LINK] Device silent although connected ({why}) — reconnecting");
            UpdateStatusBar(Loc("StrLinkStaleReconnect"));
            // Not intentional: OnConnectionStateChanged(false) starts TryReconnectAsync.
            _intentionalDisconnect = false;
            Task.Run(() =>
            {
                try { _protocolService.Disconnect(); } catch { }
                try { _connectionService?.Disconnect(); } catch { }
            });
        });
    }
}
