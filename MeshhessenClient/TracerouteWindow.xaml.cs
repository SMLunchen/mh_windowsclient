using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeshhessenClient.Models;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class TracerouteWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;
    private readonly MeshtasticProtocolService _protocolService;
    private readonly NodeInfo _targetNode;
    private readonly uint _myNodeId;

    // Callback: called when "Plot on Map" is clicked, passes the current result
    public event EventHandler<TracerouteResult>? PlotOnMapRequested;
    public event EventHandler<uint>? ClearFromMapRequested; // passes DestinationNodeId

    private TracerouteResult? _lastResult;
    private bool _isRequesting = false;

    // Positions of all known nodes, supplied by MainWindow
    private Dictionary<uint, (double Lat, double Lon)> _nodePositions = new();
    private (double Lat, double Lon)? _myPosition;

    public TracerouteWindow(
        MeshtasticProtocolService protocolService,
        NodeInfo targetNode,
        uint myNodeId)
    {
        InitializeComponent();
        _protocolService = protocolService;
        _targetNode = targetNode;
        _myNodeId = myNodeId;

        TargetNameText.Text = targetNode.LongName;
        TargetIdText.Text = targetNode.Id;
        Title = $"Traceroute → {targetNode.LongName}";

        _protocolService.TracerouteReceived += OnTracerouteReceived;
    }

    public void SetKnownPositions(
        Dictionary<uint, (double Lat, double Lon)> nodePositions,
        (double Lat, double Lon)? myPosition)
    {
        _nodePositions = nodePositions;
        _myPosition = myPosition;
    }

    private void OnTracerouteReceived(object? sender, TracerouteResult result)
    {
        // Only handle results for our target node
        if (result.DestinationNodeId != _targetNode.NodeId) return;

        Dispatcher.BeginInvoke(() => ShowResult(result));
    }

    private void ShowResult(TracerouteResult result)
    {
        _lastResult = result;

        // Build hop list: [us] → relay1 → relay2 → ... → destination
        var hops = BuildHopList(result);

        StatusText.Text = $"{Loc("StrTrResultReceived")} {result.ReceivedAt:HH:mm:ss}";
        LastResultText.Text = $"{Loc("StrTrLastResult")} {result.ReceivedAt:HH:mm:ss}";
        MqttIndicator.Visibility = result.IsViaMqtt ? Visibility.Visible : Visibility.Collapsed;

        int knownCount = hops.Count(h => h.HasPosition);
        HopCountText.Text = string.Format(Loc("StrTrHopsPositions"), hops.Count, knownCount);

        RebuildHopRows(hops);

        PlotOnMapButton.IsEnabled = true;
        ClearMapButton.IsEnabled = false; // erst nach Plot aktivieren
        RequestButton.IsEnabled = true;
        _isRequesting = false;
    }

    private List<TracerouteHopItem> BuildHopList(TracerouteResult result)
    {
        var hops = new List<TracerouteHopItem>();

        // --- Start: our own node ---
        var myHop = new TracerouteHopItem
        {
            HopIndex = 0,
            NodeId = $"!{_myNodeId:x8}",
            NodeName = Loc("StrSentFrom") + " (Quelle)",
            IsSource = true,
        };
        if (_myPosition.HasValue)
        {
            myHop.HasPosition = true;
            myHop.Latitude = _myPosition.Value.Lat;
            myHop.Longitude = _myPosition.Value.Lon;
        }
        hops.Add(myHop);

        // --- Relay nodes in forward route ---
        // result.RouteForward contains intermediate nodes (not us, not dest)
        for (int i = 0; i < result.RouteForward.Count; i++)
        {
            uint nodeId = result.RouteForward[i];
            var hop = MakeHopItem(i + 1, nodeId, result.IsViaMqtt);

            // SNR towards: index i is SNR from previous node to this one
            if (i < result.SnrTowards.Count)
                hop.SnrDisplay = FormatSnr(result.SnrTowards[i]);

            // Distance from previous hop
            TracerouteHopItem prevHop = hops[^1];
            hop.Distance = CalcDistance(prevHop, hop);

            hops.Add(hop);
        }

        // --- Destination ---
        var destHop = MakeHopItem(hops.Count, _targetNode.NodeId, result.IsViaMqtt);
        destHop.IsDestination = true;
        destHop.NodeName = _targetNode.LongName;

        // Last SNR towards: after all relays, the final link is from last relay to dest
        if (result.SnrTowards.Count > result.RouteForward.Count)
            destHop.SnrDisplay = FormatSnr(result.SnrTowards[result.RouteForward.Count]);

        destHop.Distance = CalcDistance(hops[^1], destHop);
        hops.Add(destHop);

        return hops;
    }

    private TracerouteHopItem MakeHopItem(int index, uint nodeId, bool resultIsViaMqtt)
    {
        var hop = new TracerouteHopItem
        {
            HopIndex = index,
            NodeId = $"!{nodeId:x8}",
            NodeName = $"!{nodeId:x8}",
            IsViaMqtt = resultIsViaMqtt,
        };

        if (_nodePositions.TryGetValue(nodeId, out var pos))
        {
            hop.HasPosition = true;
            hop.Latitude = pos.Lat;
            hop.Longitude = pos.Lon;
        }

        // Try to get the name from the protocol service known nodes
        var knownNodes = _protocolService.GetKnownNodes();
        if (knownNodes.TryGetValue(nodeId, out var nodeInfo))
        {
            hop.NodeName = nodeInfo.LongName;
        }

        return hop;
    }

    private string CalcDistance(TracerouteHopItem from, TracerouteHopItem to)
    {
        if (!from.HasPosition || !to.HasPosition) return "?";
        double dist = HaversineKm(from.Latitude!.Value, from.Longitude!.Value,
                                   to.Latitude!.Value, to.Longitude!.Value);
        return dist < 1.0 ? $"{dist * 1000:F0} m" : $"{dist:F1} km";
    }

    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static string FormatSnr(int snrScaled) =>
        $"{snrScaled / 4.0:F1} dB";

    // ─── Build hop rows in the StackPanel ───────────────────────────────────
    private void RebuildHopRows(List<TracerouteHopItem> hops)
    {
        HopsPanel.Children.Clear();

        for (int i = 0; i < hops.Count; i++)
        {
            var hop = hops[i];
            bool isLast = i == hops.Count - 1;

            var rowBorder = new Border
            {
                Padding = new Thickness(8, 6, 8, 6),
                BorderThickness = new Thickness(0, 0, 0, isLast ? 0 : 1),
                BorderBrush = (Brush)FindResource("SystemControlForegroundBaseLowBrush"),
                Background = hop.IsSource || hop.IsDestination
                    ? new SolidColorBrush(Color.FromArgb(20, 100, 150, 255))
                    : Brushes.Transparent,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon column
            var iconText = new TextBlock
            {
                Text = hop.HopIcon,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(iconText, 0);
            grid.Children.Add(iconText);

            // Name + ID
            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            nameStack.Children.Add(new TextBlock
            {
                Text = hop.NodeName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = hop.NodeId,
                FontSize = 10,
                Opacity = 0.55,
            });
            Grid.SetColumn(nameStack, 1);
            grid.Children.Add(nameStack);

            // Distance column
            var distStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 0, 4, 0) };
            distStack.Children.Add(new TextBlock { Text = hop.Distance, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
            distStack.Children.Add(new TextBlock { Text = Loc("StrTrDistance"), FontSize = 9, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(distStack, 2);
            grid.Children.Add(distStack);

            // SNR column
            var snrStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(4, 0, 4, 0) };
            snrStack.Children.Add(new TextBlock { Text = hop.SnrDisplay, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right });
            snrStack.Children.Add(new TextBlock { Text = Loc("StrTrSnr"), FontSize = 9, Opacity = 0.5, HorizontalAlignment = HorizontalAlignment.Right });
            Grid.SetColumn(snrStack, 3);
            grid.Children.Add(snrStack);

            // Badges column
            var badgeStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            if (hop.IsViaMqtt)
            {
                badgeStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x00)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Child = new TextBlock { Text = "MQTT", FontSize = 9, Foreground = Brushes.White, FontWeight = FontWeights.Bold }
                });
            }
            if (!hop.HasPosition && !hop.IsSource)
            {
                badgeStack.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(0, hop.IsViaMqtt ? 2 : 0, 0, 0),
                    ToolTip = Loc("StrTrNoData"),
                    Child = new TextBlock { Text = "?", FontSize = 9, Foreground = Brushes.White, FontWeight = FontWeights.Bold }
                });
            }
            Grid.SetColumn(badgeStack, 4);
            grid.Children.Add(badgeStack);

            rowBorder.Child = grid;
            HopsPanel.Children.Add(rowBorder);

            // Connector arrow between hops (not after last)
            if (!isLast)
            {
                HopsPanel.Children.Add(new TextBlock
                {
                    Text = "  ↓",
                    FontSize = 11,
                    Opacity = 0.4,
                    Margin = new Thickness(8, 0, 0, 0),
                });
            }
        }
    }

    // ─── Button handlers ────────────────────────────────────────────────────

    private async void RequestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRequesting) return;
        _isRequesting = true;
        RequestButton.IsEnabled = false;
        StatusText.Text = Loc("StrTrNoRequest");
        HopCountText.Text = string.Empty;
        MqttIndicator.Visibility = Visibility.Collapsed;
        HopsPanel.Children.Clear();

        try
        {
            await _protocolService.SendTracerouteAsync(_targetNode.NodeId);
            StatusText.Text = Loc("StrConnecting");

            // Auto-reset after 30 s if no result arrives
            _ = Task.Delay(30_000).ContinueWith(_ => Dispatcher.BeginInvoke(() =>
            {
                if (!_isRequesting) return; // result already arrived
                _isRequesting = false;
                RequestButton.IsEnabled = true;
                StatusText.Text = Loc("StrTrNoData") + " – Timeout 30s";
            }));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{Loc("StrError")}: {ex.Message}";
            RequestButton.IsEnabled = true;
            _isRequesting = false;
        }
    }

    private void ClearMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult != null)
        {
            ClearFromMapRequested?.Invoke(this, _lastResult.DestinationNodeId);
            ClearMapButton.IsEnabled = false;
        }
    }

    private void PlotOnMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult != null)
        {
            PlotOnMapRequested?.Invoke(this, _lastResult);
            ClearMapButton.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _protocolService.TracerouteReceived -= OnTracerouteReceived;
        base.OnClosed(e);
    }
}
