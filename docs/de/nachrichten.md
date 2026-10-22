# 📨 Nachrichten & Kommunikation

[← Zur Projektübersicht](../../README.md) · [🇬🇧 English version](../en/messaging.md)

* **Nachrichten** senden und empfangen (Broadcast & Direct Messages)
* **Multi-Channel** – alle Kanäle deines Geräts automatisch geladen
* **Direktnachrichten (DMs)** mit separatem Chat-Fenster im Tabbed-Layout
* **PKI-Entschlüsselung** – client-seitige Entschlüsselung von PKI-verschlüsselten DMs (X25519 + AES-256-CCM, firmware-konform); Private Key wird automatisch vom verbundenen Gerät geladen
* **Node-Public-Key-Datenbank** – lokale CSV-Datei (`node_keys.csv`) zum Verwalten von Public Keys für PKI-Entschlüsselung
* **Tap-Back Reaktionen** – auf Nachrichten mit Emoji reagieren (32 Emojis, wie Android-App)
  * Rechtsklick auf Nachricht → Emoji-Picker
  * Reaktionen werden direkt an Sender/Kanal übermittelt und angezeigt
  * Funktioniert in Kanal-Chat und DMs
* **Antwort-Funktion** – auf einzelne Nachrichten antworten (Protokoll-Level)
  * Zitat-Block mit farblicher Hervorhebung der Originalnachricht
  * Funktioniert in Kanal-Chat und DMs
* **Nachrichten auswählbar & kopierbar** – Texte markieren, in Zwischenablage kopieren, Links anklicken
  * Rechtsklick auf beliebige Nachricht → „Nachricht kopieren" im Kontextmenü
  * Kopiert immer die angeklickte Nachricht (nicht die zuletzt selektierte)
* **🚨 Alert Bell Support** – Senden und Empfangen von Notrufen
  * 🚨 SOS-Button in Chat und DMs
  * Visuell: rote blinkende Umrandung + Notification-Bar mit „Zur Karte springen"-Button
* **Persistente Nachrichten-Datenbank** – Kanal- und DM-Nachrichten dauerhaft in lokaler SQLite-DB speichern (optional, aktivierbar in Einstellungen)
  * Letzten 24 Stunden beim Verbinden automatisch geladen; ältere Nachrichten per Hochscrollen nachladen
  * **DM-History:** Alle Konversationen der letzten 24 h erscheinen beim Öffnen des DM-Fensters automatisch
  * **Pro-Kanal-Löschung** direkt im Kanäle-Tab: „Nachrichten-DB leeren"-Spalte mit Zeitraum-Dialog (Alle / 30 / 90 / 365 Tage)
  * Bestätigung vor dem Löschen einer DM-Konversation (kein versehentliches Wischen)
  * Aufbewahrungsdauer konfigurierbar (30 / 90 / 365 Tage)

## Screenshots

Nachrichtenansicht mit Kanal-Chat und Direktnachrichten:
<img width="1443" height="813" alt="Meshhessen Client – Meshtastic Nachrichten, Kanal-Chat und Direktnachrichten (DM) unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/messaging.png" />

Kanal-Liste mit allen Meshtastic-Kanälen:
<img width="1443" height="814" alt="Meshhessen Client – Meshtastic Kanal-Liste (Multi-Channel) unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/channel_list.png" />

Alert Bell / SOS-Notruf Anzeige:
<img width="1446" height="816" alt="Meshhessen Client – Alert Bell SOS-Notruf Signalisierung für Meshtastic-Geräte unter Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/alert_bell.png" />
