# 🔧 T-Deck Karten-Assistent

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/t-deck/)

* **SD-Karte vorbereiten** – geführter 6-Schritt-Assistent (Tools-Tab) zum Bespielen einer SD-Karte mit Offline-Karten für das Meshtastic T-Deck
* **Schritt 1 – Willkommen:** Erklärt den Workflow; Abdeckung = Meshhessen-Tile-Server (DACH-Region; die Schnellauswahl listet deutsche Bundesländer, per Freestyle-Rechteck geht auch Österreich/Schweiz)
* **Schritt 2 – Laufwerk:** Zeigt alle Wechseldatenträger mit Größe, freiem Speicher und Dateisystem
* **Schritt 3 – Formatierung:**
  * Empfiehlt **exFAT mit 4096-Byte-Zuordnungseinheiten** (T-Deck-optimal; kleine Tiles ≈ 100 Bytes → kleine AU spart erheblich Speicher — Details zur [Speicherplanung in „Karte & Offline-Nutzung“](karte.md))
  * Formatierung per UAC-elevated PowerShell direkt aus dem Client; zwei Bestätigungsdialoge
* **Schritt 4 – Bereich:** Nach Bundesland (Checkboxen), ganz Deutschland, oder **Freestyle** (ein oder mehrere Rechtecke auf der interaktiven Karte — verwendet wird die umschließende Hülle)
* **Schritt 5 – Zoom & Kartentyp:** Max-Zoom in festen Stufen 8/10/12/14/16/17 (übertragen wird immer ab Zoom 1), Warnhinweise ab Stufe 14 bzw. 16; OSM / OSM Dark / OpenTopoMap. Der Assistent schätzt Kachelzahl und Datenmenge (~15 KB/Kachel) und warnt, wenn der freie SD-Platz nicht reicht
* **Schritt 6 – Transfer:** Additiv – SD-Tile vorhanden → skip; lokal vorhanden → kopieren; sonst → vom Tile-Server laden + lokal cachen
* **SD-Verzeichnisstruktur:** `{Laufwerk}:\maps\OSM\{z}\{x}\{y}.png` (T-Deck-kompatibel, Langklick auf Faltkartenicon schaltet zwischen Kartendiensten um)
* **ZIP-Export** – lokale Tiles als ZIP exportieren (nach Kartentyp filterbar)
* Vollständig mehrsprachig (Deutsch/Englisch)

## Der Assistent in Bildern

Schritt 1–3: Willkommen, Laufwerkswahl und Formatierungsempfehlung (exFAT, 4096 Bytes):
<img alt="Meshhessen Client – T-Deck Karten-Assistent Schritt 1: Willkommen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage1.png" />
<img alt="Meshhessen Client – T-Deck Karten-Assistent Schritt 2: Laufwerksauswahl" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage2.png" />
<img alt="Meshhessen Client – T-Deck Karten-Assistent Schritt 3: Formatierung (exFAT 4096)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage3.png" />

Schritt 4: Kartenbereich wählen — Bundesland, ganz Deutschland oder Freestyle-Rechteck auf der Karte:
<img alt="Meshhessen Client – T-Deck Karten-Assistent Schritt 4: Kartenbereich auf interaktiver Karte wählen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage4-select-maprange.png" />

Schritt 5 + 6: Zoom/Kartentyp und der laufende Transfer auf die SD-Karte:
<img alt="Meshhessen Client – T-Deck Karten-Assistent Schritt 5: Zoom und Kartentyp" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage5.png" />
<img alt="Meshhessen Client – T-Deck Karten-Assistent Schritt 6: Tiles werden auf die SD-Karte übertragen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage6-downloading.png" />
