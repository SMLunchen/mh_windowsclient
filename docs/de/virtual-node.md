# 🖧 Virtual Node – Handy-Apps über den PC anbinden

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/virtual-node/)

* **TCP-Proxy-Server** – verbinde Meshtastic-Apps (Android/iOS) direkt mit dem Meshhessen Client, als wäre er ein echtes Meshtastic-Gerät
* **Konfigurierbar im Tools-Tab:** Port (Standard: 4404), ein-/ausschalten, „Admin-Befehle von Clients blockieren“
* **Automatischer Start:** Die Checkbox „Virtual Node aktivieren“ bleibt gespeichert — der Server startet dann automatisch, sobald eine Verbindung mit dem physischen Node besteht
* **Config-Replay:** verbindende Apps erhalten sofort alle Kanäle, Node-Liste und Gerätekonfig
* **Bidirektionale Sichtbarkeit:** Nachrichten aus der App erscheinen in verbundenen Apps – und umgekehrt
* **Multi-Client:** mehrere Apps gleichzeitig verbindbar; Status und verbundene IPs werden im Tools-Tab angezeigt

## How-to: Handy-App über den PC verbinden

**Ziel:** Die Meshtastic-App auf dem Handy nutzt den am PC angeschlossenen Node mit — als wäre der Client ein eigenes Gerät im WLAN.

1. **Voraussetzung:** Der Client ist mit dem physischen Node verbunden; Handy und PC hängen im selben Netz (WLAN)
2. **Aktivieren:** Tools-Tab → „Virtual Node aktivieren“ anhaken (Standard-Port **4404**). Die Einstellung bleibt gespeichert — der Server startet künftig automatisch bei jeder Node-Verbindung. Optional: „Admin-Befehle von Clients blockieren“
3. **Handy verbinden:** Meshtastic-App → Geräteauswahl → **Netzwerk/TCP** → IP-Adresse des PCs + Port 4404 eintragen (die PC-IP zeigt z. B. `ipconfig`; verbundene Clients listet der Tools-Tab)
4. **Erfolgskontrolle:** Die App lädt sofort Kanäle, Node-Liste und Konfiguration; eine Testnachricht vom Handy erscheint im Client — und umgekehrt
5. **Mehrere Geräte:** Weitere Apps können sich parallel verbinden; der Tools-Tab zeigt Status und IPs

**Typische Fehler:**

* *App findet den PC nicht* → die Windows-Firewall fragt beim ersten Start nach der Freigabe — zulassen (oder eine eingehende Regel für Port 4404 anlegen); außerdem prüfen, ob Handy und PC wirklich im selben WLAN/Subnetz sind
* *Verbindung steht, aber keine Daten* → der Client selbst ist gerade nicht mit dem Node verbunden; die Reihenfolge ist Client ↔ Node, dann Handy ↔ Client
* *Admin-Änderungen aus der App wirken nicht* → „Admin-Befehle von Clients blockieren“ ist aktiv (auch der gesperrte [Kiosk-Modus](kiosk.md) blockiert sie zusätzlich)

<!-- SCREENSHOT-WUNSCH: Tools-Tab mit aktivem Virtual Node + verbundener Client-IP -->
