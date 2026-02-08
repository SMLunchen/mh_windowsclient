# Meshtastic Windows Client

Ein **offline-fähiger, nativer Windows-Client** für Meshtastic Geräte mit USB/serieller Verbindung.

![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Beta-yellow)

---

## 📋 Inhaltsverzeichnis

- [Features](#features)
- [Schnellstart](#schnellstart)
- [Installation & Build](#installation--build)
- [Verwendung](#verwendung)
- [Debugging](#debugging)
- [Architektur](#architektur)
- [Bekannte Einschränkungen](#bekannte-einschränkungen)
- [Roadmap](#roadmap)
- [Lizenz](#lizenz)

---

## ✨ Features

### Implementiert ✅

- **Serielle USB-Verbindung**
  - Automatische COM-Port-Erkennung
  - Stabile Verbindung mit Framing-Protokoll (0x94 0xC3)
  - Automatisches Protobuf-Parsing

- **Nachrichten senden/empfangen**
  - Text-Nachrichten über TEXT_MESSAGE_APP
  - Gesendete Nachrichten werden in UI angezeigt
  - Empfangene Nachrichten mit Absender
  - Verschlüsselte Nachrichten werden erkannt
  - Kanal-basiertes Messaging

- **Multi-Channel-Support**
  - Automatisches Laden aller konfigurierten Channels (0-7)
  - Kanal-Auswahl in Toolbar
  - PRIMARY/SECONDARY Channel-Rollen
  - PSK-Anzeige (Base64)

- **Knoten-Übersicht**
  - Alle Nodes im Mesh
  - Node-ID, Name, SNR
  - Letzte Aktivität
  - Position (wenn vorhanden)
  - Batteriestatus

- **Geräteeinstellungen**
  - LoRa-Konfiguration auslesen (Region, Modem Preset)
  - Automatische UI-Aktualisierung bei Connect
  - Region-Auswahl (EU_868, US, etc.)
  - Modem-Preset-Auswahl (LONG_FAST, SHORT_SLOW, etc.)

- **Offline-Fähigkeit**
  - Keine Internet-Verbindung erforderlich
  - Standalone EXE (~160 MB mit .NET Runtime)
  - Komplett self-contained

- **Debug-Modus**
  - Intensives Logging für Troubleshooting
  - DebugView-Kompatibilität
  - Packet-Tracing
  - Event-Flow-Logging

### In Arbeit 🚧

- Message-History (persistente Speicherung)
- Direct Messages (DM) mit Tab-System
- PSK-Entschlüsselung für verschlüsselte Channels
- Config-Bearbeitung und Speichern

### Geplant 📋

- Karten-Ansicht für Node-Positionen
- Firmware-Update über Client
- Mesh-Visualisierung (Graph)
- GPS-Wegpunkte
- Telemetrie-Anzeige
- Dark Mode
- Mehrsprachigkeit

---

## 🚀 Schnellstart

### Option 1: Fertige EXE (Empfohlen für Endbenutzer)

1. **Download:** `publish\MeshtasticClient.exe`
2. **Gerät anschließen:** Meshtastic-Device per USB
3. **Starten:** Doppelklick auf `MeshtasticClient.exe`
4. **Verbinden:** COM-Port wählen → "Verbinden"
5. **Warten:** 3-10 Sekunden bis Channels geladen sind
6. **Kanal wählen:** Dropdown oben für Multi-Channel-Support
7. **Loslegen:** Nachrichten senden und empfangen

### Option 2: Aus Quellcode bauen

**Voraussetzungen:**
- .NET 8.0 SDK oder höher
- Windows 10/11 x64

**Build-Befehle:**

```bash
# Einfach mit Build-Skript
build.bat

# Oder manuell
dotnet restore
dotnet build -c Release
dotnet publish MeshtasticClient/MeshtasticClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# EXE ist hier:
publish\MeshtasticClient.exe
```

---

## 💻 Installation & Build

### .NET SDK installieren

1. Download: https://dotnet.microsoft.com/download/dotnet/8.0
2. Installer ausführen
3. Überprüfen:
   ```bash
   dotnet --version
   # Sollte "8.0.x" oder höher zeigen
   ```

### Visual Studio (Optional)

1. Download: Visual Studio 2022 Community (kostenlos)
2. Bei Installation ".NET Desktop-Entwicklung" auswählen
3. Solution öffnen: `MeshtasticClient.sln`
4. F5 drücken zum Debuggen

### Projekt-Struktur

```
windows-client/
├── MeshtasticClient.sln          # Visual Studio Solution
├── build.bat                      # Build-Skript
├── README.md                      # Diese Datei
├── DEBUG.md                       # Debug-Anleitung
├── SCHNELLSTART.md                # Deutsche Quick-Start-Anleitung
├── LICENSE                        # MIT Lizenz
│
├── publish/                       # Fertige EXE (nach Build)
│   └── MeshtasticClient.exe      # Standalone executable
│
└── MeshtasticClient/
    ├── MeshtasticClient.csproj   # Projekt-Konfiguration
    ├── App.xaml + .cs            # WPF Application Entry Point
    ├── MainWindow.xaml + .cs     # Haupt-UI
    │
    ├── Models/                    # Datenmodelle
    │   ├── MessageItem.cs         # Nachrichten
    │   ├── NodeInfo.cs            # Node-Informationen
    │   └── ChannelInfo.cs         # Kanal-Konfiguration
    │
    ├── Services/                  # Geschäftslogik
    │   ├── SerialPortService.cs            # USB/Serielle Kommunikation
    │   └── MeshtasticProtocolService.cs    # Meshtastic-Protokoll (Protobuf)
    │
    └── Proto/                     # Protobuf-Definitionen
        ├── mesh.proto             # Meshtastic Hauptprotokoll
        ├── portnums.proto         # Port-Nummern
        └── admin.proto            # Admin-Messages für Config
```

---

## 📖 Verwendung

### Erste Verbindung

1. **Gerät vorbereiten**
   - Meshtastic-Device einschalten
   - Per USB an PC anschließen
   - Windows installiert automatisch Treiber (CP210x oder ähnlich)

2. **Client starten**
   - `MeshtasticClient.exe` ausführen
   - Klick auf "🔄" um Ports zu aktualisieren

3. **Verbinden**
   - COM-Port auswählen (z.B. COM3, COM4)
   - "Verbinden" klicken
   - Status-Indikator wird grün
   - **Warten:** 3-10 Sekunden bis Config geladen ist

4. **Channels laden**
   - Channels erscheinen automatisch in Dropdown
   - Kanal auswählen (nicht Channel 0 wenn du Custom-Channels hast!)

### Nachrichten senden

1. Text in unteres Feld eingeben
2. Enter drücken oder "Senden" klicken
3. Nachricht erscheint sofort in Liste als "Ich"
4. Wird auf ausgewähltem Kanal gesendet

### Nachrichten empfangen

- Erscheinen automatisch in der Liste
- Mit Absender-Name (falls bekannt) oder Node-ID
- Verschlüsselte Nachrichten: `[Verschlüsselte Nachricht - PSK erforderlich]`

### Knoten anzeigen

1. Tab "🌐 Knoten" öffnen
2. Alle Nodes im Mesh werden gelistet
3. Aktualisiert sich automatisch bei neuen Nodes

### Einstellungen

1. Tab "⚙️ Einstellungen" öffnen
2. **Aktuelle Werte** werden vom Gerät geladen:
   - Region (z.B. EU_868)
   - Modem Preset (z.B. SHORT_SLOW)
   - Gerätename
3. **Hinweis:** Speichern noch nicht implementiert

---

## 🔍 Debugging

### Problem: Keine Channels/Nachrichten

Der Client hat **intensives Debug-Logging**. So siehst du die Logs:

#### Mit DebugView (Empfohlen)

1. **Download:** https://learn.microsoft.com/en-us/sysinternals/downloads/debugview
2. **Starten** (als Administrator)
3. **Capture → Capture Win32** aktivieren
4. **Client starten:** `MeshtasticClient.exe`
5. **Logs live sehen:**
   ```
   === Initializing Meshtastic connection ===
   Serial data received: 127 bytes
   Found packet: length=123
   Received FromRadio packet, type: Channel
   HandleChannel called: Index=0, Name=LongFast
   TEXT MESSAGE: Text="Hello"
   ```

#### Mit Visual Studio

1. Solution öffnen: `MeshtasticClient.sln`
2. F5 drücken (Debug-Modus)
3. Output-Fenster: View → Output (Ctrl+W, O)
4. Dropdown: "Debug" auswählen

#### Typische Log-Ausgaben

**✅ Erfolgreiche Verbindung:**
```
Config complete! Received 2 channels so far
HandleChannel called: Index=1, Role=SECONDARY, Name=MyChannel
Firing ChannelInfoReceived event for channel 1: MyChannel
```

**❌ Keine Daten:**
```
Still waiting for config... (3s)
WARNING: Config not complete after timeout!
```

**❌ Channels nicht empfangen:**
```
Config complete! Received 0 channels so far
```

**❌ Nachrichten verschlüsselt:**
```
Encrypted packet - cannot decode without key
```

### Detaillierte Debug-Anleitung

Siehe **[DEBUG.md](DEBUG.md)** für:
- Komplette Troubleshooting-Anleitung
- Log-Interpretation
- Häufige Probleme und Lösungen
- Log-Export für Support

---

## 🏗️ Architektur

### Technologie-Stack

- **.NET 8.0 / C#** - Moderne, performante Entwicklung
- **WPF (Windows Presentation Foundation)** - Native Windows UI
- **ModernWPF** - Fluent Design System
- **System.IO.Ports** - Serielle Kommunikation
- **Google.Protobuf** - Meshtastic-Protokoll
- **CommunityToolkit.Mvvm** - MVVM-Pattern Support

### Protokoll-Flow

```
┌─────────────────┐
│  Windows Client │
└────────┬────────┘
         │ USB/Serial (115200 baud)
         ↓
┌─────────────────┐
│ Meshtastic Node │
└────────┬────────┘
         │ LoRa
         ↓
┌─────────────────┐
│   Mesh Network  │
└─────────────────┘
```

### Serielle Kommunikation

**Frame Format:**
```
[0x94 0xC3] [LenHi LenLo] [Protobuf Data...]
 ^^^^^^^^^   ^^^^^^^^^^^   ^^^^^^^^^^^^^^^^
 Start Bytes Length (BE)   FromRadio/ToRadio
```

**Protobuf Messages:**
- `ToRadio` - Client → Device
  - `want_config_id` - Config anfordern
  - `packet` - MeshPacket senden
- `FromRadio` - Device → Client
  - `my_info` - Node-ID
  - `node_info` - Node-Details
  - `channel` - Kanal-Config
  - `config` - Device-Config (LoRa, etc.)
  - `packet` - Empfangene MeshPackets

### Connection Sequence

```
1. Serial Port öffnen (115200 baud)
2. ToRadio.want_config_id senden
3. Warten auf FromRadio Messages:
   ├─ my_info (Node-ID)
   ├─ node_info (Nodes im Mesh)
   ├─ channel (x8, alle Channels)
   ├─ config (Device/LoRa Config)
   └─ config_complete_id
4. Optional: AdminMessages für erweiterte Config
5. Bereit für MeshPackets
```

### Message Flow

```
User Input
    ↓
MainWindow (UI)
    ↓
MeshtasticProtocolService.SendTextMessageAsync()
    ↓
MeshPacket (Protobuf)
    ↓
ToRadio
    ↓
SerialPortService.WriteAsync()
    ↓
[0x94 0xC3] [Len] [Data]
    ↓
Meshtastic Node
    ↓
LoRa Transmission
```

```
LoRa Reception
    ↓
Meshtastic Node
    ↓
[0x94 0xC3] [Len] [Data]
    ↓
SerialPortService (DataReceived Event)
    ↓
MeshtasticProtocolService.ProcessBuffer()
    ↓
FromRadio.packet
    ↓
MeshPacket.decoded
    ↓
Data (PortNum=TEXT_MESSAGE_APP)
    ↓
MessageReceived Event
    ↓
MainWindow (UI Update)
    ↓
ObservableCollection (MessageListView)
```

### Code-Struktur

**MainWindow.xaml.cs** - UI Controller
- Event-Handler für UI-Elemente
- ObservableCollections für Data-Binding
- Dispatcher für Thread-sichere UI-Updates

**SerialPortService.cs** - Low-Level Serial I/O
- `System.IO.Ports.SerialPort` Wrapper
- Framing (0x94 0xC3 Detection)
- Async Read/Write
- Connection State Management

**MeshtasticProtocolService.cs** - Protokoll-Logik
- Protobuf Parsing (FromRadio/ToRadio)
- Packet-Routing (PortNum → Handler)
- Config-Loading
- Message/Node/Channel Events

**Models/** - Datenmodelle
- UI-freundliche DTOs
- Mapping von Protobuf → UI Models

**Proto/** - Protobuf Definitionen
- Offizielle Meshtastic Protobufs
- Auto-generierte C# Klassen

---

## ⚠️ Bekannte Einschränkungen

### 1. Keine Message-History

**Problem:** Meshtastic-Nodes speichern **keine** Message-History. Beim Client-Neustart sind alte Nachrichten weg.

**Workaround:**
- Client speichert (noch) nichts persistent
- Geplant: SQLite-Datenbank für lokale History

**Wie offizielle Clients es lösen:**
- Web-Client: IndexedDB (Browser-Storage)
- Python-Client: Nur letztes Paket pro Node in Memory

### 2. Verschlüsselte Nachrichten

**Problem:** PSK-verschlüsselte Nachrichten können nicht entschlüsselt werden.

**Aktuelles Verhalten:**
- Zeigt `[Verschlüsselte Nachricht - PSK erforderlich]`
- Nachricht wird über LoRa verschlüsselt übertragen
- Node entschlüsselt mit konfiguriertem PSK
- Über Serial kommt sie **unverschlüsselt** (wenn PSK stimmt)

**Wenn verschlüsselte Nachrichten ankommen:**
- Falscher Kanal ausgewählt (anderer PSK)
- Kanal auf Node nicht richtig konfiguriert

**Geplant:** PSK-Entschlüsselung im Client (für Store&Forward)

### 3. Config-Bearbeitung

**Status:** Nur Anzeige, kein Speichern

**Aktuell:**
- LoRa-Config wird ausgelesen und angezeigt
- Änderungen haben keine Wirkung

**Geplant:**
- AdminMessage: set_config
- Config-Validierung
- Apply + Reboot

### 4. Direct Messages (DMs)

**Status:** Nicht implementiert

**Geplant:**
- Tab-System für verschiedene Conversations
- Node-Liste mit DM-Button
- Unread-Counts
- Konversations-History

### 5. Firmware-Kompatibilität

**Getestet mit:**
- Firmware 2.x

**Bekannte Probleme:**
- Firmware < 2.0: AdminMessages nicht unterstützt
- Manche Custom-Builds: Abweichende Protobuf-Definitionen

---

## 🗺️ Roadmap

### v1.0 (MVP) - In Arbeit

- [x] Serielle Verbindung
- [x] Channel-Loading
- [x] Nachrichten senden/empfangen
- [x] LoRa-Config auslesen
- [x] Multi-Channel-Support
- [ ] Persistente Message-History
- [ ] Config-Bearbeitung und Speichern
- [ ] Robustere Fehlerbehandlung

### v1.1 - Direct Messages

- [ ] DM-Tab-System
- [ ] Node-Liste mit DM-Funktion
- [ ] Separate Conversations
- [ ] Unread-Counts
- [ ] Notification-System

### v1.2 - Erweiterte Features

- [ ] PSK-Entschlüsselung
- [ ] Karten-Ansicht (GPS-Positionen)
- [ ] Mesh-Visualisierung (Graph)
- [ ] Waypoints
- [ ] Telemetrie-Dashboard

### v2.0 - Pro Features

- [ ] Firmware-Update über Client
- [ ] Multi-Device-Support (mehrere Nodes gleichzeitig)
- [ ] Remote-Node-Verwaltung
- [ ] Erweiterte Statistiken
- [ ] Export/Import (Config, Messages)

---

## 📚 Weitere Dokumentation

- **[DEBUG.md](DEBUG.md)** - Debugging & Troubleshooting
- **[SCHNELLSTART.md](SCHNELLSTART.md)** - Deutsche Quick-Start-Anleitung für DAUs
- **[.claude](.claude)** - Projekt-Kontext für AI-Assistenten

---

## 🤝 Beitragen

Contributions sind willkommen!

1. Fork das Repository
2. Feature-Branch erstellen: `git checkout -b feature/amazing-feature`
3. Änderungen committen: `git commit -m 'Add amazing feature'`
4. Branch pushen: `git push origin feature/amazing-feature`
5. Pull Request erstellen

---

## 📜 Lizenz

MIT License - siehe [LICENSE](LICENSE)

---

## 🙏 Credits

- **Meshtastic Project**: https://meshtastic.org
- **Protobuf-Definitionen**: Basierend auf offiziellen Meshtastic Protobufs
- **Python Client**: Referenz-Implementierung für Protokoll-Flow
- **Web Client**: UI/UX Inspiration
- **ModernWPF**: https://github.com/Kinnara/ModernWpf

---

## 📧 Support

Bei Fragen oder Problemen:

1. **Debug-Logs erstellen** (siehe DEBUG.md)
2. Issue auf GitHub öffnen
3. Meshtastic Community:
   - Discord: https://discord.gg/meshtastic
   - Forum: https://meshtastic.discourse.group

---

**Made with ❤️ for the Meshtastic Community**

*Entwickelt mit Claude AI • Stand: Februar 2026*
# mh_windowsclient
