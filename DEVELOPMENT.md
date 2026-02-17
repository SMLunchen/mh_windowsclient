# Meshtastic Windows Client - Entwicklungsdokumentation

## Projektübersicht

Ein offline-fähiger Windows Desktop Client für Meshtastic Geräte mit serieller Kommunikation.

**Technologie-Stack:**

* .NET 8.0 WPF Application
* ModernWPF UI Library 
* System.IO.Ports für serielle Kommunikation
* Google Protocol Buffers (Protobuf) für Meshtastic Protokoll
* CommunityToolkit.Mvvm für MVVM Pattern

## Architektur

### Services

#### SerialPortService.cs

Verwaltet die serielle Verbindung zum Meshtastic Gerät.

**Wichtige Einstellungen:**

* Baud Rate: 115200
* Data Bits: 8
* Parity: None
* Stop Bits: 1
* Read/Write Timeout: 2000ms
* DTR Enable: true (wichtig für USB-Serial!)
* RTS Enable: true (wichtig für USB-Serial!)

**Polling-basiertes Lesen:**
Anstatt SerialPort.DataReceived Event wird ein dedizierter Thread mit Read() verwendet:

```csharp
int bytesRead = _serialPort.Read(buffer, 0, 1); // Timeout 2s
```

Grund: DataReceived Event ist unzuverlässig bei USB-Serial Adaptern.

**Disconnect-Prozess:**


1. `_wantExit = true` setzen
2. Auf Reader Thread warten (3s Timeout)
3. DTR/RTS deaktivieren
4. Port schließen
5. Port disposen
6. Variablen auf null setzen

#### MeshtasticProtocolService.cs

Verarbeitet das Meshtastic Protokoll.

**Frame-Format:**

```
[0x94 0xC3] [LenHi LenLo] [Protobuf Data]
```

**Wichtige Konzepte:**


1. **_isInitializing Flag:**
   * Verhindert dass während der Initialisierung Events gefeuert werden
   * Nach Init werden alle gesammelten Daten auf einmal als Events gefeuert
2. **Thread-Safety:**
   * Alle Daten-Zugriffe geschützt mit `lock (_dataLock)`
   * Events werden AUSSERHALB der Locks gefeuert um Deadlocks zu vermeiden
3. **InitializeAsync Flow:**

   ```
   1. Wakeup-Sequenz senden (32x 0xC3)
   2. Config Request senden
   3. Auf config_complete warten (max 10s)
   4. 2s warten auf weitere Daten
   5. Events für alle gesammelten Nodes/Channels feuern
   6. Bei Bedarf Channels manuell anfordern (0-7)
   ```

### MainWindow.xaml.cs

**ObservableCollections für Datenbinding:**

* `_nodes`: Knoten-Liste
* `_messages`: Nachrichten-Liste
* `_channels`: Kanal-Liste

## Kritische Fallstricke und Lösungen

### 1. Deadlock durch Dispatcher.Invoke()

**Problem:**

```csharp
// FALSCH - verursacht Deadlock!
_ = Task.Run(async () =>
{
    await _protocolService.InitializeAsync();
    Dispatcher.Invoke(() => UpdateStatusBar("Done")); // Blockiert!
});
```

InitializeAsync läuft auf Background Thread und feuert Events. Diese Events aktualisieren ObservableCollections auf dem UI Thread. Wenn der Background Thread dann `Dispatcher.Invoke()` macht, wartet er **synchron** auf den UI Thread. Der UI Thread ist aber beschäftigt mit Collection-Updates → **Deadlock**.

**Lösung:**

```csharp
// RICHTIG - nicht blockierend
Dispatcher.BeginInvoke(() => UpdateStatusBar("Done"));
```

**Regel:** Von Background Threads **IMMER** `BeginInvoke()` verwenden, **NIE** `Invoke()`.

### 2. LINQ .All() friert UI ein

**Problem:**

```csharp
// FALSCH - blockiert bei großen Strings!
if (rawName.All(c => c >= 32 || c == '\n'))
{
    // ...
}
```

Bei Channel-Namen aus dem Device kann `.All()` die UI einfrieren.

**Lösung:**

```csharp
// RICHTIG - simple foreach Loop
bool isValid = true;
foreach (char c in rawName)
{
    if (c < 32 && c != '\n' && c != '\r' && c != '\t')
    {
        isValid = false;
        break;
    }
}
```

### 3. Event-Flood überlastet UI Thread

**Problem:**
Bei 80+ Nodes werden alle Events auf einmal gefeuert:

```csharp
// FALSCH - überlastet Dispatcher Queue!
foreach (var node in nodesToFire)
{
    NodeInfoReceived?.Invoke(this, node);
}
```

**Lösung:**

```csharp
// RICHTIG - Pausen einbauen
foreach (var node in nodesToFire)
{
    if (_isDisconnecting) break;
    NodeInfoReceived?.Invoke(this, node);

    // Alle 10 Nodes kurze Pause
    if (nodesToFire.IndexOf(node) % 10 == 9)
    {
        await Task.Delay(10);
    }
}
```

### 4. Zombie-Prozess beim Schließen

**Problem:**
`OnClosed()` Event ist zu spät - Fenster ist schon zu, Disconnect blockiert.

**Lösung:**

```csharp
// RICHTIG - OnClosing verwenden
protected override void OnClosing(CancelEventArgs e)
{
    if (_serialPortService.IsConnected)
    {
        Task.Run(() =>
        {
            _protocolService.Disconnect();
            Thread.Sleep(100);
            _serialPortService.Disconnect();
        });

        // Kurze Pause für Cleanup
        Thread.Sleep(150);
    }
    base.OnClosing(e);
}
```

### 5. Clipboard erfordert STA Thread

**Problem:**

```csharp
// FALSCH - funktioniert nicht zuverlässig
Clipboard.SetText(text);
```

**Lösung:**

```csharp
// RICHTIG - STA Thread für Clipboard
await Task.Run(() =>
{
    Thread thread = new Thread(() =>
    {
        Clipboard.SetDataObject(text, true);
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join(1000);
});
```

### 6. Case-Sensitive Enum-Matching

**Problem:**
Device sendet "Eu868" und "ShortSlow", aber ComboBox hat "EU_868" und "SHORT_SLOW".

**Lösung:**

```csharp
var regionNormalized = regionName.Replace("_", "").ToUpperInvariant();
var itemNormalized = item?.Replace("_", "").ToUpperInvariant();
if (itemNormalized == regionNormalized)
{
    // Match gefunden
}
```

## Protobuf Struktur

### admin.proto

**Wichtig:** Simpel halten! Keine komplexen Dependencies.

```proto
syntax = "proto3";
package meshtastic;
option csharp_namespace = "Meshtastic.Protobufs";
import "mesh.proto";

message AdminMessage {
  oneof payload_variant {
    uint32 get_channel_request = 1;
    Channel get_channel_response = 2;
    uint32 get_owner_request = 3;
    User get_owner_response = 4;
    uint32 get_config_request = 5;
    Config get_config_response = 6;
    uint32 get_module_config_request = 7;
    ModuleConfig get_module_config_response = 8;
    Channel set_channel = 32;
    User set_owner = 33;
    Config set_config = 34;
    ModuleConfig set_module_config = 35;
    // ... weitere Felder
  }
}
```

**Channel Parsing:**

```csharp
// GetChannelResponse gibt Channel DIREKT zurück (nicht wrapped!)
var channel = adminMsg.GetChannelResponse;
string name = channel.Settings?.Name ?? "";
int index = channel.Index;
ChannelRole role = channel.Role; // PRIMARY, SECONDARY, DISABLED
```

**Channel Request:**

```csharp
// WICHTIG: Protocol verwendet 1-based Indexing!
GetChannelRequest = (uint)(channelIndex + 1)
```

## Logging

**Logger.cs** schreibt in `meshtastic-client.log` im EXE-Verzeichnis.

**Best Practices:**

* Nur wichtige Events loggen (keine Loops, keine Hex-Dumps)
* Format: `[Timestamp] Message`
* ERROR Prefix für Fehler
* WARNING Prefix für Warnungen

**Beispiel:**

```csharp
Logger.WriteLine("My Node ID: 3B95164F");
Logger.WriteLine("ERROR: Failed to parse packet");
Logger.WriteLine("WARNING: Channel name invalid");
```

## UI Threading Guidelines

### DO's:

✅ `Dispatcher.BeginInvoke()` von Background Threads
✅ `await Task.Run()` für lange Operationen
✅ `lock (_dataLock)` kurz halten
✅ Events außerhalb von Locks feuern
✅ `if (_isDisconnecting)` vor await Checks

### DON'Ts:

❌ `Dispatcher.Invoke()` von Background Threads
❌ Lange Operationen auf UI Thread
❌ Events innerhalb von Locks feuern
❌ `.All()`, `.Any()` auf unbekannt großen Daten
❌ Synchrone SerialPort.DataReceived Event verwenden

## Build und Deployment

**Build Command:**

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

**Output:**

```
C:\Users\Gerrit\Documents\meshtastic\windows-client\public\MeshtasticClient.exe
```

**Hinweis:** Vor Publish muss die laufende Instanz geschlossen werden (Access Denied sonst).

## Bekannte offene Issues

### 1. Channel Name Encoding

**Problem:** Channel 1 sollte "Mesh Hessen" zeigen, zeigt aber Garbage oder "empty".

**Vermutung:** UTF-8 Dekodierung funktioniert nicht korrekt.

**Debug-Ansatz:**

* Raw Bytes als Hex loggen
* Verschiedene Encodings testen (UTF-8, ISO-8859-1, Windows-1252)
* Prüfen ob Bytes korrekt vom Device kommen

**Code Location:**
`MeshtasticProtocolService.cs` → `HandleAdminMessage()` → Case `GetChannelResponse`

### 2. Device Name in Settings

**Problem:** Device Name bleibt leer in Einstellungen Tab.

**Vermutung:** Timing-Problem - DeviceInfo Event kommt vor NodeInfo Event.

**Aktueller Workaround:** Retry nach 500ms in `OnDeviceInfoReceived()`.

**Code Location:**
`MainWindow.xaml.cs` → `OnDeviceInfoReceived()`

## Performance Überlegungen

### Warum Polling statt Events?

**SerialPort.DataReceived** ist problematisch:

* Unzuverlässig bei USB-Serial
* Threading-Probleme
* Manchmal werden Bytes übersprungen

**Polling mit Read():**

* Zuverlässiger
* Kontrollierbares Threading
* Konsistent wie Python/Web Implementation

### ObservableCollection Updates

**Problem:** Bei vielen Nodes (80+) wird die UI langsam.

**Optimierung:**

* Pausen zwischen Events (alle 10 Nodes)
* BeginInvoke statt Invoke
* Limit auf 10000 Log-Zeilen

**Future:** Virtualisierung der ListViews erwägen.

## Debugging Tipps

### Log-Analyse:

```
[2026-02-08 10:49:04.447] Connected to COM21
[2026-02-08 10:49:04.453] === Initializing ===
[2026-02-08 10:49:05.711] Received FromRadio packet, type: MyInfo
[2026-02-08 10:49:05.713] My Node ID: 3B95164F
```

**Wichtige Checkpoints:**


1. "Connected to COMxx" - Serial OK
2. "=== Initializing ===" - Init gestartet
3. "My Node ID: xxxxxxxx" - Device antwortet
4. "Init complete: XX nodes, YY channels" - Init erfolgreich

**Freeze nach "My Node ID":**
→ Dispatcher Deadlock oder Event-Flood

**"0 channels":**
→ Device sendet keine Channels automatisch, manuelles Request nötig

### Visual Studio Debug Mode:

* Breakpoints in InitializeAsync setzen
* Thread-Fenster beobachten
* Dispatcher Queue Monitor
* ObservableCollection Change Events tracken

## Nächste Schritte (TODOs)


1. ✅ Deadlock-Problem gelöst (BeginInvoke statt Invoke)
2. ✅ Event-Flood gelöst (Delays zwischen Events)
3. ✅ Zombie-Prozess gelöst (OnClosing mit Background Disconnect)
4. ✅ LoRa Config loading (Case-insensitive matching)
5. ⏳ Channel Name Dekodierung (aktuell: Garbage/Empty bei Channel 1)
6. ⏳ Device Name in Settings (Retry-Logic vorhanden, muss getestet werden)

## Kontakt & Support

Bei Fragen oder Problemen:

* Log-Datei analysieren: `meshtastic-client.log`
* Wichtige Checkpoints im Log suchen
* Bei Freeze: Task Manager → Prozess beenden


