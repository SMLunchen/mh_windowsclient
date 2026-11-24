# 🔌 MeshStick-Assistent

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/meshstick/)

Der **Elecrow MeshStick** ist ein USB-Stick mit SX1262-Funkmodul und TCXO – aber ohne eigenen Prozessor. Der Meshtastic-Node läuft deshalb auf dem PC: als **meshtasticd**, der nativen Meshtastic-Firmware für Linux, macOS und Windows. Unter Windows ist meshtasticd ein Dienst, der beim Hochfahren automatisch startet; der Meshhessen Client verbindet sich mit ihm wie mit jedem WLAN-Node per TCP – nur eben lokal (`127.0.0.1:4403`).

Der Assistent im **Tools-Tab** („🔌 MeshStick einrichten …") führt von „Stick eingesteckt" bis „Node funkt mit Meshhessen-Profil" durch alle Schritte.

!!! note "Voraussetzungen"
    * **Internet** – der Treiber und meshtasticd werden heruntergeladen.
    * **Administratorrechte** – für die Treiberinstallation und die Einrichtung des Windows-Dienstes erscheint je eine Windows-Sicherheitsabfrage (UAC). Ohne Adminrechte am PC geht es nicht.
    * Der Stick steckt **direkt** in einem USB-Anschluss des PCs (USB-Hubs können die CH341-Bridge aus dem Tritt bringen).
    * 64-Bit-Windows 10/11.

## Die sechs Schritte

| Schritt | Was passiert |
|---|---|
| **1 · Voraussetzungen & Treiber** | Der Assistent prüft Internet und sucht den Stick im Gerätemanager (USB-Kennung `VID_1A86&PID_5512`). Ohne Treiber meldet Windows Fehlercode 28. Der Stick braucht den Treiber **CH341PAR** des Chip-Herstellers WCH – *nicht* den seriellen CH340/CH341-Treiber. Der Client **bringt das Paket mit** (Version 2.6.2025.4, WHQL-signiert) und installiert es mit einem Klick ohne weitere Dialoge – nur die Windows-Sicherheitsabfrage. Wer eine neuere Version will, lädt `CH341PAR.EXE` oder `.ZIP` von der WCH-Seite; der Assistent erkennt die Datei im Download-Ordner (bei der EXE im WCH-Fenster „Install" klicken). Danach prüft er alle 3 Sekunden neu, bis der Stick „erkannt, Treiber installiert" meldet. |
| **2 · meshtasticd beschaffen** | Zeigt, ob meshtasticd schon installiert ist (Version, Dienststatus). Sonst Quelle wählen: **Meshhessen-Build** (GitHub `SMLunchen/mh_firmware`, Standard), **offizielle Meshtastic-Releases** (sobald dort ein Windows-MSI liegt) oder eine **lokale MSI-Datei**. Der Download landet im Ordner `meshtasticd\` neben der Client-EXE. |
| **3 · Konfiguration & Dienst** | Zeigt die `config.yaml`, die geschrieben wird (Pins des MeshSticks, TCP-Port, Standard 4403). „Installieren & einrichten" erledigt mit **einer** Sicherheitsabfrage: Konfiguration nach `%ProgramData%\Meshtastic\config.yaml` schreiben, MSI installieren, Dienstrechte setzen (siehe unten), Dienst starten – und wartet, bis der Port antwortet. Das Protokoll steht im Fenster und in `meshtasticd\setup.log`. **„Daemon testen"** startet meshtasticd 12 Sekunden im Vordergrund mit genau dieser Konfiguration und zeigt seine Ausgabe – vor der Installation (die EXE wird ohne Adminrechte aus dem MSI geholt) oder wenn der Dienst nicht hochkommt. Läuft er nach 12 Sekunden noch, passt die Konfiguration. |
| **4 · Verbinden** | Der Client verbindet sich per TCP mit `127.0.0.1:<Port>` und wartet, bis Konfiguration und Kanäle geladen sind. Diese Verbindung wird als zuletzt genutzte gespeichert und beim nächsten Start vorausgewählt. |
| **5 · LoRa-Einstellungen** | Region, Modem-Preset, Hop-Limit, Sendeleistung – oder mit einem Klick das **Meshhessen-Profil** (EU868, ShortSlow, Hop 7, MQTT-Freigabe, Kanal „Mesh Hessen"), identisch mit dem Meshhessen-Button im Kanäle-Tab. Nach dem Speichern startet der Node neu, der Client verbindet sich automatisch wieder und liest die Werte zur Kontrolle zurück. |
| **6 · Fertig** | Zusammenfassung und Ordner-Link. Ab jetzt: PC an → Dienst läuft → Client verbinden. |

## Was der Assistent am System ändert

* **Treiber:** WCH CH341PAR, installiert per `pnputil /add-driver CH341WDM.INF /install` (Geräteklasse „WCH", Dienst `CH341_A64`). Das INF legt `CH341DLLA64.DLL` nach `System32` und `CH341DLL.DLL` nach `SysWOW64` – meshtasticd lädt die 64-Bit-DLL zur Laufzeit, ohne sie startet der Node nicht. Das mitgelieferte Paket ist der unveränderte Ordner `WIN 1X` aus WCHs `CH341PAR.EXE` (siehe `Resources/drivers/README.md` im Quellcode).
* **meshtasticd:** installiert nach `%ProgramFiles%\Meshtastic\meshtasticd.exe` (MSI aus der Meshtastic-Build-Pipeline, `packaging/windows/meshtasticd.wxs`), registriert als Dienst **meshtasticd** („Meshtastic Daemon"), Start automatisch, Konto LocalSystem.
* **Konfiguration:** `%ProgramData%\Meshtastic\config.yaml` – die Pinbelegung aus der Firmware-Vorlage `lora-usb-meshstick-1262.yaml` (SX1262 über `spidev: ch341`, DIO2 als HF-Schalter, TCXO) plus `General.APIPort`. Die Node-Datenbank liegt in `%ProgramData%\Meshtastic\data`.
* **Dienstrechte:** Der Assistent erlaubt angemeldeten Benutzern, den Dienst zu starten und zu stoppen (`sc sdset`). Grund: Ein Meshtastic-„Neustart" (nach jedem Speichern von Einstellungen) beendet den Prozess des Nodes. Der Client startet den Dienst dann beim nächsten Verbinden selbst – ohne diese Rechte bräuchte er jedes Mal Adminrechte. Zusätzlich sind automatische Neustarts nach Abstürzen gesetzt.

## Im Alltag

* Der Dienst läuft unabhängig vom Client – der Node ist auch ohne geöffneten Client im Mesh.
* Verbinden: Verbindungstyp **TCP/WiFi**, Host `127.0.0.1`, Port `4403` (nach dem Assistenten vorausgewählt). Antwortet der Port nicht und der Dienst ist gestoppt, startet der Client ihn und wartet bis zu 25 Sekunden.
* Alle weiteren Einstellungen (Name, Position, Module, Kanäle) wie bei jedem anderen Node über **Node-Konfiguration**.
* Der Port lässt sich in der INI ändern (`MeshStickApiPort`), die Download-Quelle ebenfalls (`MeshStickMirrorRepo`, Standard `SMLunchen/mh_firmware`).

## Woher kommt meshtasticd für Windows?

Die Meshtastic-Firmware enthält seit 2026 einen nativen Windows-Build samt MSI und Windows-Dienst – die Build-Pipeline läuft bei jedem Release, aber die **offiziellen Releases hängen das MSI noch nicht an**. Der Meshhessen-Build stammt aus genau dieser Pipeline im Fork `SMLunchen/mh_firmware`: Ein täglicher Workflow prüft das neueste Upstream-Release, baut daraus mit den unveränderten Upstream-Build-Schritten das MSI und veröffentlicht es als Release `meshtasticd-v<Version>` mit dem Asset `meshtasticd-<Version>-x64.msi` und SHA-256-Datei. Der Assistent lädt es von dort. Sobald die offiziellen Releases das MSI führen, erscheint es unter „Offizielle Meshtastic-Releases".

**Updates:** Den Assistenten erneut starten – Schritt 2 zeigt die installierte Version und die verfügbaren Builds; ein neueres MSI ersetzt die Installation an Ort und Stelle (Konfiguration und Node-Datenbank bleiben erhalten). Die Konfiguration selbst wird dabei neu geschrieben, der Dienst neu gestartet.

## Fehlerbilder

| Symptom | Ursache / Abhilfe |
|---|---|
| Stick „nicht gefunden" | Nicht eingesteckt, defektes Kabel/Hub, oder ein anderes USB-Gerät mit CH341 – im Gerätemanager nach `VID_1A86&PID_5512` suchen. Ohne Hub direkt am PC probieren. |
| „gefunden, aber ohne Treiber (Fehlercode 28)" | CH341PAR noch nicht installiert, oder nur der serielle Treiber CH341SER. „Treiber installieren" im Assistenten klicken (mitgeliefertes Paket). |
| „Treiber vorhanden, aber CH341DLLA64.DLL fehlt" | Treiber auf anderem Weg installiert, DLL fehlt in `System32`. „Treiber installieren" im Assistenten wiederholen – das legt die DLL nach. |
| Microsoft Update Catalog bietet einen CH341A-Treiber an | Das ist eine Version von 2009 ohne die DLL – nicht verwenden, der Assistent bringt die aktuelle mit. |
| „Installation fehlgeschlagen (MSI-Fehler)" | Das MSI startet den Dienst sofort nach dem Kopieren; kommt der Node nicht hoch (MSI-Log: *Error 1920, Service 'Meshtastic Daemon' failed to start*, Ergebnis 1603), nimmt Windows Installer alles zurück. Der Assistent schreibt die Konfiguration deshalb **vor** dem MSI. Bleibt es dabei: „Daemon testen" zeigt die Ausgabe von meshtasticd – typisch sind ein abgezogener Stick, fehlende DLL oder ein belegter Port. |
| „Einrichtung fehlgeschlagen (21)" | Nach der Installation fehlt `meshtasticd.exe` unter `%ProgramFiles%\Meshtastic` – MSI defekt oder falsche Architektur (nur x64). |
| Port antwortet nicht | Dienst gestartet, aber der Node kommt nicht hoch: Stick abgezogen? Treiber/DLL fehlt? Anderer Prozess auf dem Port? Die Ausgabe des Dienstes steht in der Windows-Ereignisanzeige (Anwendung). Mit `sc query meshtasticd` den Status prüfen; der Assistent lässt sich jederzeit wiederholen. |
| Nach dem Speichern von Einstellungen bricht die Verbindung ab | Normal: Der Node startet neu, der Prozess beendet sich kurz. Der Client verbindet sich automatisch wieder (bis zu 7 Minuten Versuche) und startet den Dienst bei Bedarf. |
| Sicherheitsabfrage abgelehnt | Der Schritt wurde abgebrochen, nichts ist passiert – einfach erneut klicken. |

## Deinstallation

meshtasticd über **Apps & Features** entfernen (Eintrag „meshtasticd"). Konfiguration und Node-Datenbank in `%ProgramData%\Meshtastic` bleiben absichtlich erhalten; den CH341PAR-Treiber entfernt der Gerätemanager.

## Weiterführend

* [🚀 Schnellstart & Verbindung](schnellstart.md) · [🔧 Node-Verwaltung & Fernkonfiguration](node-verwaltung.md)
* [Meshtastic-Dokumentation: meshtasticd](https://meshtastic.org/docs/meshtasticd/) · [Elecrow MeshStick](https://meshtastic.org/docs/hardware/devices/elecrow/meshstick/)
