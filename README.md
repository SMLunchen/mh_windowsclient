# 📡 Meshhessen Client

**Dein Mesh. Deine Karte. Deine Kontrolle.**

Der kostenlose, native **Windows-Client für Meshtastic-Geräte**: Nachrichten senden, Funkstrecken analysieren, Nodes verwalten — und auch ganz ohne Internet den Überblick behalten. Entwickelt von der [Meshhessen Community](https://www.meshhessen.de), gebaut für jedes Meshtastic-Mesh — egal wo.

![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Release](https://img.shields.io/github/v/release/SMLunchen/mh_windowsclient)
![License](https://img.shields.io/github/license/SMLunchen/mh_windowsclient)
![Stars](https://img.shields.io/github/stars/SMLunchen/mh_windowsclient)

> **English summary:** Free Windows app for Meshtastic devices – offline maps (raster + vector incl. 3D terrain), radio-link analysis with elevation profiles & Fresnel zones, telemetry dashboards, traceroute, PKI decryption, full device & remote configuration, Virtual-Node TCP proxy. No installer, no cloud. [→ English version below](#-meshhessen-client-1)

<img alt="Meshhessen Client – Vektorkarte (MapLibre GL) mit Meshtastic Node-Positionen unter Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/vector_in_action.png" />

## ⬇️ Download & Schnellstart

| | |
|---|---|
| **Download** | [Neueste `MeshhessenClient.exe` aus den Releases](https://github.com/SMLunchen/mh_windowsclient/releases) — eine Datei, ohne Installer startbar, portabel |
| **Voraussetzung** | Windows 10/11 (x64) — sonst nichts: Die EXE bringt die komplette .NET-Laufzeitumgebung mit (self-contained) |
| **Geräte** | Alle Meshtastic-Geräte: LILYGO T-Beam, T-Deck, RAK4631, Heltec, Seeed u. v. m. |
| **Verbindung** | USB/Serial · TCP/WLAN · Bluetooth (BLE) |
| **Privatsphäre** | Vollständig offline-fähig — keine Cloud, keine Registrierung, keine Telemetrie zum Entwickler |

1. `MeshhessenClient.exe` herunterladen und starten — keine Installation, keine separate Runtime nötig
2. Meshtastic-Gerät anschließen (USB) oder IP/BT bereithalten → Verbindungstyp wählen → **Verbinden**
3. 3–10 Sekunden warten, bis die Kanäle geladen sind — loslegen!

> 💡 Windows SmartScreen warnt beim ersten Start vor dem „unbekannten Herausgeber" — normal bei unsignierten Open-Source-Downloads. **„Weitere Informationen → Trotzdem ausführen."** Details, Prüfsummen-Check und erste Schritte: [Schnellstart-Anleitung](docs/de/schnellstart.md).

## ✨ Das kann der Client

### ⛰ Dein gesamtes Mesh auf einer Karte
Offline-Karten (Raster + gestochen scharfe Vektorkarte), Node-Positionen, Traceroutes, Nachbar-Linien, Umwelt-Heatmaps und zuschaltbare Fach-Layer — plus echtes **3D-Gelände** mit SRTM-Höhenmodell. Einmal betrachtete Gebiete funktionieren auch ohne Internet.

<img alt="Meshhessen Client – 3D-Gelände (SRTM-Höhenmodell) mit Schummerung auf der Meshtastic-Vektorkarte" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-with-dem-elevation-profile.png" />

→ [Karte & Offline-Nutzung](docs/de/karte.md)

### 📡 Verstehe deine Funkstrecken
**Echte HF-Nachbarn** (belegt durch Direktempfang, Traceroutes und NeighborInfo — nicht bloß „gehört via MQTT"), SNR in beide Richtungen, und per Rechtsklick das **Höhenprofil jeder Funkstrecke**: SRTM-Gelände, Sichtlinie, Fresnel-Zone und die Antwort auf die Frage „steht da ein Berg im Weg?".

<img alt="Meshhessen Client – Höhenprofil einer Meshtastic-Funkstrecke: SRTM-Gelände, Sichtlinie und Fresnel-Zone" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/elevation-profile.png" />

→ [Telemetrie & Funkanalyse](docs/de/telemetrie.md) · [Traceroute](docs/de/traceroute.md)

### 📊 Erkenne Probleme, bevor Verbindungen ausfallen
Frei konfigurierbares **Telemetrie-Dashboard** (11 Widget-Typen, Schwellwert-Alarme als Windows-Toast, Tag/Nacht-Schattierung, Vollbild für Wandmonitore), Node-Statistiken mit Tag/Nacht-Auswertung und LED-Ampeln für Signal, Nachbarn, Pfad-Stabilität und Mesh-Health.

<img alt="Meshhessen Client – Telemetrie-Dashboard mit konfigurierbaren Widgets: Gauges, Heatmaps, Mesh-Health und Schwellwert-Alarmen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/telemetry-dashboard.png" />

→ [Telemetrie & Funkanalyse](docs/de/telemetrie.md)

### 💬 Kommuniziere bequem am PC
Kanal-Chat und Direktnachrichten mit Reaktionen, Antworten und optionaler Nachrichten-Historie — inklusive client-seitiger **PKI-Entschlüsselung** (X25519 + AES-256-CCM) und 🚨 Alert-Bell/SOS. Dazu **Fernverwaltung** entfernter Nodes (auch über Relay-/MQTT-Strecken) und vollständige Gerätekonfiguration.

<img width="1443" height="813" alt="Meshhessen Client – Meshtastic Nachrichten, Kanal-Chat und Direktnachrichten (DM) unter Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/new-chat-layout.png" />

→ [Nachrichten](docs/de/nachrichten.md) · [Node-Verwaltung & Fernkonfiguration](docs/de/node-verwaltung.md)

## 🧭 Für deinen Einsatzzweck

| | |
|---|---|
| **📶 Mesh-Betreiber** | Antennenstandorte analysieren, Signalverläufe vergleichen, instabile Funkstrecken mit Höhenprofil und Fresnel-Zone identifizieren |
| **🏕️ Outdoor & Offline** | Karten vorab herunterladen, Standorte darstellen, komplett ohne Internetverbindung arbeiten — auch auf dem T-Deck ([SD-Karten-Assistent](docs/de/t-deck.md)) |
| **🚒 Übungen & Notfunk** | Lageübersicht, Nachrichten, Feuerwehr-/Rettungs-Layer und Kiosk-Modus auf einer Oberfläche — als Unterstützung für Ausbildung und Übungen (kein zertifiziertes BOS-/Alarmierungssystem) |
| **🔬 Technikbegeisterte** | Geräte vollständig konfigurieren, Telemetrie auswerten, entfernte Nodes administrieren, Apps per [Virtual Node](docs/de/virtual-node.md) anbinden |

## 📚 Dokumentation

| Deutsch | English |
|---|---|
| [🚀 Schnellstart & Verbindung](docs/de/schnellstart.md) | [🚀 Getting started & connection](docs/en/getting-started.md) |
| [🗺️ Karte & Offline-Nutzung](docs/de/karte.md) | [🗺️ Map & offline use](docs/en/map.md) |
| [📨 Nachrichten & Kommunikation](docs/de/nachrichten.md) | [📨 Messaging & communication](docs/en/messaging.md) |
| [📊 Telemetrie & Funkanalyse](docs/de/telemetrie.md) | [📊 Telemetry & radio analysis](docs/en/telemetry.md) |
| [📡 Traceroute](docs/de/traceroute.md) | [📡 Traceroute](docs/en/traceroute.md) |
| [🔧 Node-Verwaltung & Fernkonfiguration](docs/de/node-verwaltung.md) | [🔧 Node management & remote admin](docs/en/node-management.md) |
| [🖧 Virtual Node](docs/de/virtual-node.md) · [🔧 T-Deck](docs/de/t-deck.md) · [🔒 Kiosk](docs/de/kiosk.md) | [🖧 Virtual Node](docs/en/virtual-node.md) · [🔧 T-Deck](docs/en/t-deck.md) · [🔒 Kiosk](docs/en/kiosk.md) |
| [🏗️ Technik & Protokoll](docs/de/technik.md) | [🏗️ Technology & protocol](docs/en/technical.md) |

📖 **Komfortabel lesen:** Dieselben Kapitel gibt es als durchsuchbares Handbuch unter [meshhessenclient.de/handbuch](https://meshhessenclient.de/handbuch/).

Außerdem: [Changelog](CHANGELOG.md) · [Website mit Telemetrie-Hilfe](https://meshhessenclient.de)

## 💬 Community & Mitmachen

Der Meshhessen Client ist ein Gemeinschaftsprojekt der Meshtastic-Community in Hessen — und läuft mit jedem Meshtastic-Mesh weltweit.

* 🌐 **Website:** [www.meshhessen.de](https://www.meshhessen.de) · [meshhessenclient.de](https://meshhessenclient.de)
* 🐛 **Fehler gefunden / Idee?** → [Issues](https://github.com/SMLunchen/mh_windowsclient/issues)
* 📡 **Netz:** Airtime ist kein All-you-can-eat → Short Slow! ;)
* 🛠️ **Aus Quellcode bauen:** siehe [Technik & Protokoll](docs/de/technik.md#aus-quellcode-bauen)

### Forks & Weiterverwendung

Der Quellcode darf im Rahmen der Lizenz geforkt und angepasst werden — wir freuen uns über abgeleitete Projekte.

> ⚠️ **Der Meshhessen-Tile-Server ist davon ausgenommen.** Die Karten-Infrastruktur (`tile.meshhessenclient.de` und die zugehörigen Vektor-/Raster-Endpunkte) wird **ausschließlich für den offiziellen Meshhessen Client** bereitgestellt und aus Spenden der Community finanziert. Forks, abgeleitete oder umgebaute Clients (auch für andere Mesh-Protokolle wie MeshCore) dürfen diese Server **nicht** nutzen und müssen **eigene Tile-Infrastruktur betreiben**. Der Client bringt dafür alles mit: Karten-Modus **„Online – eigener Tile-Server"**. Unautorisierte Zugriffe werden technisch unterbunden.

## 🙏 Credits

* **[Meshtastic Project](https://meshtastic.org)** – Firmware & Protokoll-Spezifikation
* **[ModernWPF](https://github.com/Kinnara/ModernWpf)** – Fluent UI für WPF · **[Mapsui](https://mapsui.com)** – Rasterkarte · **[MapLibre GL](https://maplibre.org)** – Vektorkarte
* **[Meshhessen Community](https://www.meshhessen.de)** – Für das Netzwerk und die Inspiration

**Made with ❤️ by the Meshhessen Community** · [www.meshhessen.de](https://www.meshhessen.de)

---

# 📡 Meshhessen Client

**Your mesh. Your map. Your control.**

The free, native **Windows client for Meshtastic devices**: send messages, analyze radio links, manage nodes — and keep the full picture even without internet. Built by the [Meshhessen Community](https://www.meshhessen.de) in Hesse, Germany — made for every Meshtastic mesh, anywhere.

## ⬇️ Download & Quick Start

| | |
|---|---|
| **Download** | [Latest `MeshhessenClient.exe` from Releases](https://github.com/SMLunchen/mh_windowsclient/releases) — a single file, runs without an installer, portable |
| **Requirements** | Windows 10/11 (x64) — nothing else: the EXE ships with the complete .NET runtime (self-contained) |
| **Devices** | All Meshtastic devices: LILYGO T-Beam, T-Deck, RAK4631, Heltec, Seeed and more |
| **Connection** | USB/Serial · TCP/WiFi · Bluetooth (BLE) |
| **Privacy** | Fully offline-capable — no cloud, no registration, no telemetry to the developer |

1. Download and launch `MeshhessenClient.exe` — no installation, no separate runtime needed
2. Plug in your Meshtastic device (USB) or have IP/BT ready → pick a connection type → **Connect**
3. Wait 3–10 seconds for the channels to load — go!

> 💡 Windows SmartScreen may warn about an "unknown publisher" on first launch — normal for unsigned open-source downloads. **"More info → Run anyway."** Details, checksum verification and first steps: [getting-started guide](docs/en/getting-started.md).

## ✨ Highlights

* **⛰ Your whole mesh on one map** – offline maps (raster + crisp vector), node pins, traceroutes, neighbor lines, environment heatmaps, fire/rescue layers — plus real **3D terrain** from an SRTM elevation model → [Map & offline use](docs/en/map.md)
* **📡 Understand your radio links** – **real RF neighbors** (proven by direct reception, traceroutes and NeighborInfo — not just "heard via MQTT"), SNR in both directions, and a right-click **elevation profile** per link: terrain, line of sight, Fresnel zone → [Telemetry](docs/en/telemetry.md) · [Traceroute](docs/en/traceroute.md)
* **📊 Spot problems before links die** – configurable **telemetry dashboard** (11 widget types, threshold toasts, fullscreen for wall monitors), day/night statistics, LED health indicators → [Telemetry](docs/en/telemetry.md)
* **💬 Communicate comfortably from your PC** – channel chat & DMs with reactions, replies and optional history, client-side **PKI decryption** (X25519 + AES-256-CCM), 🚨 alert bell/SOS → [Messaging](docs/en/messaging.md)
* **🔧 Configure everything** – full device configuration, **remote admin** across relay/MQTT paths, favorites sync → [Node management](docs/en/node-management.md)
* **🧰 Power tools** – Virtual-Node TCP proxy for phone apps, T-Deck SD-card map assistant, kiosk mode for shared stations → [Virtual Node](docs/en/virtual-node.md) · [T-Deck](docs/en/t-deck.md) · [Kiosk](docs/en/kiosk.md)

## 📚 Documentation

[🚀 Getting started](docs/en/getting-started.md) · [🗺️ Map](docs/en/map.md) · [📨 Messaging](docs/en/messaging.md) · [📊 Telemetry](docs/en/telemetry.md) · [📡 Traceroute](docs/en/traceroute.md) · [🔧 Node management](docs/en/node-management.md) · [🖧 Virtual Node](docs/en/virtual-node.md) · [🔧 T-Deck](docs/en/t-deck.md) · [🔒 Kiosk](docs/en/kiosk.md) · [🏗️ Technology](docs/en/technical.md) · [Changelog](CHANGELOG.md)

📖 **Prefer a searchable manual?** The same chapters live at [meshhessenclient.de/handbuch](https://meshhessenclient.de/handbuch/).

### Forks & reuse

You are welcome to fork and adapt the source code within the terms of the license. **The Meshhessen tile server is not part of that:** the map infrastructure (`tile.meshhessenclient.de` and the associated vector/raster endpoints) is provided exclusively for the official Meshhessen Client and funded by community donations. Forks and derivative clients (including ports to other mesh protocols) must run their own tile infrastructure — the **"Online – custom tile server"** map mode accepts any tile URLs. Unauthorized access is blocked technically.
