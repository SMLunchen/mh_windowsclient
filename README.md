# Meshhessen Client

Ein **offline-fähiger, nativer Windows-Client** für Meshtastic-Geräte mit USB/serieller Verbindung – entwickelt von und für die [Meshhessen Community](https://www.meshhessen.de).

![Windows](https://img.shields.io/badge/Windows-10%2F11-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Status](https://img.shields.io/badge/Status-v1.0--Beta-yellow)

---

## 🚀 Schnellstart

1. **Download:** Neueste `MeshhessenClient.exe` aus den [Releases](../../releases) herunterladen
2. **Gerät anschließen:** Meshtastic-Device per USB anstecken
3. **Starten:** Doppelklick auf `MeshhessenClient.exe` – keine Installation nötig
4. **Verbinden:** COM-Port wählen → „Verbinden" klicken
5. **Loslegen:** 3–10 Sekunden warten bis Kanäle geladen sind, dann Nachrichten senden

> Die App ist vollständig offline-fähig. Keine Cloud, keine Registrierung.

---

## ✨ Features

- **Nachrichten** senden und empfangen (Broadcast & Direct Messages)
- **Multi-Channel** – alle Kanäle deines Geräts automatisch geladen
- **Offline-Karte** mit OSM-Tiles und Node-Positionen als Pins
- **Direktnachrichten (DMs)** mit separatem Chat-Fenster
- **Knoten-Übersicht** – alle Nodes im Mesh mit SNR, Batterie, Entfernung
- **Dark Mode** & ModernWPF Fluent-Design
- **Automatisches Logging** aller Nachrichten (Kanal- und DM-Logs)
- **Debug-Tab** mit Live-Log

---

## 💬 Die Meshhessen Community

Der Meshhessen Client ist ein Gemeinschaftsprojekt der Meshtastic-Community in Hessen. Unser regionales LoRa-Mesh wächst stetig – mach mit!

- 🌐 **Website:** [www.meshhessen.de](https://www.meshhessen.de)
- 📡 **Netz:** Wachsendes Mesh-Netzwerk in Hessen und Umgebung
- 🤝 **Mitmachen:** Eigenen Node aufstellen, Reichweite erweitern, Community wachsen lassen

---

## 📸 Screenshots

*(folgen in Kürze)*

---

## 🔧 Aus Quellcode bauen

**Voraussetzungen:** .NET 8.0 SDK, Windows 10/11 x64

```bash
git clone <repo-url>
cd mh_windowsclient
dotnet restore
dotnet publish MeshhessenClient/MeshhessenClient.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -o public
```

EXE liegt danach unter `public\MeshhessenClient.exe`. Alternativ: `build.bat` ausführen.

---

## 🗺️ Offline-Karte einrichten

1. Tab **„🗺️ Karte"** öffnen
2. **„Tiles herunterladen"** klicken
3. Bereich (Bounding Box) und Zoom-Level eingeben – z.B. Hessen Zoom 1–14
4. Download starten (OSM Fair-Use: max. ~2 req/s)
5. Tiles werden unter `maptiles/` gespeichert und sind dauerhaft offline verfügbar

Rechtsklick auf die Karte → eigenen Standort setzen. Node-Pins erscheinen automatisch sobald GPS-Daten empfangen werden.

---

## 📝 Nachrichten-Logs

Alle Nachrichten werden automatisch geloggt unter `[EXE-Verzeichnis]/logs/`:

- `Channel_0_Primary.log` – Kanalverläufe
- `DM_DEADBEEF_Alice.log` – Direktnachrichten

---

## 🏗️ Technischer Überblick

| Komponente | Technologie |
|---|---|
| UI | WPF .NET 8, ModernWPF (Fluent) |
| Protokoll | Meshtastic Protobuf über Serial (0x94 0xC3 Framing) |
| Karte | Mapsui 4.1 + lokale OSM-Tiles |
| Serialisierung | Google.Protobuf |

**Verbindungssequenz:**
```
Windows Client → USB/Serial (115200 baud) → Meshtastic Node → LoRa → Mesh
```
1. Serial Port öffnen → `want_config_id` senden
2. `my_info`, `node_info`, `channel` (×8), `config`, `config_complete_id` empfangen
3. Bereit für MeshPackets

---

## ⚠️ Bekannte Einschränkungen

- Keine persistente Message-History (Neustart = leere UI, Logs bleiben)
- Config-Bearbeitung nur Anzeige, noch kein Speichern
- Getestet mit Firmware 2.x

---

## 🙏 Credits

- **[Meshtastic Project](https://meshtastic.org)** – Firmware & Protokoll-Spezifikation
- **[ModernWPF](https://github.com/Kinnara/ModernWpf)** – Fluent UI für WPF
- **[Mapsui](https://mapsui.com)** – Offline-Karte
- **[Meshhessen Community](https://www.meshhessen.de)** – Für das Netzwerk und die Inspiration

---

**Made with ❤️ by the Meshhessen Community** · [www.meshhessen.de](https://www.meshhessen.de)
