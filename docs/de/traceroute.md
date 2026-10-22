# 📡 Traceroute

[← Zur Projektübersicht](../../README.md) · [🇬🇧 English version](../en/traceroute.md)

* **Traceroute starten** – direkt aus dem Node-Kontextmenü (Nodes-Liste, Karte, Nachrichtenliste)
* **„Nur HF"-Modus** – sendet die Anfrage mit gelöschtem OK-to-MQTT-Bit (vorverschlüsseltes Paket): MQTT-Gateways leiten sie nicht weiter, die Route läuft ausschließlich über Funk. Hinweis: Die *Antwort* kann trotzdem über MQTT zurückkommen — das bestimmt der Ziel-Node.
* **Ehrliche MQTT-Badges:** Hops bekommen das MQTT-Badge nur, wenn für die Teilstrecke keine Funk-Messung vorliegt; das Kopf-Badge „via MQTT" bedeutet nur „die Antwort kam über MQTT an"
* **Eigenes Fenster** pro Ziel-Node mit:
  * Hop-Tabelle: Node-Name, Entfernung, SNR (in dB), MQTT-Indikator
  * Live-Status (Warten / Empfangen)
  * **⛰ Höhenprofil pro Hop** – Rechtsklick auf eine Hop-Zeile zeigt das Geländeprofil der Teilstrecke (SRTM, Sichtlinie, Fresnel-Zone)
* **Karte:** Route mit Linien plotten
  * Durchgezogene Linie wo Positionen bekannt, gestrichelte wo Positionen fehlen
  * Richtungspfeile auf Segmenten; MQTT-Brücken als Blitz-Zickzack
  * Klick auf Segment → SNR-Popup für diesen Hop
  * Fallback auf eingestellte Kartenposition wenn eigene GPS fehlt
* **Mehrere Traceroutes gleichzeitig** auf der Karte – jede bekommt eine eindeutige Farbe
* **Speichern & Laden** – Traceroutes werden automatisch in `traceroutes/` gespeichert (JSON)
  * Pfade unterschiedlicher Zeitpunkte vergleichen, mehrere Dateien gleichzeitig laden
  * Historische Traceroutes direkt aus der Datenbank laden
* **Zeitreihenfilter** – Traceroutes nach Zeitraum filtern (3d / 7d / 14d / 30d / 90d / Alle)
* **Deduplizierung** – nur neueste Traceroute pro Node-Paar anzeigen (optional)
* **Karten-Legende** – zeigt alle aktiven Traces mit Farbe und ✕-Button zum Entfernen

## Screenshots

Traceroute-Fenster mit Hop-Tabelle:
<img width="780" height="868" alt="Meshhessen Client – Traceroute für Meshtastic-Geräte: Hop-Tabelle, Entfernung und SNR unter Windows" src="https://github.com/user-attachments/assets/10880a3d-9d94-4b07-84bc-ad64af56dab1" />

Traceroute-Pfad auf der Karte:
<img width="1278" height="889" alt="Meshhessen Client – Meshtastic Traceroute-Pfad auf Offline-Karte visualisiert" src="https://github.com/user-attachments/assets/800925ef-0d4e-41ad-a7f2-56cdd06da6b7" />
