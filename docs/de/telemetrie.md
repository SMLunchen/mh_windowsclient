# 📊 Telemetrie & Funkanalyse

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](../en/telemetry.md)

## Telemetrie-Datenbank & Dashboard

* **Persistente Telemetrie-Datenbank** – empfangene Paket- und Gerätedaten werden lokal in einer SQLite-Datenbank gespeichert
* **Telemetrie-Dashboard** – frei konfigurierbares Widget-Dashboard mit benannten Dashboards (📊-Button in der Toolbar):
  * **11 Widget-Typen:** Linie, Fläche, Balken, Scatter, Gauge, Stat-Wert, Heatmap, Histogramm, Candlestick, State-Timeline, Uhrzeit
  * Multi-Node-Support, alle Metriken: SNR, RSSI, Batterie, Spannung, Kanal-/TX-Auslastung, Temperatur, Luftfeuchtigkeit, Luftdruck, Pakete/h, Noise Floor, RX/TX-Statistiken
  * Widgets per Drag-and-Drop anordnen, frei skalierbar (Resize-Griff), editierbar, duplizierbar, Auto-Refresh
  * **Zoom/Pan in Charts** (bleibt über den Auto-Refresh erhalten), Tag/Nacht-Schattierung, Schwellwert-Linien mit **Windows-Toast-Alarm** bei Über-/Unterschreitung
  * **Export:** CSV (wahlweise für deutsches Excel), Widget als PNG, Bild in Zwischenablage, ganze Dashboards als JSON teilen (Export/Import)
  * **Vollbild-Modus (F11)** für Wandmonitore; Fensterposition und zuletzt aktives Dashboard werden gemerkt
  * **Automatische Telemetrie-Abfragen (📥):** bis zu 4 Nodes werden aktiv nach LocalStats/Umwelt-/Gerätedaten gefragt (mesh-schonend: LocalStats max. 1×/h, anderes max. alle 30 min); aus den Zähler-Deltas entstehen Raten in Pakete/h
  * Dark & Light Mode, Theme-adaptiv, folgt Windows-Theme-Wechseln live; Persistenz in `dashboards.json`
  * **Unabhängiges Fenster** – kann auf einem zweiten Monitor platziert werden

**📊 Telemetrie-Dashboard** – frei konfigurierbare Widgets mit Schwellwert-Alarmen, Auto-Refresh und Vollbild:
<img alt="Meshhessen Client – Telemetrie-Dashboard mit konfigurierbaren Widgets: Gauges, Heatmaps, Mesh-Health und Schwellwert-Alarmen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/telemetry-dashboard.png" />

## Node-Statistik & echte HF-Nachbarn

* **Node-Statistik-Fenster** – detaillierte Auswertung pro Node:
  * Tag/Nacht-Auswertung der Empfangsqualität
  * Zeitreihen-Graphen für SNR, RSSI, Paketrate und Gerätedaten (Batterie, Spannung)
  * **Echte HF-Nachbarn** – belegt durch Direktempfang, Traceroute-Teilstrecken und NeighborInfo-Pakete, mit SNR in beide Richtungen (füllt sich mit der Zeit; bis dahin automatisch die klassische „gehört"-Liste)
  * **LocalStats** (neuere Firmware): Noise Floor, RX fehlerhaft/Duplikate, TX Relay u. v. m. (Routing-Tab)
* **LED-Indikatoren** im Hauptfenster zeigen auf einen Blick den Status des verbundenen Nodes:
  * 📶 **Signal** – Empfangsqualität (SNR/RSSI-Trend)
  * 👥 **Nachbarn** – Anzahl und Stabilität direkter Nachbar-Nodes
  * 🛤️ **Pfad-Stabilität** – Stabilität des Traceroute-Pfads
  * 🌐 **Mesh-Health** – Gesamtzustand des sichtbaren Meshes
  * 🌤️ **Wetter** – für Nodes mit Umweltsensor

## ⛰ Höhenprofil pro Funkstrecke

Rechtsklick auf einen Nachbarn (oder einen Traceroute-Hop): SRTM-Geländeprofil mit Sichtlinie und 60-%-Fresnel-Zone (4/3-Erdkrümmung, 868 MHz) samt Einschätzung, ob die Strecke frei ist oder Gelände im Weg steht:
<img alt="Meshhessen Client – Höhenprofil einer Meshtastic-Funkstrecke: SRTM-Gelände, Sichtlinie und Fresnel-Zone" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/elevation-profile.png" />

## Weitere Screenshots

Telemetrie-LED-Ampel im Hauptfenster:
<img width="516" height="162" alt="Meshhessen Client – Meshtastic Telemetrie-LEDs für Signal, Nachbarn, Mesh-Health und Wetter" src="https://github.com/user-attachments/assets/a580cf69-ab10-4890-a17a-1c5086c19700" />

Telemetrie-Zeitreihen-Graph (SNR, RSSI, Batterie):
<img width="902" height="604" alt="Meshhessen Client – Meshtastic Telemetrie-Graph SNR RSSI Batterie Zeitreihe OxyPlot" src="https://github.com/user-attachments/assets/a42d6a79-5b90-4f7a-8456-c74099c55c41" />

Node-Telemetrie-Statistik-Fenster:
<img width="1278" height="889" alt="Meshhessen Client – Meshtastic Node-Telemetrie Tag/Nacht-Auswertung Statistik Windows" src="https://github.com/user-attachments/assets/4821c996-c524-499a-8fba-f738fb051f88" />

Node-Telemetrie-Übersicht (Signal):

![Meshhessen Client – Meshtastic Node-Telemetrie-Übersicht: Signal-Analyse, Routing, Airtime und Nachbar-Statistik](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node-telemetry.png)

Echte HF-Nachbarn mit SNR in beide Richtungen – Rechtsklick öffnet das Höhenprofil des Links:

![Meshhessen Client – Echte HF-Nachbarn mit SNR in beide Richtungen und Höhenprofil-Kontextmenü](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_neighbors.png)

Airtime-Auswertung pro Node (TX/RX-Sendezeit, Kanalauslastung):

![Meshhessen Client – Meshtastic Airtime-Analyse: TX-Airtime, RX-Airtime und Kanalauslastung pro Node](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_airtime.png)

Batterie & Spannungs-Analyse (Tag/Nacht-Profil, Nachtabfall):

![Meshhessen Client – Meshtastic Batterie-Analyse: Spannungs-Verlauf, Tag/Nacht-Profil und Nachtabfall](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_power.png)

Routing-Statistik (Hop-Anzahl, Pfad-Stabilität, Pfadwechsel-Rate):

![Meshhessen Client – Meshtastic Routing-Statistik: Hop-Anzahl, Pfad-Stabilität und Pfadwechsel-Rate](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_routing.png)

> 📖 Was die einzelnen Messwerte bedeuten (SNR, RSSI, Kanalauslastung, Hop-Kosten …) erklärt ausführlich die [Telemetrie-Hilfe auf der Website](https://meshhessenclient.de/#telemetrie).
