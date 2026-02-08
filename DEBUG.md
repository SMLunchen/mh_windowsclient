# Debug-Anleitung

## 🎉 NEU: Debug-Logging ohne Visual Studio!

Das Logging-System wurde komplett überarbeitet und funktioniert jetzt **auch ohne Visual Studio**:

✅ **Debug-Tab** direkt in der Anwendung (EMPFOHLEN)
✅ **Log-Datei** automatisch erstellt (meshtastic-client.log)
✅ **DebugView** funktioniert jetzt auch mit Release-Builds
✅ **Debug-Build** verfügbar für erweiterte Diagnose

**Welche Option soll ich nutzen?**
- **Normal:** Debug-Tab in der Anwendung (Option 1)
- **Zum Teilen:** Log-Datei (Option 2)
- **Fortgeschritten:** DebugView (Option 3)
- **Noch mehr Details:** Debug-Build (Option 4)

---

## Problem: Keine Nachrichten/Kanäle werden empfangen

Die neue Version hat **intensives Debug-Logging**. Es gibt mehrere Möglichkeiten, die Logs zu sehen:

### Option 1: Debug-Tab in der Anwendung (EMPFOHLEN!)

Die einfachste Methode - kein zusätzliches Tool nötig!

1. **Starte den Client** (`publish\MeshtasticClient.exe`)

2. **Klicke auf den "🐛 Debug" Tab**

3. **Alle Log-Ausgaben erscheinen in Echtzeit** im Debug-Fenster

4. **Buttons:**
   - "Log löschen" - Löscht die Anzeige (nicht die Datei)
   - "Log kopieren" - Kopiert alles in die Zwischenablage
   - "Log-Datei öffnen" - Öffnet die Log-Datei in einem Texteditor

**Vorteile:**
- Keine zusätzliche Software nötig
- Echtzeit-Anzeige
- Einfach zu kopieren und zu teilen

### Option 2: Log-Datei (Gut zum Teilen)

1. **Starte den Client** (`publish\MeshtasticClient.exe`)

2. **Log-Datei wird automatisch erstellt:**
   ```
   publish\meshtastic-client.log
   ```

3. **Öffne die Datei** mit Notepad, Notepad++, oder einem anderen Texteditor

4. **Oder: Klicke im Debug-Tab auf "Log-Datei öffnen"**

**Vorteile:**
- Bleibt nach Programm-Ende erhalten
- Einfach per E-Mail/Discord zu teilen
- Kann mit Suche durchsucht werden

### Option 3: Mit DebugView (Für Fortgeschrittene)

1. **Download DebugView** von Microsoft Sysinternals:
   https://learn.microsoft.com/en-us/sysinternals/downloads/debugview

2. **DebugView starten** (als Administrator)

3. **Capture → Capture Win32** aktivieren

4. **Meshtastic Client starten** (`publish\MeshtasticClient.exe`)

5. **Im DebugView siehst du jetzt alle Debug-Ausgaben:**
   ```
   === Initializing Meshtastic connection ===
   Serial data received: 127 bytes
   Found packet: length=123, buffer has 127 bytes
   Received FromRadio packet, type: MyInfo
   My Node ID: ABCD1234
   HandleChannel called: Index=0, Role=PRIMARY, Name=LongFast
   TEXT MESSAGE: From=12345678, To=FFFFFFFF, Channel=0, Text="Hello"
   ```

### Option 4: Debug-Build (Für zusätzliche Diagnose)

Falls noch mehr Details benötigt werden:

1. **Debug-Build erstellen:**
   ```
   build-debug.bat
   ```

2. **Debug-EXE verwenden:**
   ```
   publish-debug\MeshtasticClient.exe
   ```

3. **Mit DebugView kombinieren** (optional) für noch mehr Details

**Unterschied:** Debug-Build hat zusätzliche Diagnose-Informationen und funktioniert besser mit DebugView.

### Option 5: Mit Visual Studio (Für Entwickler)

1. Öffne `MeshtasticClient.sln` in Visual Studio 2022

2. Drücke **F5** (Debug starten)

3. Client wird mit Debugger gestartet

4. **Output-Fenster** zeigt alle Debug.WriteLine Ausgaben:
   - View → Output (oder Ctrl+W, O)
   - Dropdown: "Debug" auswählen

5. Verbinde mit deinem Gerät und beobachte den Output

### Option 6: Mit dotnet run (Command Line)

```bash
cd MeshtasticClient
dotnet run
```

Debug-Ausgaben erscheinen direkt in der Console (nur wenn Debug-Build).

---

## Was die Logs zeigen

### Erfolgreiche Verbindung

```
=== Initializing Meshtastic connection ===
Requesting device configuration (want_config_id)...
Waiting for config complete and initial data...
Serial data received: 156 bytes
Found packet: length=152, buffer has 156 bytes
Received FromRadio packet, type: MyInfo
My Node ID: ABCD1234
Serial data received: 87 bytes
Found packet: length=83, buffer has 87 bytes
Received FromRadio packet, type: Channel
HandleChannel called: Index=0, Role=PRIMARY, Name=LongFast
Firing ChannelInfoReceived event for channel 0: LongFast
HandleChannel called: Index=1, Role=SECONDARY, Name=MyChannel
Firing ChannelInfoReceived event for channel 1: MyChannel
Config complete! Received 2 channels so far
=== Initialization complete: 2 channels, 0 nodes ===
```

### Nachricht empfangen

```
Serial data received: 45 bytes
Found packet: length=41, buffer has 45 bytes
Received FromRadio packet, type: Packet
MeshPacket: From=87654321, To=FFFFFFFF, Channel=0, PayloadType=Decoded
  Decoded packet, PortNum=1
  -> TEXT_MESSAGE_APP
TEXT MESSAGE: From=87654321, To=FFFFFFFF, Channel=0, Text="Hello World"
  Sender node !87654321 not in known nodes
  Firing MessageReceived event
```

### Problem: Keine Pakete

```
=== Initializing Meshtastic connection ===
Requesting device configuration (want_config_id)...
Waiting for config complete and initial data...
Still waiting for config... (1s)
Still waiting for config... (2s)
Still waiting for config... (3s)
WARNING: Config not complete after timeout!
```

**Bedeutet:** Keine Daten vom Gerät empfangen!

**Mögliche Ursachen:**
- Falscher COM-Port
- Gerät nicht eingeschaltet
- USB-Kabel defekt (nur laden, keine Daten)
- Anderes Programm nutzt Port (Meshtastic Web, Python-Script)

### Problem: Pakete kommen an, aber keine Channels

```
Serial data received: 234 bytes
Found packet: length=230, buffer has 234 bytes
Received FromRadio packet, type: MyInfo
My Node ID: ABCD1234
Still waiting for config... (1s)
Still waiting for config... (2s)
Config complete! Received 0 channels so far
```

**Bedeutet:** MyInfo kam an, aber keine Channels!

**Mögliche Ursachen:**
- Gerät hat keine Channels konfiguriert
- Firmware-Problem
- Channels kommen später (warte länger)

### Problem: Channels kommen an, aber keine Nachrichten

```
HandleChannel called: Index=0, Role=PRIMARY, Name=LongFast
Firing ChannelInfoReceived event for channel 0: LongFast
(Keine TEXT MESSAGE Zeilen erscheinen)
```

**Bedeutet:** Config OK, aber keine Text-Nachrichten!

**Prüfe:**
- Sendet der andere Node wirklich auf dem richtigen Kanal?
- Ist die Nachricht verschlüsselt? (zeigt "Encrypted packet")
- Debug-Output vom anderen Node prüfen

---

## Typische Probleme und Lösungen

### "No packet start found, clearing X bytes"

**Problem:** Gerät sendet ungültige Daten oder Log-Ausgaben

**Lösung:** Normal! Das Gerät sendet manchmal Debug-Logs. Diese werden übersprungen.

### "Encrypted packet - cannot decode without key"

**Problem:** Nachricht ist mit PSK verschlüsselt

**Lösung:**
- **Das ist NORMAL!** Verschlüsselte Nachrichten sollten "[Verschlüsselt]" in der Liste zeigen
- Wenn gar keine Nachrichten ankommen → falscher Kanal ausgewählt
- PSK-Entschlüsselung kommt in zukünftiger Version

### "Sender node !87654321 not in known nodes"

**Problem:** Absender-Node ist nicht in der Node-Datenbank

**Lösung:**
- Normal beim ersten Empfang
- Node-Name wird als "!87654321" angezeigt statt echter Name
- Warte auf NODEINFO_APP Paket für den Namen

### Channels werden geladen, aber Dropdown bleibt ausgegraut

**Problem:** UI-Event `ChannelInfoReceived` wird nicht gefeuert oder nicht empfangen

**Lösung:**
- Prüfe ob "Firing ChannelInfoReceived event" im Log erscheint
- Wenn ja → UI-Problem (EventHandler nicht registriert)
- Wenn nein → Protocol-Problem (Exception im Handler)

---

## Log-Ausgaben exportieren

**Einfachste Methode (Debug-Tab):**
1. Klicke auf "🐛 Debug" Tab
2. Klicke auf "Log kopieren"
3. Fertig - Log ist in der Zwischenablage!

**Log-Datei:**
1. Öffne `publish\meshtastic-client.log`
2. Kopiere den kompletten Inhalt
3. Oder: Im Debug-Tab auf "Log-Datei öffnen" klicken

**In DebugView:**
1. Edit → Copy
2. In Textdatei einfügen

---

## Nächste Schritte

1. **Client starten** (`publish\MeshtasticClient.exe`)
2. **Debug-Tab öffnen** (🐛 Symbol)
3. **Mit Gerät verbinden**
4. **10-20 Sekunden warten**
5. **Log kopieren** (Button "Log kopieren")
6. **Log-Ausgabe zeigen/teilen**

Damit kann ich genau sehen wo das Problem liegt!
