# 🗺️ Map & Offline Use

[← Handbook home](../index.md) · [🇩🇪 Deutsche Version](../de/karte.md)

## Map styles & vector map

* **Three map styles:** OSM Standard, OSM Dark Mode, OpenTopoMap (topographic)
* **Vector maps** (switchable in settings, raster remains the default):
  * Razor-sharp at every zoom level (MapLibre GL), much smaller offline data, style updates without a client update
  * **🚒 Fire service / rescue layer** (see below)
  * Layer selection in the settings and via the 🗂️ button directly on the map; while a layer is off, not a single byte is loaded for it
  * **Automatic offline cache:** areas viewed online keep working without internet; plus a **vector offline package downloader** (federal state / custom bounding box, topo extras and extra layers as opt-in)
  * Full feature set: node pins, traceroutes, neighbor lines, position tracks and the right-click menu also work on the vector map
  * **⛰ 3D terrain** – a real elevation model (SRTM, 90 m) with hillshading, toggled via the 🗂️ layer popup; Ctrl+drag tilts the map, a north arrow appears while rotated/tilted (click to re-north); areas viewed once keep working offline
  * Requires the Microsoft WebView2 runtime (pre-installed on Windows 11); without it the client falls back to raster automatically

The vector map (MapLibre GL) – razor-sharp at every zoom level, with node pins:
<img alt="Meshhessen Client – vector map (MapLibre GL) with Meshtastic node positions on Windows" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/vector_in_action.png" />

**⛰ 3D terrain** – a real elevation model (SRTM) with hillshading on the vector map, tilted via Ctrl+drag:
<img alt="Meshhessen Client – 3D terrain (SRTM elevation model) with hillshading on the Meshtastic vector map" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-with-dem-elevation-profile.png" />

## Map modes

| Mode | Description | Coverage |
|----|----|----|
| **Offline** (default) | Pre-downloaded tiles from the Meshhessen server. No internet required. | Germany only |
| **Online – Meshhessen server** | Tiles fetched on demand from `tile.meshhessenclient.de` and stored permanently. All three styles. | Germany only |
| **Online – custom tile server** | Same, but with your own configured tile URLs; public OSM/OpenTopoMap servers are not allowed here (tile usage policy). | depends on server |
| **Online – OpenStreetMap** | Worldwide coverage via `tile.openstreetmap.org`; standard map only; OSM tile policy compliant. | Worldwide |

> The OSM online mode is intended for areas outside Germany. The OSM tile server is donation-funded – please use sparingly.

## Downloading tiles (offline use)

> ⚠️ **Important:** Bulk tile download is intended only for the Meshhessen tile server (`tile.meshhessenclient.de`) – bulk downloads violate the OSM tile policy.

1. **Settings → Map:** select map mode **Offline** (or "Online – Meshhessen") and pick a map style
2. Click **"Download Tiles"** — the download dialog opens:

   <img width="567" alt="Meshhessen Client – offline map tile downloader: bounding box, zoom levels and progress" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/tile_downloader.png" />

3. **Define the area:** enter the bounding box as `latMin,lonMin,latMax,lonMax` — e.g. Hesse: `49.3,7.7,51.7,10.2`
4. **Pick zoom levels:** `1-12` is enough for an overview, `1-14` is the recommendation for towns, `15+` only for small areas — **every extra level quadruples** the tile count
5. **Start the download** and watch the progress — existing tiles are skipped, so aborting + restarting is completely harmless
6. Tiles land under `maptiles/`, stay available offline permanently and are portable via USB
7. Don't forget the **offline test** (see below)

**Typical pitfalls:**

* **Download refuses to start** → public OSM/OpenTopoMap servers are blocked for bulk downloads (tile policy) — use the Meshhessen server or your own tile server
* **Blank areas outside Germany** → the Meshhessen server covers Germany only; use the OSM online mode (viewing only, no bulk download) abroad
* **Download gets huge** → lower the zoom cap or shrink the bounding box — going from zoom 14 to 15 alone quadruples the data

**How much storage?** Rough raster-map figures: Hesse up to zoom 14 ≈ 2–4 GB per map style; all of Germany up to zoom 14 ≈ 20–30 GB. The vector map is far more compact — a vector offline package for a federal state is usually below 1 GB.

> ⚠️ **On SD cards, factor in the allocation unit size** (e.g. for the T-Deck): many tiles are only a few hundred bytes but each occupies a full cluster. With the default formatting of large cards (often 128 KB clusters) the real usage multiplies compared to the figures above. Format exFAT with **4096-byte allocation units** — the [T-Deck assistant](tools.md) recommends and does exactly that.

**Offline test:** switch the map mode to **Offline** (or briefly disconnect the network) and pan/zoom through your target region — whatever renders now is genuinely stored locally. Missing tiles show as blank areas; extend the download area/zoom in that case.

## On the map

* **Node positions** as colored pins; right-click a node → set color, send DM, edit note, start traceroute, open telemetry
* **Node paths** – record and display GPS position history
* **Direct neighbor lines** – toggle in the legend box: lines to every node received **directly over RF (0 hops, no MQTT)**; gradient by **SNR** or **age**; a **"Permanent"** option shows every neighbor ever heard directly
* **Waypoints** – show received waypoints; create and send via right-click on the map
* **Environment & radio overlay** (🌡️ button) – value boxes at the nodes and a weather-service-style interpolated heatmap; besides all environment metrics also **SNR, RSSI and noise floor** as surface overlays

### Environment & radio overlay in pictures

Temperature – isotherms, zone labels and legend:
<img alt="Meshhessen Client – temperature heatmap overlay with isotherms on the Meshtastic map" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-environment-overlay-temp.png" />

Noise floor – radio quality as a surface overlay:
<img alt="Meshhessen Client – noise floor heatmap overlay: radio quality as a surface on the Meshtastic map" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-environment-overlay-noisefloor.png" />

Barometric pressure:
<img alt="Meshhessen Client – barometric pressure heatmap overlay on the Meshtastic map" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map-environment-overlay-pressure.png" />

Mouse-over value boxes right at the nodes:
<img alt="Meshhessen Client – mouse-over value boxes of the environment overlay at the Meshtastic nodes" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/mouse-over-telemetry-environment-map.png" />

## 🚒 Fire service / rescue layer (vector map)

An optional special-purpose layer for fire services, rescue services and civil protection – meant as support during operations and exercises ("where is the nearest hydrant / rescue point?"). Data comes **live from OpenStreetMap** (daily updates on the Meshhessen server) – coverage depends on the local mapping state. Colors follow fire-plan conventions: **red** = extinguishing/alerting, **blue** = water supply, **green** = rescue, **orange** = civil protection. Clicking an object opens a detail popup with all attributes maintained in OSM (type, couplings, flow, pressure, water source, operator, location description, survey date). Icons deliberately overlap ("during an operation you want to see every point"). Works offline for cached areas; while the layer is off, not a single byte is transferred.

Comparison: classic raster map – without the optional special-purpose layers:
<img alt="Meshhessen Client – classic raster map without the fire service layer (comparison)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/raster_wo_hydrant.png" />

The same area as a vector map with the 🚒 fire service / rescue layer enabled (click shows details):
<img alt="Meshhessen Client – vector map with fire service layer: hydrants, stations and rescue points on the Meshtastic map" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/vector_w_hydrant.png" />

Clicking a hydrant shows its details – here an underground hydrant (ring symbol) with water source:
<img alt="Meshhessen Client – hydrant detail popup on the vector map: underground hydrant with type and water source" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/hydrant_unterflur.png" />

## More screenshots

Direct RF neighbors with SNR color gradient on the vector map:
<img alt="Meshhessen Client – neighbor lines with SNR color gradient on the vector map (0-hop RF)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/vector_w_neighborlinks.png" />

Offline map with node positions and distance display:
<img width="1250" height="813" alt="Meshhessen Client – Windows app for Meshtastic devices: offline map with node positions (OSM)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map.png" />

Direct RF neighbors as lines on the raster map (gradient by SNR or age):
<img alt="Meshhessen Client – direct Meshtastic neighbors as colored lines on the map (0-hop RF, SNR gradient)" src="https://raw.githubusercontent.com/SMLunchen/mh_windowsclient/master/img/map_neighbor_lines.png" />

Node position history (GPS track) on the map:
<img width="967" height="411" alt="Meshhessen Client – Meshtastic node position history GPS track on map" src="https://github.com/user-attachments/assets/0415083a-9f22-44fc-b767-be66b69892cd" />
