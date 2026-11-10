using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeshhessenClient.Services.Firmware;

namespace MeshhessenClient;

/// <summary>
/// "Firmware vorab laden": download the artifacts of one release for any number of device types into
/// the <c>firmware/&lt;version&gt;/</c> cache while online, so nodes can be flashed later in the field
/// without internet (the wizard reuses cached files and marks such releases as available offline).
/// Per device the files the wizard could need are fetched: ESP32 → OTA .bin, nRF52 → -ota.zip + .uf2,
/// RP2040 → .uf2.
/// </summary>
public class FirmwarePrefetchWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private readonly FirmwareCatalogService _catalogSvc;
    private readonly FirmwareRetriever _retriever;
    private readonly DeviceHardware? _preselect;
    private FirmwareCatalog? _catalog;

    private readonly RadioButton _stableRadio = new() { GroupName = "PfChannel", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
    private readonly RadioButton _alphaRadio = new() { GroupName = "PfChannel" };
    private readonly ComboBox _releaseCombo = new() { Margin = new Thickness(0, 6, 0, 10) };
    private readonly TextBox _filterBox = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly ListBox _deviceList = new() { Height = 220, SelectionMode = SelectionMode.Single };
    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private readonly ProgressBar _progress = new() { Height = 8, Margin = new Thickness(0, 0, 0, 6), Visibility = Visibility.Collapsed };
    private readonly TextBox _log = new() { IsReadOnly = true, Height = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"), FontSize = 11 };
    private readonly Button _downloadBtn = new() { Padding = new Thickness(16, 6, 16, 6), IsEnabled = false };
    private readonly List<DeviceRow> _rows = new();
    private CancellationTokenSource? _cts;

    private sealed class DeviceRow
    {
        public required DeviceHardware Hw { get; init; }
        public required CheckBox Box { get; init; }
        public string Label => $"{Hw.DisplayName}  ({Hw.EffectiveTarget}, {Hw.Architecture})";
    }

    private static string Loc(string key) => Application.Current.TryFindResource(key) as string ?? key;
    private static string Loc(string key, params object?[] args)
    {
        try { return string.Format(Loc(key), args); } catch (FormatException) { return Loc(key); }
    }

    public FirmwarePrefetchWindow(FirmwareCatalog? catalog, DeviceHardware? preselect)
    {
        _catalogSvc = new FirmwareCatalogService(Http);
        _retriever = new FirmwareRetriever(Http, _catalogSvc.CacheDir);
        _catalog = catalog;
        _preselect = preselect;

        Title = Loc("StrFwPrefetchTitle");
        Width = 640; Height = 680;
        MinWidth = 520; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ModernWpf.Controls.Primitives.WindowHelper.SetUseModernWindowStyle(this, true);
        Closing += (_, _) => { _cts?.Cancel(); try { Owner?.Activate(); } catch { } };

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = Loc("StrFwPrefetchHint"), TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10) });
        _stableRadio.Content = Loc("StrFwChannelStable");
        _alphaRadio.Content = Loc("StrFwChannelAlpha");
        _stableRadio.Checked += (_, _) => FillReleases();
        _alphaRadio.Checked += (_, _) => FillReleases();
        var channels = new StackPanel { Orientation = Orientation.Horizontal };
        channels.Children.Add(_stableRadio);
        channels.Children.Add(_alphaRadio);
        top.Children.Add(channels);
        top.Children.Add(_releaseCombo);
        top.Children.Add(new TextBlock { Text = Loc("StrFwPrefetchDevices"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _filterBox.TextChanged += (_, _) => ApplyFilter();
        top.Children.Add(_filterBox);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        var middle = new Grid();
        middle.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        middle.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _deviceList.Height = double.NaN;
        Grid.SetRow(_deviceList, 0);
        middle.Children.Add(_deviceList);
        Grid.SetRow(_statusText, 1);
        middle.Children.Add(_statusText);
        Grid.SetRow(_progress, 2);
        middle.Children.Add(_progress);
        Grid.SetRow(_log, 3);
        middle.Children.Add(_log);
        Grid.SetRow(middle, 1);
        root.Children.Add(middle);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        _downloadBtn.Content = Loc("StrFwPrefetchDownload");
        _downloadBtn.Click += async (_, _) => await DownloadAsync();
        var folderBtn = new Button { Content = Loc("StrFwOpenCacheDir"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 10, 0) };
        folderBtn.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(_catalogSvc.CacheDir); Process.Start(new ProcessStartInfo("explorer.exe", _catalogSvc.CacheDir) { UseShellExecute = true }); } catch { }
        };
        var closeBtn = new Button { Content = Loc("StrFwCloseBtn"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(10, 0, 0, 0), IsCancel = true };
        closeBtn.Click += (_, _) => Close();
        buttons.Children.Add(folderBtn);
        buttons.Children.Add(_downloadBtn);
        buttons.Children.Add(closeBtn);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        if (_catalog == null || _catalog.IsEmpty)
        {
            _statusText.Text = Loc("StrFwCatalogLoading");
            _catalog = await _catalogSvc.LoadAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
        if (_catalog.IsEmpty)
        {
            _statusText.Text = Loc("StrFwPrefetchNoCatalog");
            _statusText.Foreground = Brushes.OrangeRed;
            return;
        }
        _statusText.Text = _catalog.FromCache
            ? Loc("StrFwCatalogCache", _catalog.CacheTimestampUtc?.ToLocalTime().ToString("g") ?? "?")
            : Loc("StrFwCatalogOnline");

        foreach (var hw in _catalog.Hardware.Where(h => h.IsEsp32 || h.IsNrf52 || h.IsRp2040).OrderBy(h => h.DisplayName))
        {
            var row = new DeviceRow { Hw = hw, Box = new CheckBox { Margin = new Thickness(0, 2, 0, 2) } };
            row.Box.Content = row.Label;
            row.Box.IsChecked = _preselect != null && hw.HwModel == _preselect.HwModel &&
                                string.Equals(hw.PlatformioTarget, _preselect.PlatformioTarget, StringComparison.OrdinalIgnoreCase);
            row.Box.Checked += (_, _) => UpdateButton();
            row.Box.Unchecked += (_, _) => UpdateButton();
            _rows.Add(row);
        }
        ApplyFilter();
        FillReleases();
        UpdateButton();
    }

    private void FillReleases()
    {
        if (_catalog == null) return;
        var list = (_alphaRadio.IsChecked == true ? _catalog.Alpha : _catalog.Stable).ToList();
        _releaseCombo.ItemsSource = list;
        _releaseCombo.DisplayMemberPath = nameof(FirmwareRelease.Title);
        _releaseCombo.SelectedIndex = list.Count > 0 ? 0 : -1;
        UpdateButton();
    }

    private void ApplyFilter()
    {
        var f = _filterBox.Text.Trim();
        _deviceList.Items.Clear();
        foreach (var row in _rows)
            if (f.Length == 0 || row.Label.Contains(f, StringComparison.OrdinalIgnoreCase) || row.Box.IsChecked == true)
                _deviceList.Items.Add(row.Box);
    }

    private void UpdateButton() =>
        _downloadBtn.IsEnabled = _cts == null && _releaseCombo.SelectedItem != null && _rows.Any(r => r.Box.IsChecked == true);

    private async Task DownloadAsync()
    {
        if (_releaseCombo.SelectedItem is not FirmwareRelease release) return;
        var selected = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Hw).ToList();
        if (selected.Count == 0) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        UpdateButton();
        _progress.Visibility = Visibility.Visible;
        _progress.Value = 0;
        _log.Clear();
        int ok = 0, missing = 0, step = 0;
        var jobs = new List<(DeviceHardware Hw, string Kind, Func<IProgress<double>, Task<FirmwareArtifact?>> Fetch)>();
        foreach (var hw in selected)
        {
            if (hw.IsEsp32) jobs.Add((hw, "OTA .bin", p => _retriever.GetEsp32OtaImageAsync(release, hw, p, ct)));
            if (hw.IsNrf52)
            {
                jobs.Add((hw, "-ota.zip", p => _retriever.GetNrf52DfuPackageAsync(release, hw, p, ct)));
                jobs.Add((hw, ".uf2", p => _retriever.GetUf2Async(release, hw, p, ct)));
            }
            if (hw.IsRp2040) jobs.Add((hw, ".uf2", p => _retriever.GetUf2Async(release, hw, p, ct)));
        }

        try
        {
            foreach (var job in jobs)
            {
                ct.ThrowIfCancellationRequested();
                _statusText.Text = Loc("StrFwPrefetchWorking", job.Hw.DisplayName, job.Kind, release.Version);
                var prog = new Progress<double>(p => _progress.Value = (step + p) / jobs.Count * 100);
                FirmwareArtifact? art = null;
                try { art = await job.Fetch(prog); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Log($"✗ {job.Hw.EffectiveTarget} {job.Kind}: {ex.Message}"); }
                if (art != null) { ok++; Log($"✓ {art.FileName} ({art.Size / 1024} kB)"); }
                else { missing++; Log($"✗ {job.Hw.EffectiveTarget} {job.Kind}: {Loc("StrFwPrefetchNotInRelease")}"); }
                step++;
                _progress.Value = (double)step / jobs.Count * 100;
            }
            _statusText.Text = Loc("StrFwPrefetchDone", ok, missing, _retriever.ReleaseDir(release));
            _statusText.Foreground = missing == 0 ? Brushes.Green : Brushes.DarkOrange;
        }
        catch (OperationCanceledException)
        {
            _statusText.Text = Loc("StrFwErrCancelled");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            UpdateButton();
        }
    }

    private void Log(string line)
    {
        _log.AppendText(line + "\n");
        _log.ScrollToEnd();
    }
}
