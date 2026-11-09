using System.Windows;
using MeshhessenClient.Services;

namespace MeshhessenClient;

/// <summary>
/// Own node status (StatusMessage module, port 36 / FW 2.7.20+): the header button next
/// to the Funk-Echo radar opens a small dialog; the text is written via
/// set_module_config(statusmessage), which the firmware applies WITHOUT a reboot and
/// then broadcasts (on change and every 12 h).
/// </summary>
public partial class MainWindow
{
    /// <summary>nanopb max_size:80 → 79 usable UTF-8 bytes + NUL.</summary>
    private const int NodeStatusMaxBytes = 79;

    private StatusMessageConfig? _myStatusConfig;

    private void OnStatusMessageConfigReceived(object? sender, StatusMessageConfig config)
    {
        _myStatusConfig = config;
        Dispatcher.BeginInvoke(UpdateNodeStatusButtonVisual);
    }

    /// <summary>Button lights up and carries the current status as tooltip when one is set.</summary>
    private void UpdateNodeStatusButtonVisual()
    {
        if (NodeStatusIcon == null) return;
        bool hasStatus = !string.IsNullOrWhiteSpace(_myStatusConfig?.NodeStatus);
        NodeStatusIcon.Opacity = hasStatus ? 1.0 : 0.55;
        NodeStatusButton.ToolTip = hasStatus
            ? $"{Loc("StrNodeStatusTooltip")}: „{_myStatusConfig!.NodeStatus}“"
            : Loc("StrNodeStatusTooltip");
    }

    private async void NodeStatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_connectionService.IsConnected)
        {
            UpdateStatusBar(Loc("StrNodeStatusNotConnected"));
            return;
        }

        // Warm the cache on first use: the module config isn't fetched during connect.
        if (_myStatusConfig == null)
        {
            try
            {
                await _protocolService.RequestStatusMessageConfigAsync();
                await Task.Delay(800); // response fills _myStatusConfig via the event
            }
            catch { /* dialog still opens, just without prefill */ }
        }

        var dialog = new NodeStatusDialog(_myStatusConfig?.NodeStatus ?? "") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        string status = Utf8Text.Clamp(dialog.StatusText.Trim(), NodeStatusMaxBytes);
        try
        {
            var cfg = _myStatusConfig?.Clone() ?? new StatusMessageConfig();
            cfg.NodeStatus = status;
            await _protocolService.SetStatusMessageConfigAsync(cfg);
            _myStatusConfig = cfg;
            UpdateNodeStatusButtonVisual();
            UpdateStatusBar(string.IsNullOrEmpty(status)
                ? Loc("StrNodeStatusCleared")
                : string.Format(Loc("StrNodeStatusSaved"), status));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc("StrError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
