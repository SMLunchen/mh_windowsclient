# 📊 Telemetrie & Funkanalyse

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/telemetry/)

## Telemetrie-Datenbank & Dashboard

* **Persistente Telemetrie-Datenbank** – empfangene Paket- und Gerätedaten werden lokal in einer SQLite-Datenbank gespeichert
* **Telemetrie-Dashboard** – frei konfigurierbares Widget-Dashboard mit benannten Dashboards (📊-Button in der Toolbar):
  * **14 Widget-Typen:** Linie, Fläche, Balken, Scatter, Gauge, Stat-Wert, Heatmap, Histogramm, Candlestick, State-Timeline, Ranking, Multi-Stat, Mesh-Health, Uhrzeit
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

## How-to: Dashboard anlegen, Alarm einrichten, Werte deuten

**Ziel:** Ein Dashboard, das die Batterie deines Solar-Routers überwacht und dich warnt, bevor er nachts leerläuft.

1. **Öffnen:** 📊-Button in der Toolbar des Hauptfensters → beim ersten Öffnen ist das Fenster leer; mit „+ Neu“ legst du dein erstes Dashboard an (Namensvorschlag „Dashboard 1“ — nenn es z. B. „Solar-Monitoring“)
2. **Widget hinzufügen:** „+ Widget hinzufügen“ → Typ wählen (für Batterie: **Zeitverlauf/Linie**), Node(s) anhaken, Metrik **Batterie (%)**, Zeitraum **7 Tage** (voreingestellt) → Speichern. Auf der fertigen Karte schaltest du per 1d/7d/14d/30d-Buttons um, sortierst per ⠿-Griff um und skalierst am Griff unten rechts
3. **Alarm einrichten:** Im Widget den gewünschten Grenzwert ins **Schwellwert**-Feld eintragen (z. B. `30`). Die Schwellwert-Linie erscheint im Chart; **kreuzt** der Wert die Schwelle bei einer Aktualisierung, kommt ein **Windows-Toast** (nur beim Übergang — kein Dauer-Gepiepe)
4. **Aktuell halten:** Auto-Refresh oben rechts (z. B. 30 s); sendet der Node selbst zu selten, nimm ihn über den **📥 Abfragen**-Button in den Abfrageplan (max. 4 Nodes, mesh-schonend)
5. **Erfolgskontrolle & Alarm-Test:** Das Widget zeigt eine Kurve mit Tag/Nacht-Schattierung; unten steht die letzte Aktualisierungszeit. Den Alarm testest du in zwei Schritten: Schwellwert erst auf dieselbe Seite wie der aktuelle Wert legen (z. B. deutlich unter den Batteriestand) und eine Aktualisierung abwarten — dieser erste Check merkt sich still die Ausgangslage. Dann die Schwelle über den aktuellen Wert schieben und „↺ Aktualisieren“ drücken (oder den Auto-Refresh abwarten): jetzt kommt der Toast
6. **Werte deuten:** Fällt die Batterie nachts um mehr als ~15–20 %, lohnt der Blick ins Kapitel [Messwerte verstehen](messwerte.md) (Nachtabfall, Spannung); die SNR/RSSI-Interpretation steht dort und im [Glossar](glossar.md)

**Typische Fehler:**

* *Widget bleibt leer* → für diesen Node/diese Metrik liegen noch keine Daten in der Telemetrie-Datenbank (der Node sendet die Metrik nicht → Polling aktivieren oder per Rechtsklick „Informationen anfordern“)
* *Kein Toast trotz Schwellwert* → geprüft wird nur bei einer **Aktualisierung** (Auto-Refresh, „↺“ oder Editor-Speichern), nicht schon beim Eintippen — und der allererste Check nach dem Öffnen setzt stumm die Ausgangslage, gemeldet wird erst der nächste **Übergang**. Außerdem prüfen: Sind Windows-Benachrichtigungen für Apps erlaubt?
* *Zoom springt zurück* → Zoom/Pan bleibt beim Auto-Refresh erhalten; der manuelle „Aktualisieren“-Knopf setzt die Ansicht bewusst zurück

<!-- SCREENSHOT-WUNSCH: Widget-Editor offen (Typ/Node/Metrik/Threshold sichtbar) -->
<!-- SCREENSHOT-WUNSCH: Windows-Toast einer Schwellwert-Warnung -->

## Node-Statistik & echte HF-Nachbarn

* **Node-Statistik-Fenster** – detaillierte Auswertung pro Node:
  * Tag/Nacht-Auswertung der Empfangsqualität
  * Zeitreihen-Graphen für SNR, RSSI, Paketrate und Gerätedaten (Batterie, Spannung)
  * **Echte HF-Nachbarn** – belegt durch Direktempfang, Traceroute-Teilstrecken und NeighborInfo-Pakete, mit SNR in beide Richtungen (füllt sich mit der Zeit; bis dahin automatisch die klassische „gehört"-Liste)
  * **LocalStats** (neuere Firmware): Noise Floor, RX fehlerhaft/Duplikate, TX Relay u. v. m. (Routing-Tab)
* **LED-Ampel „Netz:“** im Hauptfenster — der Zustand des gesamten sichtbaren Meshes auf einen Blick:
  * **Wetter/KT** – „Signal-Wetter“: Kurzzeit-SNR/RSSI-Trend aller Nodes (letzte 6 h)
  * **LZ-SNR** – Antennen-Gesundheit: Langzeit-SNR-Trend aller Nodes (letzte 7 Tage)
  * **NB** – Nachbar-Qualität: durchschnittliche Verbindungsqualität zu den Nachbar-Nodes
  * **PS** – Routen-Stabilität: Häufigkeit von Routenänderungen im Mesh
  * **Mesh Health** – Gesamt-Score 0–100 % aus Pfadkosten, Routenwechseln, Empfangsrate und Kanalauslastung

## ⛰ Höhenprofil pro Funkstrecke

Echte HF-Nachbarn mit SNR in beide Richtungen — der **Rechtsklick** auf einen Nachbarn (oder einen Traceroute-Hop) öffnet das Höhenprofil des Links:

![Meshhessen Client – Echte HF-Nachbarn mit SNR in beide Richtungen und Höhenprofil-Kontextmenü](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_neighbors.png)

Das Höhenprofil zeigt dann das SRTM-Geländeprofil der Funkstrecke mit Sichtlinie und 60-%-Fresnel-Zone (4/3-Erdkrümmung, 868 MHz) samt Einschätzung, ob die Strecke frei ist oder Gelände im Weg steht:
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

Airtime-Auswertung pro Node (TX/RX-Sendezeit, Kanalauslastung):

![Meshhessen Client – Meshtastic Airtime-Analyse: TX-Airtime, RX-Airtime und Kanalauslastung pro Node](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_airtime.png)

Batterie & Spannungs-Analyse (Tag/Nacht-Profil, Nachtabfall):

![Meshhessen Client – Meshtastic Batterie-Analyse: Spannungs-Verlauf, Tag/Nacht-Profil und Nachtabfall](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_power.png)

Routing-Statistik (Hop-Anzahl, Pfad-Stabilität, Pfadwechsel-Rate):

![Meshhessen Client – Meshtastic Routing-Statistik: Hop-Anzahl, Pfad-Stabilität und Pfadwechsel-Rate](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_routing.png)

> 📖 Was die einzelnen Messwerte bedeuten (SNR, RSSI, Kanalauslastung, Hop-Kosten …) erklärt ausführlich das Kapitel [Messwerte verstehen](messwerte.md).
