# 🚀 Getting Started & Connection

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/schnellstart/)

## Installation in 5 steps

1. **Download:** Get the latest `MeshhessenClient.exe` from [Releases](https://github.com/SMLunchen/mh_windowsclient/releases) — the EXE ships with the complete .NET runtime (self-contained), **nothing to install**
2. **Connect device:** Plug in your Meshtastic device via USB (or have TCP/Bluetooth ready)
3. **Launch:** Double-click `MeshhessenClient.exe` – the app runs directly, no installer
4. **Connect:** Select connection type (Serial, TCP, or Bluetooth) → click "Connect"
5. **Start:** Wait 3–10 seconds for channels to load, then send messages

> Only if you [build the client from source](technical.md#building-from-source) do you need the .NET 8 SDK.

> The app is fully offline-capable. No cloud, no registration, no telemetry to the developer.

### ⚠️ Windows SmartScreen

On first launch Windows SmartScreen may warn about an "unknown publisher" — normal for unsigned open-source downloads: the EXE is not signed with a (paid) code-signing certificate. **"More info" → "Run anyway"** starts the app. To be safe, download only from the official [GitHub releases](https://github.com/SMLunchen/mh_windowsclient/releases) and compare the SHA-256 checksum published there (PowerShell: `Get-FileHash MeshhessenClient.exe`).

### What the app writes to disk

Everything stays in the EXE directory (portable, USB-stick friendly): `meshhessen-client.ini` (settings), `telemetry.db` (telemetry), `maptiles/` and `vectortiles/` (map cache), `logs/` (message logs), `traceroutes/` (saved traceroutes).

## Connection types

| Type | Transport | Notes |
|----|----|----|
| USB/Serial | COM port, 115200 baud | Wake-up sequence, device debug text detected and logged separately |
| TCP/WiFi | TCP socket | Configurable hostname/IP + port |
| Bluetooth | BLE GATT | Direct FromRadio/ToRadio packets |

* **Remember last connection** – connection type (Serial / BT / WiFi) and last used BT device are saved and pre-selected on next launch
* **Auto-reconnect** – after settings changes that require a device reboot
* **Per-node station name** – a dedicated station name per connected node (✏ button next to the Connect button)

## System & UI

* **Update check** – automatically checks for new versions on startup; a clickable hint appears in the status bar if an update is available (offline-safe)
* **Multi-language** – German and English (switchable in settings)
* **Dark mode** & ModernWPF Fluent design
* **Automatic logging** of all messages (`logs/`)
* **Debug tab** with live log for troubleshooting
* **Meshhessen quick-config** – one-click Short Slow + EU868 + Meshhessen channel setup

## Message logs

All messages are logged automatically under `[EXE directory]/logs/`:

* `Channel_0_Primary.log` – channel history
* `DM_DEADBEEF_Alice.log` – direct messages

## Known limitations

* Tested with firmware 2.x
* T-Deck: channels are not always included in the config sequence (retry workaround active) – the T-Deck is barely keeping up with itself, so everything takes a bit longer there…

## Further reading

* [🗺️ Map & offline use](map.md)
* [📨 Messaging & communication](messaging.md)
* [📊 Telemetry & radio analysis](telemetry.md)
* [🔧 Node management & remote admin](node-management.md)
* [🖧 Virtual Node](virtual-node.md) · [🔧 T-Deck assistant](t-deck.md) · [🔒 Kiosk mode](kiosk.md)
* [🏗️ Technology & protocol](technical.md)
