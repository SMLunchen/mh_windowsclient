# 🚀 Schnellstart & Verbindung

[← Zur Projektübersicht](../../README.md) · [🇬🇧 English version](../en/getting-started.md)

## Installation in 5 Schritten

1. **Download:** Neueste `MeshhessenClient.exe` aus den [Releases](https://github.com/SMLunchen/mh_windowsclient/releases) herunterladen — die EXE bringt die komplette .NET-Laufzeitumgebung bereits mit (self-contained), es muss **nichts installiert** werden
2. **Gerät anschließen:** Meshtastic-Device per USB anstecken (oder TCP/Bluetooth bereithalten)
3. **Starten:** Doppelklick auf `MeshhessenClient.exe` – die App läuft ohne Installer direkt los
4. **Verbinden:** Verbindungstyp wählen (Serial, TCP oder Bluetooth) → „Verbinden" klicken
5. **Loslegen:** 3–10 Sekunden warten bis Kanäle geladen sind, dann Nachrichten senden

> Nur wer den Client [selbst aus dem Quellcode baut](technik.md#aus-quellcode-bauen), braucht das .NET 8 SDK.

> Die App ist vollständig offline-fähig. Keine Cloud, keine Registrierung, keine Telemetrie zum Entwickler.

### ⚠️ Windows SmartScreen

Beim ersten Start kann Windows SmartScreen warnen („Unbekannter Herausgeber") — das ist bei unsignierten Open-Source-Downloads normal: Die EXE ist nicht mit einem (kostenpflichtigen) Code-Signing-Zertifikat signiert. **„Weitere Informationen" → „Trotzdem ausführen"** startet die App. Wer auf Nummer sicher gehen will, lädt nur aus den offiziellen [GitHub-Releases](https://github.com/SMLunchen/mh_windowsclient/releases) und vergleicht die dort angegebene SHA-256-Prüfsumme (PowerShell: `Get-FileHash MeshhessenClient.exe`).

### Was die App auf die Platte legt

Alles bleibt im EXE-Verzeichnis (portabel, per USB-Stick mitnehmbar): `meshhessen-client.ini` (Einstellungen), `telemetry.db` (Telemetrie), `maptiles/` und `vectortiles/` (Kartencache), `logs/` (Nachrichten-Logs), `traceroutes/` (gespeicherte Traceroutes).

## Verbindungstypen

| Typ | Transport | Besonderheiten |
|----|----|----|
| USB/Serial | COM-Port, 115200 baud | Wakeup-Sequenz, Geräte-Debug-Text wird erkannt und separat geloggt |
| TCP/WiFi | TCP-Socket | Hostname/IP + Port konfigurierbar |
| Bluetooth | BLE GATT | Direkte FromRadio/ToRadio-Pakete |

* **Letzte Verbindung merken** – Verbindungsart (Serial / BT / WiFi) und zuletzt genutztes BT-Gerät werden gespeichert und beim nächsten Start automatisch vorausgewählt
* **Auto-Reconnect** – nach Einstellungsänderungen, die einen Geräte-Neustart erfordern
* **Per-Node-Stationsname** – pro verbundenem Node ein eigener Stationsname (✏-Button neben dem Verbinden-Button)

## System & Oberfläche

* **Update-Check** – beim Start wird automatisch nach neuen Versionen gesucht; bei verfügbarem Update erscheint ein klickbarer Hinweis in der Statusleiste (offline-fähig: kein Fehler ohne Internet)
* **Multi-Sprache** – Deutsch und Englisch (umschaltbar in Einstellungen)
* **Dark Mode** & ModernWPF Fluent-Design
* **Automatisches Logging** aller Nachrichten (`logs/`)
* **Debug-Tab** mit Live-Log fürs Troubleshooting
* **Meshhessen-Schnellkonfiguration** – One-Click für Short Slow + EU868 + Meshhessen-Kanal

## Nachrichten-Logs

Alle Nachrichten werden automatisch geloggt unter `[EXE-Verzeichnis]/logs/`:

* `Channel_0_Primary.log` – Kanalverläufe
* `DM_DEADBEEF_Alice.log` – Direktnachrichten

## Bekannte Einschränkungen

* Getestet mit Firmware 2.x
* T-Deck: Channels werden nicht immer in der Config-Sequenz mitgesendet (Retry-Workaround aktiv) – das T-Deck ist fast schon mit sich selbst überfordert, daher dauert dort alles etwas länger…

## Weiterführend

* [🗺️ Karte & Offline-Nutzung](karte.md)
* [📨 Nachrichten & Kommunikation](nachrichten.md)
* [📊 Telemetrie & Funkanalyse](telemetrie.md)
* [🔧 Node-Verwaltung & Fernkonfiguration](node-verwaltung.md)
* [🧰 Tools: Virtual Node, T-Deck-Assistent, Kiosk-Modus](tools.md)
* [🏗️ Technik & Protokoll](technik.md)
