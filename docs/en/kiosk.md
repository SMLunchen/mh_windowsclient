# 🔒 Kiosk / Training Mode

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/kiosk/)

For shared stations (club room, training, events):

* Enable in settings, set a password and choose what gets hidden while locked: tabs (Nodes, Channels, Settings, Info, Tools, Debug), node configuration, remote admin, telemetry dashboard, SOS button, Meshhessen quick-config
* **🔒 lock in the footer bar** to lock/unlock (only shown when kiosk mode is enabled **and** a password is set); unlock via password, the app always starts locked in kiosk mode
* **The Messages and Map tabs always stay visible** — deliberately not lockable, so the station remains usable
* While locked, **admin commands from [Virtual Node](virtual-node.md) clients are additionally blocked** (regardless of the VNode setting)
* ⚠️ **Protects against accidents, not attacks** – the password is stored as a PBKDF2 hash, but anyone who can edit the INI file can disable the mode
* **Forgot the password?** Close the client and set `KioskModeEnabled=True` to `False` in `meshhessen-client.ini` (or clear the `KioskPasswordHash=…` line and set a new one)

Kiosk / training mode – lockable UI for shared stations (settings):
<img alt="Meshhessen Client – kiosk and training mode: lockable UI with password protection for shared stations" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/kiosk_settings.png" />

<!-- SCREENSHOT REQUEST: main window in the LOCKED kiosk state (reduced tabs, 🔒 in the footer bar) -->
