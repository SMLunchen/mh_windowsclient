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

Wenn der Client im Debug-Log immer wieder `[BLE] Device disconnected` meldet und danach 10–50 Sekunden lang nicht neu verbinden kann („Meshtastic service not found … Unreachable"), reißt die Funkverbindung ab – der Client beendet sie nicht. Nach dem dritten Abbruch innerhalb von 10 Minuten zeigt der Client dazu einen Hinweis.

Häufigste Ursache ist eine **zu schwache Funkstrecke**: Node auf dem Dachboden, PC ein oder zwei Etagen tiefer, ein schwacher Bluetooth-Adapter im PC oder 2,4-GHz-Störer (WLAN, USB-3-Geräte direkt neben dem Adapter). Typisch dafür: Der Abbruch kommt mitten im Datenstrom – etwa beim Laden der Node-Liste nach dem Verbinden – und der Node ist danach einige Sekunden nicht erreichbar, bis Windows ihn wieder findet. Abhilfe: Abstand verringern oder einen Bluetooth-Adapter mit externer Antenne nutzen.

Der Client federt das ab: Beim automatischen Neuverbinden mit demselben Node lädt er die Node-Liste nicht erneut, sondern nur Konfiguration und Kanäle (der lange Download war der Moment, in dem schwache Strecken meist abrissen), und die Pausen zwischen den Versuchen wachsen mit jedem Fehlversuch. Beim Verbinden schreibt er die Signalstärke des Nodes ins Log („Node signal during connect: −87 dBm“) – Werte unter etwa −85 dBm sind zu schwach. Zur Fehlersuche gibt es unter Einstellungen → Debug die Option „Node-Log über Bluetooth mitlesen“; sie braucht auf dem Node „Debug-Log über API“ in der Sicherheits-Konfiguration und belastet die Funkstrecke zusätzlich.

**Wie der Client liest:** Er liest den Node nur dann, wenn der Node etwas angekündigt hat (FROMNUM-Meldung) oder der Client gerade etwas gesendet hat – so wie die Android-App und die Python-CLI. Dazu kommt alle 1,5 Sekunden ein Sicherheits-Blick, falls Windows eine Meldung verschluckt. Beim Verbinden fordert er das schnellste Verbindungsintervall an, damit Konfiguration und Node-Liste zügig laden, und gibt es danach wieder frei; der Node darf dann sein eigenes, ruhigeres Intervall wählen. Die Einstellung **„Schonender Bluetooth-Modus“** (Einstellungen → Bluetooth) verzichtet von Anfang an auf das schnelle Intervall: Die Node-Liste lädt langsamer, dafür bekommt eine schwache Strecke mehr Luft. Nach wiederholten Abbrüchen bietet der Client diesen Modus im Hinweis-Dialog direkt an.

#### 📶 Bluetooth-Anzeige in der Kopfzeile

Bei einer Bluetooth-Verbindung steht neben dem grünen Verbindungspunkt **📶 −72 dBm**: die Signalstärke des Nodes, gemessen beim Verbindungsaufbau. Windows liefert für eine stehende Verbindung keinen Wert, deshalb ändert sich die Zahl erst beim nächsten Verbinden; „📶 –“ heißt, dass beim Verbinden kein Advertising-Paket gehört wurde. Mauszeiger darauf zeigt die Zähler der Verbindung, ein Klick öffnet sie als Fenster mit **„Kopieren“** – für eine Fehlermeldung einfach einfügen. Dieselben Zahlen stehen alle 10 Minuten und beim Trennen als `[BLE] Link stats` im Log.

| Zeile | Bedeutung |
|---|---|
| **Signal beim Verbinden** | Stärke der Advertising-Pakete des Nodes in dBm, dazu die Zahl der gehörten Pakete. Ab −70 dBm gut, bis −85 dBm mittel, darunter schwach – dann erklärt die Funkstrecke die Abbrüche. |
| **Modus · Phase** | *schnell* (Standard) oder *schonend* (Einstellung). *Initialisierung* = schnelles Intervall angefordert, solange Konfiguration und Node-Liste laden; *Dauerbetrieb* = Anforderung freigegeben, der Node wählt sein eigenes Intervall. |
| **Verbindungsintervall** | Das von Windows ausgehandelte Intervall in ms, dazu Latenz und Supervision-Timeout. Beim Laden etwa 15 ms, danach typisch 30–50 ms. Nur ab Windows 11 22H2; ältere Versionen melden es nicht. |
| **Verbunden seit** | Dauer der aktuellen Verbindung. |
| **Lesevorgänge** | Alle Abfragen des Nodes, davon Anteil leer, mit Daten und fehlgeschlagen. Ein hoher Leer-Anteil ist normal (Sicherheits-Poll). Fehler sollten bei 0 bleiben – drei in Folge gelten als abgerissene Verbindung. |
| **FROMNUM-Meldungen** | Wie oft der Node neue Daten angekündigt hat, und ob das Abo steht. Bleibt die Zahl bei stehender Verbindung lange bei 0, kommen die Meldungen auf diesem Adapter nicht an; dann trägt allein der Sicherheits-Poll. |
| **Leseläufe** | Wodurch gelesen wurde: nach Meldung, nach eigenem Senden, per Sicherheits-Poll (Intervall in Klammern). Im gesunden Betrieb überwiegen Meldung und Senden. |
| **Schreibvorgänge** | An den Node gesendete Pakete und davon fehlgeschlagene. |

**Kopplung:** Der Client entfernt eine Windows-Kopplung **nie ohne Rückfrage**. Ist der Node nicht erreichbar (aus, außer Reichweite, Neustart), versucht er es einfach weiter. Nur wenn der Node antwortet und die gespeicherte Kopplung nicht mehr zu ihm passt – typisch nach Firmware-Update, Werksreset oder Bluetooth-OTA –, fragt er, ob die Kopplung entfernt und neu aufgebaut werden soll; im automatischen Neuverbinden frühestens beim zweiten solchen Befund und nur einmal.

Zweite Ursache bei **nRF52-Geräten (RAK4631, T-Echo, ThinkNode M1/M6, T114, T1000-E …) mit Firmware vor 2.8.0**: Diese Firmware schaltet Bluetooth komplett ab, sobald ein serieller Client per USB die Meshtastic-Schnittstelle öffnet – Python-CLI, Web-Client, Handy am USB-Kabel oder eine zweite Client-Instanz. Bluetooth kommt erst zurück, wenn dieser Client trennt, spätestens nach 15 Minuten. Das betrifft nur Nodes, an deren USB-Buchse tatsächlich ein Rechner hängt – ein reines Netzteil zählt nicht. Abhilfe: USB-Datenverbindung trennen bzw. das andere Programm beenden – oder die Firmware auf **2.8.0 oder neuer** aktualisieren ([Firmware-Update](firmware-update.md)), dort ist das behoben. Der Meshhessen Client selbst meldet beim Trennen das Sitzungsende an den Node (`disconnect`), sodass nach einer seriellen Sitzung im Client Bluetooth sofort wieder frei ist.

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
