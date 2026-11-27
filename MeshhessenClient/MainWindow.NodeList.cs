// Node-Liste: Sortierung & Filter
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
    // ========== Node List Sorting and Filtering ==========

    private void NodeColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is GridViewColumnHeader header && header.Tag is string column)
        {
            // Toggle sort direction if same column, otherwise default to ascending
            if (_nodeSortColumn == column)
            {
                _nodeSortAscending = !_nodeSortAscending;
            }
            else
            {
                _nodeSortColumn = column;
                _nodeSortAscending = true;
            }

            ApplyNodeSortAndFilterCore();
        }
    }

    private void NodeContextMenu_NodeInfo_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is NodeInfo node)
            ShowNodeInfoDialog(node);
    }

    private void NodeContextMenu_ShowOnMap_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedNodeForMenu is not NodeInfo node || !node.Latitude.HasValue || !node.Longitude.HasValue)
        {
            MessageBox.Show("Position für diesen Node ist nicht bekannt.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MainTabs.SelectedIndex = 3;
        CenterMapOnNode(node.Latitude.Value, node.Longitude.Value);
    }

    private void NodeFilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyNodeSortAndFilterCore();
    }

    // Packet-driven path: throttled in tile mode (max 1 update per 700 ms)
    private bool IsTileViewActive => TileViewGrid?.Visibility == Visibility.Visible;

    // Packet-driven refresh: always coalesced via a timer so a burst of node/DM
    // traffic can't saturate the UI thread (rebuilding the bound node collection is
    // expensive with many nodes, even when the Nodes tab is not the active view).
    private void ApplyNodeSortAndFilter()
    {
        if (!_tileSortFilterPending)
        {
            _tileSortFilterPending = true;
            if (_tileSortFilterTimer == null)
            {
                _tileSortFilterTimer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(700) };
                _tileSortFilterTimer.Tick += (_, _) =>
                {
                    _tileSortFilterTimer.Stop();
                    _tileSortFilterPending = false;
                    ApplyNodeSortAndFilterCore();
                };
            }
            _tileSortFilterTimer.Start();
        }
    }

    private void ApplyNodeSortAndFilterCore()
    {
        var filterText = NodeFilterTextBox?.Text?.ToLowerInvariant() ?? string.Empty;

        // Start with all nodes
        var filtered = _allNodes.AsEnumerable();

        // Apply filter
        if (!string.IsNullOrWhiteSpace(filterText))
        {
            filtered = filtered.Where(n =>
                n.Name.ToLowerInvariant().Contains(filterText) ||
                n.ShortName.ToLowerInvariant().Contains(filterText) ||
                n.Id.ToLowerInvariant().Contains(filterText) ||
                n.Distance.ToLowerInvariant().Contains(filterText) ||
                n.SnrDisplay.ToLowerInvariant().Contains(filterText) ||
                n.Rssi.ToLowerInvariant().Contains(filterText) ||
                n.Battery.ToLowerInvariant().Contains(filterText) ||
                n.LastSeen.ToLowerInvariant().Contains(filterText)
            );
        }

        // Advanced filters
        if (_nodeFilterLastSeenMinutes > 0)
            filtered = filtered.Where(n =>
                n.LastSeenDateTime.HasValue &&
                (DateTime.Now - n.LastSeenDateTime.Value).TotalMinutes <= _nodeFilterLastSeenMinutes);

        if (_nodeFilterHideMqtt)
            filtered = filtered.Where(n => !n.IsViaMqtt);

        if (_nodeFilterOnlyFavorites)
            filtered = filtered.Where(n => n.IsFavorite);

        if (_nodeFilterOnlyDirect)
            filtered = filtered.Where(NodeSortKeys.IsDirectLink);

        // Apply sorting. SNR/RSSI/Battery/Distance sort by what the column shows (NodeSortKeys, issue #83):
        // rows with a value first (grouped like the display), then the value in the chosen direction, ties by
        // short name.
        if (!string.IsNullOrEmpty(_nodeSortColumn))
        {
            IEnumerable<NodeInfo> Numeric(Func<NodeInfo, int> group, Func<NodeInfo, double> key) =>
                _nodeSortAscending
                    ? filtered.OrderBy(group).ThenBy(key).ThenBy(n => n.ShortName)
                    : filtered.OrderBy(group).ThenByDescending(key).ThenBy(n => n.ShortName);

            filtered = _nodeSortColumn switch
            {
                "Name" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.Name)
                    : filtered.OrderByDescending(n => n.Name),
                "ShortName" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.ShortName)
                    : filtered.OrderByDescending(n => n.ShortName),
                "Id" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.Id)
                    : filtered.OrderByDescending(n => n.Id),
                "Distance" => Numeric(NodeSortKeys.DistanceGroup, NodeSortKeys.DistanceKey),
                "Snr"      => Numeric(NodeSortKeys.SnrGroup,      NodeSortKeys.SnrKey),
                "Rssi"     => Numeric(NodeSortKeys.RssiGroup,     NodeSortKeys.RssiKey),
                "Battery"  => Numeric(NodeSortKeys.BatteryGroup,  NodeSortKeys.BatteryKey),
                "LastSeen" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.LastSeenDateTime ?? DateTime.MinValue)
                    : filtered.OrderByDescending(n => n.LastSeenDateTime ?? DateTime.MinValue),
                _ => filtered
            };
        }

        // Own node always first and pinned nodes next (explicit "pin to top"). Favorites are grouped on top only
        // in the default order: with a column sort active they take part in it — with a dozen favorites the
        // grouping hid the sort completely ("SNR/RSSI sorting does not work", #83).
        filtered = string.IsNullOrEmpty(_nodeSortColumn)
            ? filtered.OrderBy(n => n.NodeId == _myNodeId ? 0 : n.IsFavorite ? 1 : n.IsPinned ? 2 : 3)
            : filtered.OrderBy(n => n.NodeId == _myNodeId ? 0 : n.IsPinned ? 1 : 2);

        if (IsTileViewActive && NodeTileView != null)
        {
            // Group into rows of _tileColumnCount for VirtualizingStackPanel
            // — only visible rows are rendered regardless of total node count
            var allFiltered = filtered.ToList();
            int cols = Math.Max(1, _tileColumnCount);
            var rows = allFiltered
                .Select((n, i) => (n, i))
                .GroupBy(x => x.i / cols)
                .Select(g => g.Select(x => x.n).ToList())
                .ToList();
            NodeTileView.ItemsSource = rows;

            if (TileNodeCountText != null)
                TileNodeCountText.Text = $"{allFiltered.Count} / {_allNodes.Count} Nodes";

            // Keep _nodes in sync for any fallback usage
            _nodes.Clear();
            foreach (var n in allFiltered) _nodes.Add(n);
        }
        else
        {
            _nodes.Clear();
            foreach (var node in filtered)
                _nodes.Add(node);
        }
    }

    // Numeric parsing for the sort lives in Models.NodeSortKeys (culture-tolerant, display-consistent, unit-tested).

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // Haversine formula
        const double R = 6371; // Earth radius in km

        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private string FormatDistance(double distanceKm)
    {
        if (distanceKm < 1.0)
            return $"{(int)(distanceKm * 1000)}m";
        else if (distanceKm < 10.0)
            return $"{distanceKm:F2}km";
        else
            return $"{distanceKm:F1}km";
    }

}
