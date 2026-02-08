# Schnellstart - Meshtastic Windows Client

## Für Anfänger (DAUs)

### Was du brauchst

1. **Computer** mit Windows 10 oder 11
2. **Meshtastic-Gerät** (z.B. LILYGO T-Beam, Heltec, RAK)
3. **USB-Kabel** zum Anschließen des Geräts

### Installation in 3 Schritten

#### Schritt 1: .NET 8.0 SDK installieren

1. Gehe zu: https://dotnet.microsoft.com/download/dotnet/8.0
2. Klicke auf "Download .NET SDK x64" (für Windows)
3. Führe den Installer aus und folge den Anweisungen
4. Fertig!

**Überprüfung:**
- Öffne die Eingabeaufforderung (Windows-Taste + R, dann "cmd" eingeben)
- Tippe: `dotnet --version`
- Du solltest etwas wie "8.0.x" sehen

#### Schritt 2: Projekt bauen

1. Öffne die Eingabeaufforderung
2. Navigiere zum Projektordner:
   ```
   cd C:\Users\Gerrit\Documents\meshtastic\windows-client
   ```
3. Führe das Build-Skript aus:
   ```
   build.bat
   ```
4. Warte, bis der Build fertig ist (kann 2-5 Minuten dauern)
5. Die fertige EXE ist hier: `publish\MeshtasticClient.exe`

#### Schritt 3: Client verwenden

1. Schließe dein Meshtastic-Gerät per USB an
2. Doppelklick auf `publish\MeshtasticClient.exe`
3. Wähle deinen COM-Port aus der Liste
4. Klicke "Verbinden"
5. Fertig! Du kannst jetzt Nachrichten senden

## Häufige Probleme

### "dotnet wird nicht als Befehl erkannt"

**Problem:** .NET SDK wurde nicht korrekt installiert

**Lösung:**
1. Starte den Computer neu
2. Installiere .NET SDK erneut
3. Überprüfe mit `dotnet --version`

### "Ich finde den COM-Port nicht"

**Problem:** Gerät wird nicht erkannt

**Lösung:**
1. Überprüfe das USB-Kabel (manche laden nur, übertragen aber keine Daten!)
2. Öffne den Geräte-Manager (Windows-Taste + X → Geräte-Manager)
3. Suche unter "Anschlüsse (COM & LPT)" nach deinem Gerät
4. Notiere die COM-Nummer (z.B. "COM3")
5. Klicke im Client auf das Aktualisieren-Symbol (🔄)

### "EXE startet nicht" / "Antivirus blockiert"

**Problem:** Windows Defender oder Antivirus blockiert die EXE

**Lösung:**
1. Rechtsklick auf `MeshtasticClient.exe`
2. Eigenschaften → Allgemein
3. Häkchen bei "Zulassen" setzen (unten)
4. Oder: Ausnahme in deinem Antivirus hinzufügen

### "Verbindung fehlgeschlagen"

**Problem:** Port wird bereits verwendet oder Gerät nicht bereit

**Lösung:**
1. Schließe alle anderen Programme, die auf das Gerät zugreifen könnten
   - Meshtastic Web-Client im Browser
   - Python-Skripte
   - Arduino IDE
2. Trenne das Gerät und schließe es erneut an
3. Warte 5-10 Sekunden
4. Versuche erneut zu verbinden

## Erste Schritte nach der Verbindung

### 1. Nachrichten senden

- Unten im Fenster ist ein Textfeld
- Tippe deine Nachricht ein
- Drücke Enter oder klicke "Senden"
- Die Nachricht geht an alle im Mesh

### 2. Knoten anzeigen

- Klicke auf den Tab "🌐 Knoten"
- Hier siehst du alle Geräte im Mesh
- Warte ein paar Minuten, bis Knoten auftauchen

### 3. Einstellungen ändern

- Klicke auf den Tab "⚙️ Einstellungen"
- **Wichtig**: Stelle die richtige **Region** ein!
  - Deutschland/Europa: `EU_868`
  - USA: `US`
  - Andere: Siehe Meshtastic-Dokumentation
- Wähle ein **Modem Preset**:
  - `LONG_FAST`: Standard, gute Balance
  - `LONG_SLOW`: Mehr Reichweite, langsamer
  - `SHORT_FAST`: Weniger Reichweite, schneller

### 4. Kanäle verwalten

- Klicke auf den Tab "📡 Kanäle"
- Hier siehst du deine konfigurierten Kanäle
- Kanal 0 ist der Standard-Kanal

## Tipps für den Einstieg

### Reichweite maximieren

1. Verwende `LONG_SLOW` oder `LONG_MODERATE` Preset
2. Stelle die Region korrekt ein
3. Positioniere das Gerät hoch und mit freier Sicht
4. Verwende eine gute Antenne

### Batterie schonen

1. Verwende `LONG_FAST` statt `LONG_SLOW`
2. Reduziere die Sendeleistung in den Einstellungen
3. Deaktiviere GPS wenn nicht benötigt

### Mesh verstehen

- **Node/Knoten**: Ein Gerät im Mesh
- **Hop**: Sprung von einem Gerät zum anderen
- **SNR**: Signalqualität (höher = besser)
- **RSSI**: Signalstärke (weniger negativ = besser)

## Visual Studio Alternative

Falls du lieber Visual Studio verwendest:

1. Installiere Visual Studio 2022 Community (kostenlos)
   - Download: https://visualstudio.microsoft.com/de/downloads/
2. Bei Installation ".NET Desktop-Entwicklung" auswählen
3. Öffne `MeshtasticClient.sln`
4. Drücke F5 zum Starten
5. Für Release-Build:
   - Rechtsklick auf Projekt → Veröffentlichen
   - Ordner → Konfigurieren → win-x64
   - Veröffentlichen

## Weitere Hilfe

- **Meshtastic Dokumentation**: https://meshtastic.org/docs/getting-started
- **Discord**: https://discord.gg/meshtastic
- **Forum**: https://meshtastic.discourse.group

## Was als Nächstes?

1. Schließe dich der Meshtastic-Community an
2. Finde andere Mesh-Benutzer in deiner Nähe
3. Experimentiere mit verschiedenen Einstellungen
4. Teile deine Erfahrungen

---

**Viel Erfolg mit deinem Meshtastic Windows Client!**
