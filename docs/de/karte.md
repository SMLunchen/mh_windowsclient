# 🗺️ Karte & Offline-Nutzung

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/map/)

## Kartentypen & Vektorkarte

* **Drei Kartentypen:** OSM Standard, OSM Dark Mode, OpenTopoMap (topografisch)
* **Vektorkarten** (umschaltbar in den Einstellungen, Raster bleibt Standard):
  * Gestochen scharf in jeder Zoomstufe (MapLibre GL), deutlich kleinere Offline-Daten, Kartenstil-Updates ohne Client-Update
  * **🚒 Feuerwehr-/Rettungs-Layer** zuschaltbar (siehe unten)
  * Layer-Auswahl in den Einstellungen und über den 🗂️-Button direkt auf der Karte; solange ein Layer aus ist, wird kein Byte dafür geladen
  * **Automatischer Offline-Cache:** online betrachtete Gebiete sind ohne Internet weiter nutzbar; zusätzlich **Vektor-Offlinepaket-Downloader** (Bundesland/eigene Bounding-Box, Topo-Extras und Zusatz-Layer als Opt-in)
  * Voller Funktionsumfang: Node-Pins, Traceroutes, Nachbar-Linien, Positionsverläufe und Rechtsklick-Menü auch auf der Vektorkarte
  * **⛰ 3D-Gelände** – echtes Höhenmodell (SRTM, 90 m) mit Schummerung, zuschaltbar über das 🗂️-Layer-Popup; Strg+Ziehen neigt die Karte, bei gedrehter/geneigter Ansicht erscheint ein Nordpfeil (Klick = einnorden); einmal betrachtete Gebiete funktionieren auch offline
  * Benötigt die Microsoft-WebView2-Runtime (auf Windows 11 vorinstalliert); ohne sie fällt der Client automatisch auf Raster zurück

Die Vektorkarte (MapLibre GL) – gestochen scharf in jeder Zoomstufe, mit Node-Pins:
<img alt="Meshhessen Client – Vektorkarte (MapLibre GL) mit Meshtastic Node-Positionen unter Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/vector_in_action.png" />

**⛰ 3D-Gelände** – echtes Höhenmodell (SRTM) mit Schummerung auf der Vektorkarte, per Strg+Ziehen geneigt:
<img alt="Meshhessen Client – 3D-Gelände (SRTM-Höhenmodell) mit Schummerung auf der Meshtastic-Vektorkarte" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-with-dem-elevation-profile.png" />

## Karten-Modi

| Modus | Beschreibung | Abdeckung |
|----|----|----|
| **Offline** (Standard) | Vorher heruntergeladene Tiles vom Meshhessen-Server. Kein Internet erforderlich. | Deutschland, Österreich & Schweiz (DACH) |
| **Online – Meshhessen-Server** | Tiles werden bei Bedarf von `tile.meshhessenclient.de` geladen und dauerhaft lokal gespeichert. Alle drei Stile. | Deutschland, Österreich & Schweiz (DACH) |
| **Online – eigener Tile-Server** | Wie oben, aber mit selbst konfigurierten Tile-URLs; öffentliche OSM-/OpenTopoMap-Server sind hier nicht zulässig (Tile-Usage-Policy). | je nach Server |
| **Online – OpenStreetMap** | Weltweite Abdeckung über `tile.openstreetmap.org`; nur Standardkarte; OSM-Policy-konform (kein Bulk-Download, Tiles lokal gecacht). | Weltweit |

> Der OSM-Online-Modus ist für Gebiete außerhalb Deutschlands gedacht. Der OSM-Tile-Server ist spendenfinanziert – bitte sparsam nutzen.

## Tiles herunterladen (Offline-Nutzung)

> ⚠️ **Wichtig:** Tile-Download ist nur für den Meshhessen-Tile-Server (`tile.meshhessenclient.de`) vorgesehen – Bulk-Downloads verstoßen gegen die OSM-Tile-Policy.

1. **Einstellungen → Karte:** Karten-Modus **Offline** (oder „Online – Meshhessen") wählen und den gewünschten Kartenstil einstellen
2. **„Tiles herunterladen"** klicken — der Download-Dialog öffnet sich:

   <img width="567" alt="Meshhessen Client – Offline-Karten-Tile-Downloader: Bounding Box, Zoomstufen und Fortschritt" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/tile_downloader.png" />

3. **Gebiet festlegen:** Bounding Box als `latMin,lonMin,latMax,lonMax` eingeben — z. B. Hessen: `49.3,7.7,51.7,10.2`
4. **Zoomstufen wählen:** `1-12` reicht für die Übersicht, `1-14` ist die Empfehlung für Ortslagen, ab `15` nur für kleine Gebiete — **jede weitere Stufe vervierfacht** die Kachelzahl
5. **Download starten** und den Fortschritt beobachten — bereits vorhandene Kacheln werden übersprungen, ein Abbruch + Neustart ist also völlig harmlos
6. Tiles landen unter `maptiles/`, sind dauerhaft offline verfügbar und portabel (per USB übertragbar)
7. **Offline-Test** nicht vergessen (siehe unten)

**Typische Stolpersteine:**

* **Download startet nicht / wird abgelehnt** → öffentliche OSM-/OpenTopoMap-Server sind für Bulk-Downloads gesperrt (Tile-Policy) — den Meshhessen-Server oder einen eigenen Tile-Server verwenden
* **Leere Flächen außerhalb der DACH-Region** → der Meshhessen-Server liefert Deutschland, Österreich und die Schweiz; für andere Gebiete den OSM-Online-Modus (nur Betrachten, kein Bulk-Download) nutzen
* **Download wird riesig** → Zoom-Obergrenze senken oder die Bounding Box verkleinern — Zoom 14 → 15 allein vervierfacht die Datenmenge

**Wieviel Speicher brauche ich?** Grobe Richtwerte für die Rasterkarte: Hessen bis Zoom 14 ≈ 2–4 GB je Kartenstil; ganz Deutschland bis Zoom 14 ≈ 20–30 GB. Die Vektorkarte ist deutlich kompakter — das Vektor-Offlinepaket für ein Bundesland liegt meist unter 1 GB.

> ⚠️ **Auf SD-Karten die Zuordnungseinheit mitdenken** (z. B. fürs T-Deck): Viele Tiles sind nur wenige hundert Bytes groß, belegen aber je einen vollen Cluster. Mit Standard-Formatierung großer Karten (oft 128-KB-Cluster) vervielfacht sich der tatsächliche Platzbedarf gegenüber den Richtwerten oben. Deshalb exFAT mit **4096-Byte-Zuordnungseinheiten** formatieren — der [T-Deck-Assistent](tools.md) empfiehlt und erledigt genau das.

**Offline-Test:** Karten-Modus auf **Offline** stellen (oder kurz WLAN/LAN trennen) und die Zielregion durchzoomen — was jetzt sichtbar ist, ist wirklich lokal gespeichert. Fehlende Kacheln erscheinen als leere Flächen; dann den Download-Bereich/Zoom erweitern.

## Auf der Karte

* **Node-Positionen** als farbige Pins; Rechtsklick auf Node → Farbe setzen, DM senden, Notiz bearbeiten, Traceroute starten, Telemetrie öffnen
* **Node-Pfade** – GPS-Positionsverläufe aufzeichnen und anzeigen
* **Direkte Nachbar-Linien** – aktivierbar im Legenden-Feld: Linien zu allen Nodes, die wir **direkt über HF (0 Hops, kein MQTT)** empfangen haben
  * Farbverlauf wählbar nach **SNR** (rot → gelb → grün) oder **Alter** der letzten direkten Verbindung (cyan → violett)
  * Option **„Dauerhaft"** zeigt alle je direkt gehörten Nachbarn (statt nur der letzten 24 h)
* **Wegpunkte (Waypoints)** – empfangene Wegpunkte anzeigen; per Karte-Rechtsklick erstellen und senden
* **Umwelt- & Funk-Overlay** (🌡️-Button) – Messwert-Boxen an den Nodes und interpolierte Heatmap im Wetterdienst-Stil; neben allen Umweltmetriken (Temperatur, Feuchte, IAQ, Wind, Regen …) auch **SNR, RSSI und Noise Floor** als Flächen-Overlay

### Umwelt- & Funk-Overlay in Bildern

Temperatur – Isothermen, Zonen-Beschriftung und Legende:
<img alt="Meshhessen Client – Temperatur-Heatmap-Overlay mit Isothermen auf der Meshtastic-Karte" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-environment-overlay-temp.png" />

Noise Floor – Funkqualität als Flächen-Overlay:
<img alt="Meshhessen Client – Noise-Floor-Heatmap-Overlay: Funkqualität als Fläche auf der Meshtastic-Karte" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-environment-overlay-noisefloor.png" />

Luftdruck:
<img alt="Meshhessen Client – Luftdruck-Heatmap-Overlay auf der Meshtastic-Karte" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-environment-overlay-pressure.png" />

Mouse-Over-Messwert-Boxen direkt an den Nodes:
<img alt="Meshhessen Client – Mouse-Over-Messwert-Boxen des Umwelt-Overlays an den Meshtastic-Nodes" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/mouse-over-telemetry-environment-map.png" />

## 🚒 Feuerwehr-/Rettungs-Layer (Vektorkarte)

Zuschaltbarer Fach-Layer für Feuerwehr, Rettungsdienst und Katastrophenschutz – gedacht als Unterstützung im Einsatz- und Übungsfall (z. B. „Wo ist der nächste Hydrant / Rettungspunkt?"). Die Daten kommen **live aus OpenStreetMap** (tägliche Updates auf dem Meshhessen-Server) – die Abdeckung hängt also vom lokalen Mapping-Stand ab. Farbkonvention wie im Feuerwehrplan: **rot** = Löschmittel/Meldung, **blau** = Löschwasser-Vorrat, **grün** = Rettung, **orange** = Katastrophenschutz.

| Symbol | Objekttyp | sichtbar ab Zoom |
|---|---|---|
| 🔴 H (gefüllt = Überflur, Ring = **Unterflur**) | Hydrant | 15 |
| 🔴 Flamme + Name | Feuerwache | 13 |
| 🔴 Kreuz + Name | Rettungswache | 13 |
| 🔴 Sirene | Sirene | 13 |
| 🔴 Leiter | Anleiterstelle | 14 |
| 🔴 E / F / M / C / K | Einspeisung / Feuerlöscher / Feuermelder / Löschschlauch / Feuerwehrschlüsseldepot (FSD) | 16 |
| 🔵 S | Saugstelle | 14 |
| 🔵 T | Löschwasserbehälter | 14 |
| 🔵 L | Löschteich | 13 |
| 🔵 N | Notrufsäule | 15 |
| 🟢 Kreuz + **Nummer** | Rettungspunkt | 13 |
| 🟢 Sammelplatz | Sammelplatz / Evakuierungspunkt | 13 |
| 🟢 Herz (AED) | Defibrillator | 15 |
| 🟢 Kreuz | Erste-Hilfe-Kasten | 16 |
| 🟢 Rettungsring | Rettungsring / Wasserrettung | 14–16 |
| 🟠 Kreuz | Katastrophenschutz-Anlaufstelle | 13 |
| 🔴 Kreuz | Bergrettung | 13 |
| Helipad | Hubschrauberlandeplatz | 13 |

* **Klick auf ein Objekt** öffnet ein Detail-Popup mit allen in OSM gepflegten Attributen: Bauart (z. B. Unterflur/Überflur/Wandhydrant), Kupplungen, Durchfluss, Druck, Wasserquelle, Wasservolumen, Betreiber, Öffnungszeiten, Standortbeschreibung (bei Defis z. B. „Foyer, 1. OG") und Erfassungsdatum
* Icons überlappen bewusst („im Einsatz will man jeden Punkt sehen") und verdrängen keine Kartenbeschriftung
* **Offline nutzbar:** online betrachtete Gebiete landen automatisch im Cache; im Vektor-Offlinepaket-Downloader lässt sich der Layer gezielt für ein Gebiet mitladen
* Solange der Layer ausgeschaltet ist, wird **kein einziges Byte** dafür übertragen
* Die Architektur ist für weitere Fach-Layer vorbereitet (z. B. THW, Krankenhäuser)

Vergleich: klassische Rasterkarte – ohne zuschaltbare Fach-Layer:
<img alt="Meshhessen Client – klassische Rasterkarte ohne Feuerwehr-Layer (Vergleichsbild)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/raster_wo_hydrant.png" />

Derselbe Ausschnitt als Vektorkarte mit aktivem 🚒 Feuerwehr-/Rettungs-Layer (Klick zeigt Details):
<img alt="Meshhessen Client – Vektorkarte mit Feuerwehr-Layer: Hydranten, Wachen und Rettungspunkte auf der Meshtastic-Karte" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/vector_w_hydrant.png" />

Klick auf einen Hydranten zeigt die Details – hier ein Unterflurhydrant (Ring-Symbol) mit Wasserquelle:
<img alt="Meshhessen Client – Hydranten-Detail-Popup auf der Vektorkarte: Unterflurhydrant mit Bauart und Wasserquelle" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/hydrant_unterflur.png" />

## Weitere Screenshots

Direkte HF-Nachbarn mit SNR-Farbverlauf auf der Vektorkarte:
<img alt="Meshhessen Client – Nachbar-Linien mit SNR-Farbverlauf auf der Vektorkarte (0-Hop HF)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/vector_w_neighborlinks.png" />

Offline-Karte mit Node-Positionen und Entfernungsanzeige:
<img width="1250" height="813" alt="Meshhessen Client – Windows-App für Meshtastic-Geräte: Offline-Karte mit Node-Positionen (OSM)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map.png" />

Direkte HF-Nachbarn als Linien auf der Rasterkarte (Farbverlauf nach SNR oder Alter):
<img alt="Meshhessen Client – Direkte Meshtastic-Nachbarn als farbige Linien auf der Karte (0-Hop HF, SNR-Farbverlauf)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map_neighbor_lines.png" />

Node-Standort-Verlauf (GPS-Track) auf der Karte:
<img width="967" height="411" alt="Meshhessen Client – Meshtastic Node-Standort-Verlauf GPS-Track auf Karte" src="https://github.com/user-attachments/assets/0415083a-9f22-44fc-b767-be66b69892cd" />
