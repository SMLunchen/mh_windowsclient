using System.IO.Compression;
using System.Text;
using Google.Protobuf;
using Meshtastic.Protobufs;
using MeshhessenClient.Services;
using MeshhessenClient.Services.Firmware;
using MeshhessenClient.Services.Firmware.Dfu;

namespace MeshhessenClient.Tests;

/// <summary>
/// Pure-logic coverage of the firmware-update feature: API/manifest parsing, hardware resolution,
/// file plausibility checks, Unified-OTA / Nordic-DFU wire parsing, DFU package handling, and the
/// admin messages the wizard puts on the wire. BLE/TCP transports themselves need hardware.
/// </summary>
public class FirmwareUpdateTests
{
    // ── Catalog & releases ─────────────────────────────────────────────────

    private const string HardwareJson = """
        [
          {"hwModel":43,"hwModelSlug":"HELTEC_V3","platformioTarget":"heltec-v3","architecture":"esp32-s3","activelySupported":true,"displayName":"Heltec V3","supportLevel":1},
          {"hwModel":9,"hwModelSlug":"RAK4631","platformioTarget":"rak4631","architecture":"nrf52840","activelySupported":true,"displayName":"RAK WisBlock 4631","requiresDfu":true},
          {"hwModel":9,"hwModelSlug":"RAK4631","platformioTarget":"rak4631_eink","architecture":"nrf52840","activelySupported":true,"displayName":"RAK WisBlock 4631 (E-Ink)"},
          {"hwModel":1,"hwModelSlug":"TLORA_V2","platformioTarget":"tlora-v2","architecture":"esp32","activelySupported":false,"displayName":"LILYGO T-LoRa V2"}
        ]
        """;

    private const string ReleasesJson = """
        {"releases":{
          "stable":[{"id":"v2.7.26.54e0d8d","title":"Meshtastic Firmware 2.7.26.54e0d8d Beta","page_url":"https://github.com/meshtastic/firmware/releases/tag/v2.7.26.54e0d8d","zip_url":"https://github.com/meshtastic/firmware/releases/download/v2.7.26.54e0d8d/firmware-2.7.26.54e0d8d.json","release_notes":"notes"}],
          "alpha":[{"id":"v2.8.2.d5f1619","title":"Meshtastic Firmware 2.8.2.d5f1619 Alpha","page_url":"","zip_url":"","release_notes":""}]
        },"pullRequests":[{"id":"pr"}]}
        """;

    [Fact]
    public void Catalog_parses_hardware_and_releases()
    {
        var hw = FirmwareCatalogService.ParseDeviceHardware(HardwareJson);
        Assert.Equal(4, hw.Count);
        Assert.True(hw[0].IsEsp32);
        Assert.True(hw[1].IsNrf52);
        Assert.True(hw[1].RequiresDfu);
        Assert.Null(hw[0].RequiresBootloaderUpgradeForOta);

        var rel = FirmwareCatalogService.ParseReleases(ReleasesJson);
        Assert.Equal(2, rel.Count);
        Assert.Equal(FirmwareChannel.Stable, rel[0].Channel);
        Assert.Equal("2.7.26.54e0d8d", rel[0].Version);
        Assert.Equal(new Version(2, 7, 26), rel[0].NumericVersion);
        Assert.Equal(FirmwareChannel.Alpha, rel[1].Channel);
    }

    [Fact]
    public void ResolveHardware_disambiguates_by_pioEnv_and_falls_back()
    {
        var hw = FirmwareCatalogService.ParseDeviceHardware(HardwareJson);

        var plain = FirmwareCatalogService.ResolveHardware(hw, 9, null)!;
        Assert.Equal("rak4631", plain.PlatformioTarget);

        var eink = FirmwareCatalogService.ResolveHardware(hw, 9, "rak4631_eink")!;
        Assert.Equal("rak4631_eink", eink.PlatformioTarget);
        Assert.Contains("E-Ink", eink.DisplayName);

        // Case-insensitive match keeps the catalog row but adopts the reported spelling.
        var ci = FirmwareCatalogService.ResolveHardware(hw, 9, "RAK4631_EINK")!;
        Assert.Equal("RAK4631_EINK", ci.PlatformioTarget);

        // Unknown variant: first row of the model, target overridden by what the device reports.
        var variant = FirmwareCatalogService.ResolveHardware(hw, 9, "rak4631_nomadstar")!;
        Assert.Equal("rak4631_nomadstar", variant.PlatformioTarget);
        Assert.Equal("RAK WisBlock 4631", variant.DisplayName);

        // Unknown hwModel but known target → target-only lookup.
        Assert.Equal("heltec-v3", FirmwareCatalogService.ResolveHardware(hw, 999, "heltec-v3")!.PlatformioTarget);
        Assert.Null(FirmwareCatalogService.ResolveHardware(hw, 999, "nope"));
    }

    [Theory]
    [InlineData("HELTEC_V3", "esp32")]
    [InlineData("TBEAM", "esp32")]
    [InlineData("RAK4631", "nrf52840")]
    [InlineData("T_ECHO", "nrf52840")]
    [InlineData("HELTEC_MESH_NODE_T114", "nrf52840")]
    [InlineData("RPI_PICO", "rp2040")]
    [InlineData("", null)]
    public void GuessArchitecture_covers_common_models(string name, string? expected) =>
        Assert.Equal(expected, FirmwareCatalogService.GuessArchitecture(name));

    // ── Retriever helpers ──────────────────────────────────────────────────

    [Fact]
    public void Retriever_builds_release_urls_and_reads_manifest()
    {
        var rel = new FirmwareRelease("v2.7.26.54e0d8d", "t", "", "", "", FirmwareChannel.Stable);
        Assert.Equal("https://release.meshtastic.org/2.7.26.54e0d8d", FirmwareRetriever.ArtifactBaseUrl(rel));
        Assert.Equal("firmware-heltec-v3-2.7.26.54e0d8d-ota.zip", FirmwareRetriever.FileName("heltec-v3", rel.Version, "-ota.zip"));

        const string manifest = """
            {"version":"2.7.26.54e0d8d","mcu":"esp32s3","files":[
              {"name":"firmware-heltec-v3-2.7.26.54e0d8d.elf","md5":"aa","bytes":1},
              {"name":"firmware-heltec-v3-2.7.26.54e0d8d.bin","md5":"d7e2dba98f7ecdebdad7bc64550eeae1","bytes":2109248,"part_name":"app0"},
              {"name":"littlefs-heltec-v3-2.7.26.54e0d8d.bin","md5":"bb","bytes":2,"part_name":"spiffs"},
              {"name":"mt-esp32s3-ota.bin","md5":"cc","bytes":3,"part_name":"app1"}]}
            """;
        var app0 = FirmwareRetriever.FindOtaEntry(manifest)!;
        Assert.Equal("firmware-heltec-v3-2.7.26.54e0d8d.bin", app0.Name);
        Assert.Equal(2109248, app0.Bytes);
        Assert.Equal("esp32s3", FirmwareRetriever.ManifestMcu(manifest));
        Assert.Null(FirmwareRetriever.FindOtaEntry("""{"files":[{"name":"x.bin","part_name":"spiffs"}]}"""));
        Assert.Null(FirmwareRetriever.FindOtaEntry("not json"));
    }

    [Fact]
    public void Retriever_parses_release_target_list_and_zip_arch_swap()
    {
        var targets = FirmwareRetriever.ParseReleaseTargets("""{"version":"2.7.26","targets":[{"board":"rak4631","platform":"nrf52840"},{"board":"heltec-v3","platform":"esp32s3"}]}""")!;
        Assert.Contains("rak4631", targets);
        Assert.Contains("HELTEC-V3", targets); // case-insensitive
        Assert.Null(FirmwareRetriever.ParseReleaseTargets("{}"));

        Assert.Equal("https://x/firmware-nrf52840-2.5.0.zip",
            FirmwareRetriever.ResolveZipUrl("https://x/firmware-esp32-2.5.0.zip", "nrf52840"));
    }

    // ── File inspector ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("firmware-heltec-v3-2.7.26.54e0d8d.bin", "heltec-v3", ".bin", true)]
    [InlineData("firmware-heltec-v3-2.7.26.54e0d8d-update.bin", "heltec-v3", ".bin", true)]
    [InlineData("firmware-tlora-v2-1-1_6-2.7.14.bin", "tlora-v2", ".bin", false)] // sibling variant must not match
    [InlineData("firmware-tlora-v2-2.7.14.bin", "tlora-v2", ".bin", true)]
    [InlineData("firmware-heltec-v3-2.7.26.factory.bin", "heltec-v3", ".bin", false)]
    [InlineData("littlefs-heltec-v3-2.7.26.bin", "heltec-v3", ".bin", false)]
    [InlineData("firmware-rak4631-2.7.26.54e0d8d-ota.zip", "rak4631", "-ota.zip", true)]
    [InlineData("firmware-rak4631_eink-2.7.26-ota.zip", "rak4631", "-ota.zip", false)]
    [InlineData("firmware-rak4631-2.7.26.uf2", "rak4631", ".uf2", true)]
    [InlineData("sub/dir/firmware-rak4631-2.7.26.uf2", "rak4631", ".uf2", true)]
    [InlineData("firmware-rak4631-2.7.26.uf2", "", ".uf2", false)]
    public void Filename_validation_matches_android(string name, string target, string ext, bool expected) =>
        Assert.Equal(expected, FirmwareFileInspector.IsValidFirmwareFileName(name, target, ext));

    [Fact]
    public void Filename_version_extraction()
    {
        Assert.Equal("2.7.26.54e0d8d", FirmwareFileInspector.VersionFromFileName("firmware-heltec-v3-2.7.26.54e0d8d.bin"));
        Assert.Equal("2.7.26.54e0d8d", FirmwareFileInspector.VersionFromFileName(@"C:\x\firmware-rak4631-2.7.26.54e0d8d-ota.zip"));
        Assert.Null(FirmwareFileInspector.VersionFromFileName("random.bin"));
    }

    [Fact]
    public void Uf2_header_family_and_address()
    {
        var block = new byte[512];
        BitConverter.GetBytes(FirmwareFileInspector.Uf2MagicStart0).CopyTo(block, 0);
        BitConverter.GetBytes(FirmwareFileInspector.Uf2MagicStart1).CopyTo(block, 4);
        BitConverter.GetBytes(FirmwareFileInspector.Uf2FlagFamilyId).CopyTo(block, 8);
        BitConverter.GetBytes(0x26000u).CopyTo(block, 12);
        BitConverter.GetBytes(FirmwareFileInspector.Uf2FamilyNrf52840).CopyTo(block, 28);

        Assert.True(FirmwareFileInspector.IsUf2(block));
        Assert.Equal(FirmwareFileInspector.Uf2FamilyNrf52840, FirmwareFileInspector.Uf2FamilyId(block));
        Assert.Equal(0x26000u, FirmwareFileInspector.Uf2FirstTargetAddress(block));

        BitConverter.GetBytes(0u).CopyTo(block, 8); // family flag cleared → offset 28 is a file size
        Assert.Null(FirmwareFileInspector.Uf2FamilyId(block));
        Assert.Null(FirmwareFileInspector.Uf2FamilyId(new byte[100]));

        Assert.Contains(FirmwareFileInspector.Uf2FamilyNrf52840, FirmwareFileInspector.ExpectedUf2Families("nrf52840"));
        Assert.Contains(FirmwareFileInspector.Uf2FamilyRp2040, FirmwareFileInspector.ExpectedUf2Families("rp2040"));
        Assert.Empty(FirmwareFileInspector.ExpectedUf2Families("esp32-s3"));
    }

    private static byte[] FakeEspAppImage(ushort chipId, string version, string project)
    {
        var img = new byte[1024];
        img[0] = FirmwareFileInspector.EspImageMagic;
        img[1] = 3;
        img[12] = (byte)(chipId & 0xFF);
        img[13] = (byte)(chipId >> 8);
        BitConverter.GetBytes(FirmwareFileInspector.EspAppDescMagic).CopyTo(img, 0x20);
        Encoding.ASCII.GetBytes(version).CopyTo(img, 0x20 + 16);
        Encoding.ASCII.GetBytes(project).CopyTo(img, 0x20 + 48);
        return img;
    }

    [Fact]
    public void Esp_image_header_chip_and_app_descriptor()
    {
        var img = FakeEspAppImage(0x0009, "2.7.26.54e0d8d", "firmware");
        Assert.Equal(0x0009, FirmwareFileInspector.EspImageChipId(img));
        Assert.True(FirmwareFileInspector.HasEspAppDescriptor(img));
        Assert.Equal("2.7.26.54e0d8d", FirmwareFileInspector.EspAppVersion(img));
        Assert.Equal("firmware", FirmwareFileInspector.EspAppProjectName(img));
        Assert.Equal(0x0009, FirmwareFileInspector.ExpectedEspChipId("esp32-s3"));
        Assert.Equal(0x0000, FirmwareFileInspector.ExpectedEspChipId("esp32"));
        Assert.Null(FirmwareFileInspector.ExpectedEspChipId("nrf52840"));
        Assert.Equal("ESP32-S3", FirmwareFileInspector.EspChipName(9));

        var bootloaderLike = new byte[1024];
        bootloaderLike[0] = 0xE9;
        Assert.False(FirmwareFileInspector.HasEspAppDescriptor(bootloaderLike));
        Assert.Null(FirmwareFileInspector.EspImageChipId(new byte[] { 0x00, 0x01 }));
    }

    [Fact]
    public void Inspect_flags_chip_mismatch_and_factory_images()
    {
        var hw = new DeviceHardware(43, "HELTEC_V3", "heltec-v3", "esp32-s3", true, "Heltec V3", null, null, null);
        var dir = Directory.CreateTempSubdirectory("fwtest");
        try
        {
            var good = Path.Combine(dir.FullName, "firmware-heltec-v3-2.7.26.54e0d8d.bin");
            File.WriteAllBytes(good, FakeEspAppImage(0x0009, "2.7.26.54e0d8d", "firmware"));
            var r = FirmwareFileInspector.Inspect(good, hw, FirmwareUpdateMethod.Esp32Ble);
            Assert.False(r.HasErrors);
            Assert.Equal("2.7.26.54e0d8d", r.EmbeddedVersion);
            Assert.Contains(r.Items, i => i.Key == "StrFwCheckChipOk");

            var wrongChip = Path.Combine(dir.FullName, "firmware-heltec-v3-2.7.26.bin");
            File.WriteAllBytes(wrongChip, FakeEspAppImage(0x0000, "2.7.26", "firmware"));
            var r2 = FirmwareFileInspector.Inspect(wrongChip, hw, FirmwareUpdateMethod.Esp32Wifi);
            Assert.True(r2.HasErrors);
            Assert.Contains(r2.Items, i => i.Key == "StrFwCheckChipMismatch");

            var factory = Path.Combine(dir.FullName, "firmware-heltec-v3-2.7.26.factory.bin");
            File.WriteAllBytes(factory, FakeEspAppImage(0x0009, "2.7.26", "firmware"));
            var r3 = FirmwareFileInspector.Inspect(factory, hw, FirmwareUpdateMethod.Esp32Wifi);
            Assert.Contains(r3.Items, i => i.Key == "StrFwCheckFactoryImage" && i.Level == FileCheckLevel.Error);

            var wrongExt = Path.Combine(dir.FullName, "firmware-heltec-v3-2.7.26.uf2");
            File.WriteAllBytes(wrongExt, new byte[10]);
            var r4 = FirmwareFileInspector.Inspect(wrongExt, hw, FirmwareUpdateMethod.Esp32Wifi);
            Assert.Contains(r4.Items, i => i.Key == "StrFwCheckWrongExt");
        }
        finally { dir.Delete(true); }
    }

    // ── Unified OTA ────────────────────────────────────────────────────────

    [Fact]
    public void OtaResponse_parse_covers_loader_vocabulary()
    {
        Assert.IsType<OtaResponse.Ok>(OtaResponse.Parse("OK\n"));
        var rich = Assert.IsType<OtaResponse.Ok>(OtaResponse.Parse("OK 1 2.3.4 45 v2.3.4-abc123"));
        Assert.Equal("2.3.4", rich.FwVersion);
        Assert.Equal(45, rich.RebootCount);
        Assert.IsType<OtaResponse.Erasing>(OtaResponse.Parse("ERASING\r\n"));
        Assert.IsType<OtaResponse.Ack>(OtaResponse.Parse("ACK"));
        Assert.Equal("Hash Rejected", Assert.IsType<OtaResponse.Error>(OtaResponse.Parse("ERR Hash Rejected")).Message);
        Assert.Equal("Unknown error", Assert.IsType<OtaResponse.Error>(OtaResponse.Parse("ERR")).Message);
        Assert.StartsWith("Unknown response", Assert.IsType<OtaResponse.Error>(OtaResponse.Parse("WAT")).Message);
        Assert.Equal("OTA 1234 abcd\n", OtaResponse.StartCommand(1234, "abcd"));
    }

    [Fact]
    public void Ota_confirmation_detection_mirrors_firmware_strings()
    {
        Assert.True(Esp32OtaUpdater.IsOtaStatusNotification("Rebooting to BLE OTA"));
        Assert.True(Esp32OtaUpdater.IsOtaStatusNotification("Cannot start OTA: Cannot find OTA Loader partition."));
        Assert.False(Esp32OtaUpdater.IsOtaStatusNotification("QUOTA exceeded"));
        Assert.False(Esp32OtaUpdater.IsOtaStatusNotification("ROTATE now"));
        Assert.False(Esp32OtaUpdater.IsOtaStatusNotification(""));
        Assert.True(Esp32OtaUpdater.IsOtaConfirmation("Rebooting to WiFi OTA", Esp32OtaUpdater.ModeWifi));
        Assert.False(Esp32OtaUpdater.IsOtaConfirmation("Rebooting to WiFi OTA", Esp32OtaUpdater.ModeBle));
        Assert.False(Esp32OtaUpdater.IsOtaConfirmation("OTA Loader does not support BLE", Esp32OtaUpdater.ModeBle));
    }

    [Fact]
    public void Wifi_discovery_beacon_pattern()
    {
        Assert.True(WifiOtaDiscovery.IsBeacon(Encoding.ASCII.GetBytes("Meshtastic_1A2B 2.7.26\n")));
        Assert.False(WifiOtaDiscovery.IsBeacon(Encoding.ASCII.GetBytes("Meshtastic_1A2 2.7.26")));
        Assert.False(WifiOtaDiscovery.IsBeacon(Encoding.ASCII.GetBytes("hello")));
    }

    // ── BLE helpers ────────────────────────────────────────────────────────

    [Fact]
    public void Ble_address_plus_one_wraps_and_formats()
    {
        Assert.Equal(0xAABBCCDDEE00UL, BleAddress.PlusOne(0xAABBCCDDEEFFUL));
        Assert.Equal(0xAABBCCDDEE11UL, BleAddress.PlusOne(0xAABBCCDDEE10UL));
        Assert.Equal("AA:BB:CC:DD:EE:FF", BleAddress.Format(0xAABBCCDDEEFFUL));
    }

    // ── Nordic DFU wire ────────────────────────────────────────────────────

    [Fact]
    public void SecureDfu_response_parsing()
    {
        var sel = Assert.IsType<SecureDfuResponse.SelectResult>(SecureDfuResponse.Parse(new byte[]
            { 0x60, 0x06, 0x01, 0x00, 0x10, 0x00, 0x00, 0x34, 0x12, 0x00, 0x00, 0x78, 0x56, 0x34, 0x12 }));
        Assert.Equal(0x1000, sel.MaxSize);
        Assert.Equal(0x1234, sel.Offset);
        Assert.Equal(0x12345678u, sel.Crc32);

        var crc = Assert.IsType<SecureDfuResponse.ChecksumResult>(SecureDfuResponse.Parse(new byte[]
            { 0x60, 0x03, 0x01, 0x10, 0x00, 0x00, 0x00, 0xEF, 0xBE, 0xAD, 0xDE }));
        Assert.Equal(16, crc.Offset);
        Assert.Equal(0xDEADBEEFu, crc.Crc32);

        Assert.Equal(0x04, Assert.IsType<SecureDfuResponse.Success>(SecureDfuResponse.Parse(new byte[] { 0x60, 0x04, 0x01 })).Opcode);
        var fail = Assert.IsType<SecureDfuResponse.Failure>(SecureDfuResponse.Parse(new byte[] { 0x60, 0x01, 0x0B, 0x05 }));
        Assert.Equal(0x0B, fail.ResultCode);
        Assert.Equal((byte)0x05, fail.ExtendedError);
        Assert.Contains("FW version failure", fail.ToString());
        Assert.IsType<SecureDfuResponse.Unknown>(SecureDfuResponse.Parse(new byte[] { 0x11, 0x00 }));
        Assert.IsType<SecureDfuResponse.Failure>(SecureDfuResponse.Parse(new byte[] { 0x60, 0x06, 0x01, 0x00 })); // short SELECT
    }

    [Fact]
    public void LegacyDfu_response_parsing_and_payloads()
    {
        Assert.Equal(0x01, Assert.IsType<LegacyDfuResponse.Success>(LegacyDfuResponse.Parse(new byte[] { 0x10, 0x01, 0x01 })).RequestOpcode);
        var fail = Assert.IsType<LegacyDfuResponse.Failure>(LegacyDfuResponse.Parse(new byte[] { 0x10, 0x01, 0x02 }));
        Assert.Equal(LegacyDfuStatus.InvalidState, fail.Status);
        Assert.Contains("INVALID_STATE", fail.ToString());
        Assert.Equal(0x12345678u, Assert.IsType<LegacyDfuResponse.PacketReceipt>(LegacyDfuResponse.Parse(new byte[] { 0x11, 0x78, 0x56, 0x34, 0x12 })).BytesReceived);
        Assert.IsType<LegacyDfuResponse.Unknown>(LegacyDfuResponse.Parse(Array.Empty<byte>()));
        Assert.IsType<LegacyDfuResponse.Unknown>(LegacyDfuResponse.Parse(new byte[] { 0x11, 0x01 }));

        var sizes = LegacyDfuPayloads.ImageSizes(0x12345);
        Assert.Equal(12, sizes.Length);
        Assert.Equal(0, BitConverter.ToInt32(sizes, 0));
        Assert.Equal(0, BitConverter.ToInt32(sizes, 4));
        Assert.Equal(0x12345, BitConverter.ToInt32(sizes, 8));
        Assert.Equal(new byte[] { 0x08, 0x0A, 0x00 }, LegacyDfuPayloads.PrnRequest(10));
    }

    [Fact]
    public void Crc32_matches_reference_vector()
    {
        // IEEE 802.3 CRC-32 of "123456789" is 0xCBF43926.
        Assert.Equal(0xCBF43926u, DfuCrc32.Compute(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0u, DfuCrc32.Compute(ReadOnlySpan<byte>.Empty));
    }

    private static byte[] MakeDfuZip(string manifest, params (string Name, byte[] Data)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open())) w.Write(manifest);
            foreach (var (name, data) in files)
                using (var s = zip.CreateEntry(name).Open()) s.Write(data);
        }
        return ms.ToArray();
    }

    [Fact]
    public void DfuPackage_parses_application_and_rejects_bootloader_only()
    {
        var init = new byte[] { 1, 2, 3, 4 };
        var fw = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
        var pkg = DfuPackage.FromBytes(MakeDfuZip(
            """{"manifest":{"application":{"bin_file":"firmware.bin","dat_file":"firmware.dat"}}}""",
            ("firmware.bin", fw), ("firmware.dat", init)));
        Assert.Equal(init, pkg.InitPacket);
        Assert.Equal(fw, pkg.Firmware);
        Assert.Equal(1, pkg.ImageCount);

        var ex = Assert.Throws<DfuException>(() => DfuPackage.FromBytes(MakeDfuZip(
            """{"manifest":{"bootloader":{"bin_file":"bl.bin","dat_file":"bl.dat"}}}""",
            ("bl.bin", fw), ("bl.dat", init))));
        Assert.Contains("no 'application' image", ex.Message);

        Assert.Throws<DfuException>(() => DfuPackage.FromBytes(MakeDfuZip(
            """{"manifest":{"application":{"bin_file":"missing.bin","dat_file":"firmware.dat"}}}""",
            ("firmware.dat", init))));

        using var noManifest = new MemoryStream();
        using (var zip = new ZipArchive(noManifest, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("x.bin");
        Assert.Throws<DfuException>(() => DfuPackage.FromBytes(noManifest.ToArray()));
    }

    // ── UF2 drive info ─────────────────────────────────────────────────────

    [Fact]
    public void Uf2_info_file_parsing()
    {
        var d = Uf2UsbFlasher.Parse(@"E:\", "UF2 Bootloader 0.9.2 lib/nrfx (v2.0.0) lib/tinyusb (0.10.1-293-gaf8e5a90)\r\nModel: WisBlock RAK4631 Board\r\nBoard-ID: RAK4631\r\nSoftDevice: S140 version 6.1.1\r\nDate: Jun 24 2022\r\n");
        Assert.Equal("RAK4631", d.BoardId);
        Assert.Equal("0.9.2", d.BootloaderVersion);
        Assert.Equal("S140 version 6.1.1", d.SoftDevice);
        Assert.Null(Uf2UsbFlasher.Parse(@"F:\", "Model: Pico\nBoard-ID: RPI-RP2\n").SoftDevice);
    }

    // ── Admin messages on the wire ─────────────────────────────────────────

    [Fact]
    public async Task OtaRequest_and_EnterDfu_are_sent_as_admin_messages()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        SeedSessionKey(svc);
        var hash = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        await svc.SendOtaRequestAsync(Esp32OtaUpdater.ModeWifi, hash);
        var admin = fake.Written.Select(DecodeAdmin).Last(a => a?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.OtaRequest)!;
        Assert.Equal(OTAMode.OtaWifi, admin.OtaRequest.RebootOtaMode);
        Assert.Equal(hash, admin.OtaRequest.OtaHash.ToByteArray());
        Assert.False(admin.SessionPasskey.IsEmpty); // write-class admin verb carries the passkey

        await svc.EnterDfuModeAsync();
        Assert.Contains(fake.Written.Select(DecodeAdmin),
            a => a?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.EnterDfuModeRequest && a.EnterDfuModeRequest);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.SendOtaRequestAsync(1, new byte[5]));
    }

    [Fact]
    public void ClientNotification_is_surfaced_as_event()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        string? got = null;
        svc.ClientNotificationReceived += (_, m) => got = m;

        var fr = new FromRadio { ClientNotification = new ClientNotification { Message = "Rebooting to BLE OTA", Level = LogRecord.Types.Level.Warning } };
        fake.Receive(Frame(fr));
        Assert.Equal("Rebooting to BLE OTA", got);
    }

    [Fact]
    public void Handshake_metadata_sets_firmware_and_hardware_model()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x11, PioEnv = "heltec-v3" } }));
        fake.Receive(Frame(new FromRadio { Metadata = new DeviceMetadata { FirmwareVersion = "2.7.26.54e0d8d", HwModel = HardwareModel.HeltecV3, HasWifi = true } }));

        Assert.Equal("2.7.26.54e0d8d", svc.GetFirmwareVersion());
        Assert.Equal(HardwareModel.HeltecV3, svc.GetMyHardwareModel());
        Assert.True(svc.GetDeviceMetadata()!.HasWifi);

        // A repeated my_info (e.g. a second want_config stage) must not wipe what metadata taught us.
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x11, PioEnv = "heltec-v3" } }));
        Assert.Equal("2.7.26.54e0d8d", svc.GetFirmwareVersion());

        // Own NodeInfo from the node database also carries the hardware model.
        var fake2 = new FakeConnectionService(ConnectionType.Tcp);
        var svc2 = new MeshtasticProtocolService(fake2);
        fake2.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x22 } }));
        fake2.Receive(Frame(new FromRadio { NodeInfo = new Meshtastic.Protobufs.NodeInfo { Num = 0x22, User = new User { Id = "!00000022", LongName = "me", HwModel = HardwareModel.Rak4631 } } }));
        Assert.Equal(HardwareModel.Rak4631, svc2.GetMyHardwareModel());
    }

    [Fact]
    public void Snapshot_config_and_backup_json_reflect_handshake()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x12345678, PioEnv = "heltec-v3" } }));
        fake.Receive(Frame(new FromRadio { Config = new Config { Network = new Config.Types.NetworkConfig { WifiEnabled = true, WifiSsid = "mesh" } } }));
        fake.Receive(Frame(new FromRadio { Channel = new Channel { Index = 0, Role = Channel.Types.Role.Primary, Settings = new ChannelSettings { Name = "LongFast" } } }));

        Assert.Equal("heltec-v3", svc.GetMyPioEnv());
        var net = svc.GetSnapshotConfig(Config.PayloadVariantOneofCase.Network)!.Network;
        Assert.True(net.WifiEnabled);
        Assert.Equal("mesh", net.WifiSsid);
        Assert.Null(svc.GetSnapshotConfig(Config.PayloadVariantOneofCase.Lora));

        var json = svc.BuildConfigBackupJson();
        Assert.Contains("\"nodeId\": \"!12345678\"", json);
        Assert.Contains("\"wifiSsid\": \"mesh\"", json);
        Assert.Contains("LongFast", json);
        Assert.Contains("\"pioEnv\": \"heltec-v3\"", json);
    }

    [Fact]
    public async Task Config_backup_roundtrips_and_restores_in_one_transaction()
    {
        var fake = new FakeConnectionService(ConnectionType.Tcp);
        var svc = new MeshtasticProtocolService(fake);
        fake.Receive(Frame(new FromRadio { MyInfo = new MyNodeInfo { MyNodeNum = 0x12345678 } }));
        fake.Receive(Frame(new FromRadio { NodeInfo = new Meshtastic.Protobufs.NodeInfo { Num = 0x12345678, User = new User { Id = "!12345678", LongName = "Hütte Nord", ShortName = "HÜNO", HwModel = HardwareModel.HeltecV3 } } }));
        fake.Receive(Frame(new FromRadio { Config = new Config { Lora = new Config.Types.LoRaConfig { Region = Config.Types.LoRaConfig.Types.RegionCode.Eu868, ModemPreset = Config.Types.LoRaConfig.Types.ModemPreset.ShortSlow, HopLimit = 5 } } }));
        fake.Receive(Frame(new FromRadio { Config = new Config { Security = new Config.Types.SecurityConfig { PrivateKey = ByteString.CopyFrom(new byte[32]) } } }));
        fake.Receive(Frame(new FromRadio { ModuleConfig = new ModuleConfig { Mqtt = new ModuleConfig.Types.MQTTConfig { Enabled = true, Address = "mqtt.example" } } }));
        fake.Receive(Frame(new FromRadio { Channel = new Channel { Index = 0, Role = Channel.Types.Role.Primary, Settings = new ChannelSettings { Name = "LongFast", Psk = ByteString.CopyFrom(1) } } }));
        fake.Receive(Frame(new FromRadio { Channel = new Channel { Index = 1, Role = Channel.Types.Role.Secondary, Settings = new ChannelSettings { Name = "Hessen", Psk = ByteString.CopyFrom(new byte[16]) } } }));

        var json = svc.BuildConfigBackupJson();
        var backup = ConfigBackupFile.Parse(json);
        Assert.Equal("12345678", backup.NodeIdHex);
        Assert.Equal(2, backup.Configs.Count);
        Assert.Single(backup.ModuleConfigs);
        Assert.Equal(2, backup.Channels.Count);
        Assert.Equal("Hütte Nord", backup.Owner!.LongName);
        Assert.True(backup.HasSecurity);
        Assert.Equal(5u, backup.Configs.First(c => c.PayloadVariantCase == Config.PayloadVariantOneofCase.Lora).Lora.HopLimit);
        Assert.Contains("Hessen", ConfigBackupFile.Describe(backup));
        Assert.Throws<InvalidDataException>(() => ConfigBackupFile.Parse("{\"foo\": 1}"));

        // Restore onto a *different* node: security must be skipped, everything else written inside begin/commit.
        var target = new FakeConnectionService(ConnectionType.Tcp);
        var tsvc = new MeshtasticProtocolService(target);
        SeedSessionKey(tsvc);
        await tsvc.RestoreConfigBackupAsync(backup, includeSecurity: false);
        var admins = target.Written.Select(DecodeAdmin).Where(a => a != null).Select(a => a!).ToList();
        Assert.Equal(AdminMessage.PayloadVariantOneofCase.BeginEditSettings, admins.First().PayloadVariantCase);
        Assert.Equal(AdminMessage.PayloadVariantOneofCase.CommitEditSettings, admins.Last().PayloadVariantCase);
        Assert.Contains(admins, a => a.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.SetOwner && a.SetOwner.LongName == "Hütte Nord");
        Assert.Contains(admins, a => a.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.SetConfig && a.SetConfig.PayloadVariantCase == Config.PayloadVariantOneofCase.Lora && a.SetConfig.Lora.HopLimit == 5);
        Assert.DoesNotContain(admins, a => a.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.SetConfig && a.SetConfig.PayloadVariantCase == Config.PayloadVariantOneofCase.Security);
        Assert.Contains(admins, a => a.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.SetModuleConfig && a.SetModuleConfig.Mqtt.Address == "mqtt.example");
        Assert.Equal(2, admins.Count(a => a.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.SetChannel));
        Assert.Contains(admins, a => a.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.SetChannel && a.SetChannel.Index == 1 && a.SetChannel.Settings.Name == "Hessen");

        // Same node: security goes along.
        var target2 = new FakeConnectionService(ConnectionType.Tcp);
        var tsvc2 = new MeshtasticProtocolService(target2);
        SeedSessionKey(tsvc2);
        await tsvc2.RestoreConfigBackupAsync(backup, includeSecurity: true);
        Assert.Contains(target2.Written.Select(DecodeAdmin), a => a?.PayloadVariantCase == AdminMessage.PayloadVariantOneofCase.SetConfig && a.SetConfig.PayloadVariantCase == Config.PayloadVariantOneofCase.Security);
    }

    private static void SeedSessionKey(MeshtasticProtocolService svc) =>
        typeof(MeshtasticProtocolService)
            .GetField("_sessionPasskey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(svc, new byte[] { 1, 2, 3, 4 });

    private static byte[] Frame(FromRadio fr)
    {
        var payload = fr.ToByteArray();
        var frame = new byte[4 + payload.Length];
        frame[0] = 0x94;
        frame[1] = 0xC3;
        frame[2] = (byte)(payload.Length >> 8);
        frame[3] = (byte)(payload.Length & 0xFF);
        Array.Copy(payload, 0, frame, 4, payload.Length);
        return frame;
    }

    private static AdminMessage? DecodeAdmin(byte[] frame)
    {
        try
        {
            if (frame.Length < 4 || frame[0] != 0x94 || frame[1] != 0xC3) return null;
            var toRadio = ToRadio.Parser.ParseFrom(frame.AsSpan(4).ToArray());
            if (toRadio.PayloadVariantCase != ToRadio.PayloadVariantOneofCase.Packet) return null;
            if (toRadio.Packet.Decoded?.Portnum != PortNum.AdminApp) return null;
            return AdminMessage.Parser.ParseFrom(toRadio.Packet.Decoded.Payload);
        }
        catch { return null; }
    }
}
