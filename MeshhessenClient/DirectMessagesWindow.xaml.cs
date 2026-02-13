using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class DirectMessagesWindow : Window
{
    private readonly MeshtasticProtocolService _protocolService;
    private readonly ObservableCollection<DirectMessageConversation> _conversations = new();
    private readonly Dictionary<uint, TabItem> _tabByNodeId = new();
    private uint _myNodeId;

    public DirectMessagesWindow(MeshtasticProtocolService protocolService, uint myNodeId)
    {
        InitializeComponent();
        _protocolService = protocolService;
        _myNodeId = myNodeId;
    }

    public void AddOrUpdateMessage(MessageItem message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Bestimme den Chat-Partner (nicht ich selbst)
                uint partnerId = message.FromId == _myNodeId ? message.ToId : message.FromId;

                // Finde oder erstelle Conversation
                var conversation = _conversations.FirstOrDefault(c => c.NodeId == partnerId);
                if (conversation == null)
                {
                    conversation = new DirectMessageConversation
                    {
                        NodeId = partnerId,
                        NodeName = message.FromId == _myNodeId ? $"→ {message.From}" : message.From
                    };
                    _conversations.Add(conversation);
                    CreateTabForConversation(conversation);
                }

                // Füge Nachricht hinzu
                conversation.Messages.Add(message);

                // Log die Nachricht
                Services.MessageLogger.LogDirectMessage(partnerId, conversation.NodeName, message.From, message.Message, message.IsViaMqtt);

                // Markiere als ungelesen wenn Tab nicht aktiv
                var tab = _tabByNodeId[partnerId];
                if (DmTabControl.SelectedItem != tab)
                {
                    conversation.HasUnread = true;
                    UpdateTabHeader(tab, conversation);
                }

                // Zeige Fenster bei neuer Nachricht (nur bei eingehenden, nicht bei eigenen)
                if (message.FromId != _myNodeId)
                {
                    MakeWindowProminent();

                    // Show alert animation if message has alert bell
                    if (message.HasAlertBell)
                    {
                        ShowAlertBellAnimation();
                    }
                }

                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR in DM window: {ex.Message}");
            }
        });
    }

    private void CreateTabForConversation(DirectMessageConversation conversation)
    {
        var tab = new TabItem();
        _tabByNodeId[conversation.NodeId] = tab;

        // Tab Header
        UpdateTabHeader(tab, conversation);

        // Tab Content: ListView + TextBox
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Messages ListView
        var listView = new ListView
        {
            Margin = new Thickness(10),
            ItemsSource = conversation.Messages
        };

        var gridView = new GridView();

        // Alert Bell Column
        var alertBellColumn = new GridViewColumn { Header = "🔔", Width = 50 };
        var alertBellTemplate = new DataTemplate();
        var alertBellFactory = new FrameworkElementFactory(typeof(TextBlock));
        alertBellFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("HasAlertBell")
        {
            Converter = (System.Windows.Data.IValueConverter)this.FindResource("AlertBellIconConverter")
        });
        alertBellFactory.SetValue(TextBlock.FontSizeProperty, 18.0);
        alertBellFactory.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Red);
        alertBellFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        alertBellFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        alertBellFactory.SetValue(TextBlock.ToolTipProperty, "Diese Nachricht enthält ein Alert Bell");
        alertBellTemplate.VisualTree = alertBellFactory;
        alertBellColumn.CellTemplate = alertBellTemplate;
        gridView.Columns.Add(alertBellColumn);

        gridView.Columns.Add(new GridViewColumn { Header = "Zeit", Width = 100, DisplayMemberBinding = new System.Windows.Data.Binding("Time") });
        gridView.Columns.Add(new GridViewColumn { Header = "Von", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("From") });
        gridView.Columns.Add(new GridViewColumn { Header = "Nachricht", Width = 350, DisplayMemberBinding = new System.Windows.Data.Binding("Message") });
        listView.View = gridView;

        Grid.SetRow(listView, 0);
        grid.Children.Add(listView);

        // Send Message Area
        var sendGrid = new Grid { Margin = new Thickness(10) };
        sendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new TextBox
        {
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10),
            Text = "" // Placeholder würde hier gesetzt werden wenn verfügbar
        };
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SendDirectMessage(conversation.NodeId, textBox.Text);
                textBox.Clear();
            }
        };

        Grid.SetColumn(textBox, 0);
        sendGrid.Children.Add(textBox);

        var alertBellButton = new Button
        {
            Content = "🚨 SOS",
            Width = 80,
            Margin = new Thickness(10, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(204, 0, 0)), // Red
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            ToolTip = "Notruf senden (Alert Bell)"
        };
        alertBellButton.Click += async (s, e) =>
        {
            var result = MessageBox.Show(
                "Möchten Sie wirklich einen NOTRUF (Alert Bell) senden?\n\nDies wird als wichtige Benachrichtigung gesendet!",
                "Notruf bestätigen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            // Send Alert Bell - use EMOJI 🔔 (as used by other clients)
            string alertMessage;
            var additionalText = textBox.Text.Trim();

            if (!string.IsNullOrEmpty(additionalText))
            {
                // Bell emoji + user text
                alertMessage = "🔔 " + additionalText;
            }
            else
            {
                // Bell emoji + standard text (compatible with other Meshtastic clients)
                alertMessage = "🔔 Alert Bell Character!";
            }

            // Debug log with hex dump (always log for DM alerts as they're critical)
            var bytes = System.Text.Encoding.UTF8.GetBytes(alertMessage);
            var hexDump = string.Join(" ", bytes.Select(b => $"{b:X2}"));
            Services.Logger.WriteLine($"[MSG DEBUG] Sending DM Alert Bell {bytes.Length} bytes to !{conversation.NodeId:X8}: {hexDump}");

            SendDirectMessage(conversation.NodeId, alertMessage);
            textBox.Clear();
        };

        Grid.SetColumn(alertBellButton, 1);
        sendGrid.Children.Add(alertBellButton);

        var sendButton = new Button
        {
            Content = "Senden",
            Width = 100,
            Margin = new Thickness(10, 0, 0, 0)
        };
        sendButton.Click += (s, e) =>
        {
            SendDirectMessage(conversation.NodeId, textBox.Text);
            textBox.Clear();
        };

        Grid.SetColumn(sendButton, 2);
        sendGrid.Children.Add(sendButton);

        Grid.SetRow(sendGrid, 1);
        grid.Children.Add(sendGrid);

        tab.Content = grid;

        // Event: Markiere als gelesen wenn Tab gewählt wird
        tab.GotFocus += (s, e) =>
        {
            conversation.HasUnread = false;
            UpdateTabHeader(tab, conversation);
        };

        DmTabControl.Items.Add(tab);
        DmTabControl.SelectedItem = tab; // Aktiviere neuen Tab
    }

    private void UpdateTabHeader(TabItem tab, DirectMessageConversation conversation)
    {
        var headerText = new TextBlock
        {
            Text = conversation.NodeName,
            FontWeight = conversation.HasUnread ? FontWeights.Bold : FontWeights.Normal,
            Foreground = conversation.HasUnread ? new SolidColorBrush(Colors.Orange) : SystemColors.ControlTextBrush
        };
        tab.Header = headerText;
    }

    private async void SendDirectMessage(uint toNodeId, string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        try
        {
            await _protocolService.SendTextMessageAsync(messageText, toNodeId, 0);
            Services.Logger.WriteLine($"DM sent to {toNodeId:X8}: {messageText}");

            // Zeige gesendete Nachricht im Chat
            var conversation = _conversations.FirstOrDefault(c => c.NodeId == toNodeId);
            if (conversation != null)
            {
                var sentMessage = new MessageItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    From = "Ich",
                    Message = messageText,
                    FromId = _myNodeId,
                    ToId = toNodeId
                };
                conversation.Messages.Add(sentMessage);

                // Log die Nachricht
                Services.MessageLogger.LogDirectMessage(toNodeId, conversation.NodeName, "Ich", messageText, false);
            }
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR sending DM: {ex.Message}");
            MessageBox.Show($"Fehler beim Senden: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateStatusBar()
    {
        StatusText.Text = $"{_conversations.Count} aktive Chat(s)";
    }

    private void MakeWindowProminent()
    {
        try
        {
            // Zeige Fenster falls versteckt
            if (!IsVisible)
            {
                Show();
            }

            // Bringe Fenster in den Vordergrund
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            // Aktiviere Fenster (bringt es in den Vordergrund)
            Activate();

            // Falls Fenster nicht aktiviert werden konnte, lasse Taskbar blinken
            if (!IsActive)
            {
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(this);
                FlashWindow(helper.Handle, true);
            }

            // Spiele System-Benachrichtigungssound
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"ERROR making DM window prominent: {ex.Message}");
        }
    }

    public void OpenChatWithNode(uint nodeId, string nodeName)
    {
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                // Finde oder erstelle Conversation
                var conversation = _conversations.FirstOrDefault(c => c.NodeId == nodeId);
                if (conversation == null)
                {
                    conversation = new DirectMessageConversation
                    {
                        NodeId = nodeId,
                        NodeName = nodeName
                    };
                    _conversations.Add(conversation);
                    CreateTabForConversation(conversation);
                }

                // Aktiviere den Tab für diese Conversation
                if (_tabByNodeId.ContainsKey(nodeId))
                {
                    DmTabControl.SelectedItem = _tabByNodeId[nodeId];
                }

                // Zeige Fenster
                MakeWindowProminent();

                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                Services.Logger.WriteLine($"ERROR opening chat with node: {ex.Message}");
            }
        });
    }

    // Windows API für Taskbar-Blinken
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool bInvert);

    private void ShowAlertBellAnimation()
    {
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                // Start blink animation
                var storyboard = new System.Windows.Media.Animation.Storyboard();

                // Create animation for opacity (blink effect)
                var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(300),
                    AutoReverse = true,
                    RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(6) // 6 blinks (3 seconds)
                };

                System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnimation, AlertBellOverlay);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(Border.OpacityProperty));

                storyboard.Children.Add(opacityAnimation);

                // Show overlay and start animation
                AlertBellOverlay.Visibility = Visibility.Visible;

                storyboard.Completed += (s, e) =>
                {
                    AlertBellOverlay.Visibility = Visibility.Collapsed;
                };

                storyboard.Begin();

                // Flash window in taskbar
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                FlashWindow(hwnd, true);
            });
        }
        catch (Exception ex)
        {
            Services.Logger.WriteLine($"Error showing alert bell animation in DM window: {ex.Message}");
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Verstecke Fenster statt es zu schließen
        e.Cancel = true;
        Hide();
    }
}
