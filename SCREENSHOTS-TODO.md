# 📸 Screenshot-Wunschliste (Doku)

Offene Screenshots für Handbuch & Website — zum Abarbeiten und Nachreichen.
Ablage wie gehabt unter `img/` (Dateinamen-Vorschläge unten); die Einbau-Stellen
stehen als `<!-- SCREENSHOT-WUNSCH: … -->`-Kommentare bereits in den Kapiteln.
Auflösung wie bei den bisherigen Shots (~1600 px Fensterbreite, Handy nativ),
Dark Mode passend zur restlichen Galerie.

## Pflicht (Platzhalter stehen schon im Handbuch)

| # | Datei-Vorschlag (`img/…`) | Motiv | Was zu sehen sein muss | Einbau-Stelle |
|---|---|---|---|---|
| 1 | `dashboard-widget-editor.png` | Widget-Editor (Telemetrie-Dashboard) | Dialog beim Anlegen eines Widgets: Typ-Auswahl, Node-Haken, Metrik „Batterie", Zeitraum, ausgefülltes **Threshold-Feld** | `docs/de/telemetrie.md` + `docs/en/telemetry.md` (How-to) |
| 2 | `dashboard-threshold-toast.png` | Schwellwert-Alarm | Windows-Toast einer ausgelösten Warnung; im Hintergrund das Dashboard mit sichtbarer Schwellwert-Linie im Chart | `docs/de/telemetrie.md` + `docs/en/telemetry.md` (How-to) |
| 3 | `dm-window-pki.png` | DM-Fenster + 🔑 | Direktnachrichten-Fenster mit laufender Konversation (ACK-Haken sichtbar) und Node-Liste mit 🔑-Spalte — idealerweise ein 🔑- und ein 🔓-Node im Bild | `docs/de/nachrichten.md` + `docs/en/messaging.md` (How-to) |
| 4 | `tools-virtual-node-connected.png` | Tools-Tab Virtual Node | Virtual Node aktiv (Port 4404), mindestens **ein verbundener Handy-Client in der Liste** (IP sichtbar) | `docs/de/virtual-node.md` + `docs/en/virtual-node.md` |
| 5 | `phone-app-tcp-connect.png` | Meshtastic-App Netzwerk-Dialog | Handy-Screenshot: Geräteauswahl → Netzwerk/TCP mit eingetragener PC-IP + Port 4404 | `docs/de/virtual-node.md` + `docs/en/virtual-node.md` (How-to Schritt 3) |
| 6 | `kiosk-locked.png` | Kiosk gesperrt | Hauptfenster im **gesperrten** Kiosk-Zustand: reduzierte Tabs, 🔒 in der Fußleiste | `docs/de/kiosk.md` + `docs/en/kiosk.md` |

## Optional / nice-to-have

| # | Datei-Vorschlag (`img/…`) | Motiv | Was zu sehen sein muss | Einbau-Stelle |
|---|---|---|---|---|
| 7 | `map-style-comparison.png` | Kartenstil-Vergleich | Gleicher Ausschnitt dreimal: OSM Standard / OSM Dark / OpenTopoMap (gern als eine Montage) | `docs/de/karte.md` + `docs/en/map.md` (neue Stil-Tabelle) |
| 8 | `handbuch-hero.png` | Handbuch-Startseite | Neues Handbuch live mit Logo/Favicon und Dark Mode (für README/Website) | README / `docs/index.html` |

## Erledigt ✅

*(hierhin verschieben, wenn eingebaut)*
