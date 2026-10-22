# 📨 Messaging & Communication

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/nachrichten/)

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

## How-to: send your first message & an encrypted DM

**Goal:** send a broadcast into a channel, then an end-to-end encrypted direct message to a specific node.

1. **Channel message:** Messages tab → pick a channel at the bottom → type → Enter. Your own message shows right-aligned; replies arrive in the same thread
2. **Start a DM:** right-click the target node (node list, map, or one of its messages) → **"Direct message"** — the DM window opens the conversation as its own tab
3. **Check encryption:** the 🔑 column in the node list shows whether the target's public key is known. 🔑 = the DM goes out PKI-encrypted (X25519 + AES-256-CCM), 🔓 = channel encryption only
4. **Success check:** the sent message gets a delivery check mark (ACK); the reply appears in the tab. Received PKI DMs are decrypted by the client itself
5. **Extras:** right-click a message → emoji reaction or "Reply" (quote block); text is selectable and copyable

**Typical pitfalls:**

* *🔓 instead of 🔑 on the target* → the public key is missing locally: receive the node directly once or pull its user info via right-click "Request information"
* *No ACK* → target currently unreachable (test with a [traceroute](traceroute.md)) or the link is congested — wait briefly instead of resending (airtime!)
* *Old conversation missing* → the message database is optional: enable it in the settings, otherwise history only starts at connect time

<!-- SCREENSHOT REQUEST: DM window with a running conversation + visible 🔑 status in the node list -->

## Screenshots

Messaging view with channel chat and direct messages:
<img width="1443" height="813" alt="Meshhessen Client – Meshtastic messages, channel chat and direct messages (DM) on Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/new-chat-layout.png" />

Channel list with all Meshtastic channels:
<img width="1443" height="814" alt="Meshhessen Client – Meshtastic channel list (multi-channel) on Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/channel_list.png" />

Alert Bell / SOS emergency display:
<img width="1446" height="816" alt="Meshhessen Client – Alert Bell SOS emergency signalling for Meshtastic devices on Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/alert_bell.png" />
