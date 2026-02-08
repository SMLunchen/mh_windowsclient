# Meshtastic Windows Client - Projekt-Kontext für AI-Assistenten

## Projekt-Übersicht

Ein nativer Windows-Client für Meshtastic Geräte mit USB/serieller Verbindung.

- **Sprache:** C# / .NET 8.0
- **UI-Framework:** WPF (Windows Presentation Foundation) mit ModernWPF
- **Protokoll:** Meshtastic Protobuf über serielle Schnittstelle
- **Status:** Beta / MVP in Entwicklung
- **Zielgruppe:** DAUs (Dümmster Anzunehmender User) - einfache Bedienung wichtig!

## Entwicklungs-Stand (Februar 2026)

### ✅ Implementiert

1. **Serielle Kommunikation**
   - `SerialPortService.cs` - System.IO.Ports Wrapper
   - Framing: 0x94 0xC3 + Length (BE) + Protobuf Data
   - 115200 baud, 8N1
   - Async Read/Write mit Events

2. **Meshtastic-Protokoll**
   - `MeshtasticProtocolService.cs` - Protobuf-Handler
   - FromRadio/ToRadio Messages
   - MeshPacket Routing (PortNum → Handler)
   - TEXT_MESSAGE_APP, NODEINFO_APP, POSITION_APP, ADMIN_APP, TELEMETRY_APP

3. **Config-Loading**
   - `want_config_id` Sequenz
   - Channels werden automatisch über `FromRadio.channel` empfangen (0-7)
   - LoRa-Config über `FromRadio.config` oder AdminMessage
   - NodeInfo-Datenbank
   - Warten auf `config_complete_id`

4. **Multi-Channel-Support**
   - Toolbar mit Kanal-Dropdown
   - Automatische Auswahl des PRIMARY Channels
   - Kanalbasiertes Senden

5. **UI (WPF)**
   - MainWindow mit 4 Tabs: Nachrichten, Knoten, Kanäle, Einstellungen
   - ObservableCollections für Data-Binding
   - Dispatcher.Invoke für Thread-sichere UI-Updates
   - ModernWPF für Fluent Design

6. **Debug-Logging**
   - Intensives System.Diagnostics.Debug.WriteLine
   - DebugView-kompatibel
   - Packet-Tracing
   - Event-Flow-Logging

### 🚧 In Arbeit

- Persistente Message-History (SQLite geplant)
- Config-Bearbeitung (AdminMessage: set_config)
- Direct Messages mit Tab-System
- PSK-Entschlüsselung

### ❌ Bekannte Probleme

1. **Channels werden nicht immer geladen**
   - Timing-Problem: Config-Complete kommt manchmal vor Channels
   - Lösung: Längere Wartezeit (10s) nach config_complete

2. **Nachrichten kommen nicht in UI an**
   - Event-Handler manchmal nicht registriert
   - Debug-Logs zeigen "Firing MessageReceived event"
   - Prüfen: Event-Subscription in MainWindow Constructor

3. **Verschlüsselte Nachrichten**
   - Zeigt nur Hinweis "[Verschlüsselt]"
   - PSK-Entschlüsselung nicht implementiert
   - **Wichtig:** Verschlüsselung passiert AUF DEM NODE!
   - Über Serial sollten Nachrichten unverschlüsselt ankommen (wenn PSK stimmt)

## Architektur

### Schichten

```
┌─────────────────────────────────────┐
│        MainWindow.xaml.cs          │ ← UI Controller
│    (ObservableCollections, Events)  │
├─────────────────────────────────────┤
│   MeshtasticProtocolService.cs     │ ← Protokoll-Logik
│  (Protobuf, Routing, Events)       │
├─────────────────────────────────────┤
│      SerialPortService.cs          │ ← Serial I/O
│   (Framing, Async Read/Write)      │
├─────────────────────────────────────┤
│       System.IO.Ports              │ ← .NET Framework
└─────────────────────────────────────┘
        ↕ USB/Serial (115200 baud)
┌─────────────────────────────────────┐
│       Meshtastic Node              │
└─────────────────────────────────────┘
```

### Wichtige Klassen

**SerialPortService**
- `ConnectAsync(string portName, int baudRate = 115200)`
- `Disconnect()`
- `WriteAsync(byte[] data)`
- Event: `DataReceived(object sender, byte[] data)`
- Event: `ConnectionStateChanged(object sender, bool isConnected)`

**MeshtasticProtocolService**
- `InitializeAsync()` - Config-Loading-Sequenz
- `SendTextMessageAsync(string text, uint destinationId, uint channel)`
- Events:
  - `MessageReceived(object sender, MessageItem message)`
  - `NodeInfoReceived(object sender, NodeInfo node)`
  - `ChannelInfoReceived(object sender, ChannelInfo channel)`
  - `LoRaConfigReceived(object sender, LoRaConfig config)`

**MainWindow**
- ObservableCollections: `_messages`, `_nodes`, `_channels`
- Event-Handler für UI-Buttons
- Dispatcher.Invoke für Thread-Safety

### Protokoll-Flow

**Connection:**
```
1. SerialPort.Open()
2. SendToRadioAsync(ToRadio { want_config_id })
3. Warten auf FromRadio Messages:
   - my_info (Node-ID)
   - channel (x8, alle Channels)
   - node_info (Nodes im Mesh)
   - config (LoRa, Device, etc.)
   - config_complete_id
4. Config vollständig → UI aktivieren
```

**Message Send:**
```
User Input
  → MainWindow.SendMessage()
  → MeshtasticProtocolService.SendTextMessageAsync()
  → MeshPacket { decoded: { portnum: TEXT_MESSAGE_APP, payload: UTF-8 }}
  → ToRadio { packet }
  → SerialPortService.WriteAsync()
  → [0x94 0xC3] [Len] [Protobuf]
  → Node → LoRa
```

**Message Receive:**
```
LoRa → Node
  → [0x94 0xC3] [Len] [Protobuf]
  → SerialPort DataReceived Event
  → MeshtasticProtocolService.OnDataReceived()
  → ProcessBuffer() → FindPacketStart()
  → ProcessPacket() → FromRadio.Parser
  → HandleFromRadio() → switch (PayloadVariantCase)
  → HandleMeshPacket() → switch (data.Portnum)
  → HandleTextMessage()
  → MessageReceived Event
  → MainWindow.OnMessageReceived()
  → Dispatcher.Invoke(() => _messages.Add(messageItem))
  → ObservableCollection Update
  → UI Render
```

## Code-Konventionen

### Naming

- **Services:** `...Service.cs` (e.g. SerialPortService)
- **Models:** UI-DTOs ohne "Model" Suffix (e.g. MessageItem, NodeInfo)
- **Protobuf:** Import mit Alias wenn Konflikt:
  ```csharp
  using ProtoNodeInfo = Meshtastic.Protobufs.NodeInfo;
  using ModelNodeInfo = MeshtasticClient.Models.NodeInfo;
  ```

### Threading

**Wichtig:** Serial DataReceived Event läuft auf anderem Thread!
- **Immer** `Dispatcher.Invoke()` für UI-Updates
- Keine UI-Elemente direkt im Event-Handler ändern

### Error Handling

- Try-Catch in allen Event-Handlers
- Debug.WriteLine für Logging
- Exceptions nicht schlucken - loggen!
- User-Feedback über MessageBox oder StatusBar

### Protobuf

- Proto-Dateien in `MeshtasticClient/Proto/`
- Build-Zeit Code-Generierung via Grpc.Tools
- Namespace: `Meshtastic.Protobufs`
- Import-Pfad: `ProtoRoot="Proto"` in .csproj

## Häufige Aufgaben

### Neue Protobuf Message hinzufügen

1. `.proto` Datei in `Proto/` erstellen/anpassen
2. Import in `mesh.proto` oder `admin.proto` falls nötig
3. Build → Auto-generierte C# Klassen in `obj/`
4. Handler in `MeshtasticProtocolService.cs`:
   ```csharp
   case FromRadio.PayloadVariantOneofCase.NewMessage:
       HandleNewMessage(fromRadio.NewMessage);
       break;
   ```

### Neuen PortNum Handler hinzufügen

In `HandleMeshPacket()`:
```csharp
case 123: // NEW_APP
    System.Diagnostics.Debug.WriteLine("  -> NEW_APP");
    HandleNewApp(packet, data);
    break;
```

### UI-Element hinzufügen

1. XAML in `MainWindow.xaml`:
   ```xml
   <Button x:Name="MyButton" Click="MyButton_Click" />
   ```
2. Code-Behind in `MainWindow.xaml.cs`:
   ```csharp
   private void MyButton_Click(object sender, RoutedEventArgs e)
   {
       // UI-Updates nur im Dispatcher!
   }
   ```
3. ObservableCollection für Listen:
   ```csharp
   private ObservableCollection<MyItem> _items = new();
   // In Constructor:
   MyListView.ItemsSource = _items;
   ```

### Debug-Logging hinzufügen

```csharp
System.Diagnostics.Debug.WriteLine($"Status: {value}");
```

**Logs sehen:**
- DebugView (Download von Sysinternals)
- Visual Studio Output Window (Debug-Modus)

## Bekannte Eigenheiten

### Verschlüsselung

**Wichtig:** Verschlüsselung passiert **auf dem Node**, nicht im Client!

- Node hat PSK konfiguriert
- Node verschlüsselt vor LoRa-Übertragung
- Node entschlüsselt nach LoRa-Empfang
- **Über Serial kommt alles unverschlüsselt** (wenn richtig konfiguriert)

Wenn Client verschlüsselte Pakete empfängt:
- Falscher Kanal auf Node (anderer PSK)
- Node-Config stimmt nicht
- **Nicht:** Client muss entschlüsseln

### Message-History

Nodes **speichern keine** Message-History!
- Nur letztes Paket pro Node in RAM
- Bei Reboot: Alles weg
- Client muss selbst speichern (SQLite geplant)

### Channel-Loading

Channels kommen über `FromRadio.channel`, **nicht** über AdminMessage!
- AdminMessage: get_channel_request funktioniert, aber langsam
- Besser: Auf automatische Channel-Pushes warten
- Timing: Manchmal kommen Channels nach config_complete

### Config-Timing

```
want_config_id
  ↓
my_info (sofort)
  ↓
node_info (mehrere, nach und nach)
  ↓
channel (x8, manchmal verzögert!)
  ↓
config (mehrere, nach und nach)
  ↓
config_complete_id
```

**Problem:** config_complete kann VOR den letzten Channels kommen!
**Lösung:** Nach config_complete noch 2-3 Sekunden warten

## Offizielle Clients als Referenz

### Python Client (`meshtastic-python`)

**Gelesen:** `C:\Users\Gerrit\Documents\meshtastic\python`

**Wichtige Erkenntnisse:**
- Sequential Channel-Loading (0-7) mit AdminMessage
- Kein Message-Buffer auf Node
- Request-Response-Pattern für Config
- Nur letztes Paket pro Node gespeichert

**Referenz-Files:**
- `node.py` - Config-Loading, Channel-Requests
- `mesh_interface.py` - Protokoll-Handler
- `serial_interface.py` - Serial I/O

### Web Client (`@meshtastic/web`)

**Gelesen:** `C:\Users\Gerrit\Documents\meshtastic\web`

**Wichtige Erkenntnisse:**
- 3-Panel-Layout (Links: Channels, Mitte: Chat, Rechts: Nodes)
- Tabs für Channels (Radix UI)
- IndexedDB für Message-History (persistente Speicherung)
- Unread-Counts pro Channel/DM
- **Keine separaten Fenster** für DMs!

**Referenz-Files:**
- `Channels.tsx` - Channel-Tabs UI
- `Messages/` - Message-Display
- `useConnections.ts` - Connection-Management
- `messageStore/` - Message-History (Zustand)

## Build & Deployment

### Development

```bash
# Debug-Build
dotnet build

# Mit Visual Studio
# F5 → Debug-Modus mit Breakpoints
```

### Release

```bash
# Manuell
dotnet publish MeshtasticClient/MeshtasticClient.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o publish

# Mit Skript
build.bat
```

**Ergebnis:** `publish\MeshtasticClient.exe` (~160 MB)

### Publish-Settings (.csproj)

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishTrimmed>false</PublishTrimmed>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

- **PublishSingleFile:** Alles in eine EXE
- **SelfContained:** .NET Runtime einbetten
- **PublishTrimmed:** false (sonst Protobuf-Probleme)

## Testing

### Manuell

1. Build: `build.bat`
2. Node per USB anschließen
3. `publish\MeshtasticClient.exe` starten
4. DebugView parallel laufen lassen
5. Verbinden und Logs beobachten

### Debug-Session

1. Visual Studio: Solution öffnen
2. F5 drücken
3. Breakpoints setzen in:
   - `OnDataReceived` - Alle Serial-Daten
   - `ProcessPacket` - Protobuf-Parsing
   - `HandleTextMessage` - Message-Handler
   - `OnMessageReceived` - UI-Update

### Log-Analyse

**Erfolg:**
```
Config complete! Received 2 channels
HandleChannel: Index=1, Name=MyChannel
TEXT MESSAGE: Text="Hello"
Firing MessageReceived event
```

**Fehler - Keine Channels:**
```
Config complete! Received 0 channels
```
→ Node hat keine Channels oder Timing-Problem

**Fehler - Keine UI-Updates:**
```
Firing MessageReceived event
(Aber nichts in UI)
```
→ Event-Handler nicht registriert oder Dispatcher-Problem

## Zukunft (Roadmap)

### Priorität 1 (v1.0 MVP)

1. **Message-History** - SQLite für persistente Speicherung
2. **Config-Speichern** - AdminMessage: set_config
3. **Bessere Fehlerbehandlung** - Reconnect, Timeouts

### Priorität 2 (v1.1)

1. **Direct Messages** - Tab-System wie Web-Client
2. **Node-Liste** - Klickbar für DMs
3. **Unread-Counts** - Badge-System

### Priorität 3 (v1.2+)

1. **PSK-Entschlüsselung** - Für Store&Forward
2. **Karten-Ansicht** - GPS-Positionen
3. **Mesh-Graph** - Visualisierung
4. **Firmware-Update** - OTA

## Debugging-Tipps

### DebugView Setup

1. Download: https://learn.microsoft.com/en-us/sysinternals/downloads/debugview
2. Als Admin starten
3. Capture → Capture Win32 ✓
4. Filter: `*` (alles)
5. Client starten → Logs erscheinen live

### Häufige Debug-Szenarien

**"Channels werden nicht geladen"**
1. DebugView: Suche "HandleChannel"
2. Wenn nicht gefunden → Channels kommen nicht an
3. Wenn gefunden → Event wird nicht gefeuert (Code-Problem)

**"Nachrichten kommen nicht an"**
1. DebugView: Suche "TEXT MESSAGE"
2. Wenn nicht gefunden → Keine Text-Pakete empfangen
3. Wenn gefunden → UI-Update Problem (Dispatcher?)

**"Config timeout"**
1. DebugView: Suche "Config complete"
2. Wenn nicht gefunden → Node antwortet nicht
3. Prüfe: COM-Port korrekt? Anderes Programm nutzt Port?

## Wichtige Notizen für AI-Assistenten

1. **Immer .NET 8.0 / C# Syntax verwenden**
2. **WPF Threading beachten** - Dispatcher.Invoke für UI!
3. **Protobuf-Konflikte** - Alias verwenden (ProtoNodeInfo vs ModelNodeInfo)
4. **Debug.WriteLine** für Logging - nicht Console.WriteLine
5. **Verschlüsselung** - Passiert auf Node, nicht im Client!
6. **Message-History** - Node speichert nichts, Client muss selbst speichern
7. **Channel-Loading** - Via FromRadio.channel, nicht AdminMessage (langsamer)
8. **Timing** - Nach config_complete noch warten (Channels kommen später)
9. **DAU-freundlich** - Einfache UI, wenig Optionen, klare Fehler-Meldungen
10. **Offline-First** - Keine Internet-Abhängigkeit!

## Referenzen

- Meshtastic Docs: https://meshtastic.org/docs
- Protobuf Defs: https://github.com/meshtastic/protobufs
- Python Client: https://github.com/meshtastic/python
- Web Client: https://github.com/meshtastic/web

## Kontakt

- Project Lead: Gerrit
- Working Directories:
  - `C:\Users\Gerrit\Documents\meshtastic\windows-client` (dieser Client)
  - `C:\Users\Gerrit\Documents\meshtastic\python` (Referenz)
  - `C:\Users\Gerrit\Documents\meshtastic\web` (Referenz)

---

*Letzte Aktualisierung: Februar 2026*
*Für Claude AI und andere AI-Assistenten optimiert*
