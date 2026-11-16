# 🚀 Schnellstart & Verbindung

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/getting-started/)

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

### Bluetooth bricht ständig ab?

Wenn der Client im Debug-Log immer wieder `[BLE] Device disconnected` meldet und danach 10–50 Sekunden lang nicht neu verbinden kann („Meshtastic service not found … Unreachable"), trennt der **Node** die Verbindung, nicht der Client. Nach dem dritten Abbruch innerhalb von 10 Minuten zeigt der Client dazu einen Hinweis.

Häufigste Ursache: **nRF52-Geräte (RAK4631, T-Echo, ThinkNode M1/M6, T114, T1000-E …) mit Firmware vor 2.8.0** schalten Bluetooth komplett ab, sobald ein serieller Client per USB die Meshtastic-Schnittstelle öffnet – Python-CLI, Web-Client, Handy am USB-Kabel oder eine zweite Client-Instanz. Bluetooth kommt erst zurück, wenn dieser Client trennt, spätestens nach 15 Minuten. Ein Programm, das den Node regelmäßig per USB abfragt, erzeugt so eine Endlosschleife aus Abbrüchen.

Abhilfe: USB-Datenverbindung zum Node trennen bzw. das andere Programm beenden – oder die Firmware auf **2.8.0 oder neuer** aktualisieren ([Firmware-Update](firmware-update.md)), dort ist das behoben. Der Meshhessen Client selbst meldet seit 1.7.4 beim Trennen das Sitzungsende an den Node (`disconnect`), sodass nach einer seriellen Sitzung im Client Bluetooth sofort wieder frei ist.

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
* [🖧 Virtual Node](virtual-node.md) · [🔧 T-Deck-Assistent](t-deck.md) · [🔒 Kiosk-Modus](kiosk.md)
* [🏗️ Technik & Protokoll](technik.md)
