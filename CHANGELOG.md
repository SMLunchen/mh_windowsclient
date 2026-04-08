# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.

Das Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.0.0/),
und dieses Projekt folgt [Semantic Versioning](https://semver.org/lang/de/).

---

## [Unreleased]

### ✨ Hinzugefügt

#### Persistente Nachrichten-Datenbank
- **SQLite-Nachrichtenspeicher** für Kanal- und DM-Nachrichten (optional, in Einstellungen aktivierbar)
  - Je Kanal eine eigene DB-Datei (`messages/channel_{index}_{name}.db`), DMs in `messages/dm.db`
  - WAL-Modus, Insert/LoadSince/LoadBefore/ClearAll/ClearOlderThan, per `partner_id` filterbar
- **Automatisches Laden** der letzten 24 h nach dem Verbinden; älteres Nachladen per Hochscrollen (Lazy Load)
- **DM-History:** Beim Öffnen des DM-Fensters werden alle gespeicherten Konversationen der letzten 24 h automatisch als Tabs wiederhergestellt
- **Pro-Kanal-Löschung** im Kanäle-Tab: neue Spalte „Nachrichten-DB leeren" mit Button pro Zeile
  - Klick öffnet Dialog mit Zeitraum-Auswahl (Alle / Älter als 30 / 90 / 365 Tage)
- **Bestätigung** vor dem Leeren einer DM-Konversation (versehentliches Löschen verhindert)
- **Aufbewahrungsdauer** konfigurierbar (30 / 90 / 365 Tage), Retention wird beim Start angewendet
- Neue Einstellungen: `EnableMessageDb`, `MessageDbRetentionDays`

#### Verbindung merken
- **Letzte Verbindungsart** (Serial / Bluetooth / WiFi) wird in der INI gespeichert und beim Start automatisch vorausgewählt
- **Letztes BT-Gerät** wird gespeichert; nach BT-Scan wird es automatisch in der Geräteliste vorausgewählt
- Neue Einstellungen: `LastConnectionType`, `LastBtDevice`

#### Alert Bell Support
- **🚨 Notruf-Funktion** integriert (Meshtastic Alert Bell Character)
  - SOS-Button in Hauptchat und DM-Fenstern
  - Emoji-basiert (🔔) für Kompatibilität mit Android/Web-Apps
  - **Visuelle Benachrichtigung**: Rote blinkende Umrandung (6 Blinks über 3 Sekunden)
  - **Akustische Benachrichtigung**: Sirenen-Sound (WAV-generiert, funktioniert auch bei stummen System-Sounds)
  - **Notification-Bar**: Erscheint oben im Fenster mit Absender-Name
  - **"Zur Karte springen" Button**: Springt direkt zur Node-Position auf der Karte (Zoom Level 12)
    - Button wird nur angezeigt wenn Position des Nodes bekannt ist
    - Wechselt automatisch zum Karten-Tab und zentriert auf Node
  - Notification verschwindet automatisch nach 30 Sekunden oder manuell schließbar
  - 🔔 Icon in Nachrichtenlisten für empfangene Alert Bells

#### Karten-Erweiterungen
- **OSM Dark Mode**: Dunkle Kartenansicht für bessere Sicht bei Nacht
- **OpenTopoMap**: Topografische Karte mit Höhenlinien
- **Drei Kartentypen** wählbar in Einstellungen: OSM Standard, OSM Dark, OpenTopoMap
- **Eigener Tile-Server**: Umstellung auf eigenen Server (tile.schwarzes-seelenreich.de)
  - OSM-Policy verbietet Offline-Downloads für unsere Nutzung
  - Eigener Server erlaubt explizit Offline-Downloads
  - Tile-Server-URL individuell konfigurierbar in Einstellungen
- **Rate-Limiting** nur für externe Server (nicht für eigene Server)
- **Copyright-Hinweise** auf der Karte (unten rechts)
  - Dynamischer Text je nach Kartenquelle (OSM, OpenTopoMap)
  - Verlinkung zu Datenquellen
- **Support für weitere Bundesländer**: Offline-Tiles für ganz Deutschland und angrenzende Gebiete

### 🐛 Behoben

- **Zombie-Prozess beim Beenden**: App beendet sich jetzt sauber mit `Application.Current.Shutdown()`
  - Synchroner Disconnect statt asynchron
  - Keine hängenden Prozesse mehr nach Fenster-Schließen
- **Tab-Navigation**: "Zur Karte" Button springt jetzt korrekt zum Karten-Tab (nicht zur Node-Liste)

### 🔄 Geändert

- **Alert Bell Format**: Umstellung von ASCII Control Character (0x07) auf Emoji (🔔)
  - Kompatibel mit Android und Web-Apps
  - Emoji wird beim Empfang automatisch aus Nachrichtentext entfernt
  - Unterstützt beide Varianten beim Empfang (ASCII + Emoji)

---

## [1.0-Beta] - 2026-02-08

### ✨ Hinzugefügt

#### Direct Messages (DMs)
- **Separates DM-Fenster** mit Tab-System für verschiedene Konversationen
- **Automatische Benachrichtigungen** bei neuen DMs:
  - Fenster wird automatisch angezeigt und in den Vordergrund gebracht
  - Taskbar blinkt bei neuer Nachricht (Windows API Integration)
  - System-Sound Benachrichtigung
- **Orange Hervorhebung** und **fette Schrift** für ungelesene Nachrichten in Tabs
- **Rechtsklick-Menü** in Knoten-Tab: "💬 DM senden" öffnet direkt Chat
- **Gesendete DM-Nachrichten** werden jetzt korrekt im Chat angezeigt
- DM-Button in Toolbar mit Bold-Markierung bei neuen ungelesenen Nachrichten

#### Message-Logging
- **Automatisches Logging** aller Nachrichten (Kanal + DMs)
- **Separate Log-Dateien** pro Kanal: `logs/Channel_[Index]_[Name].log`
- **Separate Log-Dateien** pro DM-Partner: `logs/DM_[NodeID]_[Name].log`
- **Speicherort**: Direkt neben der EXE im Ordner `logs/`
- **Log-Format**: `[yyyy-MM-dd HH:mm:ss] Absender: Nachricht`
- **UTF-8 Encoding** für korrekte Umlaute und Sonderzeichen
- **Thread-sicheres Schreiben** mit Lock-Mechanismus
- **Automatische Sanitisierung** von Dateinamen (ungültige Zeichen werden ersetzt)
- MessageLogger Service für zentralisierte Logging-Logik

#### UI-Verbesserungen
- **Dark Mode**: Umschaltbar in Einstellungen-Tab
  - Vollständige Unterstützung durch ModernWPF Theme
  - Einstellung wird live angewendet
- **Kanalfilter mit Buffering**:
  - Dropdown über Nachrichtenliste zum Filtern nach Kanal
  - "Alle Kanäle" zeigt alle Nachrichten
  - Nachrichten werden in separater Liste gepuffert und bleiben beim Filterwechsel erhalten
- **Kanalnamen statt Nummern**:
  - Nachrichten zeigen Kanalnamen (z.B. "Primary", "LongFast")
  - Fallback auf "Kanal [X]" wenn Name nicht verfügbar
- **Intuitivere Kanalauswahl**:
  - Kanal-Dropdown jetzt direkt neben Senden-Button
  - Logischer Workflow: Kanal wählen → Nachricht eingeben → Senden
  - Toolbar aufgeräumt (Kanalauswahl entfernt)
- **Verbindungsstatus-Anzeige**:
  - **Grau**: Nicht verbunden
  - **Gelb**: Verbinde...
  - **Orange**: Initialisiere... (Config wird geladen)
  - **Grün**: Verbunden und bereit
- **Quietscheentchen-Icon** 🦆: Freies Icon für EXE
- **Meshhessen.de Branding**: Im Footer mit Link-Farbe hervorgehoben

#### Technische Features
- DirectMessagesWindow.xaml/.cs für DM-Verwaltung
- DirectMessageConversation Model für Konversationsverwaltung
- MessageLogger Service mit thread-sicherer Datei-Verwaltung
- Windows API Integration für Taskbar-Benachrichtigungen (FlashWindow)
- Verbesserte ConnectionStatus Enum mit mehr Zuständen

### 🐛 Behoben

- **Filter-Buffering**: Nachrichten verschwinden nicht mehr beim Filtern
  - Separate `_allMessages` Liste speichert alle Nachrichten ungefiltert
  - `_messages` zeigt nur gefilterte Ansicht
  - Beim Filterwechsel werden Nachrichten aus `_allMessages` neu gefiltert
- **DM gesendete Nachrichten**: Eigene DMs werden jetzt im Chat angezeigt
- **Thread-Sicherheit**: Alle Message-Logs sind thread-sicher implementiert
- **Verbindungsstatus**: Stabilere und aussagekräftigere Status-Anzeige

### 🔄 Geändert

- **Kanalauswahl** von Toolbar nach unten zu Nachrichten-Eingabe verschoben
- **Log-Speicherort** von `%LocalAppData%` zu `[EXE-Verzeichnis]/logs/`
- **Toolbar-Layout**: Reduziert auf 2 Spalten (Connection Controls | Status)
- **MessageItem Model**: Neue Felder `ChannelName`, `FromId`, `ToId`, `IsEncrypted`
- **Ready-Status Farbe**: Von LightGreen zu LimeGreen (kräftigeres Grün)

### 📝 Dokumentation

- README komplett überarbeitet mit allen neuen Features
- Neue Sektion "Message-Logging" mit Beispielen
- Erweiterte "Verwendung" Sektion mit DM-Anleitung
- Detaillierter Changelog (diese Datei)
- Aktualisierte Projekt-Struktur in README
- Screenshots-Beschreibungen hinzugefügt

---

## [0.1-Alpha] - 2025-12-XX

### ✨ Hinzugefügt

#### Basis-Funktionalität
- **Serielle USB-Verbindung**
  - Automatische COM-Port-Erkennung
  - Framing-Protokoll (0x94 0xC3)
  - Protobuf-Parsing (FromRadio/ToRadio)
- **Nachrichten senden/empfangen**
  - Text-Nachrichten über TEXT_MESSAGE_APP
  - Broadcast-Nachrichten
  - Verschlüsselte Nachrichten Erkennung
- **Multi-Channel-Support**
  - Automatisches Laden aller Channels (0-7)
  - Channel-Auswahl in Toolbar
  - PRIMARY/SECONDARY Rollen
  - PSK-Anzeige (Base64)
- **Knoten-Übersicht**
  - Liste aller Nodes im Mesh
  - Node-ID, Name, SNR, Distanz
  - Batteriestatus
  - Letzte Aktivität
- **Geräteeinstellungen**
  - LoRa-Config auslesen (Region, Modem Preset)
  - Device-Info anzeigen
  - Hardware-Model und Firmware-Version
- **Debug-Modus**
  - Intensives Logging
  - DebugView-Kompatibilität
  - Live-Log im Debug-Tab
  - Log-Export Funktionen

#### Technische Basis
- WPF Application mit ModernWPF UI
- SerialPortService für Low-Level Serial I/O
- MeshtasticProtocolService für Protobuf-Handling
- Logger Service für Debug-Ausgaben
- Models: MessageItem, NodeInfo, ChannelInfo, DeviceInfo
- Protobuf-Definitionen (mesh.proto, portnums.proto, admin.proto)

---

## Geplante Features

### v1.1 - Persistence
- SQLite-Datenbank für Message-History
- History beim Start laden
- Message-Suche
- Export/Import von Nachrichten

### v1.2 - Config-Management
- Config-Bearbeitung und Speichern
- Kanal hinzufügen/bearbeiten/löschen
- PSK-Generator
- Device-Remote-Config

### v1.3 - Erweiterte Features
- PSK-Entschlüsselung
- Karten-Ansicht (GPS-Positionen)
- Mesh-Visualisierung (Graph)
- Waypoints
- Telemetrie-Dashboard

### v2.0 - Pro Features
- Firmware-Update über Client
- Multi-Device-Support
- Remote-Node-Verwaltung
- Erweiterte Statistiken
- Mehrsprachigkeit

---

## Legende

- ✨ **Hinzugefügt** - Neue Features
- 🐛 **Behoben** - Bug-Fixes
- 🔄 **Geändert** - Änderungen an bestehenden Features
- ⚠️ **Veraltet** - Bald zu entfernende Features
- 🗑️ **Entfernt** - Entfernte Features
- 🔒 **Sicherheit** - Sicherheits-relevante Änderungen
- 📝 **Dokumentation** - Dokumentations-Änderungen

---

**Projekt**: Meshtastic Windows Client
**Lizenz**: MIT
**Website**: Meshhessen.de
**Entwickelt mit**: Claude AI
