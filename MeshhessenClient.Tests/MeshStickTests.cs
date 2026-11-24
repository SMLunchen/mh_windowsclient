using MeshhessenClient.Services;
using MeshhessenClient.Services.MeshStick;

namespace MeshhessenClient.Tests;

/// <summary>
/// The MeshStick assistant drives Windows (driver install, MSI, service) — none of that can run in a test. What can:
/// the config.yaml it writes for meshtasticd, the elevated setup script, the release-asset filter, the device-state
/// classification and the small helpers the wizard decides on.
/// </summary>
public class MeshStickTests
{
    // ── config.yaml ─────────────────────────────────────────────────────────

    [Fact]
    public void Config_yaml_carries_the_meshstick_pin_map_and_the_api_port()
    {
        var yaml = MeshtasticdService.BuildConfigYaml(4403);
        Assert.Contains("Module: sx1262", yaml);
        Assert.Contains("spidev: ch341", yaml);
        Assert.Contains("USB_VID: 0x1A86", yaml);
        Assert.Contains("USB_PID: 0x5512", yaml);
        Assert.Contains("DIO3_TCXO_VOLTAGE: true", yaml);
        Assert.Contains("DIO2_AS_RF_SWITCH: true", yaml);
        Assert.Contains("APIPort: 4403", yaml);
        Assert.DoesNotContain("/etc/", yaml);              // no Linux paths — the daemon would try to read them
        Assert.DoesNotContain("Module: auto", yaml);       // auto-detect needs available.d, which Windows lacks
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Config_yaml_rejects_an_invalid_port(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshtasticdService.BuildConfigYaml(port));
    }

    // ── elevated setup script ───────────────────────────────────────────────

    [Fact]
    public void Setup_script_installs_the_msi_only_when_one_is_given()
    {
        var with = MeshtasticdService.BuildSetupScript(@"C:\dl\meshtasticd-2.8.3-x64.msi", @"C:\stage\config.yaml", null, @"C:\stage\setup.log");
        var without = MeshtasticdService.BuildSetupScript(null, @"C:\stage\config.yaml", null, @"C:\stage\setup.log");
        Assert.Contains("msiexec.exe", with);
        Assert.Contains("meshtasticd-2.8.3-x64.msi", with);
        Assert.Contains("/qn", with);
        Assert.Contains("/l*v", with);                                 // verbose MSI log, mined for the error lines
        Assert.DoesNotContain("msiexec", without);
        // config.yaml must be in place BEFORE msiexec: the MSI starts the service immediately and keeps an existing
        // config (NeverOverwrite); with the MSI's own default config the daemon exits and the install rolls back (1920/1603).
        Assert.True(with.IndexOf("Copy-Item", StringComparison.Ordinal) < with.IndexOf("msiexec.exe", StringComparison.Ordinal),
            "config.yaml must be written before the MSI is installed");
        foreach (var s in new[] { with, without })
        {
            Assert.Contains("sc.exe sdset meshtasticd", s);
            Assert.Contains(MeshtasticdService.ServiceSddlForUsers, s);
            Assert.Contains("sc.exe failureflag meshtasticd 1", s);
            Assert.Contains("start= auto", s);
            Assert.Contains("Start-Service -Name meshtasticd", s);
            Assert.Contains(MeshtasticdService.ConfigPath, s);
            Assert.Contains(@"C:\stage\config.yaml", s);
        }
    }

    [Fact]
    public void Setup_script_stages_the_ch341_dll_and_escapes_quotes_in_paths()
    {
        var s = MeshtasticdService.BuildSetupScript(null, @"C:\it's here\config.yaml", @"C:\tmp\CH341DLLA64.DLL", @"C:\log\setup.log");
        Assert.Contains("CH341DLLA64.DLL", s);
        Assert.Contains("'C:\\it''s here\\config.yaml'", s);   // PowerShell single-quote escaping
    }

    [Fact]
    public void Service_sddl_grants_authenticated_users_start_and_stop()
    {
        // RP = start, WP = stop in the SDDL service-rights alphabet; AU = Authenticated Users.
        Assert.Contains("(A;;CCLCSWRPWPDTLOCRRC;;;AU)", MeshtasticdService.ServiceSddlForUsers);
        Assert.StartsWith("D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)", MeshtasticdService.ServiceSddlForUsers);
    }

    // ── release lookup ──────────────────────────────────────────────────────

    private const string SampleReleases = """
        [
          {"tag_name":"v2.8.3.aaaaaaa","draft":false,"prerelease":true,"published_at":"2026-11-10T10:00:00Z",
           "assets":[
             {"name":"meshtasticd-2.8.3-x64.msi","browser_download_url":"https://example/x64.msi","size":4000000},
             {"name":"meshtasticd-2.8.3-arm64.msi","browser_download_url":"https://example/arm64.msi","size":3900000},
             {"name":"firmware-esp32-2.8.3.zip","browser_download_url":"https://example/fw.zip","size":1}
           ]},
          {"tag_name":"v2.8.2.bbbbbbb","draft":false,"prerelease":false,"published_at":"2026-11-01T10:00:00Z",
           "assets":[{"name":"meshtasticd-2.8.2-x64.msi","browser_download_url":"https://example/282.msi","size":10}]},
          {"tag_name":"v2.8.4.ccccccc","draft":true,"prerelease":false,"published_at":"2026-11-20T10:00:00Z",
           "assets":[{"name":"meshtasticd-2.8.4-x64.msi","browser_download_url":"https://example/draft.msi","size":10}]},
          {"tag_name":"v2.7.0","draft":false,"prerelease":false,"published_at":"2026-01-01T10:00:00Z","assets":[]}
        ]
        """;

    [Fact]
    public void Release_parser_keeps_only_x64_msi_assets_of_published_releases_newest_first()
    {
        var list = MeshtasticdReleases.ParseGitHubReleases(SampleReleases, "SMLunchen/mh_firmware", "Meshhessen");
        Assert.Equal(2, list.Count);
        Assert.Equal("2.8.3", list[0].Version);
        Assert.True(list[0].Prerelease);
        Assert.Equal("https://example/x64.msi", list[0].DownloadUrl);
        Assert.Equal(4000000, list[0].Size);
        Assert.Equal("2.8.2", list[1].Version);
        Assert.All(list, r => Assert.Equal("SMLunchen/mh_firmware", r.Repo));
        Assert.Contains("Meshhessen", list[0].Display);
    }

    [Theory]
    [InlineData("meshtasticd-2.8.3-x64.msi", true, "2.8.3")]
    [InlineData("meshtasticd-2.8.3.1a2b3c4-x64.msi", true, "2.8.3.1a2b3c4")]
    [InlineData("MESHTASTICD-2.9.0-AMD64.MSI", true, "2.9.0")]
    [InlineData("meshtasticd-2.8.3-arm64.msi", false, "")]
    [InlineData("meshtasticd-2.8.3-x64.zip", false, "")]
    [InlineData("firmware-2.8.3.json", false, "")]
    public void Msi_asset_pattern(string name, bool matches, string version)
    {
        var m = MeshtasticdReleases.MsiAsset.Match(name);
        Assert.Equal(matches, m.Success);
        if (matches) Assert.Equal(version, m.Groups["ver"].Value);
    }

    [Theory]
    [InlineData("SMLunchen/mh_firmware", true)]
    [InlineData("meshtastic/firmware", true)]
    [InlineData("nope", false)]
    [InlineData("", false)]
    [InlineData("a/b/c", false)]
    [InlineData("a b/c", false)]
    public void Repo_slug_validation(string repo, bool valid) => Assert.Equal(valid, MeshtasticdReleases.IsValidRepo(repo));

    // ── device state ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, 0, true, MeshStickState.NotPresent)]
    [InlineData(true, 28, true, MeshStickState.DriverMissing)]     // what Windows reports before CH341PAR is installed
    [InlineData(true, 0, false, MeshStickState.DriverLibraryMissing)]
    [InlineData(true, 0, true, MeshStickState.Ready)]
    public void Stick_state_classification(bool present, int problem, bool dll, MeshStickState expected)
    {
        var p = new MeshStickProbe(present, problem, present ? @"USB\VID_1A86&PID_5512\26073680" : null, null, null, dll);
        Assert.Equal(expected, p.State);
    }

    // ── driver package helpers ──────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\x\Downloads\CH341PAR.EXE", true)]
    [InlineData(@"C:\Users\x\Downloads\CH341PAR.ZIP", true)]
    [InlineData(@"C:\Users\x\Downloads\ch341par_v3.6.zip", true)]
    [InlineData(@"C:\Users\x\Downloads\CH341SER.EXE", false)]   // the serial driver — wrong package
    [InlineData(@"C:\Users\x\Downloads\CH341PAR.txt", false)]
    public void Driver_file_candidates(string path, bool candidate) => Assert.Equal(candidate, Ch341DriverSetup.IsCandidate(path));

    [Fact]
    public void Bundled_driver_package_is_complete_and_matches_the_stick()
    {
        using var s = typeof(Ch341DriverSetup).Assembly.GetManifestResourceStream(Ch341DriverSetup.BundledResourceName);
        Assert.NotNull(s);
        using var zip = new System.IO.Compression.ZipArchive(s!, System.IO.Compression.ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.Name.ToUpperInvariant()).ToHashSet();
        foreach (var n in new[] { "CH341WDM.INF", "CH341WDM.CAT", "CH341W64.SYS", "CH341DLLA64.DLL", "CH341DLL.DLL" })
            Assert.Contains(n, names);
        using var reader = new StreamReader(zip.Entries.First(e => e.Name.Equals("CH341WDM.INF", StringComparison.OrdinalIgnoreCase)).Open());
        var inf = reader.ReadToEnd();
        Assert.Contains(@"USB\VID_1A86&PID_5512", inf);          // the MeshStick's hardware id
        Assert.Contains("CH341DLLA64.DLL", inf);                  // the INF installs the DLL meshtasticd loads
        Assert.Contains("DriverVer = 04/14/2025, 2.6.2025.04", inf);
    }

    [Fact]
    public void Bundled_driver_package_extracts_flat()
    {
        var dir = Ch341DriverSetup.ExtractBundled();
        Assert.True(File.Exists(Path.Combine(dir, "CH341WDM.INF")));
        Assert.True(File.Exists(Path.Combine(dir, "CH341DLLA64.DLL")));
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
        Assert.EndsWith("CH341WDM.INF", Ch341DriverSetup.PickInf(files)!, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("CH341DLLA64.DLL", Ch341DriverSetup.FindBeside(files, Ch341DriverSetup.PickInf(files), "CH341DLLA64.DLL")!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Driver_install_script_runs_pnputil_and_places_the_dlls()
    {
        var s = Ch341DriverSetup.BuildInstallScript(@"C:\pkg\CH341WDM.INF", @"C:\pkg\CH341DLLA64.DLL", @"C:\pkg\CH341DLL.DLL", @"C:\log\driver-setup.log");
        Assert.Contains(@"pnputil.exe /add-driver 'C:\pkg\CH341WDM.INF' /install", s);
        Assert.Contains("-ne 3010 -and $code -ne 259", s);        // reboot pending / no device yet are not failures
        Assert.Contains("CH341DLLA64.DLL", s);
        Assert.Contains("syswow64", s, StringComparison.OrdinalIgnoreCase);
        var bare = Ch341DriverSetup.BuildInstallScript(@"C:\pkg\CH341WDM.INF", null, null, @"C:\log\driver-setup.log");
        Assert.DoesNotContain("Copy-Item", bare);
    }

    [Fact]
    public void Inf_pick_prefers_the_64bit_ch341par_inf()
    {
        var files = new[]
        {
            @"C:\t\CH341PAR\WIN7\X86\CH341PAR.INF",
            @"C:\t\CH341PAR\README.TXT",
            @"C:\t\CH341PAR\WIN10\X64\CH341PAR.INF",
            @"C:\t\CH341PAR\OTHER\SETUP.INF",
            @"C:\t\CH341PAR\WIN10\X64\CH341DLLA64.DLL",
        };
        Assert.Equal(@"C:\t\CH341PAR\WIN10\X64\CH341PAR.INF", Ch341DriverSetup.PickInf(files));
        Assert.Equal(@"C:\t\CH341PAR\WIN10\X64\CH341DLLA64.DLL", Ch341DriverSetup.FindDll(files));
        Assert.Null(Ch341DriverSetup.PickInf(new[] { @"C:\t\readme.txt" }));

        // WCH's real layout: older-Windows set at the root, Windows 10/11 set in "WIN 1X" — the latter wins,
        // and the DLL is taken from the same folder as the INF.
        var wch = new[]
        {
            @"C:\t\CH341WDM.INF", @"C:\t\CH341DLLA64.DLL", @"C:\t\SETUP.EXE",
            @"C:\t\WIN 1X\CH341WDM.INF", @"C:\t\WIN 1X\CH341DLLA64.DLL", @"C:\t\WIN 1X\CH341DLL.DLL",
        };
        var inf = Ch341DriverSetup.PickInf(wch);
        Assert.Equal(@"C:\t\WIN 1X\CH341WDM.INF", inf);
        Assert.Equal(@"C:\t\WIN 1X\CH341DLLA64.DLL", Ch341DriverSetup.FindBeside(wch, inf, "CH341DLLA64.DLL"));
        Assert.Equal(@"C:\t\WIN 1X\CH341DLL.DLL", Ch341DriverSetup.FindBeside(wch, inf, "CH341DLL.DLL"));
    }

    // ── small helpers ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("meshtasticd 2.8.3.d5f1619\n", "2.8.3.d5f1619")]
    [InlineData("Meshtastic native build v2.8.3", "2.8.3")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Version_output_parsing(string? output, string? expected) =>
        Assert.Equal(expected, MeshtasticdService.ParseVersionOutput(output));

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData(null, false)]
    public void Loopback_detection(string? host, bool loopback) => Assert.Equal(loopback, MeshtasticdService.IsLoopback(host));

    [Fact]
    public void Settings_defaults_for_the_meshstick()
    {
        var s = new AppSettings();
        Assert.Equal(4403, s.MeshStickApiPort);
        Assert.Equal(MeshtasticdReleases.DefaultMirrorRepo, s.MeshStickMirrorRepo);
        Assert.True(MeshtasticdReleases.IsValidRepo(s.MeshStickMirrorRepo));
    }
}
