# 📊 Telemetry & Radio Analysis

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/telemetrie/)

## Telemetry database & dashboard

* **Persistent telemetry database** – received packet and device data is stored locally in a SQLite database
* **Telemetry Dashboard** – freely configurable widget dashboard with named dashboards (📊 button in the toolbar):
  * **14 widget types:** line, area, bar, scatter, gauge, stat value, heatmap, histogram, candlestick, state timeline, ranking, multi-stat, mesh health, clock
  * Multi-node support, all metrics: SNR, RSSI, battery, voltage, channel/TX utilization, temperature, humidity, pressure, packets/h, noise floor, RX/TX stats
  * Drag-and-drop arrangement, freely resizable, editable, duplicatable, auto-refresh
  * **Zoom/pan on the time axis** (survives auto-refresh; double-click resets), day/night shading
  * **Warn & alarm zones (Grafana-style):** per widget one warning and one alarm bound in each direction; gauge/stat/charts color green/orange/red by zone, **Windows toasts** report every zone transition (incl. ✅ all-clear)
  * **Export:** CSV (incl. a German-Excel flavour), widget as PNG, copy image, share whole dashboards as JSON (export/import)
  * **Fullscreen mode (F11)** for wall monitors; window placement and last active dashboard are remembered
  * **Automatic telemetry polling (📥):** up to 4 nodes are actively polled for LocalStats/environment/device data (mesh-friendly: LocalStats at most 1×/h, others every 30 min max); counter deltas yield packets/h rates
  * Persisted in `dashboards.json`, theme-adaptive incl. live Windows theme switching; independent window for a second monitor

**📊 Telemetry dashboard** – freely configurable widgets with threshold alerts, auto-refresh and fullscreen:
<img alt="Meshhessen Client – telemetry dashboard with configurable widgets: gauges, heatmaps, mesh health and threshold alerts" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/telemetry-dashboard.png" />

## How-to: create a dashboard, set an alert, read the values

**Goal:** a dashboard that watches your solar router's battery and warns you before it runs dry overnight.

1. **Open:** 📊 button in the main window toolbar → on first open the window is empty; "+ New" creates your first dashboard (suggested name "Dashboard 1" — call it e.g. "Solar monitoring")
2. **Add a widget:** "+ Add Widget" → pick a type (for battery: **time series/line**), tick the node(s), metric **battery (%)**, range **7 days** (the default) → save. On the finished card you can switch via the 1d/7d/14d/30d buttons, reorder via the ⠿ grip and resize via the grip in the lower right
3. **Set the alert:** in the widget editor under **"Thresholds & alerts"** set your bounds — for battery e.g. ⚠ warning low `30` and 🚨 alarm low `15`. The lines appear in the chart (orange/red), gauge and stat value color by zone; when the value **changes zone** on a refresh you get a **Windows toast** with severity (transition only — no constant nagging; returning to normal reports ✅)
4. **Keep it fresh:** auto-refresh top right (e.g. 30 s); if the node rarely sends on its own, add it to the polling plan via the **📥 Polling** button (max. 4 nodes, mesh-friendly)
5. **Success check & alert test:** the widget shows a curve with day/night shading; the last refresh time is shown at the bottom. Test the alert in two steps: first set the bounds so the current value sits in the normal zone and wait for one refresh — this first check silently records the baseline. Then move a bound past the current value and press "↺ Refresh" (or wait for the auto-refresh): now the toast fires
6. **Reading the values:** if the battery drops more than ~15–20 % overnight, see the chapter [Understanding the values](metrics.md) (night drop, voltage); SNR/RSSI interpretation lives there and in the [glossary](glossary.md)

**Typical pitfalls:**

* *Widget stays empty* → no data for this node/metric in the telemetry database yet (the node doesn't send that metric → enable polling or right-click "Request information")
* *No toast despite alarm bounds* → checks only happen on a **refresh** (auto-refresh, "↺" or saving in the editor), never while typing — and the very first check after opening silently records the baseline; only the next **zone transition** is reported. Also check: are Windows notifications allowed for apps?
* *Zoom keeps resetting* → zoom/pan survives auto-refresh; the manual "Refresh" button deliberately resets the view

<!-- SCREENSHOT REQUEST: widget editor open (type/node/metric/threshold visible) -->
<!-- SCREENSHOT REQUEST: Windows toast of a threshold alert -->

## Node statistics & real RF neighbors

* **Node statistics window** – detailed analysis per node:
  * Day/night breakdown of reception quality
  * Time-series graphs for SNR, RSSI, packet rate and device data (battery, voltage)
  * **Real RF neighbors** – proven by direct reception, traceroute hop pairs and NeighborInfo packets, with SNR in both directions (fills over time; classic "heard" list as automatic fallback until then)
  * **LocalStats** (newer firmware): noise floor, RX bad/duplicates, TX relay and more (routing tab)
* **"Net:" LED strip** in the main window — the state of the whole visible mesh at a glance:
  * **Weather/ST** – "signal weather": short-term SNR/RSSI trend of all nodes (last 6 h)
  * **LT-SNR** – antenna health: long-term SNR trend of all nodes (last 7 days)
  * **Nbrs** – neighbor quality: average link quality to neighbor nodes
  * **Path** – route stability: frequency of route changes in the mesh
  * **Mesh Health** – overall score 0–100 % from path costs, route changes, reception rate and channel utilization

## ⛰ Elevation profile per radio link

Real RF neighbors with SNR in both directions — a **right-click** on a neighbor (or a traceroute hop) opens the link's elevation profile:

![Meshhessen Client – real RF neighbors with SNR in both directions and elevation-profile context menu](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_neighbors.png)

The elevation profile then shows the SRTM terrain profile of the radio link with line of sight and 60 % Fresnel zone (4/3 earth, 868 MHz), including a verdict whether the path is clear or terrain is in the way:
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

Airtime analysis per node (TX/RX airtime, channel utilization):

![Meshhessen Client – Meshtastic airtime analysis: TX airtime, RX airtime and channel utilization per node](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_airtime.png)

Battery & voltage analysis (day/night profile, night drop):

![Meshhessen Client – Meshtastic battery analysis: voltage curve, day/night profile and night drop](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_power.png)

Routing statistics (hop count, path stability, path change rate):

![Meshhessen Client – Meshtastic routing statistics: hop count, path stability and path change rate](https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/node_routing.png)

> 📖 What the individual metrics mean (SNR, RSSI, channel utilization, hop costs …) is explained in detail in the chapter [Understanding the values](metrics.md).
