# Vector-Tiles im Meshhessen-Client: Client-seitige Umsetzung

Stand: 2026-07-09. Server-Seite ist produktiv (vectortile.meshhessenclient.de).
Dieses Dokument beschreibt, was im Client umzusetzen ist, um die drei Karten
(OSM, OpenTopo, Dark) als Vector-Karten in gleicher Darstellungsqualität zu
erhalten — inklusive Offline-Betrieb.

---

## 1. Server-Endpunkte (alle HTTPS, ACL wie Raster: IP-Whitelist ODER User-Agent `MeshhessenClient/*` bzw. `Dalvik/*`)

| Zweck | URL |
|---|---|
| Style OSM | `https://vectortile.meshhessenclient.de/styles/osm.json` |
| Style OpenTopo | `https://vectortile.meshhessenclient.de/styles/opentopo.json` |
| Style Dark | `https://vectortile.meshhessenclient.de/styles/dark.json` |
| Basemap-Tiles (MVT) | `https://vectortile.meshhessenclient.de/basemap/{z}/{x}/{y}` (TileJSON: `/basemap`) |
| Höhenlinien (MVT) | `https://vectortile.meshhessenclient.de/contours/{z}/{x}/{y}` (z11–17) |
| Hillshade (PNG-Raster) | `https://vectortile.meshhessenclient.de/hillshade/{z}/{x}/{y}` |
| Schrift-Glyphs | `https://vectortile.meshhessenclient.de/fonts/{fontstack}/{range}.pbf` |
| Sprites | `https://vectortile.meshhessenclient.de/sprites/{bright\|dark}/sprite…` |
| **Feuerwehr-Overlay (MVT)** | `https://vectortile.meshhessenclient.de/emergency/{z}/{x}/{y}` (z13–17, siehe §9) |

Die Styles referenzieren Tiles/Glyphs/Sprites bereits absolut — der Client
braucht **nur die Style-URL**. Ein Style = eine Karte.

**Wichtig:** Der HTTP-Client der App MUSS den User-Agent `MeshhessenClient/<version>`
senden (wie beim Raster-Server), sonst 403. Bei MapLibre Native Android:
`HttpRequestUtil.setOkHttpClient(...)` mit UA-Interceptor, oder
`MapLibre.setConnectionUserAgent(...)` je nach SDK-Version.

## 2. Rendering-Bibliothek

- **Android:** MapLibre Native (`org.maplibre.gl:android-sdk:11.x`).
  Ersetzt/ergänzt die bisherige Raster-View. Minimal:
  ```kotlin
  MapLibre.getInstance(context)
  mapView.getMapAsync { map ->
      map.setStyle("https://vectortile.meshhessenclient.de/styles/opentopo.json")
  }
  ```
- **iOS:** MapLibre Native iOS, gleiche Style-URLs.
- **Web:** MapLibre GL JS ≥ 4.x (Referenz-Implementierung: Webinterface
  `https://tile.schwarzes-seelenreich.de/` — Buttons mit „V“-Badge, Quelltext
  in `/var/www/html/index.html` auf dem Tile-Server).
- **Geräteanforderung:** OpenGL ES 2.0 (praktisch jedes Android ≥ 5). Auf sehr
  alter Hardware ruckelt Vektor-Rendering — Raster-Endpoints bleiben parallel
  bestehen, im Client als Fallback-Einstellung anbieten.

## 3. Darstellungsqualität / „1:1“

Die Styles sind Ports der Mapnik-Originale (gleiche Farbwelten, gleiche
Layer-Sichtbarkeiten), kein Pixel-Klon — Vektor rendert schärfer und die
Label-Platzierung übernimmt der Client. Details:
- **OSM (Vector):** openstreetmap-carto-Palette (Land #f2efe9, Wasser #aad3df,
  Motorway-Rosa etc.) auf Basis OSM-Bright-Layerstruktur.
- **Dark (Vector):** Dark-Matter-Basis mit den Farben eures osm-carto-dark
  (Hintergrund #1a1a2e, Wasser #1a3a4a, helle Labels).
- **OpenTopo (Vector):** Topo-Palette + **Höhenlinien** (live aus der Contours-DB,
  Haupt-/Zwischenlinien, Höhenbeschriftung ab z13) + **Hillshade-Overlay**
  (identische DEM-Quelle wie die Rasterkarte, 30 % Deckkraft).
- Feinschliff (Schriftgrößen, einzelne Farbnuancen) ist stilseitig zentral auf
  dem Server: `/var/www/vectortile/build_styles.py` ändern → neu ausführen →
  alle Clients bekommen es beim nächsten Style-Load. **Kein App-Update nötig.**

## 4. Offline-Fähigkeit — zwei Stufen

### Stufe A: Offline-Regionen (SDK-Bordmittel, schnell umgesetzt)
MapLibre Native bringt einen `OfflineManager` mit: Der Client definiert
Region (BBox) + Zoombereich, das SDK lädt **alle** Style-Ressourcen (Tiles
aller Quellen, Glyphs, Sprites, Style) in seine lokale Datenbank und rendert
offline nahtlos weiter.
```kotlin
val def = OfflineTilePyramidRegionDefinition(
    styleUrl, boundsHessen, /*minZoom*/ 0.0, /*maxZoom*/ 14.0, pixelRatio)
offlineManager.createOfflineRegion(def, metadata, callback)
```
- Richtwert Größe: Hessen z0–14 als Vektor ≈ 200–400 MB (Raster wäre ×10–20).
- Achtung: Beim opentopo-Style lädt der OfflineManager auch Contours+Hillshade mit.

### Stufe B: Voll-Offline „ganz DACH in der Tasche“ (Sideload)
Die komplette Basemap ist **eine Datei**: `dach.pmtiles` (~3–5 GB, liegt auf
dem Server unter /var/lib/vector/, kann als Download-Paket angeboten werden).
- MapLibre Native ≥ 11 kann `mbtiles://`-Quellen direkt aus dem Dateisystem
  lesen; für PMTiles gibt es Adapter (pmtiles-Java-Lib bzw. Konvertierung
  PMTiles→MBTiles mit dem `mbtiles`-Tool, liegt auf dem Server unter
  /var/lib/vector/mbtiles).
- Umsetzung: lokale Kopie des Style-JSON im APK/Storage, dessen `sources`
  auf die lokale Datei zeigen (`mbtiles:///sdcard/…/dach.mbtiles`); Glyphs +
  Sprites als Assets bündeln (liegen auf dem Server unter
  /var/www/vectortile/fonts bzw. /sprites, zusammen ~50 MB, nur benötigte
  Fonts: „Noto Sans Regular/Bold/Italic“).
- Contours/Hillshade fürs Offline-Topo optional als zweites Paket
  (hillshade.mbtiles 345 MB; Contours lassen sich mit `martin-cp` in eine
  MBTiles-Datei exportieren — auf Zuruf erzeuge ich die).
- Update-Verteilung: neue Datei = neuer Kartenstand. Datei-Datum/ETag prüfen.

**Empfehlung:** Stufe A zuerst (wenige Zeilen, sofortiger Nutzen), Stufe B als
Feature „Offline-Gesamtpaket“ danach.

## 5. Fallback & Koexistenz
- Raster-Endpoints (`tile.schwarzes-seelenreich.de/{osm,opentopo,dark}/z/x/y.png`)
  bleiben unverändert in Betrieb — alte Clients sind nicht betroffen.
- Empfohlene Client-Logik: Einstellung „Kartenmodus: Vektor (neu) / Raster
  (kompatibel)“, Default nach Geräteklasse.

## 6. Betriebsdaten (serverseitig, zur Info)
- Kartenstand Basemap: wird bei Daten-Updates neu generiert (Planetiler-Lauf,
  siehe /root/osm-update.sh-Familie; Vector-Regenerierung aktuell manuell:
  `java -jar /var/lib/vector/planetiler.jar …` — Automatisierung folgt).
- Tile-Cache: nginx cached MVT 7 Tage; Client-seitig `Cache-Control` beachten.
- Nutzung beobachten: `https://tile.schwarzes-seelenreich.de/heatmap/` —
  Buttons „Vector-Base / Contours / Hillshade“ zeigen die Vector-Abrufe.

## 7. Lizenz-Pflicht (wichtig!)
Die Vector-Tiles basieren auf dem OpenMapTiles-Schema (CC-BY). Der Client
MUSS eine sichtbare Attribution anzeigen:
**„© OpenMapTiles © OpenStreetMap contributors"**
(bei den Raster-Karten genügte © OpenStreetMap; für die Vector-Karten ist
OpenMapTiles zusätzlich verpflichtend — z. B. als Kartenfußzeile.)

## 8. Abnahme-Checkliste Client
- [ ] UA-Header gesetzt (403-Test ohne UA von Nicht-Whitelist-IP)
- [ ] Alle 3 Styles laden und zoomen flüssig (z5–z17)
- [ ] OpenTopo: Höhenlinien ab z11 sichtbar, Höhenzahlen ab z13, Relief erkennbar
- [ ] Umlaute/Sonderzeichen in Labels korrekt (Noto-Glyphs)
- [ ] Offline-Region anlegen, Flugmodus, Karte navigierbar
- [ ] Fallback auf Raster funktioniert
- [ ] Feuerwehr-Overlay: Schalter an/aus, Hydranten ab z15, Wachen/Sirenen ab z13, Klick zeigt Details (§9)

---

## 9. Fach-Overlay „Feuerwehr / Rettung" (seit 2026-07-14)

Erstes zuschaltbares Overlay. Die Layer stecken **in allen drei Styles** (osm,
opentopo, dark), stehen aber auf `visibility: none`.

### 9.1 Warum das nichts kostet, solange es aus ist
Die Daten liegen in einer **eigenen Quelle** `emergency` (nicht in `extras`).
MapLibre lädt Tiles einer Quelle nur, wenn mindestens ein **sichtbarer** Layer
sie benutzt. Overlay aus ⇒ **null Requests** an `/emergency`, null Server-Last,
kein Byte im Mobilfunk. Das ist wichtig, denn dahinter stehen **1.155.877
Objekte** (davon 1.000.538 Hydranten) — die dürfen niemals in den Basis-Kacheln
mitfahren.

Größenordnung im Betrieb: Kachel Frankfurt-Innenstadt z13 ≈ 9,7 KB, z14 ≈ 3,6 KB, z16 ≈ 0,7 KB
(dichtestes Gebiet Berlin z13: 18,7 KB / 687 Objekte), Antwortzeit ~0,11 s (partieller GIST-Index in PostGIS), nginx cached 7 Tage.

### 9.2 Layer, Zoomstufen, Icons
Alle Layer-IDs beginnen mit `em-`, Quelle `emergency`, `source-layer: emergency`.
Icons: Badge mit weißem Außenring (auf Topo-Relief lesbar), Farbkonvention wie im
Feuerwehrplan — **rot** = Löschmittel/Meldung, **blau** = Löschwasser-Vorrat,
**grün** = Rettung, **orange** = Katastrophenschutz.

| Layer-ID | class | ab Zoom | Icon | Anzahl DACH |
|---|---|---|---|---|
| `em-hydrant` | `hydrant` | 15 | rotes H — gefüllt = Überflur, Ring = **Unterflur** | 1.000.538 |
| `em-defib` | `defibrillator` | 15 | grünes Herz (AED) | 37.995 |
| `em-phone` | `emergency_phone` | 15 | blaues N | 20.798 |
| `em-suction-point` | `suction_point` | 14 | blaues S (Saugstelle) | 12.787 |
| `em-water-tank` | `water_tank` | 14 | blaues T (Behälter) | 8.808 |
| `em-fire-pond` | `fire_water_pond` | 13 | blaues L (Löschteich) | 3.530 |
| `em-siren` | `siren` | 13 | rote Sirene | 8.896 |
| `em-rescue-point` | `access_point` | 13 | grünes Kreuz + **Nummer** (`ref`) | 7.979 |
| `em-assembly` | `assembly_point` | 13 | grüner Sammelplatz | 5.051 |
| `em-fire-station` | `fire_station` | 13 | rote Flamme + Name | 34.335 |
| `em-ambulance` | `ambulance_station` | 13 | rotes Kreuz + Name | 3.842 |
| `em-help-point` | `disaster_help_point` | 13 | oranges Kreuz | 2.239 |
| `em-mountain-rescue` | `mountain_rescue` | 13 | rotes Kreuz | 378 |
| `em-landing-site` | `landing_site` | 13 | Helipad | 452 |
| `em-lifeguard` | `lifeguard` | 14 | Rettungsring | 946 |
| `em-ladder-site` | `ladder_site` | 14 | rote Leiter | 200 |
| `em-inlet` | `fire_service_inlet` | 16 | rotes E (Einspeisung) | 1.149 |
| `em-extinguisher` | `fire_extinguisher` | 16 | rotes F | 2.264 |
| `em-alarm-box` | `fire_alarm_box` | 16 | rotes M (Melder) | 327 |
| `em-hose` | `fire_hose` | 16 | rotes C (Schlauch) | 190 |
| `em-key-depot` | `key_depot` | 16 | rotes K (FSD) | 291 |
| `em-life-ring` | `life_ring` | 16 | Rettungsring | 2.677 |
| `em-first-aid` | `first_aid_kit` | 16 | grünes Kreuz | 205 |

Die Icons haben `icon-allow-overlap: true` (im Einsatz will man **jeden** Punkt
sehen, auch wenn sie dicht stehen) und `icon-ignore-placement: true` (das Overlay
verdrängt keine Beschriftungen der Basiskarte).

### 9.3 Attribute (für Popups / Listen)
Pro Feature ausgeliefert: `class`, `name`, `ref`, `access`, `type`
(Hydrant-Bauart: `underground` | `pillar` | `wall` | `pipe`), `couplings`,
`colour`, `water_source`, `flow_rate`, `pressure`, `water_volume`, `operator`,
`opening_hours`, `location` (bei Defis: „Foyer, 1. OG"), `level`, `survey_date`.
Leere Tags fehlen im Feature (nicht als Leerstring prüfen).

### 9.4 Ein-/Ausschalten
**Web (MapLibre GL JS)** — Referenz: Cockpit-Header, Checkbox „🚒 Feuerwehr",
Quelltext `/var/www/html/index.html`:
```js
const ids = map.getStyle().layers.filter(l => l.id.startsWith('em-')).map(l => l.id);
ids.forEach(id => map.setLayoutProperty(id, 'visibility', on ? 'visible' : 'none'));
```
**Wichtig:** `setStyle()` (Kartenwechsel) setzt die Sichtbarkeit zurück — den
Overlay-Zustand im `styledata`-Event erneut anwenden.

**Android (MapLibre Native):**
```kotlin
style.layers.filter { it.id.startsWith("em-") }.forEach {
    it.setProperties(PropertyFactory.visibility(if (on) Property.VISIBLE else Property.NONE))
}
```

### 9.5 Antippen → Details
```js
const f = map.queryRenderedFeatures(e.point, { layers: ids })[0];   // Web
```
```kotlin
val f = map.queryRenderedFeatures(map.projection.toScreenLocation(latLng), *emIds)  // Android
```
Feinere Filterung ohne Nachladen ist möglich, solange das Attribut in der Kachel
steht — z. B. nur Unterflurhydranten:
```js
map.setFilter('em-hydrant', ['all', ['==','class','hydrant'], ['==','type','underground']]);
```

### 9.6 Offline (Achtung!)
Der `OfflineManager` lädt die Ressourcen **aller Quellen des Styles** — auch die
der versteckten Overlay-Layer. Eine Offline-Region mit den Standard-Styles zieht
also die `emergency`-Tiles mit (für Hessen z13–17 grob ~60–140 MB; nicht dramatisch,
aber bewusst entscheiden). Zwei Wege, wenn das nicht gewünscht ist:
1. Für Offline-Regionen eine Style-Kopie ohne die `em-*`-Layer + `emergency`-Quelle
   verwenden (Overlay dann nur online), **oder**
2. Overlay im Client zur Laufzeit per `addSource`/`addLayer` einhängen statt es
   aus dem Style zu beziehen — dann ist es für den OfflineManager unsichtbar.
Für den Einsatzfall („Hydrant finden, wenn kein Netz da ist") ist das Mitladen
allerdings eher ein Feature als ein Bug.

### 9.7 Serverseitig (zur Info)
- View `vector_emergency` in `/home/renderaccount/styles/OpenTopoMap/mapnik/tools/vector_views.sql`
  (Punkte + Flächen via `ST_PointOnSurface`), partielle GIST-Indizes auf
  `planet_osm_point`/`-polygon`. Nach jedem `--create`-Reimport neu einspielen.
- Martin-Quelle `emergency` in `/etc/martin/config.yaml` (z13–17), nginx-Location
  in `vectortile.conf`. **Neue Quelle = immer diese 3 Stellen.**
- Icons: `/var/www/vectortile/make_emergency_icons.py` → Sprite `bright-v21`.
- Daten sind **live** aus der gis-DB, wandern also mit dem nächtlichen
  OSM-Update mit — kein Planetiler-Lauf nötig.
- Weitere Fach-Overlays (z. B. THW/Katastrophenschutz, Schulen, Krankenhäuser)
  lassen sich nach genau diesem Muster ergänzen.
