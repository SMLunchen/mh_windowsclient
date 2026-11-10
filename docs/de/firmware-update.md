# ⬆️ Firmware-Update

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/firmware-update/)

Der Client aktualisiert die Firmware deines Nodes direkt – ohne Web-Flasher, ohne Kommandozeile – mit einem geführten Assistenten im **Tools-Tab** („⬆️ Firmware-Update starten …"). Er arbeitet wie die Meshtastic-Android-App: Firmware aus den offiziellen Releases, Gerätekatalog zur Prüfung, vier Übertragungswege. Es wird nichts an das Gerät gesendet, bevor die Prüfseite keinen roten Eintrag mehr zeigt.

!!! warning "Auf eigene Gefahr"
    Der Assistent prüft, was sich am PC prüfen lässt – Funkabbrüche, Stromausfall, ein ungeeigneter Bootloader oder Fehler in der Firmware selbst nicht. Schlägt ein Update fehl, muss das Gerät unter Umständen vor Ort neu gestartet oder per USB/Web-Flasher wiederhergestellt werden. Die Entwickler des Meshhessen Clients übernehmen dafür keine Haftung.

## Übertragungswege

| Weg | Geräte | Verbindung zum Client | Wie es funktioniert |
|---|---|---|---|
| **📶 WLAN-OTA** | ESP32 (Heltec V3/V4, T-Beam, T-Deck, Station G2 …) | beliebig; WLAN muss auf dem Node konfiguriert sein | Der Node startet in seinen OTA-Loader und nimmt die Firmware über TCP-Port 3232 entgegen. Die IP des Loaders wird per UDP-Beacon gefunden (PC im selben Netz). Der Loader prüft den SHA-256-Hash und aktiviert das Image nur bei Übereinstimmung. Schnellster Weg. |
| **🔵 Bluetooth-OTA** | ESP32 | Bluetooth | Gleicher Loader, aber über Bluetooth LE: ein Paket → eine Bestätigung. Dauert einige Minuten, braucht kein Netzwerk – ideal mit externem Stick und Richtantenne. |
| **🔵 Bluetooth-DFU** | nRF52 (RAK4631, T-Echo, Heltec T114, Nano G2 …) | Bluetooth | Nordic-DFU über den Adafruit-Bootloader. Der Client erkennt automatisch, ob der Bootloader **Legacy-DFU** (Standard bei RAK & Co.) oder **Secure-DFU** spricht, setzt bei Verbindungsabbruch fort bzw. wiederholt und räumt eine hängende DFU-Sitzung auf. Bei Abbruch bleibt das Gerät im Bootloader – nichts ist kaputt, einfach erneut starten. |
| **💾 USB-Laufwerk (UF2)** | nRF52, RP2040 | beliebig + USB-Kabel | Der Node startet als USB-Laufwerk (Bootloader); der Client kopiert die `.uf2` automatisch, der Bootloader prüft die Chip-Familie und startet neu. Der sicherste Weg. |

**Nicht unterstützt:** ESP32 seriell flashen. Dafür WLAN auf dem Gerät aktivieren (dann WLAN-OTA) oder den offiziellen [Web-Flasher](https://flasher.meshtastic.org) nutzen – der Assistent verlinkt ihn.

!!! note "Voraussetzung für ESP32-OTA"
    Das OTA-Loader-Protokoll (`ota_request`) gibt es ab Firmware **2.7.17**. Ältere ESP32-Nodes einmalig per Web-Flasher aktualisieren, danach geht alles aus dem Client.

## Die sechs Schritte

1. **Gerät & Katalog** – zeigt Hardware-Modell, Architektur, aktuelle Firmware, Build-Kennung (`pio_env`) und Verbindung. Der **Gerätekatalog** (api.meshtastic.org) wird geladen und lokal gecacht; offline arbeitet der Assistent aus dem Cache und sagt es, wenn keine Validierung möglich ist. Warnungen: Gerät nicht mehr aktiv unterstützt, Bootloader-Upgrade vor dem ersten BLE-Update nötig (mit Anleitungslink). Darunter der **aktive Bluetooth-Adapter** – siehe Kasten unten.
2. **Übertragungsweg** – nur die für Gerät und Verbindung möglichen Wege sind wählbar; alle anderen zeigen den Grund.
3. **Firmware wählen** – **Stable** (empfohlen), **Alpha** (Vorabversionen, mit Warnung) oder **lokale Datei**. Release-Notes, Versionsvergleich (Update / gleiche Version / ⚠ Downgrade) und die Prüfung, ob das Release überhaupt einen Build für dein Gerät enthält. Lokale Dateien: `.bin` (ESP32), `-ota.zip` (nRF52-DFU), `.uf2` (USB).
4. **Prüfung & Start** – die Firmware wird heruntergeladen (oder gelesen) und geprüft: Manifest-MD5/Größe, ESP-Image-Header mit Chip-ID gegen die Gerätearchitektur, App-Deskriptor (kein Factory-Image), UF2-Family-ID, DFU-Manifest, Dateiname gegen Build-Kennung, Akkustand, Transporthinweise. Optional wird vorher die **Gerätekonfiguration als JSON** gesichert (`firmware\backups\` – enthält Kanal-Schlüssel, entsprechend behandeln). Rote Einträge blockieren den Start.
5. **Übertragung** – Fortschritt, Phase und Protokoll. Der Client trennt die Mesh-Verbindung absichtlich (kein Auto-Reconnect) und verbindet sich mit Loader bzw. Bootloader.
6. **Fertig** – Ergebnis; „Erneut verbinden" stellt die Verbindung wieder her und zeigt die vom Gerät gemeldete neue Firmware-Version. Nach einem **Bluetooth-OTA (ESP32)** entfernt der Client die Windows-Kopplung des Geräts automatisch (siehe unten) und koppelt beim Verbinden neu – Windows fragt dabei ggf. nach dem PIN des Nodes.

## Bluetooth-Adapter & Richtantenne

Windows nutzt für Bluetooth LE immer genau **einen** Adapter; eine Auswahl in der Anwendung ist technisch nicht möglich. Der Assistent zeigt den aktiven Adapter (Name und Adresse). Sind mehrere Bluetooth-Adapter eingebaut/eingesteckt, erscheint ein Hinweis: den nicht gewünschten Adapter im **Geräte-Manager deaktivieren**, Bluetooth kurz aus- und wieder einschalten und den Assistenten neu öffnen. So lässt sich gezielt ein externer Stick mit Richtantenne für weiter entfernte Nodes nutzen.

## Dateien & Cache

Alles liegt im Ordner `firmware\` neben der EXE: `deviceHardware.json` und `releases.json` (Katalog-Cache), pro Version ein Unterordner mit den heruntergeladenen Images (Prefetch – ein einmal geladenes Release lässt sich später offline flashen) und `backups\` mit den Konfigurations-Sicherungen. „Firmware-Ordner" im Assistenten öffnet ihn.

## Wiederherstellung

Bricht ein Update ab, startet ein OTA-/UF2-Gerät einfach mit der alten Firmware weiter (das Image wird erst nach erfolgreicher Prüfung aktiviert). Ein nRF52 bleibt nach abgebrochener DFU im Bootloader: Den Assistenten **ohne** Mesh-Verbindung öffnen – er bietet dann Bluetooth-DFU (letzte Bluetooth-Adresse) bzw. das UF2-Laufwerk mit einer lokalen Datei an. Ein UF2-Laufwerk erscheint bei nRF52/RP2040 auch manuell durch zweimaliges kurzes Drücken der RESET-Taste.

## Typische Fehler

* **„Meshtastic-Dienst nicht gefunden" beim Verbinden nach einem Update** (Bluetooth): Windows speichert pro Bluetooth-Adresse die Dienstetabelle (GATT) des Geräts und aktualisiert sie bei gekoppelten Geräten nicht von selbst. Nach einem Bluetooth-OTA (der Loader lief unter derselben Adresse mit einer anderen Tabelle) oder wenn eine neue Firmware ihre Bluetooth-Dienste ändert, passt die gespeicherte Tabelle nicht mehr. Der Client prüft das vor jedem Bluetooth-Verbindungsaufbau aktiv: Er fragt die von Windows gespeicherte Tabelle ab, und fehlt darin der Meshtastic-Dienst, entfernt er die Kopplung automatisch und koppelt neu (Windows fragt ggf. nach dem PIN). Schlägt die Dienstsuche dennoch fehl, folgt ein zweiter Versuch mit frischer Kopplung; erst dann erscheint ein Ja/Nein-Dialog zum manuellen Entkoppeln. Manuell: Windows-Einstellungen → Bluetooth → Gerät entfernen, dann im Client erneut verbinden.
* **„Bluetooth-OTA-Dienst nicht erreichbar"** (ESP32): Der OTA-Loader meldet sich unter derselben Bluetooth-Adresse wie die Mesh-Firmware, kennt aber keine Verschlüsselung. Das Gerät bleibt bis zu einem Neustart (Reset-Taste) im OTA-Modus; danach erneut versuchen.
* **„Bootloader nicht gefunden"** (nRF52): Das Gerät ist nicht in den DFU-Modus gestartet – meist blockiert eine veraltete Windows-Kopplung den Auslöser. Gerät in den Windows-Bluetooth-Einstellungen entfernen, neu koppeln, erneut versuchen.
* **„OTA-Loader nicht erreichbar"** (WLAN): PC und Node im selben Netz? Firewall für UDP/TCP 3232? Bei der ersten Nutzung fragt Windows ggf. nach Netzwerkzugriff für den Client.
* **„Gerät lehnt den OTA-Modus ab"**: Die Firmware meldet den Grund (z. B. fehlende OTA-Partition oder ein Loader ohne BLE-Unterstützung). Die Mesh-Verbindung bleibt dabei bestehen.
* **Hash abgelehnt / Hash-Prüfung fehlgeschlagen**: Das Image wurde **nicht** aktiviert, die alte Firmware läuft weiter. Erneut versuchen.
