# Meshhessen Client – Windows-Client für Meshtastic-Geräte (WPF · .NET 8)

Der **Meshhessen Client** ist ein **kostenloser, nativer Windows-Client für Meshtastic-Geräte** (WPF/.NET 8) für Windows 10/11. Verbinde dein **Meshtastic-Gerät** (LILYGO T-Beam, T-Deck, RAK4631, Heltec u.a.) per **USB/Serial, TCP/WiFi oder Bluetooth** mit deinem Windows-PC – vollständig **offline-fähig**, keine Cloud, keine Installation. Entwickelt von und für die [Meshhessen Community](https://www.meshhessen.de).

![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Status](https://img.shields.io/badge/Status-v1.5.5-yellow)
![License](https://img.shields.io/github/license/SMLunchen/mh_windowsclient)
![Stars](https://img.shields.io/github/stars/SMLunchen/mh_windowsclient)

> **English summary:** Free Windows app for Meshtastic devices – offline map (OSM/OpenTopo), telemetry, traceroute, PKI decryption, USB/Serial/TCP/BLE support. No installation, no cloud. By the Meshhessen community (Hesse, Germany). [→ English version below](#meshhessen-client--windows-client-for-meshtastic-devices-wpf--net-8)


## 🚀 Schnellstart

1. Download: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.23-windows-x64-installer
2. Installer ausführen
3. **Download:** Neueste `MeshhessenClient.exe` aus den [Releases](../../releases) herunterladen
4. **Gerät anschließen:** Meshtastic-Device per USB anstecken
5. **Starten:** Doppelklick auf `MeshhessenClient.exe` – keine Installation nötig
6. **Verbinden:** Verbindungstyp wählen (Serial, TCP oder Bluetooth) → „Verbinden" klicken
7. **Loslegen:** 3–10 Sekunden warten bis Kanäle geladen sind, dann Nachrichten senden

> Die App ist vollständig offline-fähig. Keine Cloud, keine Registrierung, keine Telemetrie zum Entwickler.


## ✨ Features

### 📨 Nachrichten & Kommunikation
* **Nachrichten** senden und empfangen (Broadcast & Direct Messages)
* **Multi-Channel** – alle Kanäle deines Geräts automatisch geladen
* **Direktnachrichten (DMs)** mit separatem Chat-Fenster im Tabbed-Layout
* **PKI-Entschlüsselung** – client-seitige Entschlüsselung von PKI-verschlüsselten DMs (X25519 + AES-256-CTR); Private Key wird automatisch vom verbundenen Gerät geladen
* **Node-Public-Key-Datenbank** – lokale CSV-Datei (`node_keys.csv`) zum Verwalten von Public Keys für PKI-Entschlüsselung
* **Tap-Back Reaktionen** – auf Nachrichten mit Emoji reagieren (32 Emojis, wie Android-App)
  - Rechtsklick auf Nachricht → Emoji-Picker
  - Reaktionen werden direkt an Sender/Kanal übermittelt und angezeigt
  - Funktioniert in Kanal-Chat und DMs
* **🚨 Alert Bell Support** – Senden und Empfangen von Notrufen
  - 🚨 SOS-Button in Chat und DMs
  - Visuell: rote blinkende Umrandung + Notification-Bar mit „Zur Karte springen"-Button

### 📊 Telemetrie & Statistik
* **Persistente Telemetrie-Datenbank** – empfangene Paket- und Gerätedaten werden lokal in einer SQLite-Datenbank gespeichert
* **Node-Statistik-Fenster** – detaillierte Auswertung pro Node:
  - Tag/Nacht-Auswertung der Empfangsqualität
  - Zeitreihen-Graphen für SNR, RSSI, Paketrate und Gerätedaten (Batterie, Spannung)
* **LED-Indikatoren** im Hauptfenster zeigen auf einen Blick den Status des verbundenen Nodes:
  - 📶 **Signal** – Empfangsqualität (SNR/RSSI-Trend)
  - 👥 **Nachbarn** – Anzahl und Stabilität direkter Nachbar-Nodes
  - 🛤️ **Pfad-Stabilität** – Stabilität des Traceroute-Pfads
  - 🌐 **Mesh-Health** – Gesamtzustand des sichtbaren Meshes
  - 🌤️ **Wetter** – für Nodes mit Umweltsensor

### 🗺️ Offline-Karte
* **Drei Kartentypen:** OSM Standard, OSM Dark Mode, OpenTopoMap (topografisch)
* **Eigener Tile-Server** – OSM-Policy verbietet Offline-Downloads, daher nutzen wir einen eigenen Server der das erlaubt
* **Offline-Tiles** für ganz Deutschland und angrenzende Gebiete
* **Node-Positionen** als farbige Pins auf der Karte
* **Node-Pfade** – GPS-Positionsverläufe aufzeichnen und auf der Karte anzeigen

### 📡 Traceroute
* **Traceroute starten** – direkt aus dem Node-Kontextmenü (Nodes-Liste, Karte, Nachrichtenliste)
* **Eigenes Fenster** pro Ziel-Node mit:
  - Hop-Tabelle: Node-Name, Entfernung, SNR (in dB), MQTT-Indikator
  - Live-Status (Warten / Empfangen)
* **Karte:** Route mit Linien plotten
  - Durchgezogene Linie wo Positionen bekannt
  - Gestrichelte Linie wo Positionen fehlen
  - Fallback auf eingestellte Kartenposition wenn eigene GPS fehlt
* **Mehrere Traceroutes gleichzeitig** auf der Karte – jede bekommt eine eindeutige Farbe
* **Speichern & Laden** – Traceroutes werden automatisch in `traceroutes/` gespeichert (JSON)
  - Pfade unterschiedlicher Zeitpunkte vergleichen
  - Mehrere Dateien gleichzeitig laden
* **Karten-Legende** – zeigt alle aktiven Traces mit Farbe und ✕-Button zum Entfernen

### 🔧 Node-Verwaltung
* **Knoten-Übersicht** – alle Nodes im Mesh mit SNR, Batterie, Entfernung, Hop-Anzahl
* **Node-Farben** – Nodes individuell einfärben (Karte + Listen)
* **Node-Notizen** – Freitext-Notizen pro Node
* **Nodes anpinnen** – Nodes in der Liste oben fixieren (unabhängig von Sortierung)
* **Node-Konfiguration** – Einstellungen des verbundenen Geräts anpassen
* **BT-PIN ändern** – Bluetooth-PIN direkt aus dem Client setzen

### ⚙️ Verbindung & System
* **Multi-Verbindung** – USB/Serial, TCP/WiFi und Bluetooth (BLE)
* **Auto-Reconnect** – nach Einstellungsänderungen die einen Neustart erfordern
* **Update-Check** – beim Start wird automatisch nach neuen Versionen gesucht; bei verfügbarem Update erscheint ein klickbarer Hinweis in der Statusleiste (offline-fähig: kein Fehler wenn kein Internet)
* **Multi-Sprache** – Deutsch und Englisch (umschaltbar in Einstellungen)
* **Dark Mode** & ModernWPF Fluent-Design
* **Automatisches Logging** aller Nachrichten (`logs/`)
* **Debug-Tab** mit Live-Log fürs Troubleshooting
* **Meshhessen-Schnellkonfiguration** – One-Click für Short Slow + EU868 + Meshhessen-Kanal


## 💬 Die Meshhessen Community

Der Meshhessen Client ist ein Gemeinschaftsprojekt der Meshtastic-Community in Hessen. Unser regionales LoRa-Mesh wächst stetig – mach mit!

* 🌐 **Website:** [www.meshhessen.de](https://www.meshhessen.de)
* 📡 **Netz:** Wachsendes Mesh-Netzwerk in Hessen und Umgebung – Airtime ist kein All-you-can-eat → Short Slow! ;)
* 🤝 **Mitmachen:** Eigenen Node aufstellen, Reichweite erweitern, Community wachsen lassen


## 📸 Screenshots

Offline-Karte mit Node-Positionen und Entfernungsanzeige:
<img width="1250" height="813" alt="Meshhessen Client – Windows-App für Meshtastic-Geräte: Offline-Karte mit Node-Positionen (OSM)" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/map.png" />
Node-Übersicht mit SNR, Batterie und Hop-Anzahl:
<img width="1249" height="812" alt="Meshhessen Client – Node-Übersicht mit SNR, RSSI, Batterie und Signal-LEDs" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/nodes.png" />
Nachrichtenansicht mit Kanal-Chat und Direktnachrichten:
<img width="1443" height="813" alt="Meshhessen Client – Meshtastic Nachrichten, Kanal-Chat und Direktnachrichten (DM) unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/messaging.png" />
Alert Bell / SOS-Notruf Anzeige:
<img width="1446" height="816" alt="Meshhessen Client – Alert Bell SOS-Notruf Signalisierung für Meshtastic-Geräte unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/alert_bell.png" />
Offline-Tile-Downloader für Deutschland und Umgebung:
<img width="567" height="883" alt="Meshhessen Client – Offline-Karten-Tile-Downloader für Meshtastic-Geräte unter Windows (OSM, OpenTopoMap)" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/tile_downloader.png" />
Kanal-Liste mit allen Meshtastic-Kanälen:
<img width="1443" height="814" alt="Meshhessen Client – Meshtastic Kanal-Liste (Multi-Channel) unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/channel_list.png" />
Traceroute-Fenster mit Hop-Tabelle:
<img width="780" height="868" alt="Meshhessen Client – Traceroute für Meshtastic-Geräte: Hop-Tabelle, Entfernung und SNR unter Windows" src="https://github.com/user-attachments/assets/10880a3d-9d94-4b07-84bc-ad64af56dab1" />
Traceroute-Pfad auf der Karte:
<img width="1278" height="889" alt="Meshhessen Client – Meshtastic Traceroute-Pfad auf Offline-Karte visualisiert" src="https://github.com/user-attachments/assets/800925ef-0d4e-41ad-a7f2-56cdd06da6b7" />
Node-Standort-Verlauf (GPS-Track) auf der Karte:
<img width="967" height="411" alt="Meshhessen Client – Meshtastic Node-Standort-Verlauf GPS-Track auf Karte" src="https://github.com/user-attachments/assets/0415083a-9f22-44fc-b767-be66b69892cd" />
Telemetrie-LED-Ampel im Hauptfenster:
<img width="516" height="162" alt="Meshhessen Client – Meshtastic Telemetrie-LEDs für Signal, Nachbarn, Mesh-Health und Wetter" src="https://github.com/user-attachments/assets/a580cf69-ab10-4890-a17a-1c5086c19700" />
Telemetrie-Zeitreihen-Graph (SNR, RSSI, Batterie):
<img width="902" height="604" alt="Meshhessen Client – Meshtastic Telemetrie-Graph SNR RSSI Batterie Zeitreihe OxyPlot" src="https://github.com/user-attachments/assets/a42d6a79-5b90-4f7a-8456-c74099c55c41" />
Node-Telemetrie-Statistik-Fenster:
<img width="1278" height="889" alt="Meshhessen Client – Meshtastic Node-Telemetrie Tag/Nacht-Auswertung Statistik Windows" src="https://github.com/user-attachments/assets/4821c996-c524-499a-8fba-f738fb051f88" />


## ⚠️ Bekannte Einschränkungen

* Keine persistente Message-History (Neustart = leere UI; Logs bleiben erhalten, beim Start geladene Nachrichten vom Node bleiben)
* Getestet mit Firmware 2.x
* T-Deck: Channels werden nicht immer in der Config-Sequenz mitgesendet (Retry-Workaround aktiv) – Das T-Deck ist fast schon mit sich selbst überfordert, daher dauert dort alles etwas länger…


## 🗺️ Offline-Karte einrichten

**Kartentypen:** OSM Standard (hell), OSM Dark Mode, OpenTopoMap (topografisch) – wählbar in Einstellungen.

> ⚠️ **Wichtig:** Bitte NICHT auf den offiziellen OSM Tile-Server zurückstellen – Offline-Downloads verstoßen gegen deren Policy. Wir nutzen einen eigenen Server, der das explizit erlaubt. Eigenen Tile-Server in den Einstellungen konfigurierbar.

**Tiles herunterladen:**

1. Einstellungen öffnen → Kartenquelle wählen (OSM / OSM Dark / OpenTopo)
2. **„Tiles herunterladen"** klicken
3. Bereich (Bounding Box) und Zoom-Level eingeben – z.B. Hessen: `49.3,7.7,51.7,10.2`, Zoom `1-14`
4. Download starten
5. Tiles werden unter `maptiles/` gespeichert und sind dauerhaft offline verfügbar
6. Tiles sind portabel – per USB übertragbar

**Karte nutzen:**
- Tab **„🗺️ Karte"** öffnen
- Rechtsklick auf Karte → eigenen Standort setzen
- Node-Pins erscheinen automatisch sobald GPS-Daten empfangen werden
- Rechtsklick auf Node → Farbe setzen, DM senden, Notiz bearbeiten, Traceroute starten


## 📝 Nachrichten-Logs

Alle Nachrichten werden automatisch geloggt unter `[EXE-Verzeichnis]/logs/`:

* `Channel_0_Primary.log` – Kanalverläufe
* `DM_DEADBEEF_Alice.log` – Direktnachrichten


## 🏗️ Technischer Überblick

| Komponente | Technologie |
|---|---|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protokoll | Meshtastic Protobuf über Serial/TCP/BLE |
| Karte | Mapsui 4.1 + lokale OSM-Tiles |
| Serialisierung | Google.Protobuf, System.Text.Json |
| Verbindung | Serial (0x94 0xC3 Framing), TCP/WiFi, Bluetooth Low Energy |

**Verbindungstypen:**

| Typ | Transport | Framing | Besonderheiten |
|---|---|---|---|
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
* Device-Debug-Text (ANSI-Codes) wird erkannt, ANSI-Codes gestrippt, separat geloggt
* Auto-Recovery: sendet Wakeup + `want_config_id` wenn >60s kein Protobuf-Paket empfangen wurde

**Fehler-Erkennung (Geräte-Logs):**

| Code | Beschreibung |
|---|---|
| TxWatchdog | Software-Bug beim LoRa-Senden |
| NoRadio | Kein LoRa-Radio gefunden |
| TransmitFailed | Radio-Sendehardware-Fehler |
| Brownout | CPU-Spannung unter Minimum |
| SX1262Failure | SX1262 Radio Selbsttest fehlgeschlagen |
| FlashCorruptionRecoverable | Flash-Korruption erkannt (repariert) |
| FlashCorruptionUnrecoverable | Flash-Korruption (nicht reparierbar) |


## 🔧 Aus Quellcode bauen

**Voraussetzungen:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone https://github.com/SMLunchen/mh_windowsclient.git
cd mh_windowsclient
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

EXE liegt danach unter `public\MeshhessenClient.exe`. Alternativ: `build.bat` ausführen.


## 🙏 Credits

* **[Meshtastic Project](https://meshtastic.org)** – Firmware & Protokoll-Spezifikation
* **[ModernWPF](https://github.com/Kinnara/ModernWpf)** – Fluent UI für WPF
* **[Mapsui](https://mapsui.com)** – Offline-Karte
* **[Meshhessen Community](https://www.meshhessen.de)** – Für das Netzwerk und die Inspiration

**Made with ❤️ by the Meshhessen Community** · [www.meshhessen.de](https://www.meshhessen.de)

---

## 🔍 Verwandte Suchbegriffe

Meshhessen Client · Windows-Client für Meshtastic-Geräte · Meshtastic Windows App · Meshtastic PC Software · Meshtastic Desktop App · Meshtastic USB Windows · Meshtastic Serial Windows · Meshtastic WPF · Meshtastic .NET · LoRa Mesh Windows · Meshtastic Offline Karte · Meshtastic Telemetrie · Meshtastic Traceroute · Meshtastic Hessen · Meshtastic Deutschland · Meshtastic Germany · LILYGO T-Beam Windows · T-Deck Windows · RAK4631 Windows · Heltec Windows · Meshtastic BLE Windows · Meshtastic Bluetooth Windows · Meshtastic PKI · Meshhessen Community · Meshhessen

---

# Meshhessen Client – Windows Client for Meshtastic Devices (WPF · .NET 8)

**Meshhessen Client** is a **free, native Windows client for Meshtastic devices** (WPF/.NET 8) for Windows 10/11. Connect your **Meshtastic device** (LILYGO T-Beam, T-Deck, RAK4631, Heltec, etc.) via **USB/Serial, TCP/WiFi, or Bluetooth** to your Windows PC – fully **offline-capable**, no cloud, no installation required. Developed by and for the [Meshhessen Community](https://www.meshhessen.de).

![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Status](https://img.shields.io/badge/Status-v1.5.5-yellow)
![License](https://img.shields.io/github/license/SMLunchen/mh_windowsclient)
![Stars](https://img.shields.io/github/stars/SMLunchen/mh_windowsclient)


## 🚀 Quick Start

1. Download .NET runtime: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.23-windows-x64-installer
2. Run the installer
3. **Download:** Get the latest `MeshhessenClient.exe` from [Releases](../../releases)
4. **Connect device:** Plug in your Meshtastic device via USB
5. **Launch:** Double-click `MeshhessenClient.exe` – no installation required
6. **Connect:** Select connection type (Serial, TCP, or Bluetooth) → click "Connect"
7. **Start:** Wait 3–10 seconds for channels to load, then send messages

> The app is fully offline-capable. No cloud, no registration, no telemetry to the developer.


## ✨ Features

### 📨 Messaging & Communication
* **Send and receive messages** (broadcast & direct messages)
* **Multi-channel** – all channels from your device loaded automatically
* **Direct Messages (DMs)** with a dedicated chat window in a tabbed layout
* **PKI decryption** – client-side decryption of PKI-encrypted DMs (X25519 + AES-256-CTR); private key is loaded automatically from the connected device
* **Node public key database** – local CSV file (`node_keys.csv`) for managing public keys for PKI decryption
* **Tap-back reactions** – react to messages with emoji (32 emojis, like the Android app)
  - Right-click a message → emoji picker
  - Reactions are sent to the sender/channel and displayed inline
  - Works in channel chat and DMs
* **🚨 Alert Bell support** – send and receive emergency alerts
  - 🚨 SOS button in chat and DMs
  - Visual: red blinking border + notification bar with "Jump to map" button

### 📊 Telemetry & Statistics
* **Persistent telemetry database** – received packet and device data is stored locally in a SQLite database
* **Node statistics window** – detailed analysis per node:
  - Day/night breakdown of reception quality
  - Time-series graphs for SNR, RSSI, packet rate and device data (battery, voltage)
* **LED indicators** in the main window show the connected node's status at a glance:
  - 📶 **Signal** – reception quality (SNR/RSSI trend)
  - 👥 **Neighbors** – number and stability of direct neighbor nodes
  - 🛤️ **Path stability** – stability of the traceroute path
  - 🌐 **Mesh health** – overall health of the visible mesh
  - 🌤️ **Weather** – for nodes with environmental sensors

### 🗺️ Offline Map
* **Three map types:** OSM Standard, OSM Dark Mode, OpenTopoMap (topographic)
* **Own tile server** – OSM policy prohibits offline downloads; we use our own server that explicitly permits it
* **Offline tiles** for all of Germany and neighboring areas
* **Node positions** as colored pins on the map
* **Node paths** – record GPS position history and display tracks on the map

### 📡 Traceroute
* **Start traceroute** – directly from the node context menu (node list, map, message list)
* **Dedicated window** per target node with:
  - Hop table: node name, distance, SNR (in dB), MQTT indicator
  - Live status (waiting / received)
* **Map plotting** – plot the route with lines
  - Solid line where positions are known
  - Dashed line where positions are missing
  - Falls back to the configured map position if own GPS is unavailable
* **Multiple traceroutes at once** on the map – each gets a unique color
* **Save & Load** – traceroutes are automatically saved to `traceroutes/` as JSON
  - Compare routes recorded at different times
  - Load multiple files simultaneously
* **Map legend** – shows all active traces with color and individual ✕ remove button

### 🔧 Node Management
* **Node overview** – all nodes in the mesh with SNR, battery, distance, hop count
* **Node colors** – color-code nodes individually (map + lists)
* **Node notes** – free-text annotations per node
* **Pin nodes** – pin nodes to the top of the list (independent of sorting)
* **Node configuration** – adjust settings of the connected device
* **Change BT PIN** – set Bluetooth PIN directly from the client

### ⚙️ Connection & System
* **Multi-connection** – USB/Serial, TCP/WiFi, and Bluetooth (BLE)
* **Auto-reconnect** – after settings changes that require a device reboot
* **Update check** – automatically checks for new versions on startup; a clickable hint appears in the status bar if an update is available (offline-safe: no error if no internet)
* **Multi-language** – German and English (switchable in settings)
* **Dark mode** & ModernWPF Fluent design
* **Automatic logging** of all messages (`logs/`)
* **Debug tab** with live log for troubleshooting
* **Meshhessen quick-config** – one-click Short Slow + EU868 + Meshhessen channel setup


## 💬 The Meshhessen Community

The Meshhessen Client is a community project of the Meshtastic community in Hesse, Germany. Our regional LoRa mesh network is growing – join us!

* 🌐 **Website:** [www.meshhessen.de](https://www.meshhessen.de)
* 📡 **Network:** Growing mesh network in Hesse and surrounding areas – airtime is not all-you-can-eat → Short Slow! ;)
* 🤝 **Contribute:** Set up your own node, extend coverage, grow the community


## ⚠️ Known Limitations

* No persistent message history (restart = empty UI; logs are preserved, node-loaded messages at startup are preserved)
* Tested with firmware 2.x
* T-Deck: channels are not always included in the config sequence (retry workaround active) – The T-Deck is barely keeping up with itself, so everything takes a bit longer there…


## 🗺️ Setting Up the Offline Map

**Map types:** OSM Standard (light), OSM Dark Mode, OpenTopoMap (topographic) – selectable in settings.

> ⚠️ **Important:** Please do NOT switch to the official OSM tile server – offline downloads violate their policy. We use our own server that explicitly permits this. You can configure your own tile server in settings.

**Downloading tiles:**

1. Open settings → select map source (OSM / OSM Dark / OpenTopo)
2. Click **"Download Tiles"**
3. Enter bounding box and zoom levels – e.g. Hesse: `49.3,7.7,51.7,10.2`, Zoom `1-14`
4. Start download
5. Tiles are saved under `maptiles/` and are permanently available offline
6. Tiles are portable – transferable via USB

**Using the map:**
- Open the **"🗺️ Map"** tab
- Right-click on map → set own location
- Node pins appear automatically once GPS data is received
- Right-click on a node → set color, send DM, edit note, start traceroute


## 🏗️ Technical Overview

| Component | Technology |
|---|---|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protocol | Meshtastic Protobuf over Serial/TCP/BLE |
| Map | Mapsui 4.1 + local OSM tiles |
| Serialization | Google.Protobuf, System.Text.Json |
| Connection | Serial (0x94 0xC3 framing), TCP/WiFi, Bluetooth Low Energy |

**Connection types:**

| Type | Transport | Framing | Notes |
|---|---|---|---|
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


## 🔧 Building from Source

**Requirements:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone https://github.com/SMLunchen/mh_windowsclient.git
cd mh_windowsclient
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

The EXE will be at `public\MeshhessenClient.exe`. Alternatively, run `build.bat`.


## 🙏 Credits

* **[Meshtastic Project](https://meshtastic.org)** – Firmware & protocol specification
* **[ModernWPF](https://github.com/Kinnara/ModernWpf)** – Fluent UI for WPF
* **[Mapsui](https://mapsui.com)** – Offline map
* **[Meshhessen Community](https://www.meshhessen.de)** – For the network and the inspiration

**Made with ❤️ by the Meshhessen Community** · [www.meshhessen.de](https://www.meshhessen.de)

---

## 🔍 Related Search Terms

Meshhessen Client · Windows client for Meshtastic devices · Meshtastic Windows app · Meshtastic PC software · Meshtastic desktop app · Meshtastic USB Windows · Meshtastic serial Windows · Meshtastic WPF · Meshtastic .NET · LoRa mesh Windows · Meshtastic offline map · Meshtastic telemetry · Meshtastic traceroute · Meshtastic Germany · Meshtastic Hessen · LILYGO T-Beam Windows · T-Deck Windows · RAK4631 Windows · Heltec Windows · Meshtastic BLE Windows · Meshtastic Bluetooth Windows · Meshtastic PKI decryption · Meshhessen community · Meshhessen
