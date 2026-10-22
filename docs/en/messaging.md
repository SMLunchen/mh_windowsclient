# 📨 Messaging & Communication

[← Back to project overview](../../README.md) · [🇩🇪 Deutsche Version](../de/nachrichten.md)

* **Send and receive messages** (broadcast & direct messages)
* **Multi-channel** – all channels from your device loaded automatically
* **Direct Messages (DMs)** with a dedicated chat window in a tabbed layout
* **PKI decryption** – client-side decryption of PKI-encrypted DMs (X25519 + AES-256-CCM, firmware-accurate); private key is loaded automatically from the connected device
* **Node public key database** – local CSV file (`node_keys.csv`) for managing public keys for PKI decryption
* **Tap-back reactions** – react to messages with emoji (32 emojis, like the Android app); right-click a message → emoji picker; works in channel chat and DMs
* **Reply function** – reply to individual messages (protocol-level), quoted block with accent-colored highlight
* **Selectable & copyable messages** – select text, copy to clipboard, click links; right-click any message → "Copy Message" (always copies the right-clicked message)
* **🚨 Alert Bell support** – send and receive emergency alerts; 🚨 SOS button in chat and DMs; red blinking border + notification bar with "Jump to map" button
* **Persistent message database** – store channel and DM messages in a local SQLite DB (optional, enable in settings)
  * Last 24 hours loaded automatically on connect; older messages lazy-loaded when scrolling up
  * **DM history:** all conversations from the last 24 h appear automatically when the DM window is opened
  * **Per-channel deletion** directly in the Channels tab with a time-range dialog (All / 30 / 90 / 365 days)
  * Confirmation before clearing a DM conversation; configurable retention period (30 / 90 / 365 days)

## Screenshots

Messaging view with channel chat and direct messages:
<img width="1443" height="813" alt="Meshhessen Client – Meshtastic messages, channel chat and direct messages (DM) on Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/messaging.png" />

Channel list with all Meshtastic channels:
<img width="1443" height="814" alt="Meshhessen Client – Meshtastic channel list (multi-channel) on Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/channel_list.png" />

Alert Bell / SOS emergency display:
<img width="1446" height="816" alt="Meshhessen Client – Alert Bell SOS emergency signalling for Meshtastic devices on Windows" src="https://github.com/SMLunchen/mh_windowsclient/blob/master/img/alert_bell.png" />
