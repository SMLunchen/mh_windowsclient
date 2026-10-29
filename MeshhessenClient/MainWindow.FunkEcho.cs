using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MeshhessenClient.Models;

namespace MeshhessenClient;

/// <summary>
/// Funk-Echo ("Wer funkt mich an?"): surfaces directed requests other stations send to
/// our node. Detection lives in the protocol service (RequestEchoClassifier); this part
/// persists bursts to telemetry.db, pulses the header radar, shows the transient flyout
/// and owns the journal window.
/// </summary>
public partial class MainWindow
{
    private RequestEchoWindow? _funkEchoWindow;
    private int _funkEchoUnread;
    private readonly DispatcherTimer _funkEchoFlyoutTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _funkEchoTimerWired;

    private static readonly Dictionary<RequestEchoType, Color> FunkEchoColors = new()
    {
        // Satellite-dot palette from img/HF_ICON.svg, normalized to the app accents
        [RequestEchoType.NodeInfo]   = Color.FromRgb(0x8B, 0x73, 0xD8), // purple
        [RequestEchoType.Position]   = Color.FromRgb(0xDB, 0x9A, 0x44), // amber
        [RequestEchoType.Telemetry]  = Color.FromRgb(0x20, 0xC9, 0x97), // green
        [RequestEchoType.Traceroute] = Color.FromRgb(0x42, 0x8E, 0xCC), // blue
        [RequestEchoType.Admin]      = Color.FromRgb(0xF0, 0x4C, 0x4C), // red
    };

    private void OnRequestEchoReceived(object? sender, RequestEchoEvent evt)
    {
        // Persist off the UI thread (burst aggregation happens in the DB layer)
        string? route = evt.RouteTowards is { Count: > 0 }
            ? string.Join(",", evt.RouteTowards.Select(id => $"!{id:x8}"))
            : null;
        string expected = ComputeFunkEchoExpectation(evt);
        Task.Run(() =>
        {
            try
            {
                _db.UpsertRequestEcho(evt.FromId, evt.Type.ToString(), evt.TimestampUtc,
                    evt.Hops, evt.ViaMqtt, route, evt.Detail, expected);
            }
            catch (Exception ex) { Services.Logger.WriteLine($"[FunkEcho] DB upsert failed: {ex.Message}"); }
        });

        Dispatcher.BeginInvoke(() =>
        {
            if (!_currentSettings.FunkEchoEnabled) return;

            _funkEchoUnread++;
            UpdateFunkEchoBadge();
            if (!_currentSettings.FunkEchoQuiet)
            {
                string msg = FunkEchoMessage(evt.Type, evt.FromId, evt.Detail);
                PulseFunkEcho(evt.Type);
                ShowFunkEchoFlyout(msg);
                if (_currentSettings.FunkEchoToasts)
                    Services.ToastService.ShowMessage(Loc("StrFunkEchoTitle"), msg);
            }
            _funkEchoWindow?.NotifyNewEvent(evt.FromId);
        });
    }

    /// <summary>Honest reply expectation from LOCAL state at request time: our own position
    /// availability and the request channel's position precision (0-7 only; a hashed/PKI
    /// channel byte yields "full precision" as the safe default).</summary>
    private string ComputeFunkEchoExpectation(RequestEchoEvent evt)
    {
        bool hasPos = false;
        uint precision = 32;
        try
        {
            hasPos = GetMyPosition() != null;
            if (evt.Channel <= 7)
            {
                var ch = _channels.FirstOrDefault(c => c.Index == (int)evt.Channel);
                if (ch != null) precision = ch.PositionPrecision;
            }
        }
        catch { /* conservative defaults */ }
        return RequestEchoExpectation.Compute(evt.Type, evt.Detail, hasPos, precision);
    }

    /// <summary>Casual or factual one-liner for the flyout, per the configured tone.
    /// Telemetry requests name the requested variant ("… fragt nach unseren Umweltdaten").</summary>
    private string FunkEchoMessage(RequestEchoType type, uint fromId, string? detail = null)
    {
        string who = FunkEchoNodeLabel(fromId);
        bool casual = _currentSettings.FunkEchoTone != "factual";
        string prefix = casual ? "StrFunkEchoCasual" : "StrFunkEchoFactual";
        if (type == RequestEchoType.Telemetry && detail != null)
            return string.Format(Loc(prefix + "TelemetryDetail"), who, Loc("StrFunkEchoTelem" + detail));
        return string.Format(Loc(prefix + type), who);
    }

    private string FunkEchoNodeLabel(uint nodeId)
    {
        var known = _protocolService.GetKnownNodes();
        if (known.TryGetValue(nodeId, out var n))
        {
            if (!string.IsNullOrWhiteSpace(n.ShortName)) return n.ShortName;
            if (!string.IsNullOrWhiteSpace(n.Name) && n.Name != "Unknown") return n.Name;
        }
        return $"!{nodeId:x8}";
    }

    private void UpdateFunkEchoBadge()
    {
        if (FunkEchoBadge == null) return;
        if (_funkEchoUnread > 0)
        {
            FunkEchoBadgeText.Text = _funkEchoUnread > 99 ? "99+" : _funkEchoUnread.ToString();
            FunkEchoBadge.Visibility = Visibility.Visible;
        }
        else
        {
            FunkEchoBadge.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Expanding ring in the request type's color; traceroute pulses twice.</summary>
    private void PulseFunkEcho(RequestEchoType type)
    {
        if (FunkEchoPulse == null) return;
        FunkEchoPulse.Stroke = new SolidColorBrush(FunkEchoColors[type]);

        var repeat = type == RequestEchoType.Traceroute
            ? new RepeatBehavior(2) : new RepeatBehavior(1);
        var dur = TimeSpan.FromMilliseconds(850);

        var scale = new DoubleAnimation(0.6, 2.1, dur) { RepeatBehavior = repeat };
        var fade  = new DoubleAnimation(0.9, 0.0, dur) { RepeatBehavior = repeat };
        FunkEchoPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        FunkEchoPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        FunkEchoPulse.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void ShowFunkEchoFlyout(string text)
    {
        if (FunkEchoFlyout == null) return;
        FunkEchoFlyoutText.Text = text;
        FunkEchoFlyout.IsOpen = true;
        if (!_funkEchoTimerWired)
        {
            _funkEchoFlyoutTimer.Tick += (_, _) =>
            {
                _funkEchoFlyoutTimer.Stop();
                FunkEchoFlyout.IsOpen = false;
            };
            _funkEchoTimerWired = true;
        }
        _funkEchoFlyoutTimer.Stop();
        _funkEchoFlyoutTimer.Start();
    }

    private void FunkEchoIndicator_Click(object sender, RoutedEventArgs e)
    {
        _funkEchoUnread = 0;
        UpdateFunkEchoBadge();
        FunkEchoFlyout.IsOpen = false;

        if (_funkEchoWindow == null)
        {
            _funkEchoWindow = new RequestEchoWindow(_db, FunkEchoNodeLabel,
                id => _protocolService.GetKnownNodes().TryGetValue(id, out var n) ? n.Name : $"!{id:x8}",
                () => _currentSettings.FunkEchoTone != "factual",
                openNodeInfo: id => WithKnownNode(id, ShowNodeInfoDialog),
                showOnMap:    id => WithKnownNode(id, n =>
                {
                    if (n.Latitude is double lat && n.Longitude is double lon)
                    {
                        CenterMapOnNode(lat, lon);
                        MainTabs.SelectedIndex = 3; // map tab — centering alone was invisible
                        Activate();
                    }
                }),
                openDm:       id => WithKnownNode(id, OpenDmToNode),
                modemPreset:  () => _currentLoRaConfig?.ModemPreset,
                showRouteOnMap: ShowRequestRouteOnMap) { Owner = this };
            _funkEchoWindow.Closed += (_, _) => _funkEchoWindow = null;
            _funkEchoWindow.Show();
        }
        else
        {
            if (!_funkEchoWindow.IsVisible) _funkEchoWindow.Show();
            _funkEchoWindow.Activate();
            _funkEchoWindow.Refresh();
        }
    }

    private void WithKnownNode(uint nodeId, Action<Models.NodeInfo> action)
    {
        if (_protocolService.GetKnownNodes().TryGetValue(nodeId, out var node))
            action(node);
    }

    /// <summary>Draw the inbound path of a traceroute REQUEST on the map: the stored route
    /// lists the relays the request travelled through towards us, so from our perspective
    /// the chain to the requester is simply that list reversed. Reuses the traceroute map
    /// layer (legend entry, removable via ✕); no SNR values — the request carries none for
    /// the reverse direction we'd be claiming.</summary>
    private void ShowRequestRouteOnMap(uint requesterId, List<uint> relaysTowardsUs)
    {
        if (!_protocolService.GetKnownNodes().TryGetValue(requesterId, out var requester)) return;

        var forward = new List<uint>(relaysTowardsUs);
        forward.Reverse();
        forward.Add(requesterId);

        var result = new TracerouteResult
        {
            RequestId         = 0,
            SourceNodeId      = _myNodeId,
            DestinationNodeId = requesterId,
            RouteForward      = forward,
        };
        PlotTracerouteOnMap(result, requester);
        MainTabs.SelectedIndex = 3; // map tab
        Activate();
    }

    private void FunkEchoQuiet_Toggled(object sender, RoutedEventArgs e)
    {
        bool quiet = (sender as System.Windows.Controls.MenuItem)?.IsChecked == true;
        _currentSettings = _currentSettings with { FunkEchoQuiet = quiet };
        try { Services.SettingsService.Save(_currentSettings); } catch { }
        if (quiet && FunkEchoFlyout != null) FunkEchoFlyout.IsOpen = false;
    }

    /// <summary>Show/hide the header element per the FunkEchoEnabled setting.</summary>
    private void ApplyFunkEchoVisibility()
    {
        var vis = _currentSettings.FunkEchoEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (FunkEchoPanel != null) FunkEchoPanel.Visibility = vis;
        if (FunkEchoSeparator != null) FunkEchoSeparator.Visibility = vis;
        if (!_currentSettings.FunkEchoEnabled && FunkEchoFlyout != null)
            FunkEchoFlyout.IsOpen = false;
    }
}
