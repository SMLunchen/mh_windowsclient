using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MeshhessenClient.Services;
using MeshhessenClient.Services.MeshStick;

namespace MeshhessenClient;

public sealed record MeshHessenApplyResult(bool Ok, string Report, string? Error = null);

/// <summary>What the MeshStick wizard needs from the main window: the live connection state, a way to start the
/// normal TCP connect flow, and the Mesh-Hessen one-click profile.</summary>
public interface IMeshStickHost
{
    MeshtasticProtocolService? Protocol { get; }
    bool IsConnected { get; }
    /// <summary>Connected and the initial config/channel download has finished.</summary>
    bool IsReady { get; }
    LoRaConfig? CurrentLoRaConfig { get; }
    AppSettings Settings { get; }
    void UpdateSettings(Func<AppSettings, AppSettings> change);
    /// <summary>Switches the connect panel to TCP and starts the same flow as the Connect button.</summary>
    void ConnectTcp(string host, int port);
    Task DisconnectAsync();
    /// <summary>Writes EU868 + ShortSlow + hop 7 + the Mesh-Hessen channel in one transaction and waits for the reboot.</summary>
    Task<MeshHessenApplyResult> ApplyMeshHessenProfileAsync();
}

/// <summary>
/// Guided setup of an Elecrow MeshStick on this PC (Tools tab): requirements &amp; CH341 driver → meshtasticd Windows
/// build → config.yaml + service → TCP connect → LoRa settings (with Mesh-Hessen one-click) → done.
/// Everything that needs administrator rights runs through one UAC prompt per step.
/// </summary>
public partial class MeshStickWizardWindow : Window
{
    private const int StepCount = 6;
    private static readonly HttpClient Http = CreateHttp();

    private readonly IMeshStickHost _host;
    private readonly DispatcherTimer _probeTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    private int _step = 1;
    private bool _busy;
    private CancellationTokenSource? _cts;

    private MeshStickProbe? _probe;
    private bool? _internetOk;
    private string? _foundDriverFile;

    private IReadOnlyList<MeshtasticdRelease> _releases = Array.Empty<MeshtasticdRelease>();
    private MeshtasticdRelease? _release;
    private string? _msiPath;          // downloaded or picked locally — what the setup installs
    private bool _daemonInstalled;
    private bool _apiReachable;
    private bool _setupDone;
    private int _apiPort;

    private static string Loc(string key) => Application.Current.TryFindResource(key) as string ?? key;

    private static string Loc(string key, params object?[] args)
    {
        var fmt = Loc(key);
        try { return args.Length == 0 ? fmt : string.Format(fmt, args); }
        catch (FormatException) { return fmt; }
    }

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MeshhessenClient-MeshStick/1.0");
        return c;
    }

    public MeshStickWizardWindow(IMeshStickHost host)
    {
        InitializeComponent();
        ModernWpf.Controls.Primitives.WindowHelper.SetUseModernWindowStyle(this, true);
        _host = host;
        _apiPort = host.Settings.MeshStickApiPort is > 0 and <= 65535 ? host.Settings.MeshStickApiPort : MeshtasticdService.DefaultApiPort;
        SourceMirrorRadio.Content = Loc("StrMstSourceMirror", MirrorRepo);
        InstallBundledBtn.Content = Loc("StrMstInstallBundled", Ch341DriverSetup.BundledDriverVersion);
        ApiPortBox.Text = _apiPort.ToString();
        _probeTimer.Tick += async (_, _) => { if (_step == 1 && !_busy) await ProbeStickAsync(); };
        Loaded += async (_, _) => await InitAsync();
        Closing += (_, e) =>
        {
            if (_busy)
            {
                var r = MessageBox.Show(this, Loc("StrMstCancelConfirm"), Loc("StrMstTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
                _cts?.Cancel();
            }
            _probeTimer.Stop();
            // Win32 quirk (same as the other owned windows): activate the owner before this window is destroyed,
            // or the main window can minimize to the taskbar.
            try { Owner?.Activate(); } catch { }
        };
    }

    private string MirrorRepo => MeshtasticdReleases.IsValidRepo(_host.Settings.MeshStickMirrorRepo)
        ? _host.Settings.MeshStickMirrorRepo.Trim()
        : MeshtasticdReleases.DefaultMirrorRepo;

    private static void AppendLog(TextBox box, string line)
    {
        box.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        box.ScrollToEnd();
    }

    // ── Step 1: requirements, stick, driver ─────────────────────────────────

    private async Task InitAsync()
    {
        ShowStep(1);
        _probeTimer.Start();
        var internet = CheckInternetAsync();
        await ProbeStickAsync();
        _internetOk = await internet;
        InternetStatusText.Text = _internetOk == true ? Loc("StrMstInternetOk") : Loc("StrMstInternetNone");
        UpdateNav();
    }

    private static async Task<bool> CheckInternetAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://api.github.com/");
            using var resp = await Http.SendAsync(req, cts.Token);
            return true; // any HTTP answer means the network path is there (GitHub answers HEAD with 200/403)
        }
        catch { return false; }
    }

    private async Task ProbeStickAsync()
    {
        _probe = await MeshStickDeviceProbe.ProbeAsync();
        var p = _probe;
        var state = p.State;
        StickStatusText.Text = state switch
        {
            MeshStickState.NotPresent => Loc("StrMstStickNotPresent"),
            MeshStickState.DriverMissing => Loc("StrMstStickDriverMissing", p.ProblemCode),
            MeshStickState.DriverLibraryMissing => Loc("StrMstStickDllMissing"),
            _ => Loc("StrMstStickReady"),
        };
        StickDetailText.Text = p.Present
            ? $"{p.InstanceId}" + (string.IsNullOrEmpty(p.Name) ? "" : $" — {p.Name}") + (string.IsNullOrEmpty(p.Service) ? "" : $" ({p.Service})")
            : Loc("StrMstStickDetailNone", MeshStickDeviceProbe.HardwareIdPrefix);
        bool ready = state == MeshStickState.Ready;
        DriverBox.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        StickOkBox.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;

        if (!ready)
        {
            _foundDriverFile = Ch341DriverSetup.FindDownloadedInstallers().FirstOrDefault();
            InstallFoundBtn.Visibility = _foundDriverFile != null ? Visibility.Visible : Visibility.Collapsed;
            if (_foundDriverFile != null)
                InstallFoundBtn.Content = Loc("StrMstInstallFound", Path.GetFileName(_foundDriverFile));
        }
        UpdateNav();
    }

    private void OpenWch_Click(object sender, RoutedEventArgs e) => OpenUrl(Ch341DriverSetup.WchDownloadPageExe);

    private async void InstallBundled_Click(object sender, RoutedEventArgs e) => await InstallDriverAsync(null);

    private async void InstallFound_Click(object sender, RoutedEventArgs e)
    {
        if (_foundDriverFile != null) await InstallDriverAsync(_foundDriverFile);
    }

    private async void PickDriver_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc("StrMstPickDriver"),
            Filter = Loc("StrMstDriverPickFilter"),
            InitialDirectory = Directory.Exists(Ch341DriverSetup.DownloadsFolder) ? Ch341DriverSetup.DownloadsFolder : null,
        };
        if (dlg.ShowDialog(this) == true) await InstallDriverAsync(dlg.FileName);
    }

    private async void Recheck_Click(object sender, RoutedEventArgs e) => await ProbeStickAsync();

    /// <param name="file">A downloaded CH341PAR package, or null for the one that ships with the client.</param>
    private async Task InstallDriverAsync(string? file)
    {
        if (_busy) return;
        if (file != null && !Ch341DriverSetup.IsCandidate(file))
        {
            var r = MessageBox.Show(this, Loc("StrMstDriverNotCh341", Path.GetFileName(file)), Loc("StrMstTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        SetBusy(true);
        DriverStatusText.Text = Loc("StrMstDriverInstalling");
        try
        {
            void Log(string l) => Logger.WriteLine("[MeshStick] " + l);
            var (res, detail) = file == null
                ? await Ch341DriverSetup.InstallBundledAsync(Log, _cts!.Token)
                : await Ch341DriverSetup.InstallAsync(file, Log, _cts!.Token);
            DriverStatusText.Text = res switch
            {
                ElevatedRunResult.Success => Loc("StrMstDriverInstalled"),
                ElevatedRunResult.Cancelled => Loc("StrMstDriverCancelled"),
                _ => Loc("StrMstDriverFailed", detail),
            };
        }
        catch (OperationCanceledException) { DriverStatusText.Text = Loc("StrMstDriverCancelled"); }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] driver install error: {ex}");
            DriverStatusText.Text = Loc("StrMstDriverFailed", ex.Message);
        }
        finally { SetBusy(false); }
        // Windows binds the driver to the already-plugged stick within a few seconds; the timer re-probes anyway.
        await Task.Delay(1500);
        await ProbeStickAsync();
    }

    // ── Step 2: get meshtasticd ─────────────────────────────────────────────

    private async Task RenderDaemonPageAsync()
    {
        _daemonInstalled = MeshtasticdService.IsInstalled;
        var state = MeshtasticdService.QueryState();
        _apiReachable = await MeshtasticdService.IsApiReachableAsync(_apiPort);
        string stateText = state switch
        {
            DaemonServiceState.Running => Loc("StrMstSvcRunning"),
            DaemonServiceState.Stopped => Loc("StrMstSvcStopped"),
            DaemonServiceState.NotInstalled => Loc("StrMstSvcNotInstalled"),
            _ => state.ToString(),
        };
        if (_daemonInstalled)
        {
            var ver = await MeshtasticdService.GetInstalledVersionAsync(CancellationToken.None);
            DaemonStatusText.Text = Loc("StrMstDaemonInstalled", ver ?? "?", stateText, MeshtasticdService.ExePath) +
                                    (_apiReachable ? "\n" + Loc("StrMstDaemonApiOk", _apiPort) : "");
        }
        else
        {
            DaemonStatusText.Text = Loc("StrMstDaemonNotInstalled", MeshtasticdService.InstallDir);
        }
        SkipInstallCheck.IsEnabled = _daemonInstalled;
        if (!_daemonInstalled) SkipInstallCheck.IsChecked = false;

        if (_releases.Count == 0 && !SourceLocalRadio.IsChecked.GetValueOrDefault())
            await LoadReleasesAsync();
        UpdateNav();
    }

    private string SelectedRepo => SourceOfficialRadio.IsChecked == true ? MeshtasticdReleases.OfficialRepo : MirrorRepo;
    private string SelectedSourceLabel => SourceOfficialRadio.IsChecked == true ? Loc("StrMstSourceOfficialShort") : Loc("StrMstSourceMirrorShort");

    private async Task LoadReleasesAsync()
    {
        ReleaseCombo.Items.Clear();
        _release = null;
        ReleasesStatusText.Text = Loc("StrMstReleasesLoading", SelectedRepo);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _releases = await MeshtasticdReleases.FetchAsync(Http, SelectedRepo, SelectedSourceLabel, cts.Token);
            foreach (var r in _releases) ReleaseCombo.Items.Add(new ComboBoxItem { Content = r.Display, Tag = r });
            if (_releases.Count > 0)
            {
                ReleaseCombo.SelectedIndex = 0;
                ReleasesStatusText.Text = Loc("StrMstReleasesFound", _releases.Count, SelectedRepo);
            }
            else
            {
                ReleasesStatusText.Text = Loc("StrMstReleasesNone", SelectedRepo);
            }
        }
        catch (Exception ex)
        {
            _releases = Array.Empty<MeshtasticdRelease>();
            Logger.WriteLine($"[MeshStick] release lookup {SelectedRepo} failed: {ex.Message}");
            ReleasesStatusText.Text = Loc("StrMstReleasesError", SelectedRepo, ex.Message);
        }
        UpdateNav();
    }

    private async void Source_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        bool local = SourceLocalRadio.IsChecked == true;
        ReleasePanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        ReleasesStatusText.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        LocalPanel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        DownloadBtn.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        _msiPath = local && File.Exists(LocalMsiBox.Text) ? LocalMsiBox.Text : null;
        DownloadStatusText.Text = _msiPath != null ? Loc("StrMstDownloaded", Path.GetFileName(_msiPath)) : "";
        if (!local) await LoadReleasesAsync();
        UpdateNav();
    }

    private void Release_Changed(object sender, SelectionChangedEventArgs e)
    {
        _release = (ReleaseCombo.SelectedItem as ComboBoxItem)?.Tag as MeshtasticdRelease;
        var cached = _release != null ? Path.Combine(MeshtasticdService.ClientWorkDir, _release.AssetName) : null;
        _msiPath = cached != null && File.Exists(cached) && (_release!.Size <= 0 || new FileInfo(cached).Length == _release.Size) ? cached : null;
        DownloadStatusText.Text = _msiPath != null ? Loc("StrMstDownloaded", Path.GetFileName(_msiPath)) : "";
        UpdateNav();
    }

    private async void RefreshReleases_Click(object sender, RoutedEventArgs e) => await LoadReleasesAsync();

    private void BrowseMsi_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc("StrMstBrowseMsi"), Filter = Loc("StrMstMsiFilter") };
        if (dlg.ShowDialog(this) != true) return;
        LocalMsiBox.Text = dlg.FileName;
        _msiPath = dlg.FileName;
        DownloadStatusText.Text = Loc("StrMstDownloaded", Path.GetFileName(_msiPath));
        UpdateNav();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_release == null || _busy) return;
        SetBusy(true);
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        try
        {
            var progress = new Progress<double>(f =>
            {
                DownloadProgress.Value = f * 100;
                DownloadStatusText.Text = Loc("StrMstDownloading", _release.AssetName, (int)(f * 100));
            });
            _msiPath = await MeshtasticdReleases.DownloadAsync(Http, _release, MeshtasticdService.ClientWorkDir, progress, _cts!.Token);
            DownloadStatusText.Text = Loc("StrMstDownloaded", Path.GetFileName(_msiPath));
            Logger.WriteLine($"[MeshStick] downloaded {_msiPath}");
        }
        catch (OperationCanceledException) { DownloadStatusText.Text = Loc("StrMstDriverCancelled"); }
        catch (Exception ex)
        {
            _msiPath = null;
            Logger.WriteLine($"[MeshStick] download failed: {ex.Message}");
            DownloadStatusText.Text = Loc("StrMstDownloadError", ex.Message);
        }
        finally
        {
            DownloadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            UpdateNav();
        }
    }

    private void SkipInstall_Changed(object sender, RoutedEventArgs e) => UpdateNav();

    // ── Step 3: config & service ────────────────────────────────────────────

    private void RenderSetupPage()
    {
        ApiPort_Changed(this, null!);
        SetupStatusText.Text = _apiReachable ? Loc("StrMstSetupApiOk", _apiPort) : "";
    }

    private void ApiPort_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (int.TryParse(ApiPortBox.Text, out var port) && port > 0 && port <= 65535)
        {
            _apiPort = port;
            ConfigPreviewBox.Text = MeshtasticdService.BuildConfigYaml(port);
            SetupBtn.IsEnabled = !_busy;
        }
        else
        {
            ConfigPreviewBox.Text = Loc("StrMstPortInvalid");
            SetupBtn.IsEnabled = false;
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        bool install = SkipInstallCheck.IsChecked != true;
        if (install && _msiPath == null) { SetupStatusText.Text = Loc("StrMstSetupNoMsi"); return; }
        SetBusy(true);
        SetupProgress.Visibility = Visibility.Visible;
        SetupStatusText.Text = Loc("StrMstSetupRunning");
        AppendLog(SetupLogBox, install ? $"msiexec /i {Path.GetFileName(_msiPath)} /qn" : "(installation skipped)");
        AppendLog(SetupLogBox, $"config.yaml → {MeshtasticdService.ConfigPath} (APIPort {_apiPort})");
        try
        {
            var (res, code, log) = await MeshtasticdService.RunSetupAsync(install ? _msiPath : null, _apiPort, Ch341DriverSetup.StagedDll, _cts!.Token);
            foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries)) AppendLog(SetupLogBox, line.Trim());
            if (res == ElevatedRunResult.Cancelled)
            {
                SetupStatusText.Text = Loc("StrMstSetupCancelled");
                return;
            }
            if (res != ElevatedRunResult.Success)
            {
                SetupStatusText.Text = code == 20 ? Loc("StrMstSetupFailedMsi") : Loc("StrMstSetupFailed", code);
                return;
            }
            _host.UpdateSettings(s => s with { MeshStickApiPort = _apiPort });
            SetupStatusText.Text = Loc("StrMstSetupWaitingApi", _apiPort);
            AppendLog(SetupLogBox, $"waiting for 127.0.0.1:{_apiPort} …");
            _apiReachable = await MeshtasticdService.WaitForApiAsync(_apiPort, TimeSpan.FromSeconds(60), _cts.Token);
            _setupDone = true;
            SetupStatusText.Text = _apiReachable ? Loc("StrMstSetupApiOk", _apiPort) : Loc("StrMstSetupApiTimeout", _apiPort);
            AppendLog(SetupLogBox, _apiReachable ? "API port open" : "API port still closed — see the hints");
        }
        catch (OperationCanceledException) { SetupStatusText.Text = Loc("StrMstSetupCancelled"); }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] setup error: {ex}");
            SetupStatusText.Text = Loc("StrMstSetupFailed", ex.Message);
        }
        finally
        {
            SetupProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            UpdateNav();
        }
    }

    /// <summary>Runs the daemon in the foreground for 12 s with the config that would be written — before any
    /// installation (exe pulled from the MSI), or after a failed one. Its console output is the only place that
    /// says why the service does not start.</summary>
    private async void ProbeDaemon_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (await MeshtasticdService.IsApiReachableAsync(_apiPort))
        {
            SetupStatusText.Text = Loc("StrMstProbeServiceRunning", _apiPort);
            return;
        }
        SetBusy(true);
        SetupProgress.Visibility = Visibility.Visible;
        SetupStatusText.Text = Loc("StrMstProbeRunning");
        try
        {
            string? exe = MeshtasticdService.IsInstalled ? MeshtasticdService.ExePath : null;
            if (exe == null && _msiPath != null)
            {
                AppendLog(SetupLogBox, $"msiexec /a {Path.GetFileName(_msiPath)} → {MeshtasticdService.ExtractDir} (extract only, no admin rights)");
                exe = await MeshtasticdService.ExtractExeFromMsiAsync(_msiPath, _cts!.Token);
            }
            if (exe == null)
            {
                SetupStatusText.Text = Loc("StrMstProbeNoExe");
                return;
            }
            Directory.CreateDirectory(MeshtasticdService.ClientWorkDir);
            var cfg = Path.Combine(MeshtasticdService.ClientWorkDir, "config.yaml");
            await File.WriteAllTextAsync(cfg, MeshtasticdService.BuildConfigYaml(_apiPort), new UTF8Encoding(false), _cts!.Token);
            AppendLog(SetupLogBox, $"{exe} -c \"{cfg}\" -p {_apiPort}  (12 s)");
            var r = await MeshtasticdService.RunForegroundProbeAsync(exe, cfg, _apiPort, TimeSpan.FromSeconds(12), _cts.Token);
            foreach (var line in r.Output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(60))
                AppendLog(SetupLogBox, line);
            SetupStatusText.Text = r.StillRunning ? Loc("StrMstProbeOk") : Loc("StrMstProbeExited", r.ExitCode ?? -1);
            Logger.WriteLine($"[MeshStick] daemon probe: {(r.StillRunning ? "still running after 12 s" : $"exited with {r.ExitCode}")}");
        }
        catch (OperationCanceledException) { SetupStatusText.Text = Loc("StrMstSetupCancelled"); }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] daemon probe error: {ex}");
            SetupStatusText.Text = Loc("StrMstSetupFailed", ex.Message);
        }
        finally
        {
            SetupProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            UpdateNav();
        }
    }

    // ── Step 4: connect ─────────────────────────────────────────────────────

    private void RenderConnectPage()
    {
        NodeEndpointText.Text = $"{MeshtasticdService.LoopbackHost}:{_apiPort}";
        if (_host.IsReady && IsHostOnLocalDaemon()) { ShowNodeInfo(); return; }
        ConnectStatusText.Text = "";
        NodeInfoBox.Visibility = Visibility.Collapsed;
        UpdateNav();
    }

    private bool IsHostOnLocalDaemon() =>
        _host.IsConnected && _host.Settings.LastConnectionType.Equals("Tcp", StringComparison.OrdinalIgnoreCase)
        && MeshtasticdService.IsLoopback(_host.Settings.LastTcpHost) && _host.Settings.LastTcpPort == _apiPort;

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            if (_host.IsConnected && !IsHostOnLocalDaemon())
            {
                ConnectStatusText.Text = Loc("StrMstDisconnectFirst");
                await _host.DisconnectAsync();
            }
            if (!_host.IsConnected)
            {
                ConnectStatusText.Text = Loc("StrMstConnecting", _apiPort);
                _host.ConnectTcp(MeshtasticdService.LoopbackHost, _apiPort);
            }
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
            while (DateTime.UtcNow < deadline && !_cts!.IsCancellationRequested)
            {
                if (_host.IsReady) { ShowNodeInfo(); return; }
                await Task.Delay(1000, _cts.Token);
            }
            ConnectStatusText.Text = Loc("StrMstConnectTimeout");
        }
        catch (OperationCanceledException) { }
        finally { SetBusy(false); UpdateNav(); }
    }

    private void ShowNodeInfo()
    {
        var proto = _host.Protocol;
        uint myId = proto?.GetMyNodeId() ?? 0;
        string name = "";
        if (proto != null && myId != 0 && proto.GetKnownNodes().TryGetValue(myId, out var me))
            name = me.Name;
        NodeNameText.Text = string.IsNullOrEmpty(name) ? $"!{myId:x8}" : $"{name} (!{myId:x8})";
        NodeFwText.Text = proto?.GetFirmwareVersion() ?? "?";
        NodeInfoBox.Visibility = Visibility.Visible;
        ConnectStatusText.Text = Loc("StrMstConnected");
        UpdateNav();
    }

    // ── Step 5: LoRa settings ───────────────────────────────────────────────

    private void RenderLoraPage()
    {
        if (RegionCombo.Items.Count == 0)
        {
            foreach (var r in Enum.GetValues<Region>()) RegionCombo.Items.Add(new ComboBoxItem { Content = r.ToString(), Tag = r });
            foreach (var p in Enum.GetValues<ModemPreset>()) PresetCombo.Items.Add(new ComboBoxItem { Content = p.ToString(), Tag = p });
        }
        FillLoraFields(_host.CurrentLoRaConfig);
        LoraStatusText.Text = _host.IsReady ? "" : Loc("StrMstLoraNotConnected");
        MeshHessenBtn.IsEnabled = SaveLoraBtn.IsEnabled = _host.IsReady && !_busy;
    }

    private void FillLoraFields(LoRaConfig? l)
    {
        if (l == null) return;
        Select(RegionCombo, l.Region);
        Select(PresetCombo, l.ModemPreset);
        HopLimitBox.Text = l.HopLimit.ToString();
        TxPowerBox.Text = l.TxPower.ToString();
    }

    private static void Select(ComboBox combo, object value)
    {
        foreach (ComboBoxItem item in combo.Items)
            if (Equals(item.Tag, value)) { combo.SelectedItem = item; return; }
    }

    private async void MeshHessen_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_host.IsReady) return;
        SetBusy(true);
        MeshHessenBtn.IsEnabled = SaveLoraBtn.IsEnabled = false;
        LoraStatusText.Text = Loc("StrMstLoraWriting");
        AppendLog(LoraLogBox, "Mesh-Hessen: EU868 · ShortSlow · hop 7 · channel \"Mesh Hessen\"");
        try
        {
            var r = await _host.ApplyMeshHessenProfileAsync();
            foreach (var line in (r.Error ?? r.Report).Split('\n', StringSplitOptions.RemoveEmptyEntries)) AppendLog(LoraLogBox, line.TrimEnd());
            LoraStatusText.Text = r.Error != null ? Loc("StrMstLoraFailed", r.Error) : r.Ok ? Loc("StrMstLoraDone") : Loc("StrMstLoraPartial");
            FillLoraFields(_host.CurrentLoRaConfig);
        }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] Mesh-Hessen error: {ex}");
            LoraStatusText.Text = Loc("StrMstLoraFailed", ex.Message);
        }
        finally
        {
            SetBusy(false);
            MeshHessenBtn.IsEnabled = SaveLoraBtn.IsEnabled = _host.IsReady;
            UpdateNav();
        }
    }

    private async void SaveLora_Click(object sender, RoutedEventArgs e)
    {
        var proto = _host.Protocol;
        if (_busy || !_host.IsReady || proto == null) return;
        if (!int.TryParse(HopLimitBox.Text, out var hops) || hops < 1 || hops > 7) { LoraStatusText.Text = Loc("StrMstLoraBadHops"); return; }
        if (!int.TryParse(TxPowerBox.Text, out var tx) || tx < 0 || tx > 30) { LoraStatusText.Text = Loc("StrMstLoraBadTx"); return; }
        var region = (RegionCombo.SelectedItem as ComboBoxItem)?.Tag as Region? ?? Region.Unset;
        var preset = (PresetCombo.SelectedItem as ComboBoxItem)?.Tag as ModemPreset? ?? ModemPreset.LongFast;

        SetBusy(true);
        MeshHessenBtn.IsEnabled = SaveLoraBtn.IsEnabled = false;
        LoraStatusText.Text = Loc("StrMstLoraWriting");
        try
        {
            var lora = _host.CurrentLoRaConfig?.Clone() ?? new LoRaConfig();
            lora.Region = region;
            lora.UsePreset = true;
            lora.ModemPreset = preset;
            lora.HopLimit = (uint)hops;
            lora.TxPower = tx;
            lora.TxEnabled = true;
            AppendLog(LoraLogBox, $"write: region {region}, preset {preset}, hops {hops}, tx {tx} dBm");
            await proto.BeginEditSettingsAsync();
            await Task.Delay(300);
            await proto.SetLoRaConfigAsync(lora);
            await Task.Delay(300);
            await proto.CommitEditSettingsAsync();
            AppendLog(LoraLogBox, "committed — the node restarts, the client reconnects");
            LoraStatusText.Text = Loc("StrMstLoraWaitReboot");

            // The daemon restarts itself on commit; the main window's auto-reconnect brings the link back.
            bool dropped = false;
            for (int i = 0; i < 40 && !dropped; i++) { if (!_host.IsConnected) dropped = true; else await Task.Delay(500, _cts!.Token); }
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
            while (DateTime.UtcNow < deadline && !_host.IsReady) await Task.Delay(1000, _cts!.Token);
            if (!_host.IsReady) { LoraStatusText.Text = Loc("StrMstConnectTimeout"); return; }
            await Task.Delay(1500, _cts!.Token);
            var l = _host.CurrentLoRaConfig;
            FillLoraFields(l);
            bool ok = l != null && l.Region == region && l.ModemPreset == preset && l.HopLimit == hops;
            AppendLog(LoraLogBox, l == null ? "read-back: no LoRa config" : $"read-back: region {l.Region}, preset {l.ModemPreset}, hops {l.HopLimit}, tx {l.TxPower}");
            LoraStatusText.Text = ok ? Loc("StrMstLoraDone") : Loc("StrMstLoraPartial");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.WriteLine($"[MeshStick] LoRa write error: {ex}");
            LoraStatusText.Text = Loc("StrMstLoraFailed", ex.Message);
        }
        finally
        {
            SetBusy(false);
            MeshHessenBtn.IsEnabled = SaveLoraBtn.IsEnabled = _host.IsReady;
            UpdateNav();
        }
    }

    // ── Step 6: done ────────────────────────────────────────────────────────

    private void RenderDonePage()
    {
        DoneText.Text = Loc("StrMstDoneText", _apiPort);
        DoneDetailText.Text = Loc("StrMstDoneDetail", MeshtasticdService.ConfigPath, MeshtasticdService.ClientWorkDir);
    }

    private void OpenWorkDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(MeshtasticdService.ClientWorkDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{MeshtasticdService.ClientWorkDir}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { Logger.WriteLine($"[MeshStick] open folder: {ex.Message}"); }
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (busy) { _cts?.Dispose(); _cts = new CancellationTokenSource(); }
        FooterStatusText.Text = busy ? Loc("StrMstWorking") : "";
        UpdateNav();
    }

    private void ShowStep(int step)
    {
        _step = step;
        Page1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        Page4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        Page5.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        Page6.Visibility = step == 6 ? Visibility.Visible : Visibility.Collapsed;
        StepTitleText.Text = Loc($"StrMstStep{step}");
        StepIndicatorText.Text = Loc("StrMstStepOf", step, StepCount);
        UpdateNav();
    }

    private void UpdateNav()
    {
        BackBtn.Visibility = _step is > 1 and < 6 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Visibility = _step < 6 ? Visibility.Visible : Visibility.Collapsed;
        BackBtn.IsEnabled = !_busy;
        CancelBtn.Content = Loc(_step == 6 ? "StrMstCloseBtn" : "StrMstCancel");
        NextBtn.IsEnabled = !_busy && _step switch
        {
            1 => _probe?.State == MeshStickState.Ready,
            2 => (SkipInstallCheck.IsChecked == true && _daemonInstalled) || _msiPath != null,
            3 => _apiReachable,
            4 => _host.IsReady && IsHostOnLocalDaemon(),
            5 => true,
            _ => false,
        };
        if (_step == 1) InstallBundledBtn.IsEnabled = PickDriverBtn.IsEnabled = InstallFoundBtn.IsEnabled = OpenWchBtn.IsEnabled = RecheckBtn.IsEnabled = !_busy;
        if (_step == 2) DownloadBtn.IsEnabled = !_busy && _release != null && SourceLocalRadio.IsChecked != true;
        if (_step == 3)
        {
            SetupBtn.IsEnabled = !_busy && _apiPort > 0;
            ProbeDaemonBtn.IsEnabled = !_busy && _apiPort > 0 && (MeshtasticdService.IsInstalled || _msiPath != null);
        }
        if (_step == 4) ConnectBtn.IsEnabled = !_busy;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 1 && !_busy) ShowStep(_step - 1);
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        switch (_step)
        {
            case 1: _probeTimer.Stop(); ShowStep(2); await RenderDaemonPageAsync(); break;
            case 2: ShowStep(3); RenderSetupPage(); break;
            case 3: ShowStep(4); RenderConnectPage(); break;
            case 4: ShowStep(5); RenderLoraPage(); break;
            case 5: ShowStep(6); RenderDonePage(); break;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            var r = MessageBox.Show(this, Loc("StrMstCancelConfirm"), Loc("StrMstTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r == MessageBoxResult.Yes) _cts?.Cancel();
            return;
        }
        Close();
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Logger.WriteLine($"[MeshStick] open url: {ex.Message}"); }
    }
}
