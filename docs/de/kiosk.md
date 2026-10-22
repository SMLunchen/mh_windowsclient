# 🔒 Kiosk-/Trainingsmodus

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/kiosk/)

Für geteilte Stationen (Vereinslokal, Schulung, Veranstaltung):

* In den Einstellungen aktivieren, Passwort setzen und wählen, was im gesperrten Zustand ausgeblendet wird: Tabs (Nodes, Kanäle, Einstellungen, Info, Tools, Debug), Node-Konfiguration, Fernverwaltung, Telemetrie-Dashboard, SOS-Button, Meshhessen-Schnellkonfiguration
* **🔒-Schloss in der Fußleiste** zum Sperren/Entsperren (erscheint nur, wenn der Kiosk-Modus aktiviert **und** ein Passwort gesetzt ist); Entsperren per Passwort, App startet im Kiosk-Modus immer gesperrt
* **Nachrichten- und Karten-Tab bleiben immer sichtbar** — bewusst nicht sperrbar, damit die Station benutzbar bleibt
* Bei aktiver Sperre werden zusätzlich **Admin-Befehle von [Virtual-Node](virtual-node.md)-Clients blockiert** (unabhängig von der VNode-Einstellung)
* ⚠️ **Versehens-Schutz, kein Angriffs-Schutz** – das Passwort wird als PBKDF2-Hash gespeichert, aber wer die INI-Datei bearbeiten kann, kann den Modus deaktivieren
* **Passwort vergessen?** Client beenden und in `meshhessen-client.ini` die Zeile `KioskModeEnabled=True` auf `False` setzen (oder die Zeile `KioskPasswordHash=…` leeren und neu setzen)

Kiosk-/Trainingsmodus – sperrbare Oberfläche für geteilte Stationen (Einstellungen):
<img alt="Meshhessen Client – Kiosk- und Trainingsmodus: sperrbare Oberfläche mit Passwortschutz für geteilte Stationen" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/kiosk_settings.png" />

<!-- SCREENSHOT-WUNSCH: Hauptfenster im GESPERRTEN Kiosk-Zustand (reduzierte Tabs, 🔒 in der Fußleiste) -->
