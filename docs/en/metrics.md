<!-- Deliberately duplicated from docs/index.html (#telemetry) — the explainer
     lives on the website AND in the handbook. Keep both in sync. -->
# 📈 Understanding the Values

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/messwerte/)

The Meshhessen Client stores all received telemetry data in a local SQLite database and analyses it automatically. Open the analysis window for any node via the node context menu → *Telemetry*; the LED indicators in the main window give you an at-a-glance overview. This chapter explains **what the numbers mean** — where to find them in the client is covered in [Telemetry & radio analysis](telemetry.md).

## 📶 Signal analysis: SNR & RSSI

### SNR – signal-to-noise ratio

Shows how strong the signal is relative to the noise. Thanks to spreading, LoRa decodes signals below the noise floor – how far depends on the modem preset (short presets ≈ −7 dB, Long Slow down to ≈ −20 dB). The thresholds below are a practical orientation for the presets common around here, not hard limits:

* 🟢 **▲ > 5 dB** – Excellent, stable link
* 🟢 **● 0 – 5 dB** – Good, reliable link
* 🟡 **● −5 – 0 dB** – Marginal, packet loss possible
* 🔴 **▼ < −5 dB** – Poor, frequent packet loss

### RSSI – received signal strength

Absolute received signal level in dBm. Shows how loud the signal arrives at the receiver regardless of noise. Combined with SNR it gives a complete picture of link quality.

* 🟢 **> −100 dBm** – Good
* 🟡 **−100 to −115 dBm** – Marginal
* 🔴 **< −115 dBm** – Weak

### Signal trend (regression)

The client calculates the linear trend over the configured time window (default: 6 hours). A negative trend indicates a deteriorating link – e.g. due to changing weather or new obstructions.

* 🟢 **▲ Rising** – Signal improving
* 🟡 **→ Stable** – No significant trend
* 🔴 **▼ Falling** – Signal deteriorating

## 🔋 Battery & voltage

### Battery level (%)

* 🟢 **> 50 %** – Good
* 🟡 **20 – 50 %** – Medium
* 🔴 **< 20 %** – Low, charge soon

### Night-time drop

How many % battery the node consumes overnight (no solar charging). A high night drop indicates elevated idle current or missing deep-sleep configuration.

* 🟢 **< 5 %** – Normal (standby current)
* 🟡 **5 – 15 %** – Slightly elevated
* 🔴 **> 15 %** – Critical, investigate

### Voltage (V)

Raw battery voltage in volts. Typical LiPo values: 4.2 V = fully charged, 3.7 V = nominal, 3.2 V = deep-discharge threshold. Solar nodes show significantly higher voltage during the day.

## 📡 Channel utilization

Channel utilization shows what percentage of the LoRa time slot on the selected channel is occupied – by the node's own packets and by received packets from other nodes. High channel utilization increases the probability of collisions and packet loss. The Mesh Health tooltip shows the top-5 nodes by channel utilization.

| Utilization | Rating | Recommendation |
|---|---|---|
| 0 – 10 % | 🟢 Excellent | Plenty of capacity free, no action needed |
| 10 – 25 % | 🟢 Good | Normal, healthy mesh |
| 25 – 50 % | 🟡 Elevated | Reduce position update rate, consider Short Slow preset |
| > 50 % | 🔴 Critical | Many packet losses expected; reduce telemetry intervals and broadcasts |

## 🔀 Routing & path stability

### Hop count

How often a packet was forwarded by relay nodes (Meshtastic counting: **direct = 0 hops**; the number of radio legs is hops + 1). Every forward adds latency and airtime.

* 🟢 **0 hops** – Direct link, optimal
* 🟢 **1–2 hops** – Normal
* 🟡 **3–5 hops** – High, heavy airtime use
* 🔴 **6–7 hops** – Maximum, very inefficient

### Path change rate

How frequently the routing path to a node changes. Frequent path changes indicate unstable intermediate links – e.g. relay nodes at poor locations.

* 🟢 **Stable** – Consistent path
* 🟡 **Moderate** – Occasional changes
* 🔴 **Unstable** – Frequent changes, check relay nodes

### Hop cost

Weighted average hop count over the analysis period. Low hop cost means short, efficient paths; high hop cost increases latency and channel utilization for the entire mesh.

## 👥 Neighbors

Shows all nodes that the selected node hears directly (0 hops, no relay). Per neighbor the SNR of the direct link and its trend over time are displayed. Many stable neighbors indicate good integration into the mesh; few or unstable neighbors indicate range or configuration issues.

## ⏱️ Airtime

Shows how much transmit time (TX airtime) and receive time (RX airtime) the node uses. Nodes with high TX airtime increase channel utilization for all nodes in range. Telemetry intervals and broadcast frequency should be adjusted accordingly.

## 🔧 "Net:" LED strip – quick reference

The **"Net:"** strip in the main window rates the whole visible mesh:

| LED | 🟢 Green | 🟡 Yellow | 🔴 Red | ⚪ Grey |
|---|---|---|---|---|
| Weather/ST *(signal weather, short-term SNR trend, 6 h)* | SNRs stable or rising | Slight simultaneous drop | Clear simultaneous drop (weather effect) | No data |
| LT-SNR *(antenna health, 7 days)* | Long-term trend stable/rising | Creeping degradation | Falling long-term — check antenna/position | No data |
| Nbrs *(neighbor quality)* | ≥ 2 stable neighbors | 1 neighbor or declining SNRs | No direct links | No data |
| Path *(route stability)* | Stable path | Occasional changes | Frequent path changes | No traceroute |
| Mesh Health *(score 0–100 %)* | Good overall state | Individual problem areas | High channel util. or many unstable nodes | No data |

## 🛠️ Troubleshooting

* **Poor signal (low SNR/RSSI):** check antenna for loose connections or damage. Raise the node – clear line of sight significantly extends LoRa range. Keep the antenna vertical. Add a relay node as an intermediate hop.
* **Falling signal trend:** monitor weather conditions (moisture and rain attenuate signal). Check outdoor nodes for water ingress in the antenna connector. Verify frequency offset of the node.
* **High channel utilization (> 25 %):** switch modem preset to *Short Slow* (higher data rate via a lower spreading factor = shorter airtime per packet). Reduce position update rate in firmware (e.g. every 10–30 minutes). Increase telemetry intervals (device, environment). Minimize unnecessary broadcasts.
* **Path instability (frequent path changes):** identify relay nodes at poor locations using traceroute SNR analysis. Set a fixed position for stationary relay nodes – saves GPS power and stops map positions from jumping (it does not directly control next-hop routing). Analyse the SNR of the affected links via [traceroute](traceroute.md).
* **High night-time battery drop:** review power-saving options per device role: clients/trackers benefit from power saving/deep sleep, routers should stay receive-ready. Reduce display timeout. Check solar panel alignment and cleanliness. Verify charge controller operation.
* **Few or no neighbors:** verify channel settings (frequency, modem preset) match neighboring nodes. Improve antenna and node placement. Increase mesh density by deploying another node.

> 📖 Short definitions of all terms live in the [glossary](glossary.md); how to monitor these values as a dashboard is shown in the [how-to in the telemetry chapter](telemetry.md).
