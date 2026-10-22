# 🧰 Tools: Virtual Node, T-Deck Assistant & Kiosk Mode

[← Handbook home](../index.md) · [🇩🇪 Deutsche Version](../de/tools.md)

## 🖧 Virtual Node (Tools tab)

* **TCP proxy server** – connect Meshtastic apps (Android/iOS) to the Meshhessen Client as if it were a real Meshtastic device
* **Configurable in the Tools tab:** port (default: 4404), enable/disable, optionally block admin commands
* **Auto-start** as soon as a connection to the physical node is established (when enabled)
* **Config replay:** connecting apps receive all channels, node list and device config immediately
* **Bidirectional visibility:** messages sent in the app appear in connected apps – and vice versa
* **Multi-client:** multiple apps can connect simultaneously; status and connected IPs are shown in the Tools tab

## 🔧 T-Deck Map Assistant (Tools tab)

* **Prepare an SD card** – guided 6-step wizard for loading offline maps onto a Meshtastic T-Deck SD card
* **Step 1 – Welcome:** explains the workflow; note: Germany only
* **Step 2 – Drive:** shows all removable drives with size, free space and file system
* **Step 3 – Formatting:** recommends **exFAT with 4096-byte allocation units** (T-Deck-optimal; small tiles ≈ 100 bytes → small AU saves a lot of space); formatting via UAC-elevated PowerShell with two confirmations
* **Step 4 – Area:** by federal state (checkboxes), all of Germany, or **freestyle** (draw a rectangle on an interactive map)
* **Step 5 – Zoom & style:** zoom 8–17 with warnings at high levels; OSM / OSM Dark / OpenTopoMap
* **Step 6 – Transfer:** additive – SD tile exists → skip; available locally → copy; otherwise → fetch from the tile server + cache locally
* **SD directory structure:** `{drive}:\maps\OSM\{z}\{x}\{y}.png` (T-Deck compatible)
* **ZIP export** – export local tiles as a ZIP (filterable by map style)
* Fully multilingual (German/English)

### The wizard in pictures

Steps 1–3: welcome, drive selection and formatting recommendation (exFAT, 4096 bytes):
<img alt="Meshhessen Client – T-Deck map assistant step 1: welcome" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage1.png" />
<img alt="Meshhessen Client – T-Deck map assistant step 2: drive selection" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage2.png" />
<img alt="Meshhessen Client – T-Deck map assistant step 3: formatting (exFAT 4096)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage3.png" />

Step 4: choose the map area — federal state, all of Germany or a freestyle rectangle on the map:
<img alt="Meshhessen Client – T-Deck map assistant step 4: select map area on an interactive map" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage4-select-maprange.png" />

Steps 5 + 6: zoom/map style and the transfer running onto the SD card:
<img alt="Meshhessen Client – T-Deck map assistant step 5: zoom and map style" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage5.png" />
<img alt="Meshhessen Client – T-Deck map assistant step 6: tiles being transferred to the SD card" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/t-deck-assistant-stage6-downloading.png" />

## 🔒 Kiosk / training mode

For shared stations (club room, training, events):

* Enable in settings, set a password and choose what gets hidden while locked: tabs (Nodes, Channels, Settings, Info, Tools, Debug), node configuration, remote admin, telemetry dashboard, SOS button, Meshhessen quick-config
* **🔒 lock in the footer bar** to lock/unlock (only shown once a password is set); unlock via password, the app always starts locked in kiosk mode
* While locked, **admin commands from Virtual Node clients are additionally blocked** (regardless of the VNode setting)
* ⚠️ **Protects against accidents, not attacks** – the password is stored as a PBKDF2 hash, but anyone who can edit the INI file can disable the mode
* **Forgot the password?** Close the client and set `KioskModeEnabled=True` to `False` in `meshhessen-client.ini` (or clear the `KioskPasswordHash=…` line and set a new one)

Kiosk / training mode – lockable UI for shared stations (settings):
<img alt="Meshhessen Client – kiosk and training mode: lockable UI with password protection for shared stations" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/kiosk_settings.png" />
