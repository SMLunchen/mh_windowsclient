# Meshhessen Client

Ein **offline-fähiger, nativer Windows-Client** für Meshtastic-Geräte mit USB/serieller Verbindung – entwickelt von und für die [Meshhessen Community](https://www.meshhessen.de).

 ![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
 ![.NET](https://img.shields.io/badge/.NET-8.0-purple)
 ![Status](https://img.shields.io/badge/Status-v1.0--Beta-yellow)


## 🚀 Schnellstart

### .NET SDK installieren



1. Download: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.23-windows-x64-installer
2. Installer ausführen
3. **Download:** Neueste `MeshhessenClient.exe` aus den [Releases](../../releases) herunterladen
4. **Gerät anschließen:** Meshtastic-Device per USB anstecken (nur Serielle Verbindung)
5. **Starten:** Doppelklick auf `MeshhessenClient.exe` – keine Installation nötig
6. **Verbinden:** Verbindungstyp wählen (Serial, TCP oder Bluetooth) → „Verbinden" klicken
7. **Loslegen:** 3–10 Sekunden warten bis Kanäle geladen sind, dann Nachrichten senden

> Die App ist vollständig offline-fähig. Keine Cloud, keine Registrierung, keine Telemetrie zum Entwickler.


## ✨ Features

* **Nachrichten** senden und empfangen (Broadcast & Direct Messages)
* **Multi-Channel** – alle Kanäle deines Geräts automatisch geladen
* **Multi-Verbindung** – USB/Serial, TCP/WiFi und Bluetooth (BLE)
* **Offline-Karte** mit OSM-Tiles und Node-Positionen als Pins
* **Direktnachrichten (DMs)** mit separatem Chat-Fenster
* **Knoten-Übersicht** – alle Nodes im Mesh mit SNR, Batterie, Entfernung
* **Node-Markierungen** – Nodes farblich markieren und mit Notizen versehen
* **Dark Mode** & ModernWPF Fluent-Design
* **Automatisches Logging** aller Nachrichten (Kanal- und DM-Logs)
* **Debug-Tab** mit Live-Log fürs Troubleshooting


## 💬 Die Meshhessen Community

Der Meshhessen Client ist ein Gemeinschaftsprojekt der Meshtastic-Community in Hessen. Unser regionales LoRa-Mesh wächst stetig – mach mit!

* 🌐 **Website:** [www.meshhessen.de](https://www.meshhessen.de)
* 📡 **Netz:** Wachsendes Mesh-Netzwerk in Hessen und Umgebung, Airtime ist kein All-you-can-eat. → Short Slow! ;)
* 🤝 **Mitmachen:** Eigenen Node aufstellen, Reichweite erweitern, Community wachsen lassen


## 📸 Screenshots

*(folgen in Kürze)*


## ⚠️ Bekannte Einschränkungen

* Keine persistente Message-History (Neustart = leere UI, Logs bleiben, das was vom Node geladen wird bleibt)
* Kanal-Bearbeitung nur Anzeige, noch kein Speichern, Debug Einstellungen bleiben nicht erhalten.
* Getestet mit Firmware 2.x
* T-Deck: Channels werden nicht immer in der Config-Sequenz mitgesendet (Retry-Workaround aktiv) - Das T-Deck ist fast schon mit sich selbst überfordert. Daher dauert da immer alles etwas länger…


## 🗺️ Offline-Karte einrichten



1. Einstellungen öffnen
2. **„Tiles herunterladen"** klicken
3. Bereich (Bounding Box) und Zoom-Level eingeben – z.B. Hessen Zoom 1–14
4. Download starten (OSM Fair-Use: max. \~2 req/s)
5. Tiles werden unter `maptiles/` gespeichert und sind dauerhaft offline verfügbar, die Tiles sind Portabel und können z.B. per USB von Gerät zu Gerät übertragen werden.
6. Tab **„🗺️ Karte"** öffnen

Rechtsklick auf die Karte → eigenen Standort setzen. Node-Pins erscheinen automatisch sobald GPS-Daten empfangen werden.

Klick auf einen Node → Entfernungsanzeige (anhand von eigenem Standort)

Rechtsklick auf einen Node → Fabe setzen oder DM schicken


## 📝 Nachrichten-Logs

Alle Nachrichten werden automatisch geloggt unter `[EXE-Verzeichnis]/logs/`:

* `Channel_0_Primary.log` – Kanalverläufe
* `DM_DEADBEEF_Alice.log` – Direktnachrichten


## 🏗️ Technischer Überblick

| Komponente | Technologie |
|----|----|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protokoll | Meshtastic Protobuf über Serial/TCP/BLE |
| Karte | Mapsui 4.1 + lokale OSM-Tiles |
| Serialisierung | Google.Protobuf |
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
3. Falls Channels fehlen (z.B. T-Deck): Retry-Mechanismus mit bis zu 3 Runden per `GetChannelRequest`
4. Bereit für MeshPackets

**Serielles Protokoll (Robustheit):**

* Max. Paketlänge 512 Bytes (per Meshtastic-Spezifikation), darüber = korrupt → false Start überspringen
* Schutz vor partiellem Header-Verlust (letztes Byte 0x94 wird bei Buffer-Clear bewahrt)
* Stale-Packet-Timeout: unvollständige Pakete werden nach 5s verworfen
* Device-Debug-Text (ANSI-Codes) wird erkannt, ANSI-Codes gestrippt, und separat geloggt
* Auto-Recovery: sendet Wakeup + `want_config_id` wenn >60s kein Protobuf-Paket empfangen wurde

**Fehler-Erkennung:**

Das Gerät sendet serielle Debug-Ausgaben (z.B. `DEBUG | ... [RadioIf] Lora RX ...`) zwischen den Protobuf-Paketen. Der Client erkennt automatisch kritische Fehlermeldungen des Geräts und loggt sie immer – auch wenn das Device-Log deaktiviert ist:

| Code | Beschreibung |
|----|----|
| TxWatchdog | Software-Bug beim LoRa-Senden |
| NoRadio | Kein LoRa-Radio gefunden |
| TransmitFailed | Radio-Sendehardware-Fehler |
| Brownout | CPU-Spannung unter Minimum |
| SX1262Failure | SX1262 Radio Selbsttest fehlgeschlagen |
| FlashCorruptionRecoverable | Flash-Korruption erkannt (repariert) |
| FlashCorruptionUnrecoverable | Flash-Korruption (nicht reparierbar) |

**Debug-Einstellungen (unter Einstellungen → Debug):**

| Option | Beschreibung |
|----|----|
| Nachrichten-Debug | Detaillierte Infos über empfangene/gefilterte Nachrichten |
| Serielle Daten-Debug | Hex-Dump aller Protobuf-Pakete (sehr ausführlich) |
| Device-Log | Serielle Debug-Ausgabe des Geräts (DEBUG/INFO Zeilen) |
| Bluetooth-Debug | BLE-spezifische Debug-Informationen |

## 🔧 Aus Quellcode bauen

**Voraussetzungen:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone <repo-url>
cd mh_windowsclient
dotnet restore
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

EXE liegt danach unter `public\MeshhessenClient.exe`. Alternativ: `build.bat` ausführen.


## 🙏 Credits

* **[Meshtastic Project](https://meshtastic.org)** – Firmware & Protokoll-Spezifikation
* **[ModernWPF](https://github.com/Kinnara/ModernWpf)** – Fluent UI für WPF
* **[Mapsui](https://mapsui.com)** – Offline-Karte
* **[Meshhessen Community](https://www.meshhessen.de)** – Für das Netzwerk und die Inspiration


**Made with ❤️ by the Meshhessen Community** · [www.meshhessen.de](https://www.meshhessen.de)