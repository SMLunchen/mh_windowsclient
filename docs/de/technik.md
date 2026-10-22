# 🏗️ Technik & Protokoll

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](../en/technical.md)

## Technischer Überblick

| Komponente | Technologie |
|----|----|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protokoll | Meshtastic Protobuf über Serial/TCP/BLE |
| Karte | Mapsui 4.1 (Raster) + MapLibre GL in WebView2 (Vektor) |
| Serialisierung | Google.Protobuf, System.Text.Json |
| Verbindung | Serial (0x94 0xC3 Framing), TCP/WiFi, Bluetooth Low Energy |

**Verbindungstypen:**

| Typ | Transport | Framing | Besonderheiten |
|----|----|----|----|
| USB/Serial | COM-Port, 115200 baud | 4-Byte Header (0x94 0xC3 + Länge) | Wakeup-Sequenz, Debug-Text interleaved |
| TCP/WiFi | TCP-Socket | 4-Byte Header (wie Serial) | Hostname/IP + Port konfigurierbar |
| Bluetooth | BLE GATT Characteristics | Raw Protobuf (kein Framing) | Direkte FromRadio/ToRadio Pakete |

**Verbindungssequenz:**

```
Windows Client → USB/Serial | TCP/WiFi | BLE → Meshtastic Node → LoRa → Mesh
```

1. Verbindung öffnen → Wakeup-Sequenz senden (nur Serial/TCP) → `want_config_id` senden
2. `my_info`, `node_info` (×N), `channel` (×8), `config`, `config_complete_id` empfangen
3. Falls Channels fehlen (z. B. T-Deck): Retry-Mechanismus mit bis zu 3 Runden per `GetChannelRequest`
4. Bereit für MeshPackets

## Serielles Protokoll (Robustheit)

* Max. Paketlänge 512 Bytes (per Meshtastic-Spezifikation), darüber = korrupt → false Start überspringen
* Schutz vor partiellem Header-Verlust (letztes Byte 0x94 wird bei Buffer-Clear bewahrt)
* Stale-Packet-Timeout: unvollständige Pakete werden nach 5s verworfen
* Device-Debug-Text (ANSI-Codes) wird erkannt, ANSI-Codes gestrippt, separat geloggt
* Auto-Recovery: sendet Wakeup + `want_config_id` wenn >60s kein Protobuf-Paket empfangen wurde

## Fehler-Erkennung (Geräte-Logs)

| Code | Beschreibung |
|----|----|
| TxWatchdog | Software-Bug beim LoRa-Senden |
| NoRadio | Kein LoRa-Radio gefunden |
| TransmitFailed | Radio-Sendehardware-Fehler |
| Brownout | CPU-Spannung unter Minimum |
| SX1262Failure | SX1262 Radio Selbsttest fehlgeschlagen |
| FlashCorruptionRecoverable | Flash-Korruption erkannt (repariert) |
| FlashCorruptionUnrecoverable | Flash-Korruption (nicht reparierbar) |

## Aus Quellcode bauen

**Voraussetzungen:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone https://github.com/SMLunchen/mh_windowsclient.git
cd mh_windowsclient
git submodule update --init --recursive
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

EXE liegt danach unter `public\MeshhessenClient.exe`. Alternativ: `build.bat` ausführen.
