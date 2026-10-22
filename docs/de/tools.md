# 🧰 Tools: Virtual Node, T-Deck-Assistent & Kiosk-Modus

[← Zur Projektübersicht](../../README.md) · [🇬🇧 English version](../en/tools.md)

## 🖧 Virtual Node (Tools-Tab)

* **TCP-Proxy-Server** – verbinde Meshtastic-Apps (Android/iOS) direkt mit dem Meshhessen Client, als wäre er ein echtes Meshtastic-Gerät
* **Konfigurierbar im Tools-Tab:** Port (Standard: 4404), ein-/ausschalten, Admin-Befehle blockieren
* **Automatischer Start** sobald eine Verbindung mit dem physischen Node besteht (wenn aktiviert)
* **Config-Replay:** verbindende Apps erhalten sofort alle Kanäle, Node-Liste und Gerätekonfig
* **Bidirektionale Sichtbarkeit:** Nachrichten aus der App erscheinen in verbundenen Apps – und umgekehrt
* **Multi-Client:** mehrere Apps gleichzeitig verbindbar; Status und verbundene IPs werden im Tools-Tab angezeigt

## 🔧 T-Deck Karten-Assistent (Tools-Tab)

* **SD-Karte vorbereiten** – geführter 6-Schritt-Assistent zum Bespielen einer SD-Karte mit Offline-Karten für das Meshtastic T-Deck
* **Schritt 1 – Willkommen:** Erklärt den Workflow; Hinweis: nur Deutschland verfügbar
* **Schritt 2 – Laufwerk:** Zeigt alle Wechseldatenträger mit Größe, freiem Speicher und Dateisystem
* **Schritt 3 – Formatierung:**
  * Empfiehlt **exFAT mit 4096-Byte-Zuordnungseinheiten** (T-Deck-optimal; kleine Tiles ≈ 100 Bytes → kleine AU spart erheblich Speicher)
  * Formatierung per UAC-elevated PowerShell direkt aus dem Client; zwei Bestätigungsdialoge
* **Schritt 4 – Bereich:** Nach Bundesland (Checkboxen), ganz Deutschland, oder **Freestyle** (eigenes Rechteck auf interaktiver Karte zeichnen)
* **Schritt 5 – Zoom & Kartentyp:** Zoom 8–17, Warnhinweise bei hohen Stufen; OSM / OSM Dark / OpenTopoMap
* **Schritt 6 – Transfer:** Additiv – SD-Tile vorhanden → skip; lokal vorhanden → kopieren; sonst → vom Tile-Server laden + lokal cachen
* **SD-Verzeichnisstruktur:** `{Laufwerk}:\maps\OSM\{z}\{x}\{y}.png` (T-Deck-kompatibel, Langklick auf Faltkartenicon schaltet zwischen Kartendiensten um)
* **ZIP-Export** – lokale Tiles als ZIP exportieren (nach Kartentyp filterbar)
* Vollständig mehrsprachig (Deutsch/Englisch)

## 🔒 Kiosk-/Trainingsmodus

Für geteilte Stationen (Vereinslokal, Schulung, Veranstaltung):

* In den Einstellungen aktivieren, Passwort setzen und wählen, was im gesperrten Zustand ausgeblendet wird: Tabs (Nodes, Kanäle, Einstellungen, Info, Tools, Debug), Node-Konfiguration, Fernverwaltung, Telemetrie-Dashboard, SOS-Button, Meshhessen-Schnellkonfiguration
* **🔒-Schloss in der Fußleiste** zum Sperren/Entsperren (erscheint nur, wenn ein Passwort gesetzt ist); Entsperren per Passwort, App startet im Kiosk-Modus immer gesperrt
* Bei aktiver Sperre werden zusätzlich **Admin-Befehle von Virtual-Node-Clients blockiert** (unabhängig von der VNode-Einstellung)
* ⚠️ **Versehens-Schutz, kein Angriffs-Schutz** – das Passwort wird als PBKDF2-Hash gespeichert, aber wer die INI-Datei bearbeiten kann, kann den Modus deaktivieren
* **Passwort vergessen?** Client beenden und in `meshhessen-client.ini` die Zeile `KioskModeEnabled=True` auf `False` setzen (oder die Zeile `KioskPasswordHash=…` leeren und neu setzen)

Kiosk-/Trainingsmodus – sperrbare Oberfläche für geteilte Stationen (Einstellungen):
<img alt="Meshhessen Client – Kiosk- und Trainingsmodus: sperrbare Oberfläche mit Passwortschutz für geteilte Stationen" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/kiosk_settings.png" />
