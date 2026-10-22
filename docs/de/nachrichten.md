# 📨 Nachrichten & Kommunikation

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/messaging/)

* **Nachrichten** senden und empfangen (Broadcast & Direct Messages)
* **Multi-Channel** – alle Kanäle deines Geräts automatisch geladen
* **Direktnachrichten (DMs)** mit separatem Chat-Fenster im Tabbed-Layout
* **PKI-Entschlüsselung** – client-seitige Entschlüsselung von PKI-verschlüsselten DMs (X25519 + AES-256-CCM, firmware-konform); Private Key wird automatisch vom verbundenen Gerät geladen
* **Node-Public-Key-Datenbank** – lokale CSV-Datei (`node_keys.csv`), die der Client automatisch mit den Public Keys empfangener Nodes pflegt (manuelle Änderungen werden beim nächsten Start eingelesen)
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
  * Visuell: rote blinkende Umrandung + Notification-Bar mit „📍 Zur Karte"-Button
* **Persistente Nachrichten-Datenbank** – Kanal- und DM-Nachrichten dauerhaft in lokaler SQLite-DB (standardmäßig aktiv; abschaltbar in den Einstellungen unter „Nachrichten-Datenbank")
  * Letzten 24 Stunden beim Verbinden automatisch geladen; ältere Nachrichten per Hochscrollen nachladen
  * **DM-History:** Alle Konversationen der letzten 24 h erscheinen beim Öffnen des DM-Fensters automatisch
  * **Pro-Kanal-Löschung** direkt im Kanäle-Tab: „Nachrichten-DB leeren"-Spalte mit Zeitraum-Dialog (Alle / 30 / 90 / 365 Tage)
  * Bestätigung vor dem Löschen einer DM-Konversation (kein versehentliches Wischen)
  * Aufbewahrungsdauer konfigurierbar (30 / 90 / 365 Tage / unbegrenzt — Standard: 90 Tage)

## How-to: erste Nachricht & verschlüsselte DM senden

**Ziel:** Eine Broadcast-Nachricht in den Kanal schicken und danach eine Ende-zu-Ende-verschlüsselte Direktnachricht an einen bestimmten Node.

1. **Kanal-Nachricht:** Nachrichten-Tab → links in der Seitenleiste „Kanäle“ den Kanal wählen → unten Text eintippen → Enter. Die eigene Nachricht erscheint rechtsbündig; Antworten laufen im selben Verlauf ein
2. **DM starten:** Rechtsklick auf den Ziel-Node (in der Node-Liste, auf der Karte oder direkt auf eine seiner Nachrichten) → **„💬 DM senden“** — die Konversation öffnet als eigener Tab im DM-Fenster (je nach Einstellung auch direkt im Hauptfenster im Chat-Stil)
3. **Verschlüsselung prüfen:** Die 🔑-Spalte in der Node-Liste zeigt, ob der öffentliche Schlüssel des Ziels bekannt ist. 🔑 = PKI-DMs können ver- und entschlüsselt werden (X25519 + AES-256-CCM); bleibt die Zelle leer (Tabelle) bzw. zeigt 🔓 (Kachelansicht), ist der Key unbekannt — dann bleibt nur die Kanal-Verschlüsselung
4. **Erfolgskontrolle:** Die gesendete Nachricht bekommt Zustell-Haken: ✓ = gesendet, ✓✓ (grün) = vom Empfänger bestätigt (ACK); die Antwort des Gegenübers erscheint im Tab. Empfangene PKI-DMs entschlüsselt der Client selbst
5. **Extras:** Rechtsklick auf eine Nachricht → Emoji-Reaktion oder „Antworten“ (Zitat-Block); Texte sind markier- und kopierbar

**Typische Fehler:**

* *Kein 🔑 beim Ziel* → der öffentliche Schlüssel fehlt lokal: den Node einmal direkt empfangen oder per Rechtsklick „📥 Informationen anfordern → Benutzer-Info“ den Key ziehen (bei verschlüsselt empfangenen Nachrichten geht es auch über den 🔑-Button direkt an der Nachricht)
* *Kein ACK* → Ziel gerade nicht erreichbar (mit [Traceroute](traceroute.md) testen) oder die Funkstrecke ist ausgelastet — kurz warten statt mehrfach senden (Airtime!)
* *Alte Konversation fehlt* → die Nachrichten-Datenbank ist standardmäßig aktiv; prüfen, ob sie in den Einstellungen („Nachrichten-Datenbank“) abgeschaltet wurde — ohne sie beginnt der Verlauf erst mit dem Verbinden

<!-- SCREENSHOT-WUNSCH: DM-Fenster mit laufender Konversation + sichtbarem 🔑-Status in der Node-Liste -->

## Screenshots

Nachrichtenansicht mit Kanal-Chat und Direktnachrichten:
<img width="1443" height="813" alt="Meshhessen Client – Meshtastic Nachrichten, Kanal-Chat und Direktnachrichten (DM) unter Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/new-chat-layout.png" />

Kanal-Liste mit allen Meshtastic-Kanälen:
<img width="1443" height="814" alt="Meshhessen Client – Meshtastic Kanal-Liste (Multi-Channel) unter Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/channel_list.png" />

Alert Bell / SOS-Notruf Anzeige:
<img width="1446" height="816" alt="Meshhessen Client – Alert Bell SOS-Notruf Signalisierung für Meshtastic-Geräte unter Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/alert_bell.png" />
