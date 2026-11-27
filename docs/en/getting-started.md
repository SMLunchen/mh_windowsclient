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

### Bluetooth keeps dropping?

If the debug log shows `[BLE] Device disconnected` over and over, followed by 10–50 seconds in which the client cannot reconnect ("Meshtastic service not found … Unreachable"), the radio link is breaking down – the client is not closing it. After the third drop within 10 minutes the client shows a hint explaining this.

The most common cause is a **weak radio path**: node in the attic, PC one or two floors below, a weak Bluetooth adapter in the PC, or 2.4 GHz interference (Wi-Fi, USB 3 devices right next to the adapter). Typical signs: the link drops in the middle of a data burst – for example while the node list loads after connecting – and the node is unreachable for a few seconds until Windows finds it again. Remedy: reduce the distance or use a Bluetooth adapter with an external antenna.

The client softens this: when it auto-reconnects to the same node it no longer re-downloads the node list, only config and channels (the long download was the moment weak links usually broke), and the pauses between attempts grow with every failure. When connecting it logs the node's signal strength ("Node signal during connect: −87 dBm") – values below about −85 dBm are too weak. For troubleshooting, Settings → Debug offers "Read the node's log over Bluetooth"; it needs "Debug log over API" enabled in the node's Security config and puts extra load on the radio link.

**How the client reads:** it reads the node only when the node has announced something (FROMNUM notification) or the client has just sent something – the way the Android app and the Python CLI do. A safety look every 1.5 seconds covers a notification Windows swallowed. When connecting it asks for the fastest connection interval so config and node list load quickly, and releases it afterwards; the node may then choose its own, calmer interval. The setting **"Gentle Bluetooth mode"** (Settings → Bluetooth) skips the fast interval altogether: the node list loads more slowly, a weak link gets more breathing room. After repeated drops the client offers this mode right in the hint dialog.

**📶 in the header:** on a Bluetooth link the node's signal strength appears next to the green connection dot, measured while connecting (Windows reports no value for an established link). Hovering or clicking shows the link counters: mode and phase, the negotiated connection interval (Windows 11 22H2 and newer), reads with their empty and failure share, FROMNUM notifications received, writes. "Copy" puts everything on the clipboard for a bug report; the same numbers land in the log every 10 minutes and on disconnect as `[BLE] Link stats`.

**Pairing:** the client **never removes a Windows pairing without asking**. If the node is unreachable (off, out of range, rebooting) it simply keeps trying. Only when the node answers and the stored pairing no longer fits it – typical after a firmware update, factory reset or Bluetooth OTA – does it ask whether to remove the pairing and pair afresh; during the automatic reconnect not before the second such finding, and only once.

Second cause on **nRF52 boards (RAK4631, T-Echo, ThinkNode M1/M6, T114, T1000-E …) on firmware older than 2.8.0**: this firmware switches Bluetooth off completely as soon as a serial client opens the Meshtastic API over USB – the Python CLI, the web client, a phone on a USB cable or a second client instance. Bluetooth only comes back when that client disconnects, or after 15 minutes at the latest. This only affects nodes with a computer actually attached to their USB port – a plain power supply does not count. Remedy: unplug the USB data link or close the other program – or update the firmware to **2.8.0 or newer** ([firmware update](firmware-update.md)), where this is fixed. The Meshhessen Client itself tells the node that the session is over when disconnecting (`disconnect`), so Bluetooth is free again right after a serial session in the client.

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
