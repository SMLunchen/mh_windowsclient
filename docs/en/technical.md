# 🏗️ Technology & Protocol

[← Handbook home](../index.md) · [🇩🇪 Deutsche Version](../de/technik.md)

## Technical overview

| Component | Technology |
|----|----|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protocol | Meshtastic Protobuf over Serial/TCP/BLE |
| Map | Mapsui 4.1 (raster) + MapLibre GL in WebView2 (vector) |
| Serialization | Google.Protobuf, System.Text.Json |
| Connection | Serial (0x94 0xC3 framing), TCP/WiFi, Bluetooth Low Energy |

**Connection types:**

| Type | Transport | Framing | Notes |
|----|----|----|----|
| USB/Serial | COM port, 115200 baud | 4-byte header (0x94 0xC3 + length) | Wake-up sequence, debug text interleaved |
| TCP/WiFi | TCP socket | 4-byte header (same as serial) | Configurable hostname/IP + port |
| Bluetooth | BLE GATT characteristics | Raw protobuf (no framing) | Direct FromRadio/ToRadio packets |

**Connection sequence:**

```
Windows Client → USB/Serial | TCP/WiFi | BLE → Meshtastic Node → LoRa → Mesh
```

1. Open connection → send wake-up sequence (Serial/TCP only) → send `want_config_id`
2. Receive `my_info`, `node_info` (×N), `channel` (×8), `config`, `config_complete_id`
3. If channels are missing (e.g. T-Deck): retry mechanism up to 3 rounds via `GetChannelRequest`
4. Ready for MeshPackets

## Serial protocol (robustness)

* Max. packet length 512 bytes (per Meshtastic spec), beyond that = corrupt → skip false start
* Protection against partial header loss (trailing 0x94 byte preserved on buffer clear)
* Stale-packet timeout: incomplete packets discarded after 5 s
* Device debug text (ANSI codes) is detected, stripped and logged separately
* Auto-recovery: sends wake-up + `want_config_id` if no protobuf packet was received for >60 s

## Error detection (device logs)

| Code | Description |
|----|----|
| TxWatchdog | Software bug while transmitting LoRa |
| NoRadio | No LoRa radio found |
| TransmitFailed | Radio TX hardware failure |
| Brownout | CPU voltage below minimum |
| SX1262Failure | SX1262 radio self-test failed |
| FlashCorruptionRecoverable | Flash corruption detected (repaired) |
| FlashCorruptionUnrecoverable | Flash corruption (unrepairable) |

## Building from source

**Requirements:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone https://github.com/SMLunchen/mh_windowsclient.git
cd mh_windowsclient
git submodule update --init --recursive
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

The EXE will be at `public\MeshhessenClient.exe`. Alternatively, run `build.bat`.
