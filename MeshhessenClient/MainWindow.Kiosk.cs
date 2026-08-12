// Kiosk-/Trainingsmodus
// Ausgelagert aus MainWindow.xaml.cs (partial class) – reine Umsortierung, keine Logikaenderung.

using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Styles;
using Mapsui.Projections;
using Mapsui.Tiling.Layers;
using Mapsui.Extensions;
using BruTile;
using BruTile.Predefined;
using NetTopologySuite.Geometries;
using Mapsui.Nts;

namespace MeshhessenClient;

public partial class MainWindow
{
    // ── Kiosk / Training mode ─────────────────────────────────────────────────
    // Accident protection for shared stations, not a security boundary:
    // anyone who can edit meshhessen-client.ini can disable it (documented).

    private static string KioskHashPassword(string password)
    {
        byte[] salt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        byte[] hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool KioskVerifyPassword(string password, string stored)
    {
        try
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            byte[] salt     = Convert.FromBase64String(parts[0]);
            byte[] expected = Convert.FromBase64String(parts[1]);
            byte[] actual   = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
                password, salt, 100_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch { return false; }
    }

    private string CollectKioskLockedFeatures()
    {
        var f = new List<string>();
        if (KioskLockTabNodesCheckBox?.IsChecked == true)    f.Add("TabNodes");
        if (KioskLockTabChannelsCheckBox?.IsChecked == true) f.Add("TabChannels");
        if (KioskLockTabSettingsCheckBox?.IsChecked == true) f.Add("TabSettings");
        if (KioskLockTabInfoCheckBox?.IsChecked == true)     f.Add("TabInfo");
        if (KioskLockTabToolsCheckBox?.IsChecked == true)    f.Add("TabTools");
        if (KioskLockTabDebugCheckBox?.IsChecked == true)    f.Add("TabDebug");
        if (KioskLockNodeConfigCheckBox?.IsChecked == true)  f.Add("NodeConfig");
        if (KioskLockRemoteAdminCheckBox?.IsChecked == true) f.Add("RemoteAdmin");
        if (KioskLockDashboardCheckBox?.IsChecked == true)   f.Add("Dashboard");
        if (KioskLockSosCheckBox?.IsChecked == true)         f.Add("Sos");
        if (KioskLockMeshHessenCheckBox?.IsChecked == true)  f.Add("MeshHessen");
        return string.Join(",", f);
    }

    private void ApplyKioskCheckboxes(string csv)
    {
        var set = new HashSet<string>(csv.Split(',', StringSplitOptions.RemoveEmptyEntries));
        if (KioskLockTabNodesCheckBox    != null) KioskLockTabNodesCheckBox.IsChecked    = set.Contains("TabNodes");
        if (KioskLockTabChannelsCheckBox != null) KioskLockTabChannelsCheckBox.IsChecked = set.Contains("TabChannels");
        if (KioskLockTabSettingsCheckBox != null) KioskLockTabSettingsCheckBox.IsChecked = set.Contains("TabSettings");
        if (KioskLockTabInfoCheckBox     != null) KioskLockTabInfoCheckBox.IsChecked     = set.Contains("TabInfo");
        if (KioskLockTabToolsCheckBox    != null) KioskLockTabToolsCheckBox.IsChecked    = set.Contains("TabTools");
        if (KioskLockTabDebugCheckBox    != null) KioskLockTabDebugCheckBox.IsChecked    = set.Contains("TabDebug");
        if (KioskLockNodeConfigCheckBox  != null) KioskLockNodeConfigCheckBox.IsChecked  = set.Contains("NodeConfig");
        if (KioskLockRemoteAdminCheckBox != null) KioskLockRemoteAdminCheckBox.IsChecked = set.Contains("RemoteAdmin");
        if (KioskLockDashboardCheckBox   != null) KioskLockDashboardCheckBox.IsChecked   = set.Contains("Dashboard");
        if (KioskLockSosCheckBox         != null) KioskLockSosCheckBox.IsChecked         = set.Contains("Sos");
        if (KioskLockMeshHessenCheckBox  != null) KioskLockMeshHessenCheckBox.IsChecked  = set.Contains("MeshHessen");
    }

    private bool IsKioskFeatureLocked(string feature) =>
        _kioskLocked &&
        _currentSettings.KioskLockedFeatures.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(feature);

    private void ApplyKioskLock(bool locked)
    {
        _kioskLocked = locked;
        var vis = (string feature) =>
            locked && _currentSettings.KioskLockedFeatures
                .Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(feature)
                ? Visibility.Collapsed : Visibility.Visible;

        // Tabs (Messages + Map are never lockable so the app can't be emptied out)
        TabItemNodes.Visibility    = vis("TabNodes");
        TabItemChannels.Visibility = vis("TabChannels");
        TabItemSettings.Visibility = vis("TabSettings");
        TabItemInfo.Visibility     = vis("TabInfo");
        TabItemTools.Visibility    = vis("TabTools");
        TabItemDebug.Visibility    = vis("TabDebug");

        // If the currently selected tab is now hidden, jump to Messages (index 0)
        if (locked && MainTabs.SelectedItem is TabItem sel && sel.Visibility == Visibility.Collapsed)
            MainTabs.SelectedIndex = 0;

        // Buttons / functions
        NodeConfigButton.Visibility    = vis("NodeConfig");
        RemoteAdminButton.Visibility   = vis("RemoteAdmin");
        OpenDashboardButton.Visibility = vis("Dashboard");
        AlertBellButton.Visibility     = vis("Sos");
        MeshHessenButton.Visibility    = vis("MeshHessen");

        // Enforce VNode admin blocking while locked (revert to setting when unlocked)
        if (_virtualNodeService != null)
            _virtualNodeService.BlockAdminCommands = locked || _currentSettings.VirtualNodeBlockAdmin;

        UpdateKioskLockButton();
        UpdateStatusBar(Loc(locked ? "StrKioskLockedStatus" : "StrKioskUnlockedStatus"));
    }

    private void UpdateKioskLockButton()
    {
        bool available = _currentSettings.KioskModeEnabled &&
                         !string.IsNullOrEmpty(_currentSettings.KioskPasswordHash);
        KioskLockButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        KioskLockButton.Content = _kioskLocked ? "🔒" : "🔓";
        KioskLockButton.ToolTip = Loc(_kioskLocked ? "StrKioskLockedTooltip" : "StrKioskUnlockedTooltip");
    }

    private void KioskLock_Click(object sender, RoutedEventArgs e)
    {
        if (_kioskLocked)
        {
            // Unlock: ask for password
            var pw = ShowPasswordDialog(Loc("StrKioskUnlockPrompt"), Loc("StrKioskUnlockTitle"));
            if (pw == null) return; // cancelled
            if (!KioskVerifyPassword(pw, _currentSettings.KioskPasswordHash))
            {
                MessageBox.Show(Loc("StrKioskWrongPassword"), Loc("StrKioskUnlockTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ApplyKioskLock(false);
        }
        else
        {
            // Re-lock: take over the current checkbox selection and persist it,
            // so the lock state and feature list survive a restart
            _currentSettings = _currentSettings with { KioskLockedFeatures = CollectKioskLockedFeatures() };
            SettingsService.Save(_currentSettings);
            ApplyKioskLock(true);
        }
    }

    private void KioskEnable_Changed(object sender, RoutedEventArgs e)
    {
        if (KioskEnableCheckBox == null) return;
        if (KioskEnableCheckBox.IsChecked == true &&
            string.IsNullOrEmpty(_currentSettings.KioskPasswordHash))
        {
            UpdateStatusBar(Loc("StrKioskNoPasswordWarning"));
        }
    }

    private void KioskSetPassword_Click(object sender, RoutedEventArgs e)
    {
        var pw = KioskPasswordBox.Password;
        if (string.IsNullOrEmpty(pw))
        {
            MessageBox.Show(Loc("StrKioskPasswordEmpty"), Loc("StrKioskSection"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _currentSettings = _currentSettings with { KioskPasswordHash = KioskHashPassword(pw) };
        SettingsService.Save(_currentSettings);
        KioskPasswordBox.Clear();
        UpdateKioskLockButton();
        UpdateStatusBar(Loc("StrKioskPasswordSet"));
    }

    private string? ShowPasswordDialog(string prompt, string title)
    {
        var dialog = new System.Windows.Window
        {
            Title = title,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        var pwBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(pwBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = Loc("StrCancel"), Width = 80, IsCancel = true };
        ok.Click += (_, _) => { dialog.DialogResult = true; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        pwBox.Focus();
        return dialog.ShowDialog() == true ? pwBox.Password : null;
    }

    private void RefreshActiveStationName()
    {
        string resolved;
        bool isGlobal = false;
        bool isNode = false;

        if (!string.IsNullOrEmpty(_currentSettings.StationName))
        {
            resolved = _currentSettings.StationName;
            isGlobal = true;
        }
        else if (_myNodeId != 0 &&
                 _currentSettings.NodeStationNames.TryGetValue(_myNodeId, out var nodeSpecific) &&
                 !string.IsNullOrEmpty(nodeSpecific))
        {
            resolved = nodeSpecific;
            isNode = true;
        }
        else if (_myNodeId != 0)
        {
            resolved = _allNodes.FirstOrDefault(n => n.NodeId == _myNodeId)?.ShortName ?? string.Empty;
        }
        else
        {
            resolved = _currentSettings.StationName;
        }

        _activeStationName = resolved;
        StationNameLabel.Text = resolved;

        if (isGlobal)
        {
            StationNameLabel.Foreground = System.Windows.Media.Brushes.Red;
            StationNameLabel.ToolTip = Loc("StrStationNameGlobalTooltip");
        }
        else if (isNode)
        {
            StationNameLabel.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0x80, 0x00));
            StationNameLabel.ToolTip = Loc("StrStationNameNodeTooltip");
        }
        else if (_myNodeId != 0)
        {
            StationNameLabel.Foreground = System.Windows.Media.Brushes.Gray;
            StationNameLabel.ToolTip = Loc("StrStationNameAutoTooltip");
        }
        else
        {
            StationNameLabel.Foreground = System.Windows.Media.Brushes.Red;
            StationNameLabel.ToolTip = null;
        }
    }

    private void StationNameEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_myNodeId == 0) return;

        _currentSettings.NodeStationNames.TryGetValue(_myNodeId, out var current);
        var result = ShowInputDialog(
            Loc("StrSetNodeStationNamePrompt"),
            Loc("StrSetNodeStationNameTitle"),
            current ?? string.Empty);

        if (result == null) return; // cancelled

        var updated = new Dictionary<uint, string>(_currentSettings.NodeStationNames);
        if (string.IsNullOrWhiteSpace(result))
            updated.Remove(_myNodeId);
        else
            updated[_myNodeId] = result.Trim();

        _currentSettings = _currentSettings with { NodeStationNames = updated };
        SettingsService.Save(_currentSettings);
        RefreshActiveStationName();
    }

    private static string? ShowInputDialog(string prompt, string title, string defaultValue)
    {
        var dialog = new System.Windows.Window
        {
            Title = title,
            Width = 400,
            SizeToContent = System.Windows.SizeToContent.Height,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(12) };
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Margin = new System.Windows.Thickness(0, 0, 0, 8)
        });
        var textBox = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            Margin = new System.Windows.Thickness(0, 0, 0, 8)
        };
        panel.Children.Add(textBox);
        var buttons = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        string? result = null;
        var ok = new System.Windows.Controls.Button
        {
            Content = "OK", Width = 80,
            Margin = new System.Windows.Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancel = new System.Windows.Controls.Button
        {
            Content = "Abbrechen", Width = 80,
            IsCancel = true
        };
        ok.Click += (_, _) => { result = textBox.Text; dialog.DialogResult = true; };
        cancel.Click += (_, _) => { dialog.DialogResult = false; };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        textBox.Focus();
        textBox.SelectAll();
        dialog.ShowDialog();
        return dialog.DialogResult == true ? result : null;
    }

    private void UpdateStatusBar(string message)
    {
        StatusBarText.Text = message;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        DebugLogTextBox.Clear();
        UpdateStatusBar("Debug-Log gelöscht");
    }

    private async void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = DebugLogTextBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                UpdateStatusBar("Log ist leer");
                return;
            }

            CopyLogButton.IsEnabled = false;
            UpdateStatusBar("Kopiere Log...");

            // Clipboard operation im Hintergrund
            await Task.Run(() =>
            {
                try
                {
                    // WPF Clipboard benötigt STA thread
                    Thread thread = new Thread(() =>
                    {
                        try
                        {
                            Clipboard.SetDataObject(text, true);
                        }
                        catch (Exception ex)
                        {
                            Services.Logger.WriteLine($"Clipboard error: {ex.Message}");
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join(1000); // Max 1 Sekunde warten
                }
                catch (Exception ex)
                {
                    Services.Logger.WriteLine($"Clipboard error: {ex.Message}");
                }
            });

            UpdateStatusBar("Log kopiert");
            CopyLogButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            UpdateStatusBar($"Fehler: {ex.Message}");
            CopyLogButton.IsEnabled = true;
        }
    }

    private void OpenLogFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var logPath = Services.Logger.GetLogFilePath();
            if (!string.IsNullOrEmpty(logPath) && System.IO.File.Exists(logPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                });
                UpdateStatusBar($"Log-Datei geöffnet: {logPath}");
            }
            else
            {
                MessageBox.Show("Log-Datei wurde nicht gefunden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Öffnen der Log-Datei: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetConnectionStatus(ConnectionStatus status)
    {
        switch (status)
        {
            case ConnectionStatus.Disconnected:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Gray);
                StatusText.Text = Loc("StrDisconnected");
                break;
            case ConnectionStatus.Connecting:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Yellow);
                StatusText.Text = Loc("StrConnecting");
                break;
            case ConnectionStatus.Initializing:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                StatusText.Text = Loc("StrInitializing");
                break;
            case ConnectionStatus.Ready:
                StatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                StatusText.Text = Loc("StrConnected");
                // Start signal analysis background timer (5s initial delay after connect, then every 10 min)
                _analysisTimer?.Dispose();
                _analysisTimer = new System.Threading.Timer(
                    _ => RefreshSignalAnalysis(),
                    null,
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromMinutes(10));
                // Load last 24h from message DB (once per connection)
                if (_messageDbManager != null)
                    _ = Task.Run(LoadMessagesFromDbAsync);
                break;
            case ConnectionStatus.Disconnecting:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                StatusText.Text = Loc("StrDisconnecting");
                break;
            case ConnectionStatus.Error:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                StatusText.Text = Loc("StrError");
                break;
        }
    }

    // -- Message DB load / lazy-load -------------------------------------------

    /// <summary>Load last 24h from DB on connection ready. Runs on background thread.</summary>
    private void LoadMessagesFromDbAsync()
    {
        try
        {
            if (_messageDbManager == null) return;
            var since = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeSeconds();
            var entries = _messageDbManager.LoadAllChannelMessagesSince(since);
            if (entries.Count == 0) return;

            // Sort by timestamp
            entries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            Dispatcher.BeginInvoke(() =>
            {
                foreach (var entry in entries)
                {
                    // Skip if already present (same PacketId + timestamp guard)
                    if (entry.PacketId != 0 && _messageById.ContainsKey(entry.PacketId))
                        continue;

                    var msg = DbEntryToMessageItem(entry);
                    msg.IsOwnMessage = (_myNodeId != 0 && msg.FromId == _myNodeId);
                    var senderNodeA = _allNodes.FirstOrDefault(n => n.NodeId == msg.FromId);
                    if (senderNodeA != null) msg.SenderShortName = senderNodeA.ShortName;
                    _allMessages.Add(msg);
                    if (entry.PacketId != 0)
                        _messageById[entry.PacketId] = msg;

                    // Track oldest timestamp
                    if (entry.Timestamp < _dbOldestTimestamp)
                        _dbOldestTimestamp = entry.Timestamp;
                }

                // Rebuild visible list and scroll to newest
                RebuildVisibleMessages();
                if (_messages.Count > 0)
                    MessageListView.ScrollIntoView(_messages[^1]);
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"[MsgDB] LoadMessagesFromDbAsync: {ex.Message}");
        }
    }

    /// <summary>Load N messages before the oldest displayed. Called when user scrolls to top.</summary>
    private void LazyLoadOlderMessages()
    {
        if (_messageDbManager == null || _dbOldestTimestamp == long.MaxValue) return;

        // Collect entries from all channel DBs older than current oldest
        var allEntries = new List<Models.MessageDbEntry>();

        // We load 50 messages older than the oldest timestamp per channel
        foreach (var channel in _channels)
        {
            var entries = _messageDbManager.LoadChannelMessagesBefore(
                channel.Index, channel.Name, _dbOldestTimestamp, 50);
            allEntries.AddRange(entries);
        }

        if (allEntries.Count == 0) return;

        allEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // Prepend to message collections while preserving scroll position
        var firstVisible = _messages.Count > 0 ? _messages[0] : null;

        foreach (var entry in allEntries)
        {
            if (entry.PacketId != 0 && _messageById.ContainsKey(entry.PacketId)) continue;
            var msg = DbEntryToMessageItem(entry);
            msg.IsOwnMessage = (_myNodeId != 0 && msg.FromId == _myNodeId);
            var senderNodeB = _allNodes.FirstOrDefault(n => n.NodeId == msg.FromId);
            if (senderNodeB != null) msg.SenderShortName = senderNodeB.ShortName;

            // Insert at start of _allMessages (before first live entry)
            int insertIdx = 0;
            _allMessages.Insert(insertIdx++, msg);
            if (entry.PacketId != 0) _messageById[entry.PacketId] = msg;

            if (entry.Timestamp < _dbOldestTimestamp)
                _dbOldestTimestamp = entry.Timestamp;
        }

        RebuildVisibleMessages();

        // Restore scroll position to the item that was previously first
        if (firstVisible != null && _messages.Contains(firstVisible))
            MessageListView.ScrollIntoView(firstVisible);
    }

    private static Models.MessageItem DbEntryToMessageItem(Models.MessageDbEntry e)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(e.Timestamp).ToLocalTime();
        var today = DateTime.Today;
        string timeStr = dt.Date == today
            ? dt.ToString("HH:mm")
            : dt.Date == today.AddDays(-1)
                ? $"Gestern {dt:HH:mm}"
                : dt.ToString("dd.MM. HH:mm");

        return new Models.MessageItem
        {
            Id              = e.PacketId,
            Time            = timeStr,
            SortTime        = dt.LocalDateTime,
            From            = e.FromName,
            FromId          = e.FromId,
            ToId            = e.ToId,
            Message         = e.Message,
            Channel         = e.ChannelIndex.ToString(),
            ChannelIndex    = (uint)e.ChannelIndex,
            ChannelName     = e.ChannelName,
            IsEncrypted     = e.IsEncrypted,
            IsViaMqtt       = e.IsViaMqtt,
            ReplyId         = e.ReplyId,
            ReplyFromName   = e.ReplyFromName,
            ReplyPreview    = e.ReplyPreview,
            SenderColorHex  = e.SenderColorHex,
            SenderNote      = e.SenderNote
        };
    }

    private void RebuildVisibleMessages()
    {
        _messages.Clear();
        foreach (var msg in _allMessages.OrderBy(m => m.SortTime))
        {
            bool passes = true;
            if (_messageChannelFilter != null && _messageChannelFilter.Index != 999)
            {
                if (uint.TryParse(msg.Channel, out uint idx))
                    passes = idx == _messageChannelFilter.Index;
            }
            if (passes) _messages.Add(msg);
        }
    }

    private void MessageListView_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // When scrolled to (near) the top, lazy-load older messages from DB
        if (e.VerticalOffset < 10 && _messageDbManager != null && _dbOldestTimestamp != long.MaxValue)
            LazyLoadOlderMessages();
    }

    private void ClearChannelMessages_Click(object sender, RoutedEventArgs e)
    {
        if (_messageDbManager == null) return;
        int days = 0;
        if (ClearChannelAgeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var d))
            days = d;
        _messageDbManager.ClearAllChannelDbs(days);
        Services.Logger.WriteLine($"[MsgDB] Channel DBs cleared (olderThanDays={days})");
    }

    private void ClearDmMessages_Click(object sender, RoutedEventArgs e)
    {
        if (_messageDbManager == null) return;
        _messageDbManager.ClearAllDms();
        Services.Logger.WriteLine("[MsgDB] All DM messages cleared");
    }

    private void ClearSelectedChannelDb_Click(object sender, RoutedEventArgs e)
    {
        if (_messageDbManager == null)
        {
            MessageBox.Show(Loc("StrMsgDbNotEnabled"), Loc("StrMsgDbClearSelectedChannel"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (((System.Windows.Controls.Button)sender).Tag is not ChannelInfo channel) return;

        int? days = ShowClearDbDialog(channel.Name);
        if (days == null) return;

        _messageDbManager.ClearChannelDb(channel.Index, channel.Name, days.Value);
        Services.Logger.WriteLine($"[MsgDB] Channel DB cleared: {channel.Name} (olderThanDays={days})");
    }

    private int? ShowClearDbDialog(string channelName)
    {
        var dlg = new Window
        {
            Title = Loc("StrMsgDbClearSelectedChannel"),
            Width = 380, Height = 185,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };

        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = string.Format(Loc("StrMsgDbClearChannelPrompt"), channelName),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        });

        var combo = new System.Windows.Controls.ComboBox
        {
            Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 16)
        };
        combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Tag = "0",   Content = Loc("StrMsgDbClearAll") });
        combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Tag = "30",  Content = Loc("StrMsgDbClearOlder30") });
        combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Tag = "90",  Content = Loc("StrMsgDbClearOlder90") });
        combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Tag = "365", Content = Loc("StrMsgDbClearOlder365") });
        combo.SelectedIndex = 0;
        sp.Children.Add(combo);

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        int? result = null;
        var okBtn = new System.Windows.Controls.Button
        {
            Content = Loc("StrMsgDbClearConfirm"),
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0)
        };
        okBtn.Click += (_, _) =>
        {
            if (combo.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
                item.Tag is string t && int.TryParse(t, out var d))
                result = d;
            dlg.DialogResult = true;
        };
        var cancelBtn = new System.Windows.Controls.Button { Content = Loc("StrCancel"), Width = 90 };
        cancelBtn.Click += (_, _) => { dlg.DialogResult = false; };

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        sp.Children.Add(btnPanel);
        dlg.Content = sp;

        return dlg.ShowDialog() == true ? result : null;
    }

    private void UpdateMessageFilterComboBox()
    {
        // Speichere aktuelle Auswahl
        var selectedFilter = MessageChannelFilterComboBox.SelectedItem as ChannelInfo;

        // Erstelle Liste mit "Alle" Option
        var filterItems = new List<ChannelInfo>();
        filterItems.Add(new ChannelInfo { Index = 999, Name = Loc("StrAllChannels"), Role = "" });
        filterItems.AddRange(_channels);

        MessageChannelFilterComboBox.ItemsSource = filterItems;

        // Stelle Auswahl wieder her oder wähle "Alle"
        if (selectedFilter != null)
        {
            var restored = filterItems.FirstOrDefault(c => c.Index == selectedFilter.Index);
            MessageChannelFilterComboBox.SelectedItem = restored ?? filterItems[0];
        }
        else
        {
            MessageChannelFilterComboBox.SelectedIndex = 0;
        }
    }

    private void DarkMode_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            var isDarkMode = DarkModeCheckBox.IsChecked == true;
            ModernWpf.ThemeManager.Current.ApplicationTheme = isDarkMode
                ? ModernWpf.ApplicationTheme.Dark
                : ModernWpf.ApplicationTheme.Light;

            Services.Logger.WriteLine($"Theme changed to: {(isDarkMode ? "Dark" : "Light")}");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error changing theme: {ex.Message}");
        }
    }

    private void OpenDmWindow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_dmWindow == null)
            {
                _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
                _dmWindow.SetMessageDbManager(_messageDbManager);
            }

            // Zeige Fenster
            if (_dmWindow.IsVisible)
            {
                _dmWindow.Activate(); // Bringe in den Vordergrund
            }
            else
            {
                _dmWindow.Show();
                _dmWindow.Activate();
            }

            // Reset Button-Hervorhebung
            OpenDmWindowButton.FontWeight = FontWeights.Normal;
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR opening DM window: {ex.Message}");
            MessageBox.Show($"Fehler beim Öffnen des DM-Fensters: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MessageChannelFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            _messageChannelFilter = MessageChannelFilterComboBox.SelectedItem as ChannelInfo;

            // Reload messages with new filter from ALL messages
            _messages.Clear();

            foreach (var msg in _allMessages)
            {
                // Apply filter
                if (_messageChannelFilter == null || _messageChannelFilter.Index == 999)
                {
                    // "Alle Kanäle" ausgewählt - zeige alle
                    _messages.Add(msg);
                }
                else
                {
                    // Spezifischer Kanal ausgewählt - nur diesen zeigen
                    if (uint.TryParse(msg.Channel, out uint channelIndex))
                    {
                        if (channelIndex == _messageChannelFilter.Index)
                        {
                            _messages.Add(msg);
                        }
                    }
                }
            }

            // Sync send-channel dropdown to the selected filter channel
            // (skip "Alle Kanäle" sentinel with Index 999)
            if (_messageChannelFilter != null && _messageChannelFilter.Index != 999)
            {
                var match = _channels.FirstOrDefault(c => c.Index == _messageChannelFilter.Index);
                if (match != null && !Equals(ActiveChannelComboBox.SelectedItem, match))
                    ActiveChannelComboBox.SelectedItem = match;
            }

            Services.Logger.WriteLine($"Message filter changed to: {_messageChannelFilter?.Name ?? "Alle"} ({_messages.Count}/{_allMessages.Count} messages)");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error changing message filter: {ex.Message}");
        }
    }

    private void SendDmToNode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Hole ausgewählten Knoten
            var selectedNode = SelectedNodeForMenu;
            if (selectedNode == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Knoten aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Prüfe ob eigener Knoten
            if (selectedNode.NodeId == _myNodeId)
            {
                MessageBox.Show(Loc("StrNoDmToSelf"), Loc("StrHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Öffne/Erstelle DM-Fenster
            if (_dmWindow == null)
            {
                _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
                _dmWindow.SetMessageDbManager(_messageDbManager);
            }

            // Öffne Chat mit diesem Knoten
            _dmWindow.OpenChatWithNode(selectedNode.NodeId, selectedNode.Name);

            Services.Logger.WriteLine($"Opening DM chat with node: {selectedNode.Name} ({selectedNode.Id})");
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR in SendDmToNode_Click: {ex.Message}");
            MessageBox.Show($"Fehler beim Öffnen des Chats: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // Disconnect synchronously to ensure clean shutdown
            if (_connectionService.IsConnected)
            {
                Services.Logger.WriteLine("Application closing...");
                _protocolService.Disconnect();
                System.Threading.Thread.Sleep(100);
                _connectionService.Disconnect();
                Services.Logger.WriteLine("Disconnected");
            }
            Services.Logger.Close();
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error during close: {ex.Message}");
            Services.Logger.Close();
        }

        // Force application shutdown
        Application.Current.Shutdown();

        base.OnClosing(e);
    }

}
