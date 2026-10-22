# 📖 Glossary — Meshtastic terms in a nutshell

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/glossar/)

**SNR (signal-to-noise ratio, dB)**
: How far the signal sits above the noise. Thanks to spreading, LoRa decodes signals *below* the noise floor — depending on the modem preset down to about −7 dB (short presets) or −20 dB (Long Slow). Rule of thumb: > 5 dB very good, around 0 dB solid, clearly negative = marginal.

**RSSI (received signal strength indicator, dBm)**
: Absolute reception level. Typical LoRa values range from ≈ −60 dBm (very close/strong) to ≈ −125 dBm (reception limit). Unlike SNR it is independent of the noise level — both together tell the story.

**Noise floor (dBm)**
: Base noise at the receiver (environmental interference + receiver noise). Approximation: RSSI ≈ noise floor + SNR. Newer firmware reports it in LocalStats.

**Hop**
: One forward through a relay node. Every hop adds latency and airtime; the hop limit (default 3, max. 7) bounds a message's reach.

**Airtime**
: The time a transmission occupies the radio channel. A shared resource: the longer the modem preset and the more packets/hops, the less capacity remains for everyone else.

**Channel utilization (%)**
: Share of time the channel is busy (own + foreign transmissions). Persistently above ~25–40 % gets critical — packets collide and get lost.

**Modem preset (e.g. Short Slow, Long Fast)**
: Combination of bandwidth and spreading factor. "Short" presets = higher data rate, shorter airtime, less range; "long" presets the other way around. All nodes of a mesh must use the same preset.

**MQTT**
: Internet bridge between mesh islands: gateways lift packets onto a broker and back. Great for reach — but a "neighbor via MQTT" is not a radio neighbor. That's why the client labels MQTT segments separately.

**Direct neighbors / real RF neighbors**
: Nodes provably heard *directly over radio* (0 hops, no MQTT) — proven by own direct reception, traceroute hop pairs or NeighborInfo packets.

**NeighborInfo**
: Packet type with which a node itself reports whom it hears directly — one of the three evidence sources for real RF neighbors.

**Traceroute**
: Diagnostic request that records the path to a target node: every station adds itself with per-hop SNR (outbound and return).

**PKI (public key infrastructure)**
: Key pairs (X25519) for encrypted direct messages and admin access. The client decrypts PKI DMs client-side (AES-256-CCM) and manages known public keys in `node_keys.csv`.

**NodeDB**
: A device's node database: every node ever heard, with names, positions, telemetry snapshots. The client loads it on connect and enriches it with its own telemetry history.

**LocalStats**
: Statistics telemetry of newer firmware: noise floor, RX total/bad/duplicates, TX/relay counters, nodes online. The basis for rates (packets/h) and the radio overlays.

**precision_bits (position precision)**
: Meshtastic can deliberately coarsen transmitted positions (e.g. 13 bits ≈ ±2.9 km). The client displays and accounts for this blur (for instance in position estimation).

**Fresnel zone**
: Ellipsoid-shaped space around a radio link's line of sight that must stay largely clear for clean reception (rule of thumb: 60 % of the 1st zone). Terrain poking into it causes extra loss — exactly what the client's elevation profile shows.
