using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MeshhessenClient.Services;

namespace MeshhessenClient;

public partial class TDeckWizardWindow : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    private static readonly string LocalTileDir =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "maptiles");

    // ── Wizard state ──
    private int _currentStep = 1;
    private const int TotalSteps = 6;

    // Step 2: drive
    private DriveInfo? _selectedDrive;

    // Step 3: format
    private bool _formatOk;

    // Step 4: region
    private double _north, _south, _east, _west;
    private string _regionName = "";
    private bool _regionSet;

    // Step 5: zoom + type
    private int _maxZoom = 14;
    private MapSource _mapSource = MapSource.OSM;

    // Step 6: download
    private CancellationTokenSource? _cts;

    // ── German states bounding boxes: north, south, east, west ──
    private static readonly (string Name, double N, double S, double E, double W)[] States =
    {
        ("Baden-Württemberg", 49.7913, 47.5324, 10.4923, 7.5114),
        ("Bayern",            50.5648, 47.2703, 13.8395, 8.9766),
        ("Berlin",            52.6754, 52.3383, 13.7612, 13.0883),
        ("Brandenburg",       53.5588, 51.3595, 14.7654, 11.2668),
        ("Bremen",            53.2282, 53.0059,  8.9910,  8.4817),
        ("Hamburg",           53.7444, 53.3951, 10.3257,  9.7312),
        ("Hessen",            51.6567, 49.3951, 10.2367,  7.7731),
        ("Mecklenburg-Vorpommern", 54.6850, 53.1148, 14.4127, 10.5930),
        ("Niedersachsen",     53.8941, 51.2955, 11.5978,  6.6541),
        ("Nordrhein-Westfalen", 52.5314, 50.3228, 9.4614,  5.8660),
        ("Rheinland-Pfalz",   50.9391, 48.9667,  8.5077,  6.1131),
        ("Saarland",          49.6399, 49.1119,  7.4036,  6.3626),
        ("Sachsen",           51.6853, 50.1713, 15.0376, 11.8726),
        ("Sachsen-Anhalt",    53.0410, 50.9380, 13.1877, 10.5631),
        ("Schleswig-Holstein",55.0583, 53.3595, 11.3102,  7.8700),
        ("Thüringen",         51.6488, 50.2004, 12.6534,  9.8774),
    };

    // Checkboxes for states
    private readonly List<CheckBox> _stateCheckBoxes = new();

    // Freestyle bbox
    private double _freeN, _freeS, _freeE, _freeW;
    private bool _freeStyleSet;

    public TDeckWizardWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BuildStateCheckboxes();
        RefreshDrives();
        GoToStep(1);
    }

    // ──────────────────────────────────────────────────────────
    //  STATE CHECKBOXES
    // ──────────────────────────────────────────────────────────
    private void BuildStateCheckboxes()
    {
        StatesWrapPanel.Children.Clear();
        _stateCheckBoxes.Clear();
        foreach (var s in States)
        {
            var cb = new CheckBox
            {
                Content = s.Name,
                Tag     = s,
                Margin  = new Thickness(4, 3, 12, 3),
                Width   = 190
            };
            cb.Checked   += StateCheckBox_Changed;
            cb.Unchecked += StateCheckBox_Changed;
            _stateCheckBoxes.Add(cb);
            StatesWrapPanel.Children.Add(cb);
        }
    }

    private void StateCheckBox_Changed(object sender, RoutedEventArgs e) => UpdateBboxFromStates();

    private void SelectAllStates_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _stateCheckBoxes) cb.IsChecked = true;
    }

    private void SelectNoneStates_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cb in _stateCheckBoxes) cb.IsChecked = false;
    }

    private void UpdateBboxFromStates()
    {
        var selected = _stateCheckBoxes
            .Where(cb => cb.IsChecked == true)
            .Select(cb => ((string Name, double N, double S, double E, double W))cb.Tag!)
            .ToList();

        if (selected.Count == 0)
        {
            _regionSet = false;
            BboxPreviewText.Text = "";
            return;
        }

        _north = selected.Max(s => s.N);
        _south = selected.Min(s => s.S);
        _east  = selected.Max(s => s.E);
        _west  = selected.Min(s => s.W);
        _regionName = selected.Count == 1 ? selected[0].Name : $"{selected.Count} Bundesländer";
        _regionSet = true;
        BboxPreviewText.Text = string.Format(Loc("StrTDeckBboxPreview"), _north, _south, _west, _east);
        UpdateEstimateIfOnPage5();
    }

    // ──────────────────────────────────────────────────────────
    //  DRIVE HANDLING
    // ──────────────────────────────────────────────────────────
    private void RefreshDrives()
    {
        DriveComboBox.Items.Clear();
        var drives = DriveService.GetRemovableDrives();
        if (drives.Count == 0)
        {
            DriveInfoText.Text = Loc("StrTDeckNoDrives");
            _selectedDrive = null;
            return;
        }

        foreach (var d in drives)
        {
            DriveComboBox.Items.Add(new ComboBoxItem
            {
                Content = $"{d.Name} — {d.VolumeLabel}",
                Tag = d
            });
        }
        DriveComboBox.SelectedIndex = 0;
    }

    private void RefreshDrives_Click(object sender, RoutedEventArgs e) => RefreshDrives();

    private void DriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DriveComboBox.SelectedItem is ComboBoxItem item && item.Tag is DriveInfo d)
        {
            _selectedDrive = d;
            DriveInfoText.Text = string.Format(Loc("StrTDeckDriveInfo"),
                DriveService.FormatSizeString(d.TotalSize),
                DriveService.FormatSizeString(d.AvailableFreeSpace),
                d.DriveFormat ?? "?");
        }
        else
        {
            _selectedDrive = null;
        }
    }

    // ──────────────────────────────────────────────────────────
    //  FORMAT
    // ──────────────────────────────────────────────────────────
    private void UpdateFormatPage()
    {
        if (_selectedDrive == null) return;
        var status = DriveService.CheckFormat(_selectedDrive);

        if (status.IsRecommended)
        {
            _formatOk = true;
            FormatStatusBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 230, 200));
            FormatStatusText.Text = string.Format(Loc("StrTDeckFormatOk"), status.FileSystem);
            FormatStatusText.Foreground = Brushes.DarkGreen;
        }
        else
        {
            _formatOk = false;
            FormatStatusBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 240, 200));
            FormatStatusText.Text = string.Format(Loc("StrTDeckFormatWarn"), status.FileSystem);
            FormatStatusText.Foreground = Brushes.DarkOrange;
        }

        FormatResultText.Text = "";
    }

    private async void FormatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive == null) return;
        char dl = _selectedDrive.Name[0];

        // Ask filesystem
        var dlg1 = new TDeckFormatChoiceDialog(_selectedDrive)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (dlg1.ShowDialog() != true) return;
        string fsChoice = dlg1.ChosenFileSystem;

        // First confirmation
        if (MessageBox.Show(
                string.Format(Loc("StrTDeckFmtConfirm1"), $"{dl}:\\"),
                Loc("StrTDeckFmtConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // Second confirmation
        if (MessageBox.Show(
                string.Format(Loc("StrTDeckFmtConfirm2"), $"{dl}:\\"),
                Loc("StrTDeckFmtConfirmTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Stop) != MessageBoxResult.Yes)
            return;

        FormatProgressBorder.Visibility = Visibility.Visible;
        FormatProgressText.Text = string.Format(Loc("StrTDeckFormatting"), $"{dl}:\\");
        FormatBtn.IsEnabled = false;
        SkipFormatBtn.IsEnabled = false;

        var progress = new Progress<string>(msg => FormatProgressText.Text = msg);
        var result = await DriveService.FormatDriveAsync(dl, fsChoice, progress);

        FormatProgressBorder.Visibility = Visibility.Collapsed;
        FormatBtn.IsEnabled = true;
        SkipFormatBtn.IsEnabled = true;

        if (result == FormatResult.Success)
        {
            // Re-read drive info after format
            var refreshed = DriveService.GetRemovableDrives()
                .FirstOrDefault(d => d.Name[0] == dl);
            if (refreshed != null) _selectedDrive = refreshed;

            _formatOk = true;
            FormatResultText.Foreground = Brushes.DarkGreen;
            FormatResultText.Text = string.Format(Loc("StrTDeckFormatDone"), fsChoice);
            UpdateFormatPage();
        }
        else if (result == FormatResult.Cancelled)
        {
            FormatResultText.Foreground = Brushes.Gray;
            FormatResultText.Text = Loc("StrTDeckFormatFailed");
        }
        else
        {
            FormatResultText.Foreground = Brushes.Red;
            FormatResultText.Text = Loc("StrTDeckFormatFailed");
        }
    }

    private void SkipFormatBtn_Click(object sender, RoutedEventArgs e)
    {
        _formatOk = true; // user chose to skip, proceed anyway
        GoToStep(4);
    }

    // ──────────────────────────────────────────────────────────
    //  REGION
    // ──────────────────────────────────────────────────────────
    private void Region_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool byState   = RegionByStateRb.IsChecked == true;
        bool germany   = RegionGermanyRb.IsChecked == true;
        bool freestyle = RegionFreestyleRb.IsChecked == true;

        StatesPanel.Visibility    = byState   ? Visibility.Visible : Visibility.Collapsed;
        GermanyPanel.Visibility   = germany   ? Visibility.Visible : Visibility.Collapsed;
        FreestylePanel.Visibility = freestyle ? Visibility.Visible : Visibility.Collapsed;

        if (germany)
        {
            _north = 55.0997; _south = 47.2701; _east = 15.0419; _west = 5.8660;
            _regionName = "Deutschland";
            _regionSet  = true;
            BboxPreviewText.Text = string.Format(Loc("StrTDeckBboxPreview"), _north, _south, _west, _east);
        }
        else if (freestyle)
        {
            _regionSet = _freeStyleSet;
            if (_freeStyleSet)
            {
                _north = _freeN; _south = _freeS; _east = _freeE; _west = _freeW;
                BboxPreviewText.Text = string.Format(Loc("StrTDeckBboxPreview"), _north, _south, _west, _east);
            }
            else
            {
                BboxPreviewText.Text = "";
            }
        }
        else // by state
        {
            UpdateBboxFromStates();
        }
        UpdateEstimateIfOnPage5();
    }

    private void OpenMapDraw_Click(object sender, RoutedEventArgs e)
    {
        var mapWin = new TDeckRegionMapWindow { Owner = this };
        mapWin.ShowDialog();
        if (mapWin.Accepted)
        {
            _freeN = mapWin.ResultNorth;
            _freeS = mapWin.ResultSouth;
            _freeE = mapWin.ResultEast;
            _freeW = mapWin.ResultWest;
            _freeStyleSet = true;

            _north = _freeN; _south = _freeS; _east = _freeE; _west = _freeW;
            _regionName = "Freestyle";
            _regionSet  = true;

            FreestyleStatusText.Text = string.Format(Loc("StrTDeckBboxPreview"),
                _freeN, _freeS, _freeW, _freeE);
            BboxPreviewText.Text = FreestyleStatusText.Text;
            UpdateEstimateIfOnPage5();
        }
    }

    // ──────────────────────────────────────────────────────────
    //  ZOOM + MAP TYPE
    // ──────────────────────────────────────────────────────────
    private void Zoom_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var rb = (RadioButton)sender;
        _maxZoom = int.Parse((string)rb.Tag);

        ZoomWarningText.Visibility = Visibility.Collapsed;
        if (_maxZoom >= 16)
        {
            ZoomWarningText.Text = Loc("StrTDeckZoomWarn16");
            ZoomWarningText.Foreground = Brushes.Red;
            ZoomWarningText.Visibility = Visibility.Visible;
        }
        else if (_maxZoom >= 14)
        {
            ZoomWarningText.Text = Loc("StrTDeckZoomWarn14");
            ZoomWarningText.Foreground = Brushes.DarkOrange;
            ZoomWarningText.Visibility = Visibility.Visible;
        }

        UpdateEstimate();
    }

    private void MapType_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var rb = (RadioButton)sender;
        _mapSource = (string)rb.Tag switch
        {
            "Topo" => MapSource.OSMTopo,
            "Dark" => MapSource.OSMDark,
            _      => MapSource.OSM
        };
        UpdateEstimate();
    }

    private void UpdateEstimate()
    {
        if (!IsLoaded || !_regionSet || EstimateText == null) return;

        int count = TileDownloaderService.EstimateTileCount(_north, _south, _east, _west, 1, _maxZoom);
        long sizeBytes = TDeckTileService.EstimateSizeBytes(count);
        EstimateText.Text = string.Format(Loc("StrTDeckEstimate"),
            count, TDeckTileService.FormatSize(sizeBytes));

        if (_selectedDrive != null)
        {
            SDFreeText.Text = string.Format(Loc("StrTDeckSDFree"),
                DriveService.FormatSizeString(_selectedDrive.AvailableFreeSpace));
            SDLowText.Visibility = sizeBytes > _selectedDrive.AvailableFreeSpace
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void UpdateEstimateIfOnPage5()
    {
        if (_currentStep == 5) UpdateEstimate();
    }

    // ──────────────────────────────────────────────────────────
    //  DOWNLOAD
    // ──────────────────────────────────────────────────────────
    private void UpdateSummaryPage()
    {
        SumDriveText.Text  = _selectedDrive?.Name ?? "?";
        SumRegionText.Text = _regionName;
        SumZoomText.Text   = $"1–{_maxZoom}";
        SumTypeText.Text   = _mapSource switch
        {
            MapSource.OSMTopo => Loc("StrTDeckMapTopo"),
            MapSource.OSMDark => Loc("StrTDeckMapDark"),
            _                 => Loc("StrTDeckMapOSM")
        };

        int count = TileDownloaderService.EstimateTileCount(_north, _south, _east, _west, 1, _maxZoom);
        SumTilesText.Text = $"≈ {count:N0} (~{TDeckTileService.FormatSize(TDeckTileService.EstimateSizeBytes(count))})";

        DownloadProgress.Value  = 0;
        ProgressTileText.Text   = "";
        ProgressZoomText.Text   = "";
        DownloadResultText.Text = "";
    }

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrive == null) return;

        // SD card space check before starting
        int estimatedTiles = TileDownloaderService.EstimateTileCount(_north, _south, _east, _west, 1, _maxZoom);
        long estimatedBytes = TDeckTileService.EstimateSizeBytes(estimatedTiles);

        // Re-read free space (might have changed since step 5)
        try
        {
            var refreshed = new DriveInfo(_selectedDrive.Name);
            if (estimatedBytes > refreshed.AvailableFreeSpace)
            {
                var needed = TDeckTileService.FormatSize(estimatedBytes);
                var free   = DriveService.FormatSizeString(refreshed.AvailableFreeSpace);
                var answer = MessageBox.Show(
                    $"⚠️ Möglicherweise nicht genug Speicherplatz auf der SD-Karte!\n\n" +
                    $"Geschätzt benötigt: {needed}\n" +
                    $"Verfügbar:          {free}\n\n" +
                    $"Trotzdem fortfahren?",
                    Loc("StrTDeckTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }
        }
        catch { /* drive may have been removed — proceed and let the copy fail gracefully */ }

        StartBtn.Visibility           = Visibility.Collapsed;
        CancelDownloadBtn.Visibility  = Visibility.Visible;
        BackBtn.IsEnabled             = false;
        NextBtn.IsEnabled             = false;

        int total = TileDownloaderService.EstimateTileCount(_north, _south, _east, _west, 1, _maxZoom);
        DownloadProgress.Maximum = Math.Max(total, 1);

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var sdRoot = _selectedDrive.Name.TrimEnd('\\', '/');

        var progress = new Progress<TileProgress>(p =>
        {
            DownloadProgress.Value = p.Done;
            ProgressTileText.Text  = string.Format(Loc("StrTDeckProgressTile"),
                p.Done, p.Total, (double)p.Done / p.Total);
            ProgressZoomText.Text  = string.Format(Loc("StrTDeckProgressZoom"),
                p.Zoom, p.X, p.Y);
        });

        try
        {
            await TDeckTileService.TransferTilesAsync(
                _mapSource, _north, _south, _east, _west, _maxZoom,
                sdRoot, LocalTileDir, progress, _cts.Token);

            var done = (int)DownloadProgress.Value;
            DownloadResultText.Text = _cts.Token.IsCancellationRequested
                ? Loc("StrTDeckCancelled")
                : string.Format(Loc("StrTDeckDone"), done);
            DownloadResultText.Foreground = _cts.Token.IsCancellationRequested
                ? Brushes.Gray : Brushes.DarkGreen;
        }
        catch (OperationCanceledException)
        {
            DownloadResultText.Text = Loc("StrTDeckCancelled");
            DownloadResultText.Foreground = Brushes.Gray;
        }
        catch (Exception ex)
        {
            DownloadResultText.Text = string.Format(Loc("StrTDeckError"), ex.Message);
            DownloadResultText.Foreground = Brushes.Red;
        }
        finally
        {
            StartBtn.Visibility          = Visibility.Visible;
            CancelDownloadBtn.Visibility = Visibility.Collapsed;
            BackBtn.IsEnabled            = true;
            // Show Close instead of Next on completion
            NextBtn.Content   = Loc("StrTDeckClose");
            NextBtn.IsEnabled = true;
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ──────────────────────────────────────────────────────────
    //  NAVIGATION
    // ──────────────────────────────────────────────────────────
    private void NextBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep == TotalSteps) { Close(); return; }

        if (!ValidateCurrentStep()) return;

        if (_currentStep == 3 && _formatOk)
        {
            GoToStep(4);
        }
        else
        {
            GoToStep(_currentStep + 1);
        }
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1) GoToStep(_currentStep - 1);
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();

    private bool ValidateCurrentStep()
    {
        switch (_currentStep)
        {
            case 2:
                if (_selectedDrive == null)
                {
                    MessageBox.Show(Loc("StrTDeckSelectDriveFirst"), Loc("StrTDeckTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                return true;

            case 4:
                if (!_regionSet)
                {
                    MessageBox.Show(Loc("StrTDeckNoRegion"), Loc("StrTDeckTitle"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                return true;

            default:
                return true;
        }
    }

    private void GoToStep(int step)
    {
        _currentStep = step;

        // Show/hide pages
        Page1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Page4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Page5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        Page6.Visibility = step == 6 ? Visibility.Visible : Visibility.Collapsed;

        // Update header
        StepTitleText.Text = step switch
        {
            1 => Loc("StrTDeckS1Title"),
            2 => Loc("StrTDeckS2Title"),
            3 => Loc("StrTDeckS3Title"),
            4 => Loc("StrTDeckS4Title"),
            5 => Loc("StrTDeckS5Title"),
            6 => Loc("StrTDeckS6Title"),
            _ => ""
        };
        StepIndicatorText.Text = string.Format(Loc("StrTDeckStep"), step, TotalSteps);
        FooterStepText.Text    = StepIndicatorText.Text;

        BackBtn.IsEnabled = step > 1;

        if (step == TotalSteps)
        {
            NextBtn.Content   = Loc("StrTDeckClose");
            NextBtn.IsEnabled = false; // enabled after download completes
        }
        else
        {
            NextBtn.Content   = Loc("StrTDeckNext");
            NextBtn.IsEnabled = true;
        }

        // Page-specific setup
        switch (step)
        {
            case 3:
                UpdateFormatPage();
                break;
            case 4:
                Region_Checked(this, new RoutedEventArgs());
                break;
            case 5:
                UpdateEstimate();
                break;
            case 6:
                UpdateSummaryPage();
                break;
        }
    }
}

// ──────────────────────────────────────────────────────────
//  INLINE FORMAT CHOICE DIALOG
// ──────────────────────────────────────────────────────────
public class TDeckFormatChoiceDialog : Window
{
    private static string Loc(string key) =>
        Application.Current?.Resources[key] as string ?? key;

    public string ChosenFileSystem { get; private set; } = "exFAT";

    public TDeckFormatChoiceDialog(DriveInfo drive)
    {
        Title  = Loc("StrTDeckFmtChoiceTitle");
        Width  = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = Loc("StrTDeckFmtChoiceHint"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        bool preferFAT32 = drive.TotalSize <= 32L * 1024 * 1024 * 1024;

        var rbExFAT = new RadioButton
        {
            Content   = Loc("StrTDeckFmtExFAT"),
            IsChecked = true,        // always recommend exFAT for T-Deck
            Margin    = new Thickness(0, 4, 0, 4)
        };
        var rbFAT32 = new RadioButton
        {
            Content = Loc("StrTDeckFmtFAT32"),
            Margin  = new Thickness(0, 4, 0, 12)
        };
        panel.Children.Add(rbExFAT);
        panel.Children.Add(rbFAT32);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width   = 80,
            Padding = new Thickness(0, 6, 0, 6),
            Margin  = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancelBtn = new Button
        {
            Content   = Loc("StrTDeckCancel"),
            Width     = 80,
            Padding   = new Thickness(0, 6, 0, 6),
            IsCancel  = true
        };

        okBtn.Click += (s, e) =>
        {
            ChosenFileSystem = rbExFAT.IsChecked == true ? "exFAT" : "FAT32";
            DialogResult = true;
        };
        cancelBtn.Click += (s, e) => DialogResult = false;

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        Content = panel;
    }
}
