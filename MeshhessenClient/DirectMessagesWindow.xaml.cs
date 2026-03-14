using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MeshhessenClient.Helpers;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class DirectMessagesWindow : Window
{
    private MeshtasticProtocolService _protocolService;
    private readonly ObservableCollection<DirectMessageConversation> _conversations = new();
    private readonly Dictionary<uint, TabItem> _tabByNodeId = new();
    private readonly Dictionary<uint, MessageItem> _dmMessageById = new();
    private uint _myNodeId;

    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    public DirectMessagesWindow(MeshtasticProtocolService protocolService, uint myNodeId)
    {
        InitializeComponent();
        _protocolService = protocolService;
        _myNodeId = myNodeId;
    }

    public void UpdateProtocolService(MeshtasticProtocolService protocolService)
    {
        _protocolService = protocolService;
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
                        NodeName = message.FromId == _myNodeId ? $"→ {message.From}" : message.From,
                        ColorHex = message.SenderColorHex
                    };
                    _conversations.Add(conversation);
                    CreateTabForConversation(conversation);
                }

                // Populate reply preview from original DM message
                if (message.ReplyId != 0 && _dmMessageById.TryGetValue(message.ReplyId, out var origDm))
                {
                    message.ReplyFromName = origDm.From;
                    message.ReplyPreview = origDm.Message?.Length > 60 ? origDm.Message[..60] + "…" : origDm.Message ?? string.Empty;
                }

                // Store in ID lookup
                if (message.Id != 0)
                    _dmMessageById[message.Id] = message;

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
        alertBellFactory.SetValue(TextBlock.ToolTipProperty, Loc("StrAlertBellTooltip"));
        alertBellTemplate.VisualTree = alertBellFactory;
        alertBellColumn.CellTemplate = alertBellTemplate;
        gridView.Columns.Add(alertBellColumn);

        gridView.Columns.Add(new GridViewColumn { Header = Loc("StrColTime"), Width = 100, DisplayMemberBinding = new System.Windows.Data.Binding("Time") });
        gridView.Columns.Add(new GridViewColumn { Header = Loc("StrColFrom"), Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding("From") });

        // Message column with selectable text, clickable links, and reply quote block
        var msgDocConverter = new MeshhessenClient.Converters.MessageDocumentConverter();
        var boolToVis = new System.Windows.Controls.BooleanToVisibilityConverter();

        var msgCol = new GridViewColumn { Header = Loc("StrColMessage"), Width = 300 };
        var msgTemplate = new DataTemplate();

        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        var rowDef0Factory = new FrameworkElementFactory(typeof(RowDefinition));
        rowDef0Factory.SetValue(RowDefinition.HeightProperty, GridLength.Auto);
        var rowDef1Factory = new FrameworkElementFactory(typeof(RowDefinition));
        rowDef1Factory.SetValue(RowDefinition.HeightProperty, GridLength.Auto);
        gridFactory.AppendChild(rowDef0Factory);
        gridFactory.AppendChild(rowDef1Factory);

        // Reply quote block (Row 0)
        var replyBorderFactory = new FrameworkElementFactory(typeof(Border));
        replyBorderFactory.SetValue(Grid.RowProperty, 0);
        replyBorderFactory.SetBinding(Border.VisibilityProperty, new System.Windows.Data.Binding("HasReply") { Converter = boolToVis });
        replyBorderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x78, 0xD4)));
        replyBorderFactory.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)));
        replyBorderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(3, 0, 0, 0));
        replyBorderFactory.SetValue(Border.PaddingProperty, new Thickness(6, 2, 4, 2));
        replyBorderFactory.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 3));

        var replyTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        replyTextFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("ReplyQuoteText"));
        replyTextFactory.SetResourceReference(TextBlock.ForegroundProperty, "SystemControlForegroundBaseMediumHighBrush");
        replyTextFactory.SetValue(TextBlock.FontStyleProperty, FontStyles.Italic);
        replyTextFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        replyTextFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        replyBorderFactory.AppendChild(replyTextFactory);
        gridFactory.AppendChild(replyBorderFactory);

        // Main message RichTextBox (Row 1)
        var rtbFactory = new FrameworkElementFactory(typeof(RichTextBox));
        rtbFactory.SetValue(Grid.RowProperty, 1);
        rtbFactory.SetBinding(RichTextBoxHelper.BoundDocumentProperty, new System.Windows.Data.Binding("Message") { Converter = msgDocConverter });
        rtbFactory.SetValue(RichTextBox.IsReadOnlyProperty, true);
        rtbFactory.SetValue(RichTextBox.BorderThicknessProperty, new Thickness(0));
        rtbFactory.SetValue(RichTextBox.BackgroundProperty, Brushes.Transparent);
        rtbFactory.SetValue(RichTextBox.PaddingProperty, new Thickness(0, 1, 0, 1));
        rtbFactory.SetValue(RichTextBox.IsTabStopProperty, false);
        rtbFactory.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        rtbFactory.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        rtbFactory.SetValue(RichTextBox.IsDocumentEnabledProperty, true);
        rtbFactory.SetValue(ContextMenuService.IsEnabledProperty, false);
        gridFactory.AppendChild(rtbFactory);

        msgTemplate.VisualTree = gridFactory;
        msgCol.CellTemplate = msgTemplate;
        gridView.Columns.Add(msgCol);

        // Reactions column — use Emoji.Wpf.TextBlock for proper color emoji rendering
        var reactCol = new GridViewColumn { Header = "💬", Width = 90 };
        var reactFactory = new FrameworkElementFactory(typeof(Emoji.Wpf.TextBlock));
        reactFactory.SetBinding(Emoji.Wpf.TextBlock.TextProperty, new System.Windows.Data.Binding("ReactionsDisplay"));
        reactFactory.SetValue(Emoji.Wpf.TextBlock.FontSizeProperty, 14.0);
        var reactTemplate = new DataTemplate { VisualTree = reactFactory };
        reactCol.CellTemplate = reactTemplate;
        gridView.Columns.Add(reactCol);

        listView.View = gridView;

        // Per-conversation reply state
        MessageItem? dmReplyTarget = null;

        // Right-click context menu for reactions and replies on DM messages
        var cmenu = new ContextMenu();

        var copyItem = new MenuItem { Header = Loc("StrCopyMessage") };
        copyItem.Click += (s, e) =>
        {
            if (listView.SelectedItem is MeshhessenClient.Models.MessageItem msg)
                System.Windows.Clipboard.SetText(msg.Message ?? string.Empty);
        };
        cmenu.Items.Add(copyItem);
        cmenu.Items.Add(new Separator());

        var reactItem = new MenuItem { Header = Loc("StrReact") };
        reactItem.Click += (s, e) =>
        {
            if (listView.SelectedItem is MeshhessenClient.Models.MessageItem msg)
                ShowDmEmojiPicker(msg, conversation.NodeId);
        };
        cmenu.Items.Add(reactItem);

        var replyItem = new MenuItem { Header = Loc("StrReplyMessage") };
        cmenu.Items.Add(replyItem);
        listView.ContextMenu = cmenu;

        // Update selection to right-clicked item before menu opens
        listView.ContextMenuOpening += (s, e) =>
        {
            if (e.OriginalSource is DependencyObject d)
            {
                var container = ItemsControl.ContainerFromElement(listView, d) as ListViewItem;
                if (container?.Content is MeshhessenClient.Models.MessageItem clickedMsg)
                    listView.SelectedItem = clickedMsg;
            }
        };

        Grid.SetRow(listView, 0);
        grid.Children.Add(listView);

        // Send Message Area
        var sendPanel = new StackPanel();

        // Reply indicator bar
        var replyIndicatorBorder = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(0x15, 0x00, 0x78, 0xD4)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)),
            BorderThickness = new Thickness(0, 2, 0, 0),
            Padding = new Thickness(10, 4, 10, 4)
        };
        var replyIndicatorGrid = new Grid();
        replyIndicatorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        replyIndicatorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var replyIndicatorText = new TextBlock
        {
            FontStyle = FontStyles.Italic,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(replyIndicatorText, 0);
        var cancelReplyBtn = new Button
        {
            Content = Loc("StrCancelReply"),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            FontSize = 11
        };
        Grid.SetColumn(cancelReplyBtn, 1);
        replyIndicatorGrid.Children.Add(replyIndicatorText);
        replyIndicatorGrid.Children.Add(cancelReplyBtn);
        replyIndicatorBorder.Child = replyIndicatorGrid;
        sendPanel.Children.Add(replyIndicatorBorder);

        cancelReplyBtn.Click += (s, e) =>
        {
            dmReplyTarget = null;
            replyIndicatorBorder.Visibility = Visibility.Collapsed;
        };

        replyItem.Click += (s, e) =>
        {
            if (listView.SelectedItem is MeshhessenClient.Models.MessageItem msg)
            {
                dmReplyTarget = msg;
                var preview = msg.Message?.Length > 60 ? msg.Message[..60] + "…" : msg.Message ?? string.Empty;
                replyIndicatorText.Text = string.Format(Loc("StrReplyingTo"), msg.From, preview);
                replyIndicatorBorder.Visibility = Visibility.Visible;
            }
        };

        var sendGrid = new Grid { Margin = new Thickness(10) };
        sendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sendGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textBox = new TextBox
        {
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(10),
            MaxLength = 200,
            Text = ""
        };
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                var reply = dmReplyTarget;
                dmReplyTarget = null;
                replyIndicatorBorder.Visibility = Visibility.Collapsed;
                SendDirectMessage(conversation.NodeId, textBox.Text, reply?.Id ?? 0, reply);
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
            ToolTip = Loc("StrSosTooltip")
        };
        alertBellButton.Click += async (s, e) =>
        {
            var result = MessageBox.Show(
                string.Format(Loc("StrAlertConfirmTextDm"), "\n"),
                Loc("StrAlertConfirmTitle"),
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

            dmReplyTarget = null;
            replyIndicatorBorder.Visibility = Visibility.Collapsed;
            SendDirectMessage(conversation.NodeId, alertMessage);
            textBox.Clear();
        };

        Grid.SetColumn(alertBellButton, 1);
        sendGrid.Children.Add(alertBellButton);

        var sendButton = new Button
        {
            Content = Loc("StrSend"),
            Width = 100,
            Margin = new Thickness(10, 0, 0, 0)
        };
        sendButton.Click += (s, e) =>
        {
            var reply = dmReplyTarget;
            dmReplyTarget = null;
            replyIndicatorBorder.Visibility = Visibility.Collapsed;
            SendDirectMessage(conversation.NodeId, textBox.Text, reply?.Id ?? 0, reply);
            textBox.Clear();
        };

        Grid.SetColumn(sendButton, 2);
        sendGrid.Children.Add(sendButton);

        sendPanel.Children.Add(sendGrid);
        Grid.SetRow(sendPanel, 1);
        grid.Children.Add(sendPanel);

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
        // Reuse existing header to avoid replacing elements during click events
        if (tab.Header is StackPanel existingPanel)
        {
            var existingText = existingPanel.Children.OfType<TextBlock>().FirstOrDefault();
            if (existingText != null)
            {
                existingText.Text = conversation.NodeName;
                existingText.FontWeight = conversation.HasUnread ? FontWeights.Bold : FontWeights.Normal;
                existingText.Foreground = conversation.HasUnread
                    ? new SolidColorBrush(Colors.Orange)
                    : (Brush)FindResource("SystemControlForegroundBaseHighBrush");
                return;
            }
        }

        // First-time creation
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // Color indicator
        if (!string.IsNullOrEmpty(conversation.ColorHex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(conversation.ColorHex);
                var colorBox = new Border
                {
                    Width = 12,
                    Height = 12,
                    Background = new SolidColorBrush(color),
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0)
                };
                headerPanel.Children.Add(colorBox);
            }
            catch { /* ignore invalid color */ }
        }

        var headerText = new TextBlock
        {
            Text = conversation.NodeName,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = conversation.HasUnread ? FontWeights.Bold : FontWeights.Normal,
            Foreground = conversation.HasUnread
                ? new SolidColorBrush(Colors.Orange)
                : (Brush)FindResource("SystemControlForegroundBaseHighBrush")
        };
        headerPanel.Children.Add(headerText);

        var closeButton = new Button
        {
            Content = "\u2715",
            FontSize = 10,
            Padding = new Thickness(2),
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            ToolTip = Loc("StrCloseTab"),
            Tag = conversation.NodeId
        };
        closeButton.Click += CloseTab_Click;
        headerPanel.Children.Add(closeButton);

        tab.Header = headerPanel;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is uint nodeId)
        {
            if (_tabByNodeId.TryGetValue(nodeId, out var tab))
            {
                DmTabControl.Items.Remove(tab);
                _tabByNodeId.Remove(nodeId);

                var conversation = _conversations.FirstOrDefault(c => c.NodeId == nodeId);
                if (conversation != null)
                    _conversations.Remove(conversation);

                UpdateStatusBar();
            }
        }
    }

    private async void SendDirectMessage(uint toNodeId, string messageText, uint replyId = 0, MessageItem? replyTarget = null)
    {
        if (string.IsNullOrWhiteSpace(messageText))
            return;

        try
        {
            uint packetId = await _protocolService.SendTextMessageAsync(messageText, toNodeId, 0, replyId);
            Services.Logger.WriteLine($"DM sent to {toNodeId:X8}: {messageText}");

            // Zeige gesendete Nachricht im Chat
            var conversation = _conversations.FirstOrDefault(c => c.NodeId == toNodeId);
            if (conversation != null)
            {
                var sentMessage = new MessageItem
                {
                    Id = packetId,
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    From = Loc("StrSentFrom"),
                    Message = messageText,
                    FromId = _myNodeId,
                    ToId = toNodeId,
                    ReplyId = replyId,
                    ReplyFromName = replyTarget?.From ?? string.Empty,
                    ReplyPreview = replyTarget?.Message?.Length > 60 ? replyTarget.Message[..60] + "…" : replyTarget?.Message ?? string.Empty
                };
                if (packetId != 0) _dmMessageById[packetId] = sentMessage;
                conversation.Messages.Add(sentMessage);

                // Log die Nachricht
                Services.MessageLogger.LogDirectMessage(toNodeId, conversation.NodeName, Loc("StrSentFrom"), messageText, false);
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
        StatusText.Text = string.Format(Loc("StrActiveChats"), _conversations.Count);
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

    public void OpenChatWithNode(uint nodeId, string nodeName, string colorHex = "")
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
                        NodeName = nodeName,
                        ColorHex = colorHex
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

    private void ShowDmEmojiPicker(MeshhessenClient.Models.MessageItem message, uint partnerNodeId)
    {
        var quickEmojis = new[]
        {
            "👍", "👎", "❤️", "😂", "😢", "😮", "😡", "🎉",
            "❓", "❗", "‼️", "*️⃣", "1️⃣", "2️⃣", "3️⃣", "4️⃣",
            "5️⃣", "6️⃣", "7️⃣", "💩", "👋", "🤠", "🐭", "😈",
            "☀️", "☔", "☁️", "🌫️", "✅", "❌", "🔥", "💯",
        };

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            StaysOpen = false,
            AllowsTransparency = true,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        var border = new Border
        {
            Background = (Brush)FindResource("SystemControlBackgroundChromeMediumLowBrush"),
            BorderBrush = (Brush)FindResource("SystemControlForegroundBaseLowBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
        };

        var panel = new WrapPanel { MaxWidth = 380 }; // 8 columns × ~47px
        foreach (var emoji in quickEmojis)
        {
            var emojiBlock = new Emoji.Wpf.TextBlock
            {
                Text = emoji,
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var btn = new Button
            {
                Content = emojiBlock,
                Padding = new Thickness(4),
                Margin = new Thickness(2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                MinWidth = 40,
                MinHeight = 40,
            };
            btn.Click += async (_, _) =>
            {
                popup.IsOpen = false;
                try
                {
                    await _protocolService.SendReactionAsync(emoji, message.Id, partnerNodeId, 0);
                    message.AddReaction(emoji, _myNodeId);
                }
                catch (Exception ex)
                {
                    Services.Logger.WriteLine($"Error sending DM reaction: {ex.Message}");
                }
            };
            panel.Children.Add(btn);
        }

        border.Child = panel;
        popup.Child = border;
        popup.IsOpen = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Verstecke Fenster statt es zu schließen
        e.Cancel = true;
        Hide();
    }
}
