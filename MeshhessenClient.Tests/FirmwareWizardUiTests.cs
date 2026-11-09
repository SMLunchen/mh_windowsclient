using System.Text.RegularExpressions;
using System.Windows;
using MeshhessenClient;
using MeshhessenClient.Services;

namespace MeshhessenClient.Tests;

/// <summary>
/// The wizard is UI-heavy and cannot be driven against hardware in CI, so two cheap guards keep it
/// shippable: every i18n key the firmware code references exists in both language files (DE/EN parity),
/// and the window's XAML parses with all named controls present (a XAML error would otherwise only
/// surface when a user clicks the Tools button).
/// </summary>
public class FirmwareWizardUiTests
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
        return Regex.Matches(text, @"x:Key=""(StrFw[A-Za-z0-9]+)""").Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static HashSet<string> ReferencedKeys()
    {
        var files = new List<string>
        {
            Path.Combine(RepoRoot, "MeshhessenClient", "FirmwareUpdateWindow.xaml"),
            Path.Combine(RepoRoot, "MeshhessenClient", "FirmwareUpdateWindow.xaml.cs"),
            Path.Combine(RepoRoot, "MeshhessenClient", "MainWindow.FirmwareUpdate.cs"),
            Path.Combine(RepoRoot, "MeshhessenClient", "MainWindow.xaml"),
        };
        files.AddRange(Directory.EnumerateFiles(Path.Combine(RepoRoot, "MeshhessenClient", "Services", "Firmware"), "*.cs", SearchOption.AllDirectories));
        var keys = new HashSet<string>();
        foreach (var f in files)
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"StrFw[A-Za-z0-9]+"))
                keys.Add(m.Value);
        // Step titles are built dynamically ($"StrFwStep{step}").
        for (int i = 1; i <= 6; i++) keys.Add($"StrFwStep{i}");
        keys.Remove("StrFwStep"); // the interpolation prefix itself
        return keys;
    }

    [Fact]
    public void Every_referenced_firmware_key_exists_in_both_languages()
    {
        var de = KeysIn("Strings.de.xaml");
        var en = KeysIn("Strings.en.xaml");
        var missingDe = ReferencedKeys().Where(k => !de.Contains(k)).OrderBy(k => k).ToList();
        var missingEn = ReferencedKeys().Where(k => !en.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(missingDe.Count == 0, "Missing in Strings.de.xaml: " + string.Join(", ", missingDe));
        Assert.True(missingEn.Count == 0, "Missing in Strings.en.xaml: " + string.Join(", ", missingEn));
    }

    [Fact]
    public void Firmware_keys_have_DE_EN_parity()
    {
        var de = KeysIn("Strings.de.xaml");
        var en = KeysIn("Strings.en.xaml");
        Assert.True(de.SetEquals(en),
            "Only DE: " + string.Join(", ", de.Except(en)) + " | Only EN: " + string.Join(", ", en.Except(de)));
    }

    private sealed class FakeHost : IFirmwareUpdateHost
    {
        public MeshtasticProtocolService? Protocol => null;
        public bool IsConnected => false;
        public ConnectionParameters? ConnectionParams => null;
        public Task DisconnectForFirmwareUpdateAsync() => Task.CompletedTask;
        public void ReconnectAfterFirmwareUpdate() { }
    }

    [Fact]
    public void Wizard_window_xaml_parses_with_all_named_controls()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current == null)
                    _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/MeshhessenClient;component/Resources/Strings.de.xaml")
                });

                var w = new FirmwareUpdateWindow(new FakeHost());
                foreach (var name in new[]
                         {
                             "Page1", "Page2", "Page3", "Page4", "Page5", "Page6",
                             "MethodWifiRadio", "MethodBleOtaRadio", "MethodBleDfuRadio", "MethodUf2Radio",
                             "ReleaseCombo", "LocalFileBox", "PreflightList", "StartBtn", "TransferProgress",
                             "LogBox", "ReconnectBtn", "BackBtn", "NextBtn", "CancelBtn", "BtMultiBox",
                         })
                    Assert.NotNull(w.FindName(name));
                w.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.True(failure == null, failure?.ToString());
    }
}
