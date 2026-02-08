using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeshhessenClient.Models;
using MeshhessenClient.Services;
using LoRaConfig = Meshtastic.Protobufs.LoRaConfig;

namespace MeshhessenClient;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Initializing,
    Ready,
    Disconnecting,
    Error
}

public partial class MainWindow : Window
{
    private readonly SerialPortService _serialPortService;
    private readonly MeshtasticProtocolService _protocolService;

    private ObservableCollection<MessageItem> _messages = new();
    private List<MessageItem> _allMessages = new(); // Ungefilterte Liste aller Nachrichten
    private ObservableCollection<Models.NodeInfo> _nodes = new();
    private ObservableCollection<ChannelInfo> _channels = new();
    private int _activeChannelIndex = 0;
    private bool _showEncryptedMessages = true;
    private ChannelInfo? _messageChannelFilter = null;
    private DirectMessagesWindow? _dmWindow = null;
    private uint _myNodeId = 0;

    public MainWindow()
    {
        InitializeComponent();

        _serialPortService = new SerialPortService();
        _protocolService = new MeshtasticProtocolService(_serialPortService);

        MessageListView.ItemsSource = _messages;
        NodesListView.ItemsSource = _nodes;
        ChannelsListView.ItemsSource = _channels;
        ActiveChannelComboBox.ItemsSource = _channels;

        _serialPortService.ConnectionStateChanged += OnConnectionStateChanged;
        _protocolService.MessageReceived += OnMessageReceived;
        _protocolService.NodeInfoReceived += OnNodeInfoReceived;
        _protocolService.ChannelInfoReceived += OnChannelInfoReceived;
        _protocolService.LoRaConfigReceived += OnLoRaConfigReceived;
        _protocolService.DeviceInfoReceived += OnDeviceInfoReceived;

        RefreshPorts();
        LoadRegions();
        LoadModemPresets();

        // Logger Event abonnieren für Debug-Fenster
        Services.Logger.LogMessageReceived += OnLogMessageReceived;

        // Zeige Log-Datei-Pfad in der Status-Leiste
        var logPath = Services.Logger.GetLogFilePath();
        if (!string.IsNullOrEmpty(logPath))
        {
            UpdateStatusBar($"Log-Datei: {logPath}");
        }

        // Checkbox für verschlüsselte Nachrichten
        ShowEncryptedMessagesCheckBox.Checked += (s, e) => _showEncryptedMessages = true;
        ShowEncryptedMessagesCheckBox.Unchecked += (s, e) => _showEncryptedMessages = false;
    }

    private void OnLogMessageReceived(object? sender, string logMessage)
    {
        Dispatcher.BeginInvoke(() =>
        {
            DebugLogTextBox.AppendText(logMessage + Environment.NewLine);
            DebugLogTextBox.ScrollToEnd();

            // Begrenze auf maximal 10000 Zeilen
            var lines = DebugLogTextBox.Text.Split('\n');
            if (lines.Length > 10000)
            {
                DebugLogTextBox.Text = string.Join('\n', lines.Skip(lines.Length - 10000));
            }
        });
    }

    private void ActiveChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ActiveChannelComboBox.SelectedItem is ChannelInfo channel)
        {
            _activeChannelIndex = channel.Index;
            UpdateStatusBar($"Aktiver Kanal: {channel.Name}");
        }
    }

    private void RefreshPorts()
    {
        var ports = SerialPort.GetPortNames();
        PortComboBox.ItemsSource = ports;
        if (ports.Length > 0)
        {
            PortComboBox.SelectedIndex = 0;
        }
    }

    private void LoadRegions()
    {
        RegionComboBox.ItemsSource = new[] { "Unset", "US", "EU_868", "CN", "JP", "ANZ", "KR", "TW", "RU", "IN", "NZ_865", "TH", "LORA_24", "UA_868", "MY_919", "MY_433", "SG_923", "WLAN" };
        RegionComboBox.SelectedIndex = 0;
    }

    private void LoadModemPresets()
    {
        ModemPresetComboBox.ItemsSource = new[] { "LONG_FAST", "LONG_SLOW", "VERY_LONG_SLOW", "MEDIUM_FAST", "MEDIUM_SLOW", "SHORT_FAST", "SHORT_SLOW", "LONG_MODERATE" };
        ModemPresetComboBox.SelectedIndex = 0;
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e)
    {
        RefreshPorts();
        UpdateStatusBar("Ports aktualisiert");
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_serialPortService.IsConnected)
        {
            try
            {
                ConnectButton.IsEnabled = false;
                UpdateStatusBar("Trenne Verbindung...");
                SetConnectionStatus(ConnectionStatus.Disconnecting);

                // Disconnect im Hintergrund, nicht auf UI-Thread blockieren
                await Task.Run(() =>
                {
                    _protocolService.Disconnect();
                    System.Threading.Thread.Sleep(200);
                    _serialPortService.Disconnect();
                });

                ConnectButton.Content = "Verbinden";
                UpdateStatusBar("Getrennt");
                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Disconnect error: {ex.Message}");
                UpdateStatusBar("Fehler beim Trennen");
                SetConnectionStatus(ConnectionStatus.Error);
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        }
        else
        {
            var selectedPort = PortComboBox.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedPort))
            {
                MessageBox.Show("Bitte wählen Sie einen COM Port aus.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ConnectButton.IsEnabled = false;
                UpdateStatusBar($"Verbinde mit {selectedPort}...");
                SetConnectionStatus(ConnectionStatus.Connecting);

                await _serialPortService.ConnectAsync(selectedPort);

                // GUI sofort als "Verbunden" anzeigen
                ConnectButton.Content = "Trennen";
                ConnectButton.IsEnabled = true;
                UpdateStatusBar($"Verbunden mit {selectedPort} - Initialisiere...");
                SetConnectionStatus(ConnectionStatus.Initializing);

                // Initialisierung im Hintergrund starten (nicht blockieren!)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _protocolService.InitializeAsync();
                        Dispatcher.BeginInvoke(() =>
                        {
                            UpdateStatusBar($"Verbunden mit {selectedPort} - Bereit");
                            SetConnectionStatus(ConnectionStatus.Ready);
                        });
                    }
                    catch (Exception initEx)
                    {
                        Services.Logger.WriteLine($"Initialization error: {initEx.Message}");
                        Dispatcher.BeginInvoke(() =>
                        {
                            UpdateStatusBar($"Verbunden mit {selectedPort} - Init-Fehler");
                            SetConnectionStatus(ConnectionStatus.Error);
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Verbindung fehlgeschlagen: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("Verbindung fehlgeschlagen");
                SetConnectionStatus(ConnectionStatus.Error);
                ConnectButton.IsEnabled = true;
            }
        }
    }

    private void PortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        await SendMessage();
    }

    private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        var message = MessageTextBox.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (!_serialPortService.IsConnected)
        {
            MessageBox.Show("Nicht verbunden. Bitte zuerst mit einem Gerät verbinden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            SendButton.IsEnabled = false;

            // Sende Nachricht mit dem aktiven Kanal
            await _protocolService.SendTextMessageAsync(message, 0xFFFFFFFF, (uint)_activeChannelIndex);

            // Zeige gesendete Nachricht in der Liste
            var activeChannel = _channels.FirstOrDefault(c => c.Index == _activeChannelIndex);
            var channelName = activeChannel?.Name ?? $"Kanal {_activeChannelIndex}";

            var sentMessage = new MessageItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                From = "Ich",
                Message = message,
                Channel = _activeChannelIndex.ToString(),
                ChannelName = channelName
            };
            _messages.Add(sentMessage);
            MessageListView.ScrollIntoView(sentMessage);

            // Log die gesendete Nachricht
            Services.MessageLogger.LogChannelMessage(_activeChannelIndex, channelName, "Ich", message);

            MessageTextBox.Clear();
            UpdateStatusBar($"Nachricht gesendet (Kanal {_activeChannelIndex})");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Fehler beim Senden: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }

    private void AddChannel_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Kanal hinzufügen - wird in Kürze implementiert", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void EditChannel_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Kanal bearbeiten - wird in Kürze implementiert", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteChannel_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Kanal löschen - wird in Kürze implementiert", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Einstellungen speichern - wird in Kürze implementiert", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnConnectionStateChanged(object? sender, bool isConnected)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                StatusIndicator.Fill = isConnected ? Brushes.Green : Brushes.Gray;
                StatusText.Text = isConnected ? "Verbunden" : "Nicht verbunden";

                if (!isConnected)
                {
                    ActiveChannelComboBox.IsEnabled = false;
                    _messages.Clear();
                    _allMessages.Clear();
                    _nodes.Clear();
                    _channels.Clear();
                }
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error updating connection state in UI: {ex.Message}");
            }
        });
    }

    private void OnMessageReceived(object? sender, MessageItem message)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Prüfe ob es eine Direktnachricht ist (nicht Broadcast)
                bool isDirectMessage = message.ToId != 0xFFFFFFFF && message.ToId != 0;

                if (isDirectMessage)
                {
                    // Leite an DM-Fenster weiter
                    if (_dmWindow == null)
                    {
                        _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
                    }
                    _dmWindow.AddOrUpdateMessage(message);

                    // Optional: Zeige DM-Fenster automatisch bei neuer Nachricht
                    if (!_dmWindow.IsVisible)
                    {
                        // Blinke den Button oder zeige Notification
                        OpenDmWindowButton.FontWeight = FontWeights.Bold;
                    }
                    return; // Nicht in Hauptnachrichten anzeigen
                }

                // Filter verschlüsselte Nachrichten wenn Checkbox deaktiviert
                if (message.IsEncrypted && !_showEncryptedMessages)
                {
                    return; // Nicht anzeigen
                }

                // Setze ChannelName basierend auf Channel Index
                if (uint.TryParse(message.Channel, out uint channelIndex))
                {
                    var channel = _channels.FirstOrDefault(c => c.Index == channelIndex);
                    message.ChannelName = channel?.Name ?? $"Kanal {channelIndex}";
                }
                else
                {
                    message.ChannelName = message.Channel;
                }

                // Speichere in ungefilterte Liste
                _allMessages.Add(message);

                // Log die Kanal-Nachricht
                if (uint.TryParse(message.Channel, out uint logChannelIndex))
                {
                    Services.MessageLogger.LogChannelMessage((int)logChannelIndex, message.ChannelName, message.From, message.Message);
                }

                // Prüfe ob Nachricht den aktuellen Filter passiert
                bool passesFilter = true;
                if (_messageChannelFilter != null && _messageChannelFilter.Index != 999)
                {
                    if (uint.TryParse(message.Channel, out uint msgChannelIndex))
                    {
                        passesFilter = (msgChannelIndex == _messageChannelFilter.Index);
                    }
                }

                // Füge zu sichtbarer Liste hinzu wenn Filter passt
                if (passesFilter)
                {
                    _messages.Add(message);
                    MessageListView.ScrollIntoView(message);
                }
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error adding message to UI: {ex.Message}");
            }
        });
    }

    private void OnNodeInfoReceived(object? sender, Models.NodeInfo node)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var existing = _nodes.FirstOrDefault(n => n.Id == node.Id);
                if (existing != null)
                {
                    _nodes.Remove(existing);
                }
                _nodes.Add(node);
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error updating node in UI: {ex.Message}");
            }
        });
    }

    private void OnChannelInfoReceived(object? sender, ChannelInfo channel)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var existing = _channels.FirstOrDefault(c => c.Index == channel.Index);
                if (existing != null)
                {
                    _channels.Remove(existing);
                }
                _channels.Add(channel);

                // Aktiviere Kanal-Auswahl wenn Kanäle vorhanden sind
                if (_channels.Count > 0 && !ActiveChannelComboBox.IsEnabled)
                {
                    ActiveChannelComboBox.IsEnabled = true;

                    // Wähle ersten PRIMARY Kanal oder ersten Kanal aus
                    var primaryChannel = _channels.FirstOrDefault(c => c.Role == "PRIMARY");
                    if (primaryChannel != null)
                    {
                        ActiveChannelComboBox.SelectedItem = primaryChannel;
                    }
                    else if (ActiveChannelComboBox.SelectedItem == null)
                    {
                        ActiveChannelComboBox.SelectedIndex = 0;
                    }
                }

                // Update Message Filter ComboBox
                UpdateMessageFilterComboBox();

                UpdateStatusBar($"Kanal {channel.Index} empfangen: {channel.Name}");
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"Error updating channel in UI: {ex.Message}");
            }
        });
    }

    private void OnDeviceInfoReceived(object? sender, Models.DeviceInfo deviceInfo)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Services.Logger.WriteLine($"OnDeviceInfoReceived: NodeId={deviceInfo.NodeIdHex}");

                // Speichere eigene Node-ID für DM-Fenster
                _myNodeId = deviceInfo.NodeId;

                // Suche die eigene NodeInfo in der Node-Liste
                var myNode = _nodes.FirstOrDefault(n => n.NodeId == deviceInfo.NodeId);
                if (myNode != null)
                {
                    DeviceNameTextBox.Text = myNode.Name;
                    Services.Logger.WriteLine($"  Set device name: {myNode.Name}");
                }
                else
                {
                    Services.Logger.WriteLine($"  WARNING: Own node not found in node list (have {_nodes.Count} nodes)");
                    // Warte kurz und probiere nochmal (Node könnte noch nicht da sein)
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            var node = _nodes.FirstOrDefault(n => n.NodeId == deviceInfo.NodeId);
                            if (node != null)
                            {
                                DeviceNameTextBox.Text = node.Name;
                                Services.Logger.WriteLine($"  Set device name (delayed): {node.Name}");
                            }
                        });
                    });
                }

                UpdateStatusBar($"Eigene Node-ID: {deviceInfo.NodeIdHex}");
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR updating DeviceInfo in UI: {ex.Message}");
            }
        });
    }

    private void OnLoRaConfigReceived(object? sender, LoRaConfig loraConfig)
    {
        // Async Update - blockiert nicht den UI Thread
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                Services.Logger.WriteLine($"OnLoRaConfigReceived: Region={loraConfig.Region}, Preset={loraConfig.ModemPreset}");

                // Region setzen (Case-insensitive, Unterstriche ignorieren)
                var regionName = loraConfig.Region.ToString();
                var regionNormalized = regionName.Replace("_", "").ToUpperInvariant();
                bool regionSet = false;

                for (int i = 0; i < RegionComboBox.Items.Count; i++)
                {
                    var item = RegionComboBox.Items[i].ToString();
                    var itemNormalized = item?.Replace("_", "").ToUpperInvariant();

                    if (itemNormalized == regionNormalized)
                    {
                        RegionComboBox.SelectedIndex = i;
                        regionSet = true;
                        Services.Logger.WriteLine($"  Set Region to index {i}: {item}");
                        break;
                    }
                }
                if (!regionSet)
                {
                    Services.Logger.WriteLine($"  WARNING: Region '{regionName}' not found in ComboBox");
                }

                // Modem Preset setzen (Case-insensitive, Unterstriche ignorieren)
                var presetName = loraConfig.ModemPreset.ToString();
                var presetNormalized = presetName.Replace("_", "").ToUpperInvariant();
                bool presetSet = false;

                for (int i = 0; i < ModemPresetComboBox.Items.Count; i++)
                {
                    var item = ModemPresetComboBox.Items[i].ToString();
                    var itemNormalized = item?.Replace("_", "").ToUpperInvariant();

                    if (itemNormalized == presetNormalized)
                    {
                        ModemPresetComboBox.SelectedIndex = i;
                        presetSet = true;
                        Services.Logger.WriteLine($"  Set Preset to index {i}: {item}");
                        break;
                    }
                }
                if (!presetSet)
                {
                    Services.Logger.WriteLine($"  WARNING: Preset '{presetName}' not found in ComboBox");
                }

                UpdateStatusBar($"LoRa Config: {regionName}, {presetName}");
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR updating LoRa config in UI: {ex.Message}");
            }
        });
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
                StatusText.Text = "Nicht verbunden";
                break;
            case ConnectionStatus.Connecting:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Yellow);
                StatusText.Text = "Verbinde...";
                break;
            case ConnectionStatus.Initializing:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                StatusText.Text = "Initialisiere...";
                break;
            case ConnectionStatus.Ready:
                StatusIndicator.Fill = new SolidColorBrush(Colors.LimeGreen);
                StatusText.Text = "Verbunden";
                break;
            case ConnectionStatus.Disconnecting:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Orange);
                StatusText.Text = "Trenne...";
                break;
            case ConnectionStatus.Error:
                StatusIndicator.Fill = new SolidColorBrush(Colors.Red);
                StatusText.Text = "Fehler";
                break;
        }
    }

    private void UpdateMessageFilterComboBox()
    {
        // Speichere aktuelle Auswahl
        var selectedFilter = MessageChannelFilterComboBox.SelectedItem as ChannelInfo;

        // Erstelle Liste mit "Alle" Option
        var filterItems = new List<ChannelInfo>();
        filterItems.Add(new ChannelInfo { Index = 999, Name = "Alle Kanäle", Role = "" });
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
            }

            // Zeige Fenster
            if (_dmWindow.IsVisible)
            {
                _dmWindow.Activate(); // Bringe in den Vordergrund
            }
            else
            {
                _dmWindow.Show();
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
            var selectedNode = NodesListView.SelectedItem as Models.NodeInfo;
            if (selectedNode == null)
            {
                MessageBox.Show("Bitte wählen Sie einen Knoten aus.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Prüfe ob eigener Knoten
            if (selectedNode.NodeId == _myNodeId)
            {
                MessageBox.Show("Sie können keine DM an sich selbst senden.", "Hinweis", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Öffne/Erstelle DM-Fenster
            if (_dmWindow == null)
            {
                _dmWindow = new DirectMessagesWindow(_protocolService, _myNodeId);
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
        // Disconnect asynchron im Hintergrund - nicht auf UI Thread blockieren
        if (_serialPortService.IsConnected)
        {
            Task.Run(() =>
            {
                try
                {
                    Services.Logger.WriteLine("Application closing...");
                    _protocolService.Disconnect();
                    System.Threading.Thread.Sleep(100);
                    _serialPortService.Disconnect();
                    Services.Logger.WriteLine("Disconnected");
                    Services.Logger.Close();
                }
                catch (Exception ex)
                {
                    Services.Logger.WriteLine($"Error during close: {ex.Message}");
                    Services.Logger.Close();
                }
            });

            // Gib dem Task kurz Zeit (aber nicht blockieren)
            System.Threading.Thread.Sleep(150);
        }
        else
        {
            Services.Logger.Close();
        }

        base.OnClosing(e);
    }
}
