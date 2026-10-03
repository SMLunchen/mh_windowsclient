# Meshhessen Client â€“ Windows-Client fÃ¼r Meshtastic-GerÃ¤te (WPF Â· .NET 8)

Der **Meshhessen Client** ist ein **kostenloser, nativer Windows-Client fÃ¼r Meshtastic-GerÃ¤te** (WPF/.NET 8) fÃ¼r Windows 10/11. Verbinde dein **Meshtastic-GerÃ¤t** (LILYGO T-Beam, T-Deck, RAK4631, Heltec u.a.) per **USB/Serial, TCP/WiFi oder Bluetooth** mit deinem Windows-PC â€“ vollstÃ¤ndig **offline-fÃ¤hig**, keine Cloud, keine Installation. Entwickelt von und fÃ¼r die [Meshhessen Community](https://www.meshhessen.de).

![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Status](https://img.shields.io/badge/Status-v1.6.4.5-yellow)
![License](https://img.shields.io/github/license/SMLunchen/mh_windowsclient)
![Stars](https://img.shields.io/github/stars/SMLunchen/mh_windowsclient)

> **English summary:** Free Windows app for Meshtastic devices â€“ offline map (OSM/OpenTopo), telemetry, traceroute, PKI decryption, full device configuration, USB/Serial/TCP/BLE support, remote admin, favorites, telemetry dashboard & **Virtual Node TCP proxy**. No installation, no cloud. By the Meshhessen community (Hesse, Germany). [â†’ English version below](#meshhessen-client--windows-client-for-meshtastic-devices-wpf--net-8)


## ðŸš€ Schnellstart


1. [**.NET 8 Desktop Runtime**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) herunterladen und installieren
2. **Download:** Neueste `MeshhessenClient.exe` aus den [Releases](../../releases) herunterladen
3. **GerÃ¤t anschlieÃŸen:** Meshtastic-Device per USB anstecken
4. **Starten:** Doppelklick auf `MeshhessenClient.exe` â€“ keine Installation nÃ¶tig
5. **Verbinden:** Verbindungstyp wÃ¤hlen (Serial, TCP oder Bluetooth) â†’ â€žVerbinden" klicken
6. **Loslegen:** 3â€“10 Sekunden warten bis KanÃ¤le geladen sind, dann Nachrichten senden

> Die App ist vollstÃ¤ndig offline-fÃ¤hig. Keine Cloud, keine Registrierung, keine Telemetrie zum Entwickler.


## âœ¨ Features

### ðŸ“¨ Nachrichten & Kommunikation

* **Nachrichten** senden und empfangen (Broadcast & Direct Messages)
* **Multi-Channel** â€“ alle KanÃ¤le deines GerÃ¤ts automatisch geladen
* **Direktnachrichten (DMs)** mit separatem Chat-Fenster im Tabbed-Layout
* **PKI-EntschlÃ¼sselung** â€“ client-seitige EntschlÃ¼sselung von PKI-verschlÃ¼sselten DMs (X25519 + AES-256-CTR); Private Key wird automatisch vom verbundenen GerÃ¤t geladen
* **Node-Public-Key-Datenbank** â€“ lokale CSV-Datei (`node_keys.csv`) zum Verwalten von Public Keys fÃ¼r PKI-EntschlÃ¼sselung
* **Tap-Back Reaktionen** â€“ auf Nachrichten mit Emoji reagieren (32 Emojis, wie Android-App)
  * Rechtsklick auf Nachricht â†’ Emoji-Picker
  * Reaktionen werden direkt an Sender/Kanal Ã¼bermittelt und angezeigt
  * Funktioniert in Kanal-Chat und DMs
* **Antwort-Funktion** â€“ auf einzelne Nachrichten antworten (Protokoll-Level)
  * Zitat-Block mit farblicher Hervorhebung der Originalnachricht
  * Funktioniert in Kanal-Chat und DMs
* **Nachrichten auswÃ¤hlbar & kopierbar** â€“ Texte markieren, in Zwischenablage kopieren, Links anklicken
  * Rechtsklick auf beliebige Nachricht â†’ â€žNachricht kopieren" im KontextmenÃ¼
  * Kopiert immer die angeklickte Nachricht (nicht die zuletzt selektierte)
  * KontextmenÃ¼ auch bei Nachrichten von unbekannten Sendern
* **ðŸš¨ Alert Bell Support** â€“ Senden und Empfangen von Notrufen
  * ðŸš¨ SOS-Button in Chat und DMs
  * Visuell: rote blinkende Umrandung + Notification-Bar mit â€žZur Karte springen"-Button
* **Persistente Nachrichten-Datenbank** â€“ Kanal- und DM-Nachrichten dauerhaft in lokaler SQLite-DB speichern (optional, aktivierbar in Einstellungen)
  * Letzten 24 Stunden beim Verbinden automatisch geladen; Ã¤ltere Nachrichten per Hochscrollen nachladen
  * **DM-History:** Alle Konversationen der letzten 24 h erscheinen beim Ã–ffnen des DM-Fensters automatisch
  * **Pro-Kanal-LÃ¶schung** direkt im KanÃ¤le-Tab: â€žNachrichten-DB leeren"-Spalte mit Zeitraum-Dialog (Alle / 30 / 90 / 365 Tage)
  * BestÃ¤tigung vor dem LÃ¶schen einer DM-Konversation (kein versehentliches Wischen)
  * Aufbewahrungsdauer konfigurierbar (30 / 90 / 365 Tage)

### ðŸ“Š Telemetrie & Statistik

* **Persistente Telemetrie-Datenbank** â€“ empfangene Paket- und GerÃ¤tedaten werden lokal in einer SQLite-Datenbank gespeichert
* **Telemetrie-Dashboard** â€“ frei konfigurierbares Widget-Dashboard mit benannten Dashboards (ðŸ“Š-Button in der Toolbar):
  * **11 Widget-Typen:** Linie, FlÃ¤che, Balken, Scatter, Gauge, Stat-Wert, Heatmap, Histogramm, Candlestick, State-Timeline, Uhrzeit
  * Multi-Node-Support, alle Metriken: SNR, RSSI, Batterie, Spannung, Kanal-/TX-Auslastung, Temperatur, Luftfeuchtigkeit, Luftdruck, Pakete/h
  * Widgets per Drag-and-Drop anordnen, frei skalierbar (Resize-Griff), editierbar, Auto-Refresh
  * Dark & Light Mode, Theme-adaptiv (OxyPlot + WPF)
  * Persistenz in `dashboards.json`
  * **UnabhÃ¤ngiges Fenster** â€“ kann auf einem zweiten Monitor platziert werden
* **Node-Statistik-Fenster** â€“ detaillierte Auswertung pro Node:
  * Tag/Nacht-Auswertung der EmpfangsqualitÃ¤t
  * Zeitreihen-Graphen fÃ¼r SNR, RSSI, Paketrate und GerÃ¤tedaten (Batterie, Spannung)
* **LED-Indikatoren** im Hauptfenster zeigen auf einen Blick den Status des verbundenen Nodes:
  * ðŸ“¶ **Signal** â€“ EmpfangsqualitÃ¤t (SNR/RSSI-Trend)
  * ðŸ‘¥ **Nachbarn** â€“ Anzahl und StabilitÃ¤t direkter Nachbar-Nodes
  * ðŸ›¤ï¸ **Pfad-StabilitÃ¤t** â€“ StabilitÃ¤t des Traceroute-Pfads
  * ðŸŒ **Mesh-Health** â€“ Gesamtzustand des sichtbaren Meshes; 
  * ðŸŒ¤ï¸ **Wetter** â€“ fÃ¼r Nodes mit Umweltsensor

### ðŸ—ºï¸ Karte

* **Drei Kartentypen:** OSM Standard, OSM Dark Mode, OpenTopoMap (topografisch)
* **NEU: Vektorkarten** (umschaltbar in den Einstellungen, Raster bleibt Standard):
  * Gestochen scharf in jeder Zoomstufe (MapLibre GL), deutlich kleinere Offline-Daten, Kartenstil-Updates ohne Client-Update
  * **ðŸš’ Feuerwehr-/Rettungs-Layer** zuschaltbar â€“ alle Objekttypen im Abschnitt â€žFeuerwehr-/Rettungs-Layer" direkt unter dieser Liste
  * Layer-Auswahl in den Einstellungen und Ã¼ber den ðŸ—‚ï¸-Button direkt auf der Karte; solange ein Layer aus ist, wird kein Byte dafÃ¼r geladen
  * **Automatischer Offline-Cache:** online betrachtete Gebiete sind ohne Internet weiter nutzbar; zusÃ¤tzlich **Vektor-Offlinepaket-Downloader** (Bundesland/eigene Bounding-Box, Topo-Extras und Zusatz-Layer als Opt-in)
  * Voller Funktionsumfang: Node-Pins, Traceroutes, Nachbar-Linien, PositionsverlÃ¤ufe und Rechtsklick-MenÃ¼ auch auf der Vektorkarte
  * BenÃ¶tigt die Microsoft-WebView2-Runtime (auf Windows 11 vorinstalliert); ohne sie fÃ¤llt der Client automatisch auf Raster zurÃ¼ck

### ðŸš’ Feuerwehr-/Rettungs-Layer (Vektorkarte)

Zuschaltbarer Fach-Layer fÃ¼r Feuerwehr, Rettungsdienst und Katastrophenschutz â€“ gedacht als UnterstÃ¼tzung im Einsatz- und Ãœbungsfall (z. B. â€žWo ist der nÃ¤chste Hydrant / Rettungspunkt?"). Die Daten kommen **live aus OpenStreetMap** (tÃ¤gliche Updates auf dem Meshhessen-Server) â€“ die Abdeckung hÃ¤ngt also vom lokalen Mapping-Stand ab. Farbkonvention wie im Feuerwehrplan: **rot** = LÃ¶schmittel/Meldung, **blau** = LÃ¶schwasser-Vorrat, **grÃ¼n** = Rettung, **orange** = Katastrophenschutz.

| Symbol | Objekttyp | sichtbar ab Zoom |
|---|---|---|
| ðŸ”´ H (gefÃ¼llt = Ãœberflur, Ring = **Unterflur**) | Hydrant | 15 |
| ðŸ”´ Flamme + Name | Feuerwache | 13 |
| ðŸ”´ Kreuz + Name | Rettungswache | 13 |
| ðŸ”´ Sirene | Sirene | 13 |
| ðŸ”´ Leiter | Anleiterstelle | 14 |
| ðŸ”´ E / F / M / C / K | Einspeisung / FeuerlÃ¶scher / Feuermelder / LÃ¶schschlauch / FeuerwehrschlÃ¼sseldepot (FSD) | 16 |
| ðŸ”µ S | Saugstelle | 14 |
| ðŸ”µ T | LÃ¶schwasserbehÃ¤lter | 14 |
| ðŸ”µ L | LÃ¶schteich | 13 |
| ðŸ”µ N | NotrufsÃ¤ule | 15 |
| ðŸŸ¢ Kreuz + **Nummer** | Rettungspunkt | 13 |
| ðŸŸ¢ Sammelplatz | Sammelplatz / Evakuierungspunkt | 13 |
| ðŸŸ¢ Herz (AED) | Defibrillator | 15 |
| ðŸŸ¢ Kreuz | Erste-Hilfe-Kasten | 16 |
| ðŸŸ¢ Rettungsring | Rettungsring / Wasserrettung | 14â€“16 |
| ðŸŸ  Kreuz | Katastrophenschutz-Anlaufstelle | 13 |
| ðŸ”´ Kreuz | Bergrettung | 13 |
| Helipad | Hubschrauberlandeplatz | 13 |

* **Klick auf ein Objekt** Ã¶ffnet ein Detail-Popup mit allen in OSM gepflegten Attributen: Bauart (z. B. Unterflur/Ãœberflur/Wandhydrant), Kupplungen, Durchfluss, Druck, Wasserquelle, Wasservolumen, Betreiber, Ã–ffnungszeiten, Standortbeschreibung (bei Defis z. B. â€žFoyer, 1. OG") und Erfassungsdatum
* Icons Ã¼berlappen bewusst (â€žim Einsatz will man jeden Punkt sehen") und verdrÃ¤ngen keine Kartenbeschriftung
* **Offline nutzbar:** online betrachtete Gebiete landen automatisch im Cache; im Vektor-Offlinepaket-Downloader lÃ¤sst sich der Layer gezielt fÃ¼r ein Gebiet mitladen
* Solange der Layer ausgeschaltet ist, wird **kein einziges Byte** dafÃ¼r Ã¼bertragen
* Die Architektur ist fÃ¼r weitere Fach-Layer vorbereitet (z. B. THW, KrankenhÃ¤user)
* **Vier Karten-Modi** (umschaltbar in Einstellungen):
  * **Offline** (Standard) â€“ vorher heruntergeladene Tiles vom Meshhessen-Server, kein Internet, nur Deutschland
  * **Online â€“ Meshhessen-Server** â€“ Tiles werden on-demand von `tile.meshhessenclient.de` geladen und dauerhaft lokal gespeichert; alle drei Stile, nur Deutschland
  * **Online â€“ eigener Tile-Server** â€“ wie oben, aber mit den selbst konfigurierten Tile-URLs aus den Einstellungen; Ã¶ffentliche OSM-/OpenTopoMap-Server sind hier nicht zulÃ¤ssig (Tile-Usage-Policy)
  * **Online â€“ OpenStreetMap** â€“ weltweite Abdeckung Ã¼ber `tile.openstreetmap.org`; nur Standardkarte; OSM-Tile-Policy-konform (kein Bulk-Download, Tiles lokal gecacht)
* **Node-Positionen** als farbige Pins auf der Karte
* **Node-Pfade** â€“ GPS-PositionsverlÃ¤ufe aufzeichnen und auf der Karte anzeigen
* **Direkte Nachbar-Linien** â€“ aktivierbar im Legenden-Feld: zieht Linien von der eigenen Position zu allen Nodes, die wir **direkt Ã¼ber HF (0 Hops, kein MQTT)** empfangen haben
  * Farbverlauf wÃ¤hlbar nach **SNR** (rot â†’ gelb â†’ grÃ¼n) oder **Alter** der letzten direkten Verbindung (cyan â†’ violett)
  * Option **â€žDauerhaft"** zeigt alle je direkt gehÃ¶rten Nachbarn (statt nur der letzten 24 h); historische 0-Hop-Kontakte kommen aus der Telemetrie-DB
  * Dunkler Umriss unter jeder Linie fÃ¼r gute Sichtbarkeit auch auf der Topo-Karte
* **Wegpunkte (Waypoints)** â€“ empfangene Wegpunkte werden auf der Karte angezeigt; Wegpunkte per Karte-Rechtsklick erstellen und senden

### ðŸ“¡ Traceroute

* **Traceroute starten** â€“ direkt aus dem Node-KontextmenÃ¼ (Nodes-Liste, Karte, Nachrichtenliste)
* **Eigenes Fenster** pro Ziel-Node mit:
  * Hop-Tabelle: Node-Name, Entfernung, SNR (in dB), MQTT-Indikator (âš¡ Blitz-Symbol)
  * Live-Status (Warten / Empfangen)
* **Karte:** Route mit Linien plotten
  * Durchgezogene Linie wo Positionen bekannt
  * Gestrichelte Linie wo Positionen fehlen
  * Richtungspfeile auf Segmenten
  * Klick auf Segment â†’ SNR-Popup fÃ¼r diesen Hop
  * Fallback auf eingestellte Kartenposition wenn eigene GPS fehlt
* **Mehrere Traceroutes gleichzeitig** auf der Karte â€“ jede bekommt eine eindeutige Farbe
* **Speichern & Laden** â€“ Traceroutes werden automatisch in `traceroutes/` gespeichert (JSON)
  * Pfade unterschiedlicher Zeitpunkte vergleichen
  * Mehrere Dateien gleichzeitig laden
  * Historische Traceroutes direkt aus der Datenbank laden
* **Zeitreihenfilter** â€“ Traceroutes nach Zeitraum filtern (3d / 7d / 14d / 30d / 90d / Alle)
* **Deduplizierung** â€“ nur neueste Traceroute pro Node-Paar anzeigen (optional)
* **Karten-Legende** â€“ zeigt alle aktiven Traces mit Farbe und âœ•-Button zum Entfernen

### ðŸ–§ Virtual Node (Tools-Tab)

* **TCP-Proxy-Server** â€“ verbinde Meshtastic-Apps (Android/iOS) direkt mit dem Meshhessen Client, als wÃ¤re er ein echtes Meshtastic-GerÃ¤t
* **Konfigurierbar im Tools-Tab:** Port (Standard: 4404), ein-/ausschalten, Admin-Befehle blockieren
* **Automatischer Start** sobald eine Verbindung mit dem physischen Node besteht (wenn aktiviert)
* **Config-Replay:** verbindende Apps erhalten sofort alle KanÃ¤le, Node-Liste und GerÃ¤tekonfig
* **Bidirektionale Sichtbarkeit:** Nachrichten aus der App erscheinen in verbundenen Apps â€“ und umgekehrt
* **Multi-Client:** mehrere Apps gleichzeitig verbindbar; Status und verbundene IPs werden im Tools-Tab angezeigt

### ðŸ”§ T-Deck Karten-Assistent (Tools-Tab)

* **SD-Karte vorbereiten** â€“ gefÃ¼hrter 6-Schritt-Assistent zum Bespielen einer SD-Karte mit Offline-Karten fÃ¼r das Meshtastic T-Deck
* **Schritt 1 â€“ Willkommen:** ErklÃ¤rt den Workflow; Hinweis: nur Deutschland verfÃ¼gbar
* **Schritt 2 â€“ Laufwerk:** Zeigt alle WechseldatentrÃ¤ger mit GrÃ¶ÃŸe, freiem Speicher und Dateisystem
* **Schritt 3 â€“ Formatierung:**
  * Empfiehlt **exFAT mit 4096-Byte-Zuordnungseinheiten** (T-Deck-optimal; kleine Tiles â‰ˆ100 Bytes â†’ kleine AU spart erheblich Speicher)
  * Formatierung per UAC-elevated PowerShell direkt aus dem Client; zwei BestÃ¤tigungsdialoge
* **Schritt 4 â€“ Bereich:** Nach Bundesland (Checkboxen), ganz Deutschland, oder **Freestyle** (eigenes Rechteck auf interaktiver Mapsui-Karte zeichnen)
* **Schritt 5 â€“ Zoom & Kartentyp:** Zoom 8â€“17, Warnhinweise bei hohen Stufen; OSM / OSM Dark / OpenTopoMap
* **Schritt 6 â€“ Transfer:** Additiv â€“ SD-Tile vorhanden â†’ skip; lokal vorhanden â†’ kopieren; sonst â†’ vom Tile-Server laden + lokal cachen
* **SD-Verzeichnisstruktur:** `{Laufwerk}:\maps\OSM\{z}\{x}\{y}.png` (T-Deck-kompatibel, Langklick auf Faltkartenicon schaltet zwischen Kartendiensten um)
* **ZIP-Export** â€“ lokale Tiles als ZIP exportieren (nach Kartentyp filterbar)
* VollstÃ¤ndig mehrsprachig (Deutsch/Englisch)

### ðŸ”§ Node-Verwaltung

* **Knoten-Ãœbersicht** â€“ alle Nodes im Mesh mit SNR, Batterie, Entfernung, Hop-Anzahl
  * **Kachelansicht (Fancy View)** â€“ optional in Einstellungen â†’ Darstellung aktivierbar: tauscht die Tabelle gegen eine responsive Kachel-OberflÃ¤che (Spaltenanzahl wÃ¤chst/schrumpft mit der Fensterbreite)
    * Pro Kachel: ShortName-Badge in Node-Farbe, ðŸ”‘/ðŸ”“ PKI-Status, voller Name, â€žvor X min", â­-Favoritenstern, ðŸ“¡ Infrastruktur- und â˜ MQTT-Symbol
    * Strom (extern versorgt oder Batterie + Spannung), Entfernung + HÃ¶he, Hops, RSSI/SNR mit Farbverlauf, Umwelt-Telemetrie (ðŸŒ¡/ðŸ’§/ðŸŒ¬), Hardware-Modell Â· GerÃ¤terolle Â· Node-ID
    * Eigener Node immer ganz oben, virtualisiertes Scrolling auch bei 1000+ Nodes
  * **SNR/RSSI nur bei direktem Empfang** â€“ Signalwerte werden nur angezeigt, wenn wir den Node direkt (0 Hops, kein MQTT) hÃ¶ren; bei Relay-Nodes zeigt die Spalte stattdessen die Hop-Anzahl
  * **Erweiterte Filter** â€“ nach â€žzuletzt gesehen" (5 Min bis 60 Tage), MQTT-Nodes ausblenden, nur Favoriten, SNR-EinfÃ¤rbung an/aus; Sortier-Dropdown im Kachelmodus
* **Informationen anfordern** â€“ Rechtsklick-UntermenÃ¼ (Liste, Kachel, Karte) fordert wie die Android-App gezielt Daten von einem Node an: Benutzer-Info, Position, GerÃ¤te-/Umwelt-/LuftqualitÃ¤t-/Strom-/Host-Metriken, SignalqualitÃ¤t/Mesh-Statistik, PAX-ZÃ¤hler
* **Firmware-Version & Hardware-Modell** â€“ werden automatisch beim Verbinden abgefragt und im Node-Info-Fenster angezeigt
* **PKI-SchlÃ¼ssel-Indikator** â€“ ðŸ”‘-Spalte zeigt, ob der Public Key des Nodes bekannt ist
* **Node-Farben** â€“ Nodes individuell einfÃ¤rben (Karte + Listen)
* **Node-Notizen** â€“ Freitext-Notizen pro Node
* **Nodes anpinnen** â€“ Nodes in der Liste oben fixieren (unabhÃ¤ngig von Sortierung); der eigene Node ist ebenfalls anheftbar
* **Per-Node-Stationsname** â€“ pro verbundenem Node ein eigener Stationsname; âœ-Button neben dem Verbinden-Button; AuflÃ¶sung global â†’ node-spezifisch â†’ ShortName
* **Favoriten** â€“ Nodes als Favoriten markieren (â˜…-Symbol, Rechtsklick-MenÃ¼); wird mit dem GerÃ¤t synchronisiert (`set_favorite_node` / `remove_favorite_node`); Favoriten erscheinen oben in der Liste
* **Fernverwaltung** â€“ vollstÃ¤ndige Remote-Admin-OberflÃ¤che fÃ¼r Favoriten-Nodes:
  * Alle Konfigurations-Reiter: Besitzer, GerÃ¤t, Position, LoRa, Bluetooth, Netzwerk, Anzeige, KanÃ¤le, **Sicherheit**, Steuerung
  * **Sicherheits-Reiter:** Public Key (read-only, Base64), Admin-SchlÃ¼ssel 1â€“3 (Base64), Flags (Admin Channel, Managed Mode, Serial, Debug Log)
  * **Favoriten remote verwalten:** Knoten auf dem Remote-GerÃ¤t als Favorit setzen oder entfernen
  * Session-SchlÃ¼ssel-Handshake, konfigurierbarer Timeout, per-Channel Retry + Neu-laden
  * **Lazy Loading:** Beim Ã–ffnen wÃ¤hlen ob alles sofort oder seitenweise geladen wird; â€žâ†» Tab neu laden"-Button fÃ¼r gezielten Reload einzelner Reiter
* **Node-Konfiguration** â€“ vollstÃ¤ndige GerÃ¤tekonfiguration direkt aus dem Client, alle Module auf einer Seite:
  * **GerÃ¤t & LoRa:** Region, Modem-Preset, TX-Power, Hop-Limit, GerÃ¤terolle, Rebroadcast-Modus
  * **Position:** GPS-Modus, Smart-Broadcast, feste Position mit Koordinaten-Eingabe; â€žVon Karte wÃ¤hlen" Ã¶ffnet ein eigenes Kartenfenster (honoriert Offline-/Online-Einstellungen); â€žEigener Kartenpin" Ã¼bernimmt die per Rechtsklick gesetzte Position
  * **Netzwerk:** WLAN SSID/PSK, DHCP/statische IP, Ethernet, NTP-Server, Syslog
  * **Display:** Screen-Timeout, Carousel, Kompass, 12h-Uhr, metrisch/imperial, Display-Modus, OLED-Typ
  * **MQTT:** Server, Credentials, TLS, JSON-Modus, Root-Topic, Proxy-Modus, Map-Reporting mit Genauigkeitsstufe (Bits 10â€“19), MQTT-Proxy-Client-Funktion
  * **Telemetrie:** GerÃ¤te-, Umgebungs-, LuftqualitÃ¤ts- und Energiemetrik-Intervalle
  * **Bluetooth:** Aktivieren, Pairing-Modus, fixer PIN
  * **Security:** Public/Private Key anzeigen & generieren, Admin-SchlÃ¼ssel, Managed Mode
  * **KanÃ¤le:** Alle 8 KanÃ¤le â€“ Rolle, Name, Uplink, Downlink, Positions-Genauigkeit (Bits 10â€“19, ~23 km bis ~45 m)
  * **Weitere Module:** Nachbar-Info, Store & Forward, Externe Benachrichtigung, Canned Messages, Range Test, Serial
* **BT-PIN Ã¤ndern** â€“ Bluetooth-PIN direkt aus dem Client setzen

### âš™ï¸ Verbindung & System

* **Multi-Verbindung** â€“ USB/Serial, TCP/WiFi und Bluetooth (BLE)
* **Letzte Verbindung merken** â€“ Verbindungsart (Serial / BT / WiFi) und zuletzt genutztes BT-GerÃ¤t werden gespeichert und beim nÃ¤chsten Start automatisch vorausgewÃ¤hlt
* **Auto-Reconnect** â€“ nach EinstellungsÃ¤nderungen die einen Neustart erfordern
* **Update-Check** â€“ beim Start wird automatisch nach neuen Versionen gesucht; bei verfÃ¼gbarem Update erscheint ein klickbarer Hinweis in der Statusleiste (offline-fÃ¤hig: kein Fehler wenn kein Internet)
* **Multi-Sprache** â€“ Deutsch und Englisch (umschaltbar in Einstellungen)
* **Dark Mode** & ModernWPF Fluent-Design
* **Automatisches Logging** aller Nachrichten (`logs/`)
* **Debug-Tab** mit Live-Log fÃ¼rs Troubleshooting
* **Meshhessen-Schnellkonfiguration** â€“ One-Click fÃ¼r Short Slow + EU868 + Meshhessen-Kanal
* **Kiosk-/Trainingsmodus** â€“ fÃ¼r geteilte Stationen (Vereinslokal, Schulung, Veranstaltung):
  * In den Einstellungen aktivieren, Passwort setzen und wÃ¤hlen, was im gesperrten Zustand ausgeblendet wird: Tabs (Nodes, KanÃ¤le, Einstellungen, Info, Tools, Debug), Node-Konfiguration, Fernverwaltung, Telemetrie-Dashboard, SOS-Button, Meshhessen-Schnellkonfiguration
  * **ðŸ”’-Schloss in der FuÃŸleiste** zum Sperren/Entsperren (erscheint nur, wenn ein Passwort gesetzt ist); Entsperren per Passwort, App startet im Kiosk-Modus immer gesperrt
  * Bei aktiver Sperre werden zusÃ¤tzlich **Admin-Befehle von Virtual-Node-Clients blockiert** (unabhÃ¤ngig von der VNode-Einstellung)
  * âš ï¸ **Versehens-Schutz, kein Angriffs-Schutz** â€“ das Passwort wird als PBKDF2-Hash gespeichert, aber wer die INI-Datei bearbeiten kann, kann den Modus deaktivieren
  * **Passwort vergessen?** Client beenden und in `meshhessen-client.ini` die Zeile `KioskModeEnabled=True` auf `False` setzen (oder die Zeile `KioskPasswordHash=â€¦` leeren und neu setzen)


## ðŸ’¬ Die Meshhessen Community

Der Meshhessen Client ist ein Gemeinschaftsprojekt der Meshtastic-Community in Hessen. Unser regionales LoRa-Mesh wÃ¤chst stetig â€“ mach mit!

* ðŸŒ **Website:** [www.meshhessen.de](https://www.meshhessen.de)
* ðŸ“¡ **Netz:** Wachsendes Mesh-Netzwerk in Hessen und Umgebung â€“ Airtime ist kein All-you-can-eat â†’ Short Slow! ;)
* ðŸ¤ **Mitmachen:** Eigenen Node aufstellen, Reichweite erweitern, Community wachsen lassen


## ðŸ“¸ Screenshots

Die neue Vektorkarte (MapLibre GL) â€“ gestochen scharf in jeder Zoomstufe, mit Node-Pins:
<img alt="Meshhessen Client â€“ Vektorkarte (MapLibre GL) mit Meshtastic Node-Positionen unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/vector_in_action.png" />

Vergleich: klassische Rasterkarte â€“ ohne zuschaltbare Fach-Layer:
<img alt="Meshhessen Client â€“ klassische Rasterkarte ohne Feuerwehr-Layer (Vergleichsbild)" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/raster_wo_hydrant.png" />

Derselbe Ausschnitt als Vektorkarte mit aktivem ðŸš’ Feuerwehr-/Rettungs-Layer (Hydranten, Wachen, Rettungspunkte â€“ Klick zeigt Details):
<img alt="Meshhessen Client â€“ Vektorkarte mit Feuerwehr-Layer: Hydranten, Wachen und Rettungspunkte auf der Meshtastic-Karte" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/vector_w_hydrant.png" />

Klick auf einen Hydranten zeigt die Details â€“ hier ein Unterflurhydrant (Ring-Symbol) mit Wasserquelle:
<img alt="Meshhessen Client â€“ Hydranten-Detail-Popup auf der Vektorkarte: Unterflurhydrant mit Bauart und Wasserquelle" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/hydrant_unterflur.png" />

Direkte HF-Nachbarn mit SNR-Farbverlauf auf der Vektorkarte:
<img alt="Meshhessen Client â€“ Nachbar-Linien mit SNR-Farbverlauf auf der Vektorkarte (0-Hop HF)" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/vector_w_neighborlinks.png" />

Kiosk-/Trainingsmodus â€“ sperrbare OberflÃ¤che fÃ¼r geteilte Stationen (Einstellungen):
<img alt="Meshhessen Client â€“ Kiosk- und Trainingsmodus: sperrbare OberflÃ¤che mit Passwortschutz fÃ¼r geteilte Stationen" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/kiosk_settings.png" />

Offline-Karte mit Node-Positionen und Entfernungsanzeige:
<img width="1250" height="813" alt="Meshhessen Client â€“ Windows-App fÃ¼r Meshtastic-GerÃ¤te: Offline-Karte mit Node-Positionen (OSM)" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/map.png" />

Node-Ãœbersicht als Tabelle â€“ mit Avatar, Signal-LEDs, Filtern und Sortierung:
<img alt="Meshhessen Client â€“ Node-Ãœbersicht als Tabelle mit Avatar, SNR, RSSI, Batterie und Signal-LEDs" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/nodelist_new.png" />

Node-Ãœbersicht als Kachelansicht (Fancy View) â€“ mit Telemetrie, Strom und SignalqualitÃ¤t pro Kachel:
<img alt="Meshhessen Client â€“ Node-Kachelansicht (Fancy View) mit Telemetrie, Batterie und SignalqualitÃ¤t pro Node" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/nodelist_new_tiles.png" />

Direkte HF-Nachbarn als Linien auf der Karte (Farbverlauf nach SNR oder Alter):
<img alt="Meshhessen Client â€“ Direkte Meshtastic-Nachbarn als farbige Linien auf der Karte (0-Hop HF, SNR-Farbverlauf)" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/map_neighbor_lines.png" />

Nachrichtenansicht mit Kanal-Chat und Direktnachrichten:
<img width="1443" height="813" alt="Meshhessen Client â€“ Meshtastic Nachrichten, Kanal-Chat und Direktnachrichten (DM) unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/messaging.png" />

Alert Bell / SOS-Notruf Anzeige:
<img width="1446" height="816" alt="Meshhessen Client â€“ Alert Bell SOS-Notruf Signalisierung fÃ¼r Meshtastic-GerÃ¤te unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/alert_bell.png" />

Offline-Tile-Downloader fÃ¼r Deutschland und Umgebung:
<img width="567" height="883" alt="Meshhessen Client â€“ Offline-Karten-Tile-Downloader fÃ¼r Meshtastic-GerÃ¤te unter Windows (OSM, OpenTopoMap)" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/tile_downloader.png" />

Kanal-Liste mit allen Meshtastic-KanÃ¤len:
<img width="1443" height="814" alt="Meshhessen Client â€“ Meshtastic Kanal-Liste (Multi-Channel) unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/channel_list.png" />

Traceroute-Fenster mit Hop-Tabelle:
<img width="780" height="868" alt="Meshhessen Client â€“ Traceroute fÃ¼r Meshtastic-GerÃ¤te: Hop-Tabelle, Entfernung und SNR unter Windows" src="https://github.com/user-attachments/assets/10880a3d-9d94-4b07-84bc-ad64af56dab1" />

Traceroute-Pfad auf der Karte:
<img width="1278" height="889" alt="Meshhessen Client â€“ Meshtastic Traceroute-Pfad auf Offline-Karte visualisiert" src="https://github.com/user-attachments/assets/800925ef-0d4e-41ad-a7f2-56cdd06da6b7" />

Node-Standort-Verlauf (GPS-Track) auf der Karte:
<img width="967" height="411" alt="Meshhessen Client â€“ Meshtastic Node-Standort-Verlauf GPS-Track auf Karte" src="https://github.com/user-attachments/assets/0415083a-9f22-44fc-b767-be66b69892cd" />

Telemetrie-LED-Ampel im Hauptfenster:
<img width="516" height="162" alt="Meshhessen Client â€“ Meshtastic Telemetrie-LEDs fÃ¼r Signal, Nachbarn, Mesh-Health und Wetter" src="https://github.com/user-attachments/assets/a580cf69-ab10-4890-a17a-1c5086c19700" />

Telemetrie-Zeitreihen-Graph (SNR, RSSI, Batterie):
<img width="902" height="604" alt="Meshhessen Client â€“ Meshtastic Telemetrie-Graph SNR RSSI Batterie Zeitreihe OxyPlot" src="https://github.com/user-attachments/assets/a42d6a79-5b90-4f7a-8456-c74099c55c41" />

Node-Telemetrie-Statistik-Fenster:
<img width="1278" height="889" alt="Meshhessen Client â€“ Meshtastic Node-Telemetrie Tag/Nacht-Auswertung Statistik Windows" src="https://github.com/user-attachments/assets/4821c996-c524-499a-8fba-f738fb051f88" />
Node-Telemetrie-Ãœbersicht (Signal):

![Meshhessen Client â€“ Meshtastic Node-Telemetrie-Ãœbersicht: Signal-Analyse, Routing, Airtime und Nachbar-Statistik](https://github.com/SMLunchen/mh_windowsclient/blob/master/img/node-telemetry.png)

Airtime-Auswertung pro Node (TX/RX-Sendezeit, Kanalauslastung):

![Meshhessen Client â€“ Meshtastic Airtime-Analyse: TX-Airtime, RX-Airtime und Kanalauslastung pro Node](https://github.com/SMLunchen/mh_windowsclient/blob/master/img/node_airtime.png)

Nachbar-Analyse (direkte Mesh-Verbindungen und SNR-Trends):

![Meshhessen Client â€“ Meshtastic Nachbar-Analyse: Direkte Verbindungen, SNR-Trends und Nachbar-StabilitÃ¤t](https://github.com/SMLunchen/mh_windowsclient/blob/master/img/node_neighbors.png)

Batterie & Spannungs-Analyse (Tag/Nacht-Profil, Nachtabfall):

![Meshhessen Client â€“ Meshtastic Batterie-Analyse: Spannungs-Verlauf, Tag/Nacht-Profil und Nachtabfall](https://github.com/SMLunchen/mh_windowsclient/blob/master/img/node_power.png)

Routing-Statistik (Hop-Anzahl, Pfad-StabilitÃ¤t, Pfadwechsel-Rate):

![Meshhessen Client â€“ Meshtastic Routing-Statistik: Hop-Anzahl, Pfad-StabilitÃ¤t und Pfadwechsel-Rate](https://github.com/SMLunchen/mh_windowsclient/blob/master/img/node_routing.png)

## âš ï¸ Bekannte EinschrÃ¤nkungen

* ~~Keine persistente Message-History~~ â†’ jetzt optional via Nachrichten-Datenbank (aktivierbar in Einstellungen)
* Getestet mit Firmware 2.x
* T-Deck: Channels werden nicht immer in der Config-Sequenz mitgesendet (Retry-Workaround aktiv) â€“ Das T-Deck ist fast schon mit sich selbst Ã¼berfordert, daher dauert dort alles etwas lÃ¤ngerâ€¦
* ~~Heltec-Boards brechen lokale Konfiguration nach 4â€“5/17 Configs ab~~ â†’ behoben durch sequentielles Laden (senden â†’ warten â†’ nÃ¤chste)


## ðŸ—ºï¸ Karte einrichten

**Kartentypen:** OSM Standard (hell), OSM Dark Mode, OpenTopoMap (topografisch) â€“ wÃ¤hlbar in Einstellungen.

### Karten-Modus wÃ¤hlen

In den Einstellungen unter **Karte / Tiles** stehen drei Modi zur VerfÃ¼gung:

| Modus | Beschreibung | Abdeckung |
|----|----|----|
| **Offline** (Standard) | Vorher heruntergeladene Tiles. Kein Internet erforderlich. | Nur Deutschland |
| **Online â€“ Meshhessen-Server** | Tiles werden bei Bedarf geladen und dauerhaft lokal gespeichert. Alle drei Kartenstile. | Nur Deutschland |
| **Online â€“ OpenStreetMap** | Weltweite Abdeckung. Nur Standardkarte. Tiles werden lokal gecacht (mind. 7 Tage). OSM-Policy-konform. | Weltweit |

> Der OSM-Online-Modus ist fÃ¼r Gebiete auÃŸerhalb Deutschlands gedacht. Der OSM-Tile-Server ist spendenfinanziert â€“ bitte sparsam nutzen.

### Tiles herunterladen (Offline- und Meshhessen-Online-Modus)

> âš ï¸ **Wichtig:** Tile-Download ist nur fÃ¼r den Meshhessen-Tile-Server (`tile.meshhessenclient.de`) vorgesehen â€“ Bulk-Downloads verstoÃŸen gegen die OSM-Tile-Policy.

1. Einstellungen Ã¶ffnen â†’ Karten-Modus **Offline** wÃ¤hlen â†’ Kartenstil wÃ¤hlen
2. **â€žTiles herunterladen"** klicken
3. Bereich (Bounding Box) und Zoom-Level eingeben â€“ z.B. Hessen: `49.3,7.7,51.7,10.2`, Zoom `1-14`
4. Download starten
5. Tiles werden unter `maptiles/` gespeichert und sind dauerhaft offline verfÃ¼gbar
6. Tiles sind portabel â€“ per USB Ã¼bertragbar

**Karte nutzen:**

* Tab **â€žðŸ—ºï¸ Karte"** Ã¶ffnen
* Rechtsklick auf Karte â†’ eigenen Standort setzen
* Node-Pins erscheinen automatisch sobald GPS-Daten empfangen werden
* Rechtsklick auf Node â†’ Farbe setzen, DM senden, Notiz bearbeiten, Traceroute starten, Telemetrie Ã¶ffnen


## ðŸ“ Nachrichten-Logs

Alle Nachrichten werden automatisch geloggt unter `[EXE-Verzeichnis]/logs/`:

* `Channel_0_Primary.log` â€“ KanalverlÃ¤ufe
* `DM_DEADBEEF_Alice.log` â€“ Direktnachrichten


## ðŸ—ï¸ Technischer Ãœberblick

| Komponente | Technologie |
|----|----|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protokoll | Meshtastic Protobuf Ã¼ber Serial/TCP/BLE |
| Karte | Mapsui 4.1 + lokale OSM-Tiles |
| Serialisierung | Google.Protobuf, System.Text.Json |
| Verbindung | Serial (0x94 0xC3 Framing), TCP/WiFi, Bluetooth Low Energy |

**Verbindungstypen:**

| Typ | Transport | Framing | Besonderheiten |
|----|----|----|----|
| USB/Serial | COM-Port, 115200 baud | 4-Byte Header (0x94 0xC3 + LÃ¤nge) | Wakeup-Sequenz, Debug-Text interleaved |
| TCP/WiFi | TCP-Socket | 4-Byte Header (wie Serial) | Hostname/IP + Port konfigurierbar |
| Bluetooth | BLE GATT Characteristics | Raw Protobuf (kein Framing) | Direkte FromRadio/ToRadio Pakete |

**Verbindungssequenz:**

```
Windows Client â†’ USB/Serial | TCP/WiFi | BLE â†’ Meshtastic Node â†’ LoRa â†’ Mesh
```


1. Verbindung Ã¶ffnen â†’ Wakeup-Sequenz senden (nur Serial/TCP) â†’ `want_config_id` senden
2. `my_info`, `node_info` (Ã—N), `channel` (Ã—8), `config`, `config_complete_id` empfangen
3. Falls Channels fehlen (z.B. T-Deck): Retry-Mechanismus mit bis zu 3 Runden per `GetChannelRequest`
4. Bereit fÃ¼r MeshPackets

**Serielles Protokoll (Robustheit):**

* Max. PaketlÃ¤nge 512 Bytes (per Meshtastic-Spezifikation), darÃ¼ber = korrupt â†’ false Start Ã¼berspringen
* Schutz vor partiellem Header-Verlust (letztes Byte 0x94 wird bei Buffer-Clear bewahrt)
* Stale-Packet-Timeout: unvollstÃ¤ndige Pakete werden nach 5s verworfen
* Device-Debug-Text (ANSI-Codes) wird erkannt, ANSI-Codes gestrippt, separat geloggt
* Auto-Recovery: sendet Wakeup + `want_config_id` wenn >60s kein Protobuf-Paket empfangen wurde

**Fehler-Erkennung (GerÃ¤te-Logs):**

| Code | Beschreibung |
|----|----|
| TxWatchdog | Software-Bug beim LoRa-Senden |
| NoRadio | Kein LoRa-Radio gefunden |
| TransmitFailed | Radio-Sendehardware-Fehler |
| Brownout | CPU-Spannung unter Minimum |
| SX1262Failure | SX1262 Radio Selbsttest fehlgeschlagen |
| FlashCorruptionRecoverable | Flash-Korruption erkannt (repariert) |
| FlashCorruptionUnrecoverable | Flash-Korruption (nicht reparierbar) |


## ðŸ”§ Aus Quellcode bauen

**Voraussetzungen:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone https://github.com/SMLunchen/mh_windowsclient.git
cd mh_windowsclient
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

EXE liegt danach unter `public\MeshhessenClient.exe`. Alternativ: `build.bat` ausfÃ¼hren.

### Forks & Weiterverwendung

Der Quellcode darf im Rahmen der Lizenz geforkt und angepasst werden â€” wir freuen uns Ã¼ber abgeleitete Projekte.

> âš ï¸ **Der Meshhessen-Tile-Server ist davon ausgenommen.** Die Karten-Infrastruktur (`tile.meshhessenclient.de` und die zugehÃ¶rigen Vektor-/Raster-Endpunkte) wird **ausschlieÃŸlich fÃ¼r den offiziellen Meshhessen Client** bereitgestellt und aus Spenden der Community finanziert. Forks, abgeleitete oder umgebaute Clients (auch fÃ¼r andere Mesh-Protokolle wie MeshCore) dÃ¼rfen diese Server **nicht** nutzen und mÃ¼ssen **eigene Tile-Infrastruktur betreiben**.
>
> Der Client bringt dafÃ¼r alles mit: der Karten-Modus **â€žOnline â€“ eigener Tile-Server"** in den Einstellungen erlaubt beliebige eigene Tile-URLs. Ein einfacher Tile-Cache (z. B. eigener OSM/OpenTopo-Proxy) ist schnell und gÃ¼nstig aufgesetzt.
>
> Unautorisierte Zugriffe auf die Meshhessen-Server werden technisch unterbunden.


## ðŸ™ Credits

* **[Meshtastic Project](https://meshtastic.org)** â€“ Firmware & Protokoll-Spezifikation
* **[ModernWPF](https://github.com/Kinnara/ModernWpf)** â€“ Fluent UI fÃ¼r WPF
* **[Mapsui](https://mapsui.com)** â€“ Offline-Karte
* **[Meshhessen Community](https://www.meshhessen.de)** â€“ FÃ¼r das Netzwerk und die Inspiration

**Made with â¤ï¸ by the Meshhessen Community** Â· [www.meshhessen.de](https://www.meshhessen.de)


---

## ðŸ” Verwandte Suchbegriffe

Meshhessen Client Â· Windows-Client fÃ¼r Meshtastic-GerÃ¤te Â· Meshtastic Windows App Â· Meshtastic PC Software Â· Meshtastic Desktop App Â· Meshtastic USB Windows Â· Meshtastic Serial Windows Â· Meshtastic WPF Â· Meshtastic .NET Â· LoRa Mesh Windows Â· Meshtastic Offline Karte Â· Meshtastic Telemetrie Â· Meshtastic Traceroute Â· Meshtastic Hessen Â· Meshtastic Deutschland Â· Meshtastic Germany Â· LILYGO T-Beam Windows Â· T-Deck Windows Â· RAK4631 Windows Â· Heltec Windows Â· Meshtastic BLE Windows Â· Meshtastic Bluetooth Windows Â· Meshtastic PKI Â· Meshhessen Community Â· Meshhessen


---

# Meshhessen Client â€“ Windows Client for Meshtastic Devices (WPF Â· .NET 8)

**Meshhessen Client** is a **free, native Windows client for Meshtastic devices** (WPF/.NET 8) for Windows 10/11. Connect your **Meshtastic device** (LILYGO T-Beam, T-Deck, RAK4631, Heltec, etc.) via **USB/Serial, TCP/WiFi, or Bluetooth** to your Windows PC â€“ fully **offline-capable**, no cloud, no installation required. Developed by and for the [Meshhessen Community](https://www.meshhessen.de).

 ![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
 ![.NET](https://img.shields.io/badge/.NET-8.0-purple)
 ![Status](https://img.shields.io/badge/Status-v1.6.4.5-yellow)
 ![License](https://img.shields.io/github/license/SMLunchen/mh_windowsclient)
 ![Stars](https://img.shields.io/github/stars/SMLunchen/mh_windowsclient)


## ðŸš€ Quick Start


1. Download and install the [**.NET 8 Desktop Runtime**](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
2. **Download:** Get the latest `MeshhessenClient.exe` from [Releases](../../releases)
3. **Connect device:** Plug in your Meshtastic device via USB
4. **Launch:** Double-click `MeshhessenClient.exe` â€“ no installation required
5. **Connect:** Select connection type (Serial, TCP, or Bluetooth) â†’ click "Connect"
6. **Start:** Wait 3â€“10 seconds for channels to load, then send messages

> The app is fully offline-capable. No cloud, no registration, no telemetry to the developer.


## âœ¨ Features

### ðŸ“¨ Messaging & Communication

* **Send and receive messages** (broadcast & direct messages)
* **Multi-channel** â€“ all channels from your device loaded automatically
* **Direct Messages (DMs)** with a dedicated chat window in a tabbed layout
* **PKI decryption** â€“ client-side decryption of PKI-encrypted DMs (X25519 + AES-256-CTR); private key is loaded automatically from the connected device
* **Node public key database** â€“ local CSV file (`node_keys.csv`) for managing public keys for PKI decryption
* **Tap-back reactions** â€“ react to messages with emoji (32 emojis, like the Android app)
  * Right-click a message â†’ emoji picker
  * Reactions are sent to the sender/channel and displayed inline
  * Works in channel chat and DMs
* **Reply function** â€“ reply to individual messages (protocol-level)
  * Quoted block with accent-colored highlight of the original message
  * Works in channel chat and DMs
* **Selectable & copyable messages** â€“ select text, copy to clipboard, click links
  * Right-click any message â†’ "Copy Message" in context menu
  * Always copies the right-clicked message (not just the last selected row)
  * Context menu available even for messages from unknown senders
* **ðŸš¨ Alert Bell support** â€“ send and receive emergency alerts
  * ðŸš¨ SOS button in chat and DMs
  * Visual: red blinking border + notification bar with "Jump to map" button
* **Persistent message database** â€“ store channel and DM messages in a local SQLite DB (optional, enable in settings)
  * Last 24 hours loaded automatically on connect; older messages lazy-loaded when scrolling up
  * **DM history:** all conversations from the last 24 h appear automatically when the DM window is opened
  * **Per-channel deletion** directly in the Channels tab: "Clear Message DB" column with a time-range dialog (All / 30 / 90 / 365 days)
  * Confirmation before clearing a DM conversation (no accidental wipe)
  * Configurable retention period (30 / 90 / 365 days)

### ðŸ“Š Telemetry & Statistics

* **Persistent telemetry database** â€“ received packet and device data is stored locally in a SQLite database
* **Telemetry Dashboard** â€“ freely configurable widget dashboard with named dashboards:
  * Four widget types: line chart (time series), bar chart (current comparison), gauge (single value), heatmap (hour Ã— day)
  * Multi-node support, all metrics: SNR, RSSI, battery, voltage, channel/TX utilization, temperature, humidity, pressure
  * Persisted in `dashboards.json`, dark OxyPlot theme
* **Node statistics window** â€“ detailed analysis per node:
  * Day/night breakdown of reception quality
  * Time-series graphs for SNR, RSSI, packet rate and device data (battery, voltage)
* **LED indicators** in the main window show the connected node's status at a glance:
  * ðŸ“¶ **Signal** â€“ reception quality (SNR/RSSI trend)
  * ðŸ‘¥ **Neighbors** â€“ number and stability of direct neighbor nodes
  * ðŸ›¤ï¸ **Path stability** â€“ stability of the traceroute path
  * ðŸŒ **Mesh health** â€“ overall health of the visible mesh
  * ðŸŒ¤ï¸ **Weather** â€“ for nodes with environmental sensors

### ðŸ—ºï¸ Map

* **Three map styles:** OSM Standard, OSM Dark Mode, OpenTopoMap (topographic)
* **Four map modes** (switchable in settings):
  * **Offline** (default) â€“ pre-downloaded tiles from the Meshhessen server, no internet required, Germany only
  * **Online â€“ Meshhessen Server** â€“ tiles fetched on demand from `tile.meshhessenclient.de` and stored permanently; all three styles, Germany only
  * **Online â€“ custom tile server** â€“ same as above but using your own configured tile URLs from the settings; public OSM/OpenTopoMap servers are not allowed here (tile usage policy)
  * **Online â€“ OpenStreetMap** â€“ worldwide coverage via `tile.openstreetmap.org`; standard map only; OSM tile policy compliant (no bulk download, tiles cached locally)
* **Node positions** as colored pins on the map
* **Node paths** â€“ record GPS position history and display tracks on the map
* **Direct neighbor lines** â€“ toggle in the legend box: draws lines from your own position to every node we received **directly over RF (0 hops, no MQTT)**
  * Color gradient by **SNR** (red â†’ yellow â†’ green) or by **age** of the last direct contact (cyan â†’ purple)
  * **"Permanent"** option shows every neighbor ever heard directly (instead of only the last 24 h); historical 0-hop contacts come from the telemetry DB
  * Dark outline beneath each line for good visibility even on the topo map
* **Waypoints** â€“ received waypoints are displayed on the map; create and send waypoints via right-click on the map

### ðŸ“¡ Traceroute

* **Start traceroute** â€“ directly from the node context menu (node list, map, message list)
* **Dedicated window** per target node with:
  * Hop table: node name, distance, SNR (in dB), MQTT indicator (âš¡ lightning symbol)
  * Live status (waiting / received)
* **Map plotting** â€“ plot the route with lines
  * Solid line where positions are known
  * Dashed line where positions are missing
  * Direction arrows on segments
  * Click a segment â†’ SNR popup for that hop
  * Falls back to the configured map position if own GPS is unavailable
* **Multiple traceroutes at once** on the map â€“ each gets a unique color
* **Save & Load** â€“ traceroutes are automatically saved to `traceroutes/` as JSON
  * Compare routes recorded at different times
  * Load multiple files simultaneously
  * Load historical traceroutes directly from the database
* **Time range filter** â€“ filter traceroutes by period (3d / 7d / 14d / 30d / 90d / All)
* **Deduplication** â€“ show only the latest traceroute per node pair (optional)
* **Map legend** â€“ shows all active traces with color and individual âœ• remove button

### ðŸ–§ Virtual Node (Tools Tab)

* **TCP proxy server** â€“ connect Meshtastic apps (Android/iOS) to the Meshhessen Client as if it were a real Meshtastic device
* **Configurable in the Tools tab:** port (default: 4404), enable/disable, optionally block admin commands
* **Auto-start** as soon as a connection to the physical node is established (when enabled)
* **Config replay:** connecting apps receive all channels, node list and device config immediately
* **Bidirectional visibility:** messages sent in the app appear in connected apps â€“ and vice versa
* **Multi-client:** multiple apps can connect simultaneously; status and connected IPs are shown in the Tools tab

### ðŸ”§ Node Management

* **Node overview** â€“ all nodes in the mesh with SNR, battery, distance, hop count
  * **Tile view (Fancy View)** â€“ optionally enabled in Settings â†’ Appearance: replaces the table with a responsive tile surface (column count grows/shrinks with the window width)
    * Per tile: ShortName badge in node color, ðŸ”‘/ðŸ”“ PKI status, full name, "X min ago", â­ favorite star, ðŸ“¡ infrastructure and â˜ MQTT icons
    * Power (externally powered or battery + voltage), distance + altitude, hops, RSSI/SNR with color gradient, environment telemetry (ðŸŒ¡/ðŸ’§/ðŸŒ¬), hardware model Â· device role Â· node ID
    * Own node always on top, virtualized scrolling even with 1000+ nodes
  * **SNR/RSSI only for direct reception** â€“ signal values are shown only when we hear the node directly (0 hops, no MQTT); for relayed nodes the column shows the hop count instead
  * **Advanced filters** â€“ by "last seen" (5 min to 60 days), hide MQTT nodes, favorites only, SNR coloring on/off; sort dropdown in tile mode
* **Request information** â€“ right-click submenu (list, tile, map) requests data from a node like the Android app: user info, position, device/environment/air-quality/power/host metrics, signal quality/mesh stats, PAX counter
* **Firmware version & hardware model** â€“ queried automatically on connect and shown in the node info window
* **PKI key indicator** â€“ ðŸ”‘ column shows whether a node's public key is known
* **Node colors** â€“ color-code nodes individually (map + lists)
* **Node notes** â€“ free-text annotations per node
* **Pin nodes** â€“ pin nodes to the top of the list (independent of sorting); your own node can be pinned too
* **Per-node station name** â€“ a dedicated station name per connected node; âœ button next to the Connect button; resolution global â†’ node-specific â†’ ShortName
* **Favorites** â€“ mark nodes as favorites (â˜… icon, right-click menu); synced with the device (`set_favorite_node` / `remove_favorite_node`); favorites shown at the top of the list
* **Remote Administration** â€“ full remote admin UI for favorite nodes: all configuration tabs (Owner, Device, Position, LoRa, Bluetooth, Network, Display, Channels, Security, Control), session-key handshake, configurable timeout, lazy loading (load-on-demand per tab) and "â†» Reload Tab" button
* **Node configuration** â€“ full device configuration directly from the client, all modules in one window:
  * **Device & LoRa:** region, modem preset, TX power, hop limit, device role, rebroadcast mode
  * **Position:** GPS mode, smart broadcast, fixed position with coordinate input; "Pick from Map" opens a dedicated map picker window (respects offline/online settings); "Own Map Pin" copies the position set via right-click
  * **Network:** Wi-Fi SSID/PSK, DHCP/static IP, Ethernet, NTP server, syslog
  * **Display:** screen timeout, carousel, compass, 12h clock, metric/imperial, display mode, OLED type
  * **MQTT:** server, credentials, TLS, JSON mode, root topic, proxy mode, map reporting with precision level (bits 10â€“19), MQTT proxy client
  * **Telemetry:** device, environment, air quality and power metric intervals
  * **Bluetooth:** enable, pairing mode, fixed PIN
  * **Security:** display & generate public/private key, admin keys, managed mode
  * **Channels:** all 8 channels â€“ role, name, uplink, downlink, position precision (bits 10â€“19, ~23 km to ~45 m)
  * **Additional modules:** neighbor info, store & forward, external notification, canned messages, range test, serial
* **Change BT PIN** â€“ set Bluetooth PIN directly from the client

### âš™ï¸ Connection & System

* **Multi-connection** â€“ USB/Serial, TCP/WiFi, and Bluetooth (BLE)
* **Remember last connection** â€“ connection type (Serial / BT / WiFi) and last used BT device are saved and pre-selected on next launch
* **Auto-reconnect** â€“ after settings changes that require a device reboot
* **Update check** â€“ automatically checks for new versions on startup; a clickable hint appears in the status bar if an update is available (offline-safe: no error if no internet)
* **Multi-language** â€“ German and English (switchable in settings)
* **Dark mode** & ModernWPF Fluent design
* **Automatic logging** of all messages (`logs/`)
* **Debug tab** with live log for troubleshooting
* **Meshhessen quick-config** â€“ one-click Short Slow + EU868 + Meshhessen channel setup
* **Kiosk / training mode** â€“ for shared stations (club room, training, events):
  * Enable in settings, set a password and choose what gets hidden while locked: tabs (Nodes, Channels, Settings, Info, Tools, Debug), node configuration, remote admin, telemetry dashboard, SOS button, Meshhessen quick-config
  * **ðŸ”’ lock in the footer bar** to lock/unlock (only shown once a password is set); unlock via password, the app always starts locked in kiosk mode
  * While locked, **admin commands from Virtual Node clients are additionally blocked** (regardless of the VNode setting)
  * âš ï¸ **Protects against accidents, not attacks** â€“ the password is stored as a PBKDF2 hash, but anyone who can edit the INI file can disable the mode
  * **Forgot the password?** Close the client and set `KioskModeEnabled=True` to `False` in `meshhessen-client.ini` (or clear the `KioskPasswordHash=â€¦` line and set a new one)


## ðŸ’¬ The Meshhessen Community

The Meshhessen Client is a community project of the Meshtastic community in Hesse, Germany. Our regional LoRa mesh network is growing â€“ join us!

* ðŸŒ **Website:** [www.meshhessen.de](https://www.meshhessen.de)
* ðŸ“¡ **Network:** Growing mesh network in Hesse and surrounding areas â€“ airtime is not all-you-can-eat â†’ Short Slow! ;)
* ðŸ¤ **Contribute:** Set up your own node, extend coverage, grow the community


## âš ï¸ Known Limitations

* ~~No persistent message history~~ â†’ now available as optional message database (enable in settings)
* Tested with firmware 2.x
* T-Deck: channels are not always included in the config sequence (retry workaround active) â€“ The T-Deck is barely keeping up with itself, so everything takes a bit longer thereâ€¦
* ~~Heltec boards abort local configuration after 4â€“5/17 configs~~ â†’ fixed by sequential loading (send â†’ wait for response â†’ send next)


## ðŸ—ºï¸ Setting Up the Offline Map

**Map types:** OSM Standard (light), OSM Dark Mode, OpenTopoMap (topographic) â€“ selectable in settings.

> âš ï¸ **Important:** Please do NOT switch to the official OSM tile server â€“ offline downloads violate their policy. We use our own server that explicitly permits this. You can configure your own tile server in settings.

**Downloading tiles:**


1. Open settings â†’ select map source (OSM / OSM Dark / OpenTopo)
2. Click **"Download Tiles"**
3. Enter bounding box and zoom levels â€“ e.g. Hesse: `49.3,7.7,51.7,10.2`, Zoom `1-14`
4. Start download
5. Tiles are saved under `maptiles/` and are permanently available offline
6. Tiles are portable â€“ transferable via USB

**Using the map:**

* Open the **"ðŸ—ºï¸ Map"** tab
* Right-click on map â†’ set own location
* Node pins appear automatically once GPS data is received
* Right-click on a node â†’ set color, send DM, edit note, start traceroute, open telemetry


## ðŸ—ï¸ Technical Overview

| Component | Technology |
|----|----|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protocol | Meshtastic Protobuf over Serial/TCP/BLE |
| Map | Mapsui 4.1 + local OSM tiles |
| Serialization | Google.Protobuf, System.Text.Json |
| Connection | Serial (0x94 0xC3 framing), TCP/WiFi, Bluetooth Low Energy |

**Connection types:**

| Type | Transport | Framing | Notes |
|----|----|----|----|
| USB/Serial | COM port, 115200 baud | 4-byte header (0x94 0xC3 + length) | Wake-up sequence, debug text interleaved |
| TCP/WiFi | TCP socket | 4-byte header (same as serial) | Configurable hostname/IP + port |
| Bluetooth | BLE GATT characteristics | Raw protobuf (no framing) | Direct FromRadio/ToRadio packets |

**Connection sequence:**

```
Windows Client â†’ USB/Serial | TCP/WiFi | BLE â†’ Meshtastic Node â†’ LoRa â†’ Mesh
```


1. Open connection â†’ send wake-up sequence (Serial/TCP only) â†’ send `want_config_id`
2. Receive `my_info`, `node_info` (Ã—N), `channel` (Ã—8), `config`, `config_complete_id`
3. If channels are missing (e.g. T-Deck): retry mechanism up to 3 rounds via `GetChannelRequest`
4. Ready for MeshPackets


## ðŸ”§ Building from Source

**Requirements:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone https://github.com/SMLunchen/mh_windowsclient.git
cd mh_windowsclient
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

The EXE will be at `public\MeshhessenClient.exe`. Alternatively, run `build.bat`.

### Forks & Reuse

You are welcome to fork and adapt the source code within the terms of the license â€” we're happy to see derivative projects.

> âš ï¸ **The Meshhessen tile server is not part of that.** The map infrastructure (`tile.meshhessenclient.de` and the associated vector/raster endpoints) is provided **exclusively for the official Meshhessen Client** and is funded by community donations. Forks, derivative or repurposed clients (including ports to other mesh protocols such as MeshCore) **may not** use these servers and must **run their own tile infrastructure**.
>
> The client ships with everything you need for that: the **"Online â€“ custom tile server"** map mode in settings accepts any tile URLs you like. A simple tile cache (e.g. your own OSM/OpenTopo proxy) is quick and cheap to set up.
>
> Unauthorized access to the Meshhessen servers is blocked at the technical level.


## ðŸ™ Credits

* **[Meshtastic Project](https://meshtastic.org)** â€“ Firmware & protocol specification
* **[ModernWPF](https://github.com/Kinnara/ModernWpf)** â€“ Fluent UI for WPF
* **[Mapsui](https://mapsui.com)** â€“ Offline map
* **[Meshhessen Community](https://www.meshhessen.de)** â€“ For the network and the inspiration

**Made with â¤ï¸ by the Meshhessen Community** Â· [www.meshhessen.de](https://www.meshhessen.de)


---

## ðŸ” Related Search Terms

Meshhessen Client Â· Windows client for Meshtastic devices Â· Meshtastic Windows app Â· Meshtastic PC software Â· Meshtastic desktop app Â· Meshtastic USB Windows Â· Meshtastic serial Windows Â· Meshtastic WPF Â· Meshtastic .NET Â· LoRa mesh Windows Â· Meshtastic offline map Â· Meshtastic telemetry Â· Meshtastic traceroute Â· Meshtastic Germany Â· Meshtastic Hessen Â· LILYGO T-Beam Windows Â· T-Deck Windows Â· RAK4631 Windows Â· Heltec Windows Â· Meshtastic BLE Windows Â· Meshtastic Bluetooth Windows Â· Meshtastic PKI decryption Â· Meshhessen community Â· Meshhessen
