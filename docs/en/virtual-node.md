# 🖧 Virtual Node – connect phone apps through the PC

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/virtual-node/)

* **TCP proxy server** – connect Meshtastic apps (Android/iOS) to the Meshhessen Client as if it were a real Meshtastic device
* **Configurable in the Tools tab:** port (default: 4404), enable/disable, "Block admin commands from clients"
* **Auto-start:** the "Enable Virtual Node" checkbox is persisted — the server then starts automatically as soon as a connection to the physical node is established
* **Config replay:** connecting apps receive all channels, node list and device config immediately
* **Bidirectional visibility:** messages sent in the app appear in connected apps – and vice versa
* **Multi-client:** multiple apps can connect simultaneously; status and connected IPs are shown in the Tools tab

## How-to: connect your phone app through the PC

**Goal:** the Meshtastic app on your phone shares the node attached to the PC — as if the client were a device of its own on the Wi-Fi.

1. **Prerequisite:** the client is connected to the physical node; phone and PC are on the same network (Wi-Fi)
2. **Enable:** Tools tab → tick "Enable Virtual Node" (default port **4404**). The setting is persisted — from now on the server starts automatically whenever the client connects to the node. Optional: "Block admin commands from clients"
3. **Connect the phone:** Meshtastic app → device selection → **Network/TCP** → enter the PC's IP address + port 4404 (find the IP via `ipconfig`; connected clients are listed in the Tools tab)
4. **Success check:** the app immediately loads channels, node list and configuration; a test message from the phone shows up in the client — and vice versa
5. **Multiple devices:** more apps can connect in parallel; the Tools tab shows status and IPs

**Typical pitfalls:**

* *App can't find the PC* → Windows Firewall asks for permission on first start — allow it (or add an inbound rule for port 4404); also verify phone and PC really share the same Wi-Fi/subnet
* *Connected but no data* → the client itself is not connected to the node right now; the order is client ↔ node, then phone ↔ client
* *Admin changes from the app don't apply* → "Block admin commands from clients" is active (a locked [kiosk mode](kiosk.md) blocks them additionally)

<!-- SCREENSHOT REQUEST: Tools tab with Virtual Node active + a connected client IP -->
