# 📊 Telemetry & Radio Analysis

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/telemetrie/)

## Telemetry database & dashboard

* **Persistent telemetry database** – received packet and device data is stored locally in a SQLite database
* **Telemetry Dashboard** – freely configurable widget dashboard with named dashboards (📊 button in the toolbar):
  * **11 widget types:** line, area, bar, scatter, gauge, stat value, heatmap, histogram, candlestick, state timeline, clock
  * Multi-node support, all metrics: SNR, RSSI, battery, voltage, channel/TX utilization, temperature, humidity, pressure, packets/h, noise floor, RX/TX stats
  * Drag-and-drop arrangement, freely resizable, editable, duplicatable, auto-refresh
  * **Zoom/pan in charts** (survives auto-refresh), day/night shading, threshold lines with **Windows toast alerts** on crossings
  * **Export:** CSV (incl. a German-Excel flavour), widget as PNG, copy image, share whole dashboards as JSON (export/import)
  * **Fullscreen mode (F11)** for wall monitors; window placement and last active dashboard are remembered
  * **Automatic telemetry polling (📥):** up to 4 nodes are actively polled for LocalStats/environment/device data (mesh-friendly: LocalStats at most 1×/h, others every 30 min max); counter deltas yield packets/h rates
  * Persisted in `dashboards.json`, theme-adaptive incl. live Windows theme switching; independent window for a second monitor

**📊 Telemetry dashboard** – freely configurable widgets with threshold alerts, auto-refresh and fullscreen:
<img alt="Meshhessen Client – telemetry dashboard with configurable widgets: gauges, heatmaps, mesh health and threshold alerts" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/telemetry-dashboard.png" />

## How-to: create a dashboard, set an alert, read the values

**Goal:** a dashboard that watches your solar router's battery and warns you before it runs dry overnight.

1. **Open:** 📊 button in the main window toolbar → the dashboard window starts with "Dashboard 1" ("+ New" creates more named dashboards, e.g. "Solar monitoring")
2. **Add a widget:** "+ Add Widget" → pick a type (for battery: **line**), tick the node(s), metric **battery (%)**, range **7d** → save. Widgets can be dragged around and resized via the grip in the lower right
3. **Set the alert:** enter your limit into the widget's **Threshold** field (e.g. `30`). The threshold line appears in the chart; when the latest value **crosses** it you get a **Windows toast** (transition only — no constant nagging)
4. **Keep it fresh:** auto-refresh top right (e.g. 30 s); if the node rarely sends on its own, add it to the polling plan via the **📥 Polling** button (max. 4 nodes, mesh-friendly)
5. **Success check:** the widget shows a curve with day/night shading; the last refresh time is shown at the bottom. For a test, set the threshold just past the current value — the toast must fire immediately
6. **Reading the values:** if the battery drops more than ~15–20 % overnight, check the [metrics explainer on the landing page](https://meshhessenclient.de/?lang=en#telemetry); SNR/RSSI interpretation lives in the [glossary](glossary.md)

**Typical pitfalls:**

* *Widget stays empty* → no data for this node/metric in the telemetry database yet (the node doesn't send that metric → enable polling or right-click "Request information")
* *No toast despite threshold* → toasts fire on the **transition** only; if the value was already past the threshold when you set it, the alert comes with the next crossing. Also check: are Windows notifications allowed for apps?
* *Zoom keeps resetting* → zoom/pan survives auto-refresh; the manual "Refresh" button deliberately resets the view

<!-- SCREENSHOT REQUEST: widget editor open (type/node/metric/threshold visible) -->
<!-- SCREENSHOT REQUEST: Windows toast of a threshold alert -->

## Node statistics & real RF neighbors

* **Node statistics window** – detailed analysis per node:
  * Day/night breakdown of reception quality
  * Time-series graphs for SNR, RSSI, packet rate and device data (battery, voltage)
  * **Real RF neighbors** – proven by direct reception, traceroute hop pairs and NeighborInfo packets, with SNR in both directions (fills over time; classic "heard" list as automatic fallback until then)
  * **LocalStats** (newer firmware): noise floor, RX bad/duplicates, TX relay and more (routing tab)
* **LED indicators** in the main window: 📶 signal, 👥 neighbors, 🛤️ path stability, 🌐 mesh health, 🌤️ weather

## ⛰ Elevation profile per radio link

Right-click a neighbor (or a traceroute hop): SRTM terrain profile with line of sight and 60 % Fresnel zone (4/3 earth, 868 MHz), including a verdict whether the path is clear or terrain is in the way:
<img alt="Meshhessen Client – elevation profile of a Meshtastic radio link: SRTM terrain, line of sight and Fresnel zone" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/elevation-profile.png" />

## More screenshots

Telemetry LED indicators in the main window:
<img width="516" height="162" alt="Meshhessen Client – Meshtastic telemetry LEDs for signal, neighbors, mesh health and weather" src="https://github.com/user-attachments/assets/a580cf69-ab10-4890-a17a-1c5086c19700" />

Telemetry time-series graph (SNR, RSSI, battery):
<img width="902" height="604" alt="Meshhessen Client – Meshtastic telemetry graph SNR RSSI battery time series OxyPlot" src="https://github.com/user-attachments/assets/a42d6a79-5b90-4f7a-8456-c74099c55c41" />

Node telemetry statistics window:
<img width="1278" height="889" alt="Meshhessen Client – Meshtastic node telemetry day/night statistics on Windows" src="https://github.com/user-attachments/assets/4821c996-c524-499a-8fba-f738fb051f88" />

Node telemetry overview (signal):

![Meshhessen Client – Meshtastic node telemetry overview: signal analysis, routing, airtime and neighbor statistics](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node-telemetry.png)

Real RF neighbors with SNR in both directions – right-click opens the link's elevation profile:

![Meshhessen Client – real RF neighbors with SNR in both directions and elevation-profile context menu](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_neighbors.png)

Airtime analysis per node (TX/RX airtime, channel utilization):

![Meshhessen Client – Meshtastic airtime analysis: TX airtime, RX airtime and channel utilization per node](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_airtime.png)

Battery & voltage analysis (day/night profile, night drop):

![Meshhessen Client – Meshtastic battery analysis: voltage curve, day/night profile and night drop](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_power.png)

Routing statistics (hop count, path stability, path change rate):

![Meshhessen Client – Meshtastic routing statistics: hop count, path stability and path change rate](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_routing.png)

> 📖 What the individual metrics mean (SNR, RSSI, channel utilization, hop costs …) is explained in detail in the [telemetry help on the website](https://meshhessenclient.de/#telemetrie).
