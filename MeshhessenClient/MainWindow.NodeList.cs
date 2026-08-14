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

        // Apply sorting
        if (!string.IsNullOrEmpty(_nodeSortColumn))
        {
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
                "Distance" => _nodeSortAscending
                    ? filtered.OrderBy(n => HasValidDistance(n.Distance) ? 0 : 1)
                             .ThenBy(n => ParseDistanceForSorting(n.Distance))
                             .ThenBy(n => n.ShortName)
                    : filtered.OrderBy(n => HasValidDistance(n.Distance) ? 0 : 1)
                             .ThenByDescending(n => ParseDistanceForSorting(n.Distance))
                             .ThenBy(n => n.ShortName),
                "Snr" => _nodeSortAscending
                    ? filtered.OrderBy(n => HasValidNumeric(n.Snr) ? 0 : 1)
                             .ThenBy(n => ParseNumericForSorting(n.Snr))
                             .ThenBy(n => n.ShortName)
                    : filtered.OrderBy(n => HasValidNumeric(n.Snr) ? 0 : 1)
                             .ThenByDescending(n => ParseNumericForSorting(n.Snr))
                             .ThenBy(n => n.ShortName),
                "Rssi" => _nodeSortAscending
                    ? filtered.OrderBy(n => HasValidNumeric(n.Rssi) ? 0 : 1)
                             .ThenBy(n => ParseNumericForSorting(n.Rssi))
                             .ThenBy(n => n.ShortName)
                    : filtered.OrderBy(n => HasValidNumeric(n.Rssi) ? 0 : 1)
                             .ThenByDescending(n => ParseNumericForSorting(n.Rssi))
                             .ThenBy(n => n.ShortName),
                "Battery" => _nodeSortAscending
                    ? filtered.OrderBy(n => HasValidNumeric(n.Battery) ? 0 : 1)
                             .ThenBy(n => ParseNumericForSorting(n.Battery))
                             .ThenBy(n => n.ShortName)
                    : filtered.OrderBy(n => HasValidNumeric(n.Battery) ? 0 : 1)
                             .ThenByDescending(n => ParseNumericForSorting(n.Battery))
                             .ThenBy(n => n.ShortName),
                "LastSeen" => _nodeSortAscending
                    ? filtered.OrderBy(n => n.LastSeenDateTime ?? DateTime.MinValue)
                    : filtered.OrderByDescending(n => n.LastSeenDateTime ?? DateTime.MinValue),
                _ => filtered
            };
        }

        // Own node always first, then favorites, then pinned, then rest
        filtered = filtered.OrderBy(n =>
            n.NodeId == _myNodeId ? 0 : n.IsFavorite ? 1 : n.IsPinned ? 2 : 3);

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

    private bool HasValidDistance(string distance)
    {
        if (string.IsNullOrEmpty(distance) || distance == "-")
            return false;
        var cleaned = distance.Replace("km", "").Replace("m", "").Trim();
        // Use CurrentCulture to handle comma decimal separator
        return double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out _);
    }

    private bool HasValidNumeric(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "-")
            return false;
        var cleaned = value.Replace("%", "").Replace("dB", "").Trim();
        // Use CurrentCulture to handle comma decimal separator
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var result))
        {
            // 0.0 is considered "no value" for RSSI/SNR
            return Math.Abs(result) >= 0.01;
        }
        return false;
    }

    private double ParseDistanceForSorting(string distance)
    {
        if (string.IsNullOrEmpty(distance) || distance == "-")
            return 0;

        var cleaned = distance.Replace("km", "").Replace("m", "").Trim();
        // Use CurrentCulture to handle comma decimal separator (DE: "160,2")
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value))
        {
            // Convert meters to km if needed
            if (distance.EndsWith("m") && !distance.EndsWith("km"))
                return value / 1000.0;
            return value;
        }
        return 0;
    }

    private double ParseNumericForSorting(string value)
    {
        if (string.IsNullOrEmpty(value) || value == "-")
            return 0;

        var cleaned = value.Replace("%", "").Replace("dB", "").Trim();
        // Use CurrentCulture to handle comma decimal separator (DE: "-10,8")
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var result))
        {
            return result;
        }
        return 0;
    }

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
