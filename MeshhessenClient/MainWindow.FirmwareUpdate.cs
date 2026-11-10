using System.IO;
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

    /// <summary>Prefetch firmware for any device types while online (the dialog loads the catalog itself).</summary>
    private void FirmwarePrefetchBtn_Click(object sender, RoutedEventArgs e)
    {
        new FirmwarePrefetchWindow(null, null) { Owner = this }.Show();
    }

    // ── Configuration backup / restore (Tools) ───────────────────────────────

    private static string BackupDir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firmware", "backups");

    private void ConfigBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionService?.IsConnected != true)
        {
            MessageBox.Show(Loc("StrNodeStatusNotConnected"), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            Directory.CreateDirectory(BackupDir);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc("StrCfgBackupBtn"),
                Filter = "Meshhessen config backup (*.json)|*.json",
                InitialDirectory = BackupDir,
                FileName = $"node-{_protocolService.GetMyNodeId():x8}-{DateTime.Now:yyyyMMdd-HHmmss}.json"
            };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllText(dlg.FileName, _protocolService.BuildConfigBackupJson());
            UpdateStatusBar(string.Format(Loc("StrCfgBackupSaved"), dlg.FileName));
            MessageBox.Show(string.Format(Loc("StrCfgBackupSaved"), dlg.FileName) + "\n\n" + Loc("StrCfgBackupSensitive"),
                Loc("StrCfgToolsSection"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[Backup] failed: {ex}");
            MessageBox.Show(string.Format(Loc("StrCfgBackupFailed"), ex.Message), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ConfigRestoreBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_connectionService?.IsConnected != true)
        {
            MessageBox.Show(Loc("StrNodeStatusNotConnected"), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc("StrCfgRestoreBtn"),
            Filter = "Meshhessen config backup (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(BackupDir) ? BackupDir : null
        };
        if (dlg.ShowDialog(this) != true) return;

        ConfigBackup backup;
        try
        {
            backup = ConfigBackupFile.Parse(File.ReadAllText(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Loc("StrCfgRestoreInvalid"), ex.Message), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var myId = $"{_protocolService.GetMyNodeId():x8}";
        bool sameNode = string.Equals(backup.NodeIdHex, myId, StringComparison.OrdinalIgnoreCase);
        var summary = string.Format(Loc("StrCfgRestoreSummary"),
            Path.GetFileName(dlg.FileName),
            "!" + backup.NodeIdHex, "!" + myId,
            string.IsNullOrEmpty(backup.FirmwareVersion) ? "?" : backup.FirmwareVersion,
            _protocolService.GetFirmwareVersion() ?? "?",
            backup.ExportedAtUtc?.ToLocalTime().ToString("g") ?? "?",
            ConfigBackupFile.Describe(backup));
        if (!sameNode) summary += "\n\n" + Loc("StrCfgRestoreNodeMismatch");
        else if (backup.HasSecurity) summary += "\n\n" + Loc("StrCfgRestoreSecurityIncluded");
        summary += "\n\n" + Loc("StrCfgRestoreConfirm");

        if (MessageBox.Show(summary, Loc("StrCfgRestoreBtn"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        ConfigBackupBtn.IsEnabled = false;
        ConfigRestoreBtn.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(s => UpdateStatusBar(string.Format(Loc("StrCfgRestoreProgress"), s)));
            await _protocolService.RestoreConfigBackupAsync(backup, includeSecurity: sameNode, progress);
            UpdateStatusBar(Loc("StrCfgRestoreDone"));
            MessageBox.Show(Loc("StrCfgRestoreDone"), Loc("StrCfgToolsSection"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[Restore] failed: {ex}");
            MessageBox.Show(string.Format(Loc("StrCfgRestoreFailed"), ex.Message), Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ConfigBackupBtn.IsEnabled = true;
            ConfigRestoreBtn.IsEnabled = true;
        }
    }

    /// <summary>The device reboots on its own right after ota_request / enter_dfu_mode — before the wizard
    /// gets to tear the connection down. Marking the disconnect as intentional up front keeps the
    /// auto-reconnect from grabbing the device (or its bootloader) while the loader is being driven.</summary>
    public void SetAutoReconnectSuppressed(bool suppressed)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _intentionalDisconnect = suppressed;
            if (suppressed) _isReconnecting = false;
            Services.Logger.WriteLine($"[FW] Auto-reconnect {(suppressed ? "suppressed" : "re-enabled")}");
        });
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
