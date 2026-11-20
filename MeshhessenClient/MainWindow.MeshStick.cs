using System.Windows;
using MeshhessenClient.Services;
using MeshhessenClient.Services.MeshStick;

namespace MeshhessenClient;

/// <summary>MeshStick wizard hook-up: the main window owns the connection, so it starts the TCP connect, reports when
/// the node is ready and keeps the local meshtasticd service alive — the daemon ends its process on every node
/// "reboot" (settings commit), and the service must be started again before a reconnect can succeed.</summary>
public partial class MainWindow : IMeshStickHost
{
    private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;

    MeshtasticProtocolService? IMeshStickHost.Protocol => _protocolService;
    bool IMeshStickHost.IsConnected => _connectionService?.IsConnected == true;
    bool IMeshStickHost.IsReady => _connectionService?.IsConnected == true && _connectionStatus == ConnectionStatus.Ready;
    LoRaConfig? IMeshStickHost.CurrentLoRaConfig => _currentLoRaConfig;
    AppSettings IMeshStickHost.Settings => _currentSettings;

    void IMeshStickHost.UpdateSettings(Func<AppSettings, AppSettings> change)
    {
        _currentSettings = change(_currentSettings);
        SettingsService.Save(_currentSettings);
    }

    void IMeshStickHost.ConnectTcp(string host, int port)
    {
        WifiRadioButton.IsChecked = true;            // ConnectionTypeRadio_Changed switches _currentConnectionType to Tcp
        TcpHostTextBox.Text = host;
        TcpPortTextBox.Text = port.ToString();
        if (_connectionService?.IsConnected == true) return;
        Connect_Click(ConnectButton, new RoutedEventArgs());
    }

    async Task IMeshStickHost.DisconnectAsync()
    {
        if (_connectionService?.IsConnected != true) return;
        Connect_Click(ConnectButton, new RoutedEventArgs());   // connected → the button disconnects
        for (int i = 0; i < 60 && _connectionService?.IsConnected == true; i++)
            await Task.Delay(250);
    }

    Task<MeshHessenApplyResult> IMeshStickHost.ApplyMeshHessenProfileAsync() => ApplyMeshHessenProfileCoreAsync();

    private void MeshStickWizardBtn_Click(object sender, RoutedEventArgs e)
    {
        new MeshStickWizardWindow(this) { Owner = this }.Show();
    }

    /// <summary>Before connecting to 127.0.0.1: if the local meshtasticd service is installed but nothing answers on
    /// the port, start the service (standard users may, after the wizard's setup) and wait for the API.</summary>
    private async Task EnsureLocalDaemonAsync(ConnectionParameters? parameters)
    {
        if (parameters is not TcpConnectionParameters tcp) return;
        if (!MeshtasticdService.IsLoopback(tcp.Hostname) || !MeshtasticdService.IsInstalled) return;
        if (await MeshtasticdService.IsApiReachableAsync(tcp.Port)) return;
        Services.Logger.WriteLine($"[MeshStick] nothing answers on 127.0.0.1:{tcp.Port} — starting the meshtasticd service");
        _ = Dispatcher.BeginInvoke(() => UpdateStatusBar(Loc("StrMstStartingDaemon")));
        bool up = await MeshtasticdService.EnsureRunningAsync(tcp.Port, TimeSpan.FromSeconds(25), CancellationToken.None);
        Services.Logger.WriteLine(up ? "[MeshStick] daemon answers" : "[MeshStick] daemon did not come up in time");
    }
}
