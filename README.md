# Meshhessen Client

Ein **offline-fähiger, nativer Windows-Client** für Meshtastic Geräte mit USB/serieller Verbindung.

![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Status](https://img.shields.io/badge/Status-v1.0--Beta-yellow)

---

## 📋 Inhaltsverzeichnis

- [Features](#features)
- [Screenshots](#screenshots)
- [Schnellstart](#schnellstart)
- [Installation & Build](#installation--build)
- [Verwendung](#verwendung)
- [Message-Logging](#message-logging)
- [Debugging](#debugging)
- [Architektur](#architektur)
- [Bekannte Einschränkungen](#bekannte-einschränkungen)
- [Changelog](#changelog)
- [Roadmap](#roadmap)

---

## ✨ Features

### Implementiert ✅

#### 📡 Verbindung & Kommunikation
- **Serielle USB-Verbindung**
  - Automatische COM-Port-Erkennung
  - Stabile Verbindung mit Framing-Protokoll (0x94 0xC3)
  - Automatisches Protobuf-Parsing
  - Statusanzeige: Grau (Getrennt) → Gelb (Verbinde) → Orange (Initialisiere) → Grün (Bereit)

- **Nachrichten senden/empfangen**
  - Text-Nachrichten über TEXT_MESSAGE_APP
  - Gesendete Nachrichten werden in UI angezeigt
  - Empfangene Nachrichten mit Absender
  - Verschlüsselte Nachrichten werden erkannt
  - Kanal-basiertes Messaging
  - Intuitive Kanalauswahl neben Senden-Button

- **Multi-Channel-Support**
  - Automatisches Laden aller konfigurierten Channels (0-7)
  - Kanal-Auswahl direkt beim Senden
  - PRIMARY/SECONDARY Channel-Rollen
  - PSK-Anzeige (Base64)
  - **Kanalfilter**: Nachrichten nach Kanal filtern
  - **Kanalnamen**: Zeigt Kanalnamen statt Nummern

#### 💬 Direct Messages (DMs)
- **Separates DM-Fenster**
  - Tab-System für verschiedene Konversationen
  - Automatische Tab-Erstellung bei neuer Konversation
  - Orange Hervorhebung bei ungelesenen Nachrichten
  - Fett markierte Tab-Namen für neue Nachrichten

- **Benachrichtigungen**
  - Fenster wird automatisch angezeigt bei neuer DM
  - Taskbar blinkt bei neuer Nachricht
  - System-Sound Benachrichtigung
  - Fenster wird in den Vordergrund gebracht

- **DM-Funktionen**
  - Gesendete Nachrichten werden angezeigt
  - Rechtsklick auf Knoten → "💬 DM senden"
  - Direkter Chat-Start aus Knoten-Tab
  - Separate Conversations pro DM-Partner

#### 📝 Message-Logging
- **Automatisches Logging**
  - Separate Log-Dateien pro Kanal
  - Separate Log-Dateien pro DM-Partner
  - Format: `[yyyy-MM-dd HH:mm:ss] Absender: Nachricht`
  - Speicherort: `[EXE-Verzeichnis]/logs/`
  - UTF-8 Encoding für korrekte Umlaute

- **Log-Dateien**
  - Kanal-Logs: `Channel_[Index]_[Name].log`
  - DM-Logs: `DM_[NodeID]_[Name].log`
  - Thread-sicheres Schreiben
  - Automatische Sanitisierung von Dateinamen

#### 🌐 Knoten-Übersicht
- Alle Nodes im Mesh
- Node-ID, Name, SNR
- Letzte Aktivität
- Position (wenn vorhanden)
- Batteriestatus
- **Rechtsklick-Menü**: Direkt DM an Knoten senden

#### ⚙️ Geräteeinstellungen
- LoRa-Konfiguration auslesen (Region, Modem Preset)
- Automatische UI-Aktualisierung bei Connect
- Region-Auswahl (EU_868, US, etc.)
- Modem-Preset-Auswahl (LONG_FAST, SHORT_SLOW, etc.)
- Geräteinfo und Firmware-Version

#### 🎨 Benutzeroberfläche
- **Dark Mode**: Umschaltbar in Einstellungen
- **ModernWPF**: Fluent Design System
- **Intuitives Layout**: Kanalauswahl beim Senden
- **Quietscheentchen-Icon**: 🦆 Freies Icon
- **Footer-Branding**: Meshhessen.de Link

#### 🔧 Erweiterte Features
- **Offline-Fähigkeit**
  - Keine Internet-Verbindung erforderlich
  - Standalone EXE (~159 MB mit .NET Runtime)
  - Komplett self-contained

- **Debug-Modus**
  - Intensives Logging für Troubleshooting
  - DebugView-Kompatibilität
  - Packet-Tracing
  - Event-Flow-Logging
  - Live-Log im Debug-Tab

### Geplant 📋

- **Message-Features**
  - Nachrichtenverlauf beim Neustart laden (SQLite)
  - PSK-Entschlüsselung für verschlüsselte Channels
  - Message-Suche
  - Emoji-Picker

- **Kanal-Management**
  - Config-Bearbeitung und Speichern
  - Kanal hinzufügen/bearbeiten/löschen
  - PSK-Generator

- **Visualisierung**
  - Karten-Ansicht für Node-Positionen
  - Mesh-Visualisierung (Graph)
  - GPS-Wegpunkte
  - Telemetrie-Dashboard

- **Erweiterte Features**
  - Mehrsprachigkeit (Englisch)

---

## 📸 Screenshots

### Hauptfenster
- Nachrichten-Tab mit Kanalfilter
- Kanal-Auswahl neben Senden-Button
- Statusanzeige (Verbindung)
- DM-Button in Toolbar

### Direktnachrichten
- Separates DM-Fenster
- Tab-System für verschiedene Konversationen
- Orange Markierung für neue Nachrichten

### Knoten-Übersicht
- Alle Nodes im Mesh
- Rechtsklick-Menü für DM

### Einstellungen
- LoRa-Konfiguration
- Dark Mode Toggle
- Geräteinfo

---

## 🚀 Schnellstart

### Option 1: Fertige EXE (Empfohlen für Endbenutzer)

1. **Download:** `public\MeshhessenClient.exe`
2. **Gerät anschließen:** Meshtastic-Device per USB
3. **Starten:** Doppelklick auf `MeshhessenClient.exe`
4. **Verbinden:**
   - COM-Port wählen → "Verbinden"
   - Status wird **Gelb** (Verbinde) → **Orange** (Initialisiere) → **Grün** (Bereit)
5. **Warten:** 3-10 Sekunden bis Channels geladen sind
6. **Kanal wählen:** Dropdown neben Senden-Button
7. **Loslegen:** Nachrichten senden und empfangen

### Option 2: Aus Quellcode bauen

**Voraussetzungen:**
- .NET 8.0 SDK oder höher
- Windows 10/11 x64

**Build-Befehle:**

```bash
# Projekt klonen
git clone https://github.com/SMLunchen/mh_windowsclient/meshtastic-windows-client.git
cd meshtastic-windows-client

# Bauen
dotnet restore
dotnet build -c Release

# Standalone EXE erstellen
dotnet publish -c Release

# EXE ist hier:
public\MeshhessenClient.exe
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
3. Solution öffnen: `MeshhessenClient.sln`
4. F5 drücken zum Debuggen

### Projekt-Struktur

```
windows-client/
├── MeshhessenClient.sln          # Visual Studio Solution
├── README.md                      # Diese Datei
├── CHANGELOG.md                   # Änderungsprotokoll
├── LICENSE                        # MIT Lizenz
│
├── public/                        # Fertige EXE (nach Build)
│   ├── MeshhessenClient.exe       # Standalone executable
│   └── logs/                      # Message-Logs (automatisch erstellt)
│       ├── Channel_0_Primary.log
│       └── DM_DEADBEEF_Node123.log
│
└── MeshhessenClient/
    ├── MeshhessenClient.csproj    # Projekt-Konfiguration
    ├── App.xaml + .cs             # WPF Application Entry Point
    ├── MainWindow.xaml + .cs      # Haupt-UI
    ├── DirectMessagesWindow.xaml + .cs  # DM-Fenster
    │
    ├── Models/                    # Datenmodelle
    │   ├── MessageItem.cs         # Nachrichten
    │   ├── NodeInfo.cs            # Node-Informationen
    │   ├── ChannelInfo.cs         # Kanal-Konfiguration
    │   ├── DeviceInfo.cs          # Geräte-Informationen
    │   └── DirectMessageConversation.cs  # DM-Konversationen
    │
    ├── Services/                  # Geschäftslogik
    │   ├── SerialPortService.cs            # USB/Serielle Kommunikation
    │   ├── MeshtasticProtocolService.cs    # Meshtastic-Protokoll
    │   ├── Logger.cs                       # Debug-Logging
    │   └── MessageLogger.cs                # Nachrichten-Logging
    │
    ├── Resources/                 # Assets
    │   └── app.ico                # Quietscheentchen-Icon 🦆
    │
    └── Proto/                     # Protobuf-Definitionen
        ├── mesh.proto             # Meshtastic Hauptprotokoll
        ├── portnums.proto         # Port-Nummern
        └── admin.proto            # Admin-Messages
```

---

## 📖 Verwendung

### Erste Verbindung

1. **Gerät vorbereiten**
   - Meshtastic-Device einschalten
   - Per USB an PC anschließen
   - Windows installiert automatisch Treiber (CP210x oder ähnlich)

2. **Client starten**
   - `MeshhessenClient.exe` ausführen
   - Klick auf "🔄" um Ports zu aktualisieren

3. **Verbinden**
   - COM-Port auswählen (z.B. COM3, COM4)
   - "Verbinden" klicken
   - **Status-Anzeige beobachten:**
     - **Grau**: Nicht verbunden
     - **Gelb**: Verbinde...
     - **Orange**: Initialisiere... (Config wird geladen)
     - **Grün**: Verbunden und bereit
   - **Warten:** 3-10 Sekunden bis Config geladen ist

4. **Channels nutzen**
   - Channels erscheinen automatisch in Dropdown
   - Kanal direkt beim Senden auswählen

### Nachrichten senden

1. **Kanal wählen:** Dropdown neben Nachrichtenfeld
2. **Text eingeben:** In Nachrichtenfeld
3. **Senden:** Enter drücken oder "Senden" klicken
4. Nachricht erscheint sofort in Liste als "Ich"
5. Wird automatisch geloggt in `logs/Channel_[X]_[Name].log`

### Nachrichten filtern

1. **Kanalfilter nutzen:** Dropdown über Nachrichtenliste
2. **"Alle Kanäle"** zeigt alle Nachrichten
3. **Spezifischer Kanal** zeigt nur Nachrichten dieses Kanals
4. Filter ist persistent - alle empfangenen Nachrichten werden gepuffert

### Direct Messages (DMs)

#### Option 1: Aus Knoten-Tab
1. Tab "🌐 Knoten" öffnen
2. **Rechtsklick** auf gewünschten Knoten
3. "💬 DM senden" wählen
4. DM-Fenster öffnet sich mit Chat-Tab

#### Option 2: Aus DM-Button
1. Button "💬 DMs" in Toolbar klicken
2. DM-Fenster öffnet sich
3. Tabs zeigen alle aktiven Konversationen

#### DM-Features
- **Neue Nachrichten:**
  - DM-Fenster öffnet sich automatisch
  - Taskbar blinkt
  - System-Sound
  - Tab-Name wird **fett** und **orange**

- **Konversationen:**
  - Separate Tabs pro DM-Partner
  - Gesendete + empfangene Nachrichten
  - Automatisches Logging in `logs/DM_[NodeID]_[Name].log`

### Knoten anzeigen

1. Tab "🌐 Knoten" öffnen
2. Alle Nodes im Mesh werden gelistet
3. **Rechtsklick** auf Knoten für DM-Funktion
4. Aktualisiert sich automatisch bei neuen Nodes

### Einstellungen

1. Tab "⚙️ Einstellungen" öffnen
2. **Darstellung:**
   - Dark Mode ein/aus
   - Verschlüsselte Nachrichten anzeigen/ausblenden
3. **Aktuelle Werte** werden vom Gerät geladen:
   - Region (z.B. EU_868)
   - Modem Preset (z.B. SHORT_SLOW)
   - Gerätename, Hardware-Model, Firmware-Version
4. **Hinweis:** Speichern noch nicht implementiert

---

## 📝 Message-Logging

### Automatisches Logging

Alle Nachrichten (Kanal + DMs) werden automatisch geloggt:

**Speicherort:** `[EXE-Verzeichnis]/logs/`

**Kanal-Logs:**
```
logs/
├── Channel_0_Primary.log
├── Channel_1_LongFast.log
└── Channel_2_ShortSlow.log
```

**DM-Logs:**
```
logs/
├── DM_DEADBEEF_Alice.log
├── DM_CAFEBABE_Bob.log
└── DM_12345678_Charlie.log
```

### Log-Format

```
[2026-02-08 12:30:45] Alice: Hallo, wie geht's?
[2026-02-08 12:31:02] Ich: Gut, danke!
[2026-02-08 12:31:15] Alice: Schön zu hören!
```

**Felder:**
- `[Timestamp]` - Datum und Uhrzeit (yyyy-MM-dd HH:mm:ss)
- `Absender` - Name oder "Ich" für eigene Nachrichten
- `Nachricht` - Nachrichtentext

### Features

- **UTF-8 Encoding** - Umlaute und Sonderzeichen korrekt
- **Thread-sicher** - Keine Daten-Kollisionen
- **Automatische Sanitisierung** - Ungültige Zeichen in Dateinamen werden ersetzt
- **Persistent** - Logs bleiben nach Neustart erhalten

---

## 🔍 Debugging

### Debug-Tab nutzen

1. Tab "🐛 Debug" öffnen
2. Live-Log wird angezeigt
3. Buttons:
   - "Log löschen" - Löscht Anzeige
   - "Log kopieren" - Kopiert in Zwischenablage
   - "Log-Datei öffnen" - Öffnet Log-Datei

### Problem: Keine Channels/Nachrichten

Der Client hat **intensives Debug-Logging**. So siehst du die Logs:

#### Mit DebugView (Empfohlen)

1. **Download:** https://learn.microsoft.com/en-us/sysinternals/downloads/debugview
2. **Starten** (als Administrator)
3. **Capture → Capture Win32** aktivieren
4. **Client starten:** `MeshhessenClient.exe`
5. **Logs live sehen:**
   ```
   === Initializing Meshtastic connection ===
   Serial data received: 127 bytes
   Found packet: length=123
   Received FromRadio packet, type: Channel
   HandleChannel called: Index=0, Name=LongFast
   TEXT MESSAGE: Text="Hello"
   ```

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

---

## 🏗️ Architektur

### Technologie-Stack

- **.NET 8.0 / C#** - Moderne, performante Entwicklung
- **WPF (Windows Presentation Foundation)** - Native Windows UI
- **ModernWPF** - Fluent Design System mit Dark Mode
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

### Connection Sequence

```
1. Serial Port öffnen (115200 baud)
   ├─ Status: Gelb (Verbinde)

2. ToRadio.want_config_id senden
   ├─ Status: Orange (Initialisiere)

3. Warten auf FromRadio Messages:
   ├─ my_info (Node-ID)
   ├─ node_info (Nodes im Mesh)
   ├─ channel (x8, alle Channels)
   ├─ config (Device/LoRa Config)
   └─ config_complete_id

4. Status: Grün (Bereit)
5. Bereit für MeshPackets
```

### Code-Struktur

**MainWindow.xaml.cs** - Haupt-UI Controller
- Event-Handler für UI-Elemente
- ObservableCollections für Data-Binding
- Message-Filter mit Buffering
- Dispatcher für Thread-sichere UI-Updates

**DirectMessagesWindow.xaml.cs** - DM-Fenster
- Tab-Management für Konversationen
- Benachrichtigungssystem
- Windows API Integration (Taskbar-Blinken)

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

**MessageLogger.cs** - Nachrichten-Logging
- Thread-sicheres Datei-Schreiben
- Separate Logs pro Kanal/DM
- UTF-8 Encoding
- Automatische Sanitisierung

**Models/** - Datenmodelle
- UI-freundliche DTOs
- Mapping von Protobuf → UI Models

---

## ⚠️ Bekannte Einschränkungen

### 1. Keine persistente Message-History

**Problem:** Beim Client-Neustart sind alte Nachrichten weg (nur in UI, Logs bleiben).

**Workaround:**
- Nachrichten werden in Log-Dateien gespeichert
- UI lädt noch keine History beim Start

**Geplant:** SQLite-Datenbank für lokale History mit Reload beim Start

### 2. Verschlüsselte Nachrichten

**Problem:** PSK-verschlüsselte Nachrichten können nicht entschlüsselt werden.

**Aktuelles Verhalten:**
- Zeigt `[Verschlüsselte Nachricht - PSK erforderlich]`
- Kann in Einstellungen ausgeblendet werden

**Geplant:** PSK-Entschlüsselung im Client

### 3. Config-Bearbeitung

**Status:** Nur Anzeige, kein Speichern

**Aktuell:**
- LoRa-Config wird ausgelesen und angezeigt
- Änderungen haben keine Wirkung

**Geplant:**
- AdminMessage: set_config
- Config-Validierung
- Apply + Reboot

### 4. Firmware-Kompatibilität

**Getestet mit:**
- Firmware 2.x

**Bekannte Probleme:**
- Firmware < 2.0: AdminMessages nicht unterstützt

---

## 📋 Changelog

### v1.0-Beta (Februar 2026)

#### ✨ Neue Features
- **Direct Messages (DMs)**
  - Separates DM-Fenster mit Tab-System
  - Automatische Benachrichtigungen (Fenster, Taskbar, Sound)
  - Rechtsklick auf Knoten → DM senden
  - Orange Hervorhebung bei neuen Nachrichten

- **Message-Logging**
  - Automatische Log-Dateien pro Kanal
  - Automatische Log-Dateien pro DM-Partner
  - Speicherort: `[EXE-Verzeichnis]/logs/`
  - Format: `[Timestamp] Absender: Nachricht`

- **UI-Verbesserungen**
  - Dark Mode (umschaltbar in Einstellungen)
  - Kanalfilter mit Buffering
  - Kanalnamen statt Nummern
  - Kanalauswahl neben Senden-Button (intuitiver)
  - Verbindungsstatus: Grau → Gelb → Orange → Grün
  - Quietscheentchen-Icon 🦆
  - Meshhessen.de Branding im Footer

#### 🐛 Bugfixes
- Filter-Buffering: Nachrichten verschwinden nicht mehr beim Filtern
- Thread-sichere Message-Logs
- Stabilere Verbindungsanzeige

#### 🏗️ Technisch
- MessageLogger Service
- DirectMessagesWindow mit Benachrichtigungssystem
- Windows API Integration (Taskbar-Blinken)
- Verbesserte ObservableCollection-Verwaltung

### v0.1-Alpha (Dezember 2025)
- Initiale Version
- Serielle Verbindung
- Basis-Messaging
- Multi-Channel-Support
- Knoten-Übersicht

---

## 🗺️ Roadmap

### v1.1 - Persistence
- [ ] SQLite-Datenbank für Message-History
- [ ] History beim Start laden
- [ ] Message-Suche
- [ ] Export/Import von Nachrichten

### v1.2 - Config-Management
- [ ] Config-Bearbeitung und Speichern
- [ ] Kanal hinzufügen/bearbeiten/löschen
- [ ] PSK-Generator
- [ ] Device-Remote-Config

### v1.3 - Erweiterte Features
- [ ] PSK-Entschlüsselung
- [ ] Karten-Ansicht (GPS-Positionen)
- [ ] Mesh-Visualisierung (Graph)
- [ ] Waypoints
- [ ] Telemetrie-Dashboard

### v2.0 - Pro Features
- [ ] Remote-Node-Verwaltung
- [ ] Erweiterte Statistiken
- [ ] Mehrsprachigkeit

---

## 📚 Weitere Dokumentation

- **[CHANGELOG.md](CHANGELOG.md)** - Detailliertes Änderungsprotokoll
- **Meshtastic Docs**: https://meshtastic.org/docs


## 🙏 Credits

- **Meshtastic Project**: https://meshtastic.org
- **Protobuf-Definitionen**: Basierend auf offiziellen Meshtastic Protobufs
- **ModernWPF**: https://github.com/Kinnara/ModernWpf
- **Icon**: Freies Quietscheentchen-Icon 🦆
- **Community**: Meshhessen.de

---

**Made with ❤️ for the Meshtastic Community**


**Unterstützt von Meshhessen.de** 🦆
