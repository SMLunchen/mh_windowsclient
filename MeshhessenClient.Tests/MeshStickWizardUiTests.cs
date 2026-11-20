using System.Text.RegularExpressions;
using System.Windows;
using MeshhessenClient;
using MeshhessenClient.Services;
using LoRaConfig = Meshtastic.Protobufs.Config.Types.LoRaConfig;

namespace MeshhessenClient.Tests;

/// <summary>
/// Same guards as for the firmware wizard: every StrMst… key the MeshStick code references exists in both language
/// files, DE/EN carry the same key set, and the window's XAML parses with all named controls present.
/// </summary>
public class MeshStickWizardUiTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MeshhessenClient.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static HashSet<string> KeysIn(string resourceFile)
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "MeshhessenClient", "Resources", resourceFile));
        return Regex.Matches(text, @"x:Key=""(StrMst[A-Za-z0-9]+)""").Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static HashSet<string> ReferencedKeys()
    {
        var files = new List<string>
        {
            Path.Combine(RepoRoot, "MeshhessenClient", "MeshStickWizardWindow.xaml"),
            Path.Combine(RepoRoot, "MeshhessenClient", "MeshStickWizardWindow.xaml.cs"),
            Path.Combine(RepoRoot, "MeshhessenClient", "MainWindow.MeshStick.cs"),
            Path.Combine(RepoRoot, "MeshhessenClient", "MainWindow.xaml"),
        };
        files.AddRange(Directory.EnumerateFiles(Path.Combine(RepoRoot, "MeshhessenClient", "Services", "MeshStick"), "*.cs"));
        var keys = new HashSet<string>();
        foreach (var f in files)
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"StrMst[A-Za-z0-9]+"))
                keys.Add(m.Value);
        for (int i = 1; i <= 6; i++) keys.Add($"StrMstStep{i}");   // built dynamically ($"StrMstStep{step}")
        keys.Remove("StrMstStep");
        return keys;
    }

    [Fact]
    public void Every_referenced_meshstick_key_exists_in_both_languages()
    {
        var de = KeysIn("Strings.de.xaml");
        var en = KeysIn("Strings.en.xaml");
        var referenced = ReferencedKeys();
        var missingDe = referenced.Where(k => !de.Contains(k)).OrderBy(k => k).ToList();
        var missingEn = referenced.Where(k => !en.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(missingDe.Count == 0, "Missing in Strings.de.xaml: " + string.Join(", ", missingDe));
        Assert.True(missingEn.Count == 0, "Missing in Strings.en.xaml: " + string.Join(", ", missingEn));
    }

    [Fact]
    public void Meshstick_keys_have_DE_EN_parity()
    {
        var de = KeysIn("Strings.de.xaml");
        var en = KeysIn("Strings.en.xaml");
        Assert.True(de.SetEquals(en),
            "Only DE: " + string.Join(", ", de.Except(en)) + " | Only EN: " + string.Join(", ", en.Except(de)));
    }

    private sealed class FakeHost : IMeshStickHost
    {
        public MeshtasticProtocolService? Protocol => null;
        public bool IsConnected => false;
        public bool IsReady => false;
        public LoRaConfig? CurrentLoRaConfig => null;
        public AppSettings Settings { get; private set; } = new();
        public void UpdateSettings(Func<AppSettings, AppSettings> change) => Settings = change(Settings);
        public void ConnectTcp(string host, int port) { }
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<MeshHessenApplyResult> ApplyMeshHessenProfileAsync() => Task.FromResult(new MeshHessenApplyResult(false, "", "fake"));
    }

    [Fact]
    public void Wizard_window_xaml_parses_with_all_named_controls()
    {
        WpfTestApp.Run(() =>
        {
            var w = new MeshStickWizardWindow(new FakeHost());
            foreach (var name in new[]
                     {
                         "Page1", "Page2", "Page3", "Page4", "Page5", "Page6",
                         "InternetStatusText", "StickStatusText", "DriverBox", "OpenWchBtn", "InstallFoundBtn", "PickDriverBtn", "RecheckBtn",
                         "DaemonStatusText", "SourceMirrorRadio", "SourceOfficialRadio", "SourceLocalRadio", "ReleaseCombo",
                         "RefreshReleasesBtn", "LocalMsiBox", "BrowseMsiBtn", "DownloadBtn", "DownloadProgress", "SkipInstallCheck",
                         "ApiPortBox", "ConfigPreviewBox", "SetupBtn", "ProbeDaemonBtn", "SetupLogBox",
                         "ConnectBtn", "NodeInfoBox", "NodeNameText", "NodeFwText",
                         "RegionCombo", "PresetCombo", "HopLimitBox", "TxPowerBox", "MeshHessenBtn", "SaveLoraBtn", "LoraLogBox",
                         "DoneText", "OpenWorkDirBtn", "BackBtn", "NextBtn", "CancelBtn",
                     })
                Assert.NotNull(w.FindName(name));
            // The mirror radio gets its label in the constructor (repo name from the settings).
            Assert.Contains("SMLunchen/mh_firmware", (string)((System.Windows.Controls.RadioButton)w.FindName("SourceMirrorRadio")).Content);
            w.Close();
        });
    }
}
