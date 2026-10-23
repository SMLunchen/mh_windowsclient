# 📊 Telemetrie & Funkanalyse

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/telemetry/)

## Telemetrie-Datenbank & Dashboard

* **Persistente Telemetrie-Datenbank** – empfangene Paket- und Gerätedaten werden lokal in einer SQLite-Datenbank gespeichert
* **Telemetrie-Dashboard** – frei konfigurierbares Widget-Dashboard mit benannten Dashboards (📊-Button in der Toolbar):
  * **14 Widget-Typen:** Linie, Fläche, Balken, Scatter, Gauge, Stat-Wert, Heatmap, Histogramm, Candlestick, State-Timeline, Ranking, Multi-Stat, Mesh-Health, Uhrzeit
  * Multi-Node-Support, alle Metriken: SNR, RSSI, Batterie, Spannung, Kanal-/TX-Auslastung, Temperatur, Luftfeuchtigkeit, Luftdruck, Pakete/h, Noise Floor, RX/TX-Statistiken
  * Widgets per Drag-and-Drop anordnen, frei skalierbar (Resize-Griff), editierbar, duplizierbar, Auto-Refresh
  * **Zoom/Pan auf der Zeitachse** (bleibt über den Auto-Refresh erhalten; Doppelklick setzt zurück), Tag/Nacht-Schattierung
  * **Warn- & Alarm-Zonen (Grafana-Stil):** pro Widget je eine Warn- und Alarm-Grenze nach oben und unten; Gauge/Stat/Charts färben sich grün/orange/rot nach Zone, **Windows-Toasts** melden jeden Zonen-Wechsel (inkl. ✅ Entwarnung)
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
3. **Alarm einrichten:** Im Widget-Editor unter **„Schwellwerte & Alarme“** die Grenzen setzen — für die Batterie z. B. ⚠ Warnung tief `30` und 🚨 Alarm tief `15`. Die Linien erscheinen im Chart (orange/rot), Gauge und Stat-Wert färben sich nach Zone; **wechselt** der Wert bei einer Aktualisierung die Zone, kommt ein **Windows-Toast** mit Schweregrad (nur beim Übergang — kein Dauer-Gepiepe, Rückkehr in den Normalbereich meldet ✅)
4. **Aktuell halten:** Auto-Refresh oben rechts (z. B. 30 s); sendet der Node selbst zu selten, nimm ihn über den **📥 Abfragen**-Button in den Abfrageplan (max. 4 Nodes, mesh-schonend)
5. **Erfolgskontrolle & Alarm-Test:** Das Widget zeigt eine Kurve mit Tag/Nacht-Schattierung; unten steht die letzte Aktualisierungszeit. Den Alarm testest du in zwei Schritten: Grenzen erst so setzen, dass der aktuelle Wert im Normalbereich liegt, und eine Aktualisierung abwarten — dieser erste Check merkt sich still die Ausgangslage. Dann eine Grenze über/unter den aktuellen Wert schieben und „↺ Aktualisieren“ drücken (oder den Auto-Refresh abwarten): jetzt kommt der Toast
6. **Werte deuten:** Fällt die Batterie nachts um mehr als ~15–20 %, lohnt der Blick ins Kapitel [Messwerte verstehen](messwerte.md) (Nachtabfall, Spannung); die SNR/RSSI-Interpretation steht dort und im [Glossar](glossar.md)

**Typische Fehler:**

* *Widget bleibt leer* → für diesen Node/diese Metrik liegen noch keine Daten in der Telemetrie-Datenbank (der Node sendet die Metrik nicht → Polling aktivieren oder per Rechtsklick „Informationen anfordern“)
* *Kein Toast trotz Alarm-Grenze* → geprüft wird nur bei einer **Aktualisierung** (Auto-Refresh, „↺“ oder Editor-Speichern), nicht schon beim Eintippen — und der allererste Check nach dem Öffnen setzt stumm die Ausgangslage, gemeldet wird erst der nächste **Zonen-Wechsel**. Außerdem prüfen: Sind Windows-Benachrichtigungen für Apps erlaubt?
* *Zoom springt zurück* → Zoom/Pan bleibt beim Auto-Refresh erhalten; der manuelle „Aktualisieren“-Knopf setzt die Ansicht bewusst zurück

<!-- SCREENSHOT-WUNSCH: Widget-Editor offen (Typ/Node/Metrik/Threshold sichtbar) -->
<!-- SCREENSHOT-WUNSCH: Windows-Toast einer Schwellwert-Warnung -->

## 📡 Funk-Echo: „Wer funkt mich an?“

Andere Stationen interagieren ständig mit deinem Node, ohne dass du es merkst — sie fragen NodeInfo, Position oder Telemetrie an, verfolgen per Traceroute den Funkweg zu dir oder klopfen an der Fernverwaltung an. Das Funk-Echo macht diese unsichtbaren Anfragen sichtbar:

* **Radar in der Statusleiste** (neben Mesh Health): Bei einer Anfrage sendet der kleine Funkmast eine farbige Welle aus (Farbe = Anfrageart, Traceroute pulst doppelt), ein Zähler sammelt Ungelesenes, und für ein paar Sekunden erscheint eine Meldung wie *„🛰️ KELL verfolgt den Funkweg zu uns!“*
* **Klick öffnet das Journal:** chronologische Übersicht, wer wann was angefragt hat — Anfrage-Serien derselben Station werden als ×n gebündelt, mit Quelle-Badge (direkt / n Hops / ☁ MQTT); bei Traceroute-Anfragen steht sogar der Hinweg („via …“), bei Telemetrie-Anfragen die angefragte Datengruppe (Umwelt-, Strom-, Gerätedaten …)
* **Statistik-Tab:** Anfragen, Stationen, Traces, „Was wollten die anderen wissen?“ (mit Telemetrie-Aufschlüsselung nach Datengruppe) und der neugierigste Node
* **📡 Neugier-Radar (Retro-Sonar):** eigener Tab im Stil eines klassischen Radarschirms — schwarzes Scope, phosphor-grüne Ringe, rotierender Sweep, dein Node leuchtet in der Mitte, die anfragenden Stationen drumherum (je neugieriger, desto näher, größer und heller). Trifft live eine Anfrage ein, breitet sich ein Sonar-Ping von der Station bis zur Mitte aus und wird „reflektiert“ zurückgeworfen. Klick auf eine Station zeigt stündliche Anfrage-Balken und die Aufschlüsselung
* **Filter:** Zeitraum (24 h / 7 / 30 Tage), Anfrageart und Node-Suche; Rechtsklick auf einen Eintrag öffnet Node-Info, Karte oder DM — bei Traceroute-Anfragen lässt sich der **Anfrageweg als Linie auf der Karte** zeigen
* **Antwort-Airtime:** eine Statistik-Kachel schätzt, wie viel Sendezeit die Auto-Antworten deines Nodes kosten (LoRa-Formel mit dem aktuellen Modem-Preset) — jede Anfrage kostet auch *deine* Airtime
* **Meldungsstil** in den Einstellungen umschaltbar: Locker („KELL verfolgt den Weg zu uns!“) oder Sachlich („Traceroute-Anfrage von KELL empfangen“); optional Windows-Toasts; Rechtsklick auf das Radar-Icon schaltet den 🔕 Ruhemodus; das Feature lässt sich komplett ausblenden und im [Kiosk-Modus](kiosk.md) sperren
* **Ehrlichkeit:** Erkannt wird die *Anfrage* — ob dein Node antwortet, leitet der Client nur aus der lokalen Konfiguration ab und sagt es dazu: „✓ Antwort erwartet“, bei Positionsanfragen inklusive der konfigurierten Kanal-Genauigkeit („±2,9 km“), „✕ keine Antwort (keine Position)“, „? je nach Sensorik“ oder „🔐 nur mit Admin-Berechtigung“. Die tatsächliche Aussendung ist im Client nicht sichtbar; Antworten auf deine eigenen Anfragen und mitgehörte Pakete an Dritte tauchen bewusst nicht auf
* **Dashboard-Anbindung:** die Metrik „📡 Funk-Echo (Anfragen an uns/h)“ steht im Telemetrie-Dashboard als Zeitreihe zur Verfügung — inklusive Warn-/Alarm-Zonen

<!-- SCREENSHOT-WUNSCH: Funk-Echo-Journal mit verschiedenen Anfragetypen (funk-echo-journal.png) -->
<!-- SCREENSHOT-WUNSCH: Statusleiste mit Funkmast + Flyout (funk-echo-pulse.png) -->

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
