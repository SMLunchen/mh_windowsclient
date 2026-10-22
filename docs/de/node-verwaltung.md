# 🔧 Node-Verwaltung & Fernkonfiguration

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/node-management/)

## Knoten-Übersicht

* **Alle Nodes im Mesh** mit SNR, Batterie, Entfernung, Hop-Anzahl
  * **Kachelansicht (Fancy View)** – optional in Einstellungen → Darstellung aktivierbar: tauscht die Tabelle gegen eine responsive Kachel-Oberfläche (Spaltenanzahl wächst/schrumpft mit der Fensterbreite)
    * Pro Kachel: ShortName-Badge in Node-Farbe, 🔑/🔓 PKI-Status, voller Name, „vor X min", ⭐-Favoritenstern, 📡 Infrastruktur- und ☁ MQTT-Symbol
    * Strom (extern versorgt oder Batterie + Spannung), Entfernung + Höhe, Hops, RSSI/SNR mit Farbverlauf, Umwelt-Telemetrie (🌡/💧/🌬), Hardware-Modell · Geräterolle · Node-ID
    * Eigener Node immer ganz oben, virtualisiertes Scrolling auch bei 1000+ Nodes
  * **SNR/RSSI nur bei direktem Empfang** – Signalwerte werden nur angezeigt, wenn wir den Node direkt (0 Hops, kein MQTT) hören; bei Relay-Nodes zeigt die Spalte stattdessen die Hop-Anzahl
  * **Erweiterte Filter** – nach „zuletzt gesehen" (5 Min bis 60 Tage), MQTT-Nodes ausblenden, nur Favoriten, SNR-Einfärbung an/aus; Sortier-Dropdown im Kachelmodus
* **Informationen anfordern** – Rechtsklick-Untermenü (Liste, Kachel, Karte) fordert wie die Android-App gezielt Daten von einem Node an: Benutzer-Info, Position, Geräte-/Umwelt-/Luftqualität-/Strom-/Host-Metriken, Signalqualität/Mesh-Statistik, PAX-Zähler
* **Firmware-Version & Hardware-Modell** – werden automatisch beim Verbinden abgefragt und im Node-Info-Fenster angezeigt:

<img alt="Meshhessen Client – Node-Info-Fenster mit Rolle, Akku, Signal, Zuerst/Zuletzt gesehen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node-info-window.png" />
* **PKI-Schlüssel-Indikator** – 🔑-Spalte zeigt, ob der Public Key des Nodes bekannt ist
* **Node-Farben** – Nodes individuell einfärben (Karte + Listen)
* **Node-Notizen** – Freitext-Notizen pro Node
* **Nodes anpinnen** – Nodes in der Liste oben fixieren (unabhängig von Sortierung)
* **Favoriten** – Nodes als Favoriten markieren (★-Symbol, Rechtsklick-Menü); wird mit dem Gerät synchronisiert (`set_favorite_node` / `remove_favorite_node`); Favoriten erscheinen oben in der Liste

Node-Übersicht als Tabelle – mit Avatar, Signal-LEDs, Filtern und Sortierung:
<img alt="Meshhessen Client – Node-Übersicht als Tabelle mit Avatar, SNR, RSSI, Batterie und Signal-LEDs" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/nodelist_new.png" />

Node-Übersicht als Kachelansicht (Fancy View) – mit Telemetrie, Strom und Signalqualität pro Kachel:
<img alt="Meshhessen Client – Node-Kachelansicht (Fancy View) mit Telemetrie, Batterie und Signalqualität pro Node" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/nodelist_new_tiles.png" />

## Fernverwaltung (Remote Admin)

Vollständige Remote-Admin-Oberfläche für Favoriten-Nodes — funktioniert auch über Relay- und MQTT-Strecken:

<img alt="Meshhessen Client – Remote Administration eines Meshtastic-Nodes: LoRa-Konfiguration über Funk" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/remote-admin.png" />

### So administrierst du einen entfernten Node (Schritt für Schritt)

1. **Voraussetzungen:** Dein Public Key muss auf dem Ziel-Node als Admin-Schlüssel eingetragen sein (Security-Tab des Ziel-Nodes, Admin-Schlüssel 1–3) — oder du nutzt einen gemeinsamen Admin-Channel. Außerdem muss der Ziel-Node den Public Key deines Nodes kennen (PKI).
2. Ziel-Node als **Favorit** markieren (★ im Rechtsklick-Menü) — nur Favoriten bekommen den Menüpunkt
3. Rechtsklick auf den Node → **„Fernverwaltung"** — der Client handelt einen Session-Schlüssel aus und lädt die Konfiguration (wahlweise alles sofort oder pro Tab beim Öffnen)
4. Einstellungen ändern → **„Speichern"** schreibt sie über Funk zurück; der Ziel-Node startet bei Bedarf neu
5. **Typische Fehler:** Timeout ohne Antwort → Admin-Schlüssel fehlt auf dem Ziel oder der Node ist gerade nicht erreichbar (Traceroute testen); „kein Schlüssel" → Public Key des Ziels fehlt lokal (Node einmal direkt empfangen oder Key in `node_keys.csv` eintragen). Ablehnungen meldet der Client mit konkretem Grund statt still zu scheitern.
6. Über mehrere Hops dauert jede Antwort einige Sekunden — der **Timeout** ist im Fenster einstellbar (Standard 120 s), einzelne Tabs lassen sich per „↻ Tab neu laden" wiederholen

Auch **Favoriten auf dem entfernten Gerät** lassen sich remote setzen/entfernen:
<img alt="Meshhessen Client – Favoriten-Node remote auf dem entfernten Meshtastic-Gerät setzen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/remote-admin-set-favorite-node.png" />

Features im Überblick:

* Alle Konfigurations-Reiter: Besitzer, Gerät, Position, LoRa, Bluetooth, Netzwerk, Anzeige, Kanäle, **Sicherheit**, Steuerung
* **Sicherheits-Reiter:** Public Key (read-only, Base64), Admin-Schlüssel 1–3 (Base64), Flags (Admin Channel, Managed Mode, Serial, Debug Log)
* **Favoriten remote verwalten:** Knoten auf dem Remote-Gerät als Favorit setzen oder entfernen
* Session-Schlüssel-Handshake, konfigurierbarer Timeout, per-Channel Retry + Neu-laden
* **Lazy Loading:** Beim Öffnen wählen, ob alles sofort oder seitenweise geladen wird; „↻ Tab neu laden"-Button für gezielten Reload einzelner Reiter

## Node-Konfiguration (lokal)

Vollständige Gerätekonfiguration direkt aus dem Client, alle Module auf einer Seite:

* **Gerät & LoRa:** Region, Modem-Preset, TX-Power, Hop-Limit, Geräterolle, Rebroadcast-Modus
* **Position:** GPS-Modus, Smart-Broadcast, feste Position mit Koordinaten-Eingabe; „Von Karte wählen" öffnet ein eigenes Kartenfenster (honoriert Offline-/Online-Einstellungen); „Eigener Kartenpin" übernimmt die per Rechtsklick gesetzte Position
* **Netzwerk:** WLAN SSID/PSK, DHCP/statische IP, Ethernet, NTP-Server, Syslog
* **Display:** Screen-Timeout, Carousel, Kompass, 12h-Uhr, metrisch/imperial, Display-Modus, OLED-Typ
* **MQTT:** Server, Credentials, TLS, JSON-Modus, Root-Topic, Proxy-Modus, Map-Reporting mit Genauigkeitsstufe (Bits 10–19), MQTT-Proxy-Client-Funktion
* **Telemetrie:** Geräte-, Umgebungs-, Luftqualitäts- und Energiemetrik-Intervalle
* **Bluetooth:** Aktivieren, Pairing-Modus, fixer PIN; **BT-PIN ändern** direkt aus dem Client
* **Security:** Public/Private Key anzeigen & generieren, Admin-Schlüssel, Managed Mode
* **Kanäle:** Alle 8 Kanäle – Rolle, Name, Uplink, Downlink, Positions-Genauigkeit (Bits 10–19, ~23 km bis ~45 m)
* **Weitere Module:** Nachbar-Info, Store & Forward, Externe Benachrichtigung, Canned Messages, Range Test, Serial
