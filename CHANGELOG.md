# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.

Das Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.0.0/),
und dieses Projekt folgt [Semantic Versioning](https://semver.org/lang/de/).

---

## [Unreleased]

---

## [1.7.0] - 2026-10-23

_Hinweis: enthält auch alle Änderungen der nie veröffentlichten 1.6.4.7 (Abschnitte ab „📍 Standort-Triangulation" unten)._

### 📡 Funk-Echo: „Wer funkt mich an?" (V1, #66)
- **Unsichtbare Anfragen sichtbar gemacht:** Wenn eine andere Station gezielt etwas von deinem Node wissen will (NodeInfo, Position, Telemetrie, Traceroute — jeweils mit `want_response` — sowie Admin-Pakete), erkennt der Client das jetzt und zeigt es an. Grundlage: Die Firmware reicht jedes empfangene Paket an den Client weiter — nur ihre eigenen Auto-Antworten nicht, deshalb gibt es ehrlich nur „Anfrage erkannt" (kein erfundenes „beantwortet"). Antworten auf *eigene* Anfragen und mitgehörte Fremd-Pakete werden sauber ausgefiltert (request_id/Adressat), ebenso der Verbindungs-Backlog.
- **Radar in der Statusleiste:** Neues Funkmast-Element (nach dem Vorbild von `img/HF_ICON.svg`) neben Mesh Health — bei einer Anfrage läuft eine farbige Welle nach außen (Farbe = Anfrageart, Traceroute pulst doppelt), dazu ein Zähler-Badge und für ein paar Sekunden ein Flyout wie „🛰️ KELL verfolgt den Funkweg zu uns!". Meldungsstil umschaltbar **Locker/Sachlich** (Einstellungen), das ganze Feature abschaltbar.
- **Journal & Statistik:** Klick öffnet „📡 Wer funkt mich an?" — chronologisches Journal (nach Tagen gruppiert; Anfrage-Bursts derselben Station werden als ×n gebündelt statt 30 Einzelzeilen; Quelle-Badge direkt/n Hops/☁ MQTT; bei Traceroute-Anfragen sogar der Hinweg „via …") und 7-Tage-Statistik (Anfragen/Stationen/Traces, Typ-Balken, „Neugierigster Node"). Persistiert in `telemetry.db` (Tabelle `request_echo`, unterliegt der Telemetrie-Aufbewahrung). Mit Unit-Tests (Klassifizierung + Burst-Aggregation).
- **Telemetrie-Anfragen nach Datengruppe unterschieden:** Die angefragte Variante (Gerätedaten, Umweltdaten, Luftqualität, Strom, Mesh-Statistiken, Host, Gesundheit) wird aus dem Anfrage-Payload gelesen und in Meldung („🔎 FELD fragt nach unseren Umweltdaten."), Journal und Aggregation getrennt geführt — eine Umwelt- und eine Strom-Anfrage derselben Station bleiben zwei Einträge.
- **V2 — Filter, Toasts, Ruhemodus, Kiosk, „Antwort erwartet" & Dashboard-Metrik:**
  - **Filter im Fenster:** Zeitraum (24 h / 7 / 30 Tage — gilt für Journal und Statistik), Anfrageart und Node-Suche (Name/Kurzname/!hex)
  - **Statistik mit Telemetrie-Subcharts:** unter dem Telemetrie-Balken zeigt eine eingerückte Aufschlüsselung, welche Datengruppen gefragt waren (Umwelt, Strom, Gerätedaten …)
  - **„Antwort erwartet" ehrlich aus der lokalen Konfig abgeleitet** und im Journal angezeigt: NodeInfo/Traceroute/Gerätedaten → ✓; Positionsanfragen zeigen die konfigurierte Kanal-Genauigkeit („✓ Antwort erwartet (±2,9 km)" — Datenschutz-Transparenz) bzw. „✕ keine Antwort (keine Position)"; Sensor-Telemetrie „? je nach Sensorik"; Admin „🔐 nur mit Berechtigung". Die tatsächliche Aussendung der Firmware ist für den Client unsichtbar — deshalb bewusst keine „beantwortet"-Behauptung
  - **Journal-Kontextmenü:** Rechtsklick auf einen Eintrag → Node-Info, auf Karte zeigen, DM senden
  - **Optionale Windows-Toasts** bei neuen Anfragen (Einstellungen, gebündelt über die bestehende Toast-Drosselung) und **🔕 Ruhemodus** per Rechtsklick auf das Statusleisten-Radar (Puls/Flyout/Toast aus, Journal läuft weiter)
  - **Kiosk-Modus** kann Funk-Echo jetzt mit sperren
  - **Neue Dashboard-Metrik „📡 Funk-Echo (Anfragen an uns/h)":** wer wie oft anfragt, als Zeitreihe im Telemetrie-Dashboard — inkl. Warn-/Alarm-Zonen
- **V3 — Neugier-Radar, Karten-Integration & Airtime-Quittung:**
  - **📡 Radar-Tab (Retro-Sonar):** Schwarzes Scope mit Phosphor-grünen Entfernungsringen, Fadenkreuz und **rotierendem Sweep** — dein Node als leuchtender Funkmast in der Mitte, drumherum die anfragenden Stationen (je neugieriger, desto näher, größer und heller; log-skaliert, Glow inklusive); Stationen behalten ihren Platz über Aktualisierungen (Winkel aus der Node-ID). Tooltip zeigt Name + Typ-Aufschlüsselung; die Filter (Zeitraum/Typ/Suche) wirken mit. **Trifft live eine Anfrage ein, läuft ein Sonar-Ping wie im Film:** ein Ring breitet sich von der sendenden Station bis zur Mitte aus, wird „reflektiert", läuft zurück — und die Station blitzt auf
  - **Fix:** „📍 Auf Karte zeigen" zentrierte die Karte, wechselte aber nicht auf den Karten-Tab — sah aus, als passiere nichts
  - **Drilldown pro Station:** Klick auf eine Station öffnet unten die Detail-Leiste — stündliche Anfrage-Balken („Um 03:00 kamen 24 Telemetrie-Anfragen") plus Typ-/Datengruppen-Aufschlüsselung; Rechtsklick bietet wie im Journal Node-Info/Karte/DM
  - **🛰️ Anfrageweg auf der Karte:** Traceroute-Anfragen speichern den Hinweg — per Rechtsklick (Journal oder Radar) wird die Relais-Kette vom Anfrager bis zu dir als Traceroute-Layer auf die Karte gezeichnet (Legende, per ✕ entfernbar), die Ansicht springt zum Karten-Tab
  - **Antwort-Airtime-Quittung:** Neue Statistik-Kachel schätzt, wie viel Sendezeit die Auto-Antworten deines Nodes auf diese Anfragen kosten — Semtech-LoRa-Formel mit den Preset-Parametern der Firmware (SF/BW/CR je Modem-Preset, 16-Symbol-Präambel, 16-Byte-OTA-Header), gezählt werden nur sicher erwartete Antworten. Mit Unit-Tests abgesichert. Die experimentelle ACK-Heuristik („Antwort beobachtet") bleibt bewusst draußen — zu unsicher für eine ehrliche Anzeige

### 📊 Telemetrie-Dashboard (Grafana-Runde, #49/#50)
- **Warn- & Alarm-Zonen statt Einzel-Schwellwert:** Pro Widget lassen sich jetzt vier Grenzen setzen — ⚠ Warnung tief/hoch und 🚨 Alarm tief/hoch (leer = aus). Konfiguriert wird im Widget-Editor (✎); das Schwellwert-Feld auf der Widget-Karte ist weg. Bestehende Schwellwerte werden automatisch als „Alarm hoch" übernommen. Windows-Toasts melden jeden **Zonen-Wechsel** mit Schweregrad (🚨/⚠/✅ Entwarnung), weiterhin übergangsbasiert ohne Dauer-Gepiepe. Mit Unit-Tests abgesichert.
- **Gauge & Stat färben nach Zone:** Wert und Bogen zeigen grün/orange/rot je nach Zone; ein äußerer Zonen-Ring am Gauge markiert, wo die Grenzen auf der Skala liegen (Grafana-Stil). Die alte „tief=rot → hoch=grün"-Rampe war für „hoch ist schlecht"-Metriken (z. B. Kanalauslastung) schlicht falsch — ohne konfigurierte Grenzen ist der Gauge jetzt neutral gefärbt. In Linien-/Flächen-/Scatter-/Candlestick-Charts erscheinen die Grenzen als orange/rote Linien plus rote Alarm-Zone, im Balken-/Histogramm-Chart als vertikale Linien.
- **Node-Auswahl im Widget-Editor neu:** Checkbox-Liste statt Klick-Markierung — ausgewählte Nodes bleiben oben angepinnt sichtbar (auch wenn der Suchfilter sie nicht trifft), dazu „Alle/Keine", ein „n ausgewählt"-Zähler und kein stilles Auto-Anwählen des ersten Nodes mehr. Bei Einzel-Node-Widgets verhält sich die Liste wie Radio-Buttons.
- **Zoom aufgeräumt:** Zeit-Charts zoomen nur noch auf der Zeitachse (die Werteachse bleibt fix — kein diagonales Verzerren mehr), Doppelklick setzt die Ansicht zurück (Tooltip erklärt's). Heatmap, Histogramm, Balken und State-Timeline sind nicht mehr zoombar — dort erzeugte der Zoom nur Unsinn wie eine Stunden-Achse von −24 h bis +42 h.
- **Heatmap:** Farbskala folgt jetzt dem tatsächlichen Datenbereich statt der theoretischen Metrik-Spanne (−20…20 dB machten typische SNR-Werte fast einfarbig); die X-Achse zeigt Datumsangaben statt eines Tag-Index.
- **Histogramm:** Klassen über den echten Datenbereich statt der Metrik-Spanne — vorher quetschten sich alle RSSI-Werte in zwei Balken auf einer fast leeren Achse.
- **Candlestick entflackert:** Kerzen werden nach **lokalen** Kalendertagen gebildet (vorher UTC — der Schnitt um 1/2 Uhr nachts ließ Mitternachts-Rauschen die Farbe quasi zufällig rot/grün kippen); Kerzen ohne echte Richtung (Open ≈ Close) sind jetzt neutral grau.
- **⌀ MA-Button nur noch dort, wo er wirkt** (Linie/Fläche) — auf Heatmap & Co. schaltete er bisher eine Einstellung ohne jede sichtbare Wirkung.

### 🛠️ Fernverwaltung
- **Fix:** Das Schließen des Fernverwaltungs-Fensters konnte das Hauptfenster in die Taskleiste minimieren (gleiche Win32-Eigenheit wie zuvor beim Telemetrie-Fenster) — der Owner wird jetzt vor dem Schließen aktiviert. (#53)

### 🗺️ Karte
- **Abdeckungstexte auf DACH korrigiert:** Die In-App-Texte zu Karten-Modi, Tile-Download und T-Deck-Assistent sagten noch „nur Deutschland" — der Meshhessen-Tile-Server liefert aber Deutschland, Österreich und die Schweiz (DACH). Alle betroffenen Strings (de/en) aktualisiert; der veraltete Downloader-Hinweis auf OSM-Ratenlimits wurde durch den tatsächlichen Stand ersetzt (öffentliche OSM-/OpenTopoMap-Server sind für Downloads gesperrt, geladen wird vom Meshhessen- oder eigenen Server).

### 📍 Standort-Triangulation (Easteregg)
- **Standort-Triangulation (Easteregg):** Dreifachklick auf die „Analyse der letzten X Tage"-Zeile im Telemetriefenster blendet den Button „📍 Standort triangulieren" ein. Der schätzt die Position eines Nodes ohne GPS aus seinen HF-Nachbarn: Signal → Distanz über ein Log-Distance-Pfadverlust-Modell (868 MHz, RSSI@1 km ≈ −90 dBm, Exponent 2,5; SNR wird über den Noise Floor des Ankers in Pseudo-RSSI umgerechnet, beim eigenen Node zählt der gemessene RSSI), dann gewichtete Least-Squares-Trilateration. Auf der Karte (Raster + Vektor) erscheinen gestrichelte **Triangulationskreise** um die Anker (inkl. Distanz-Label) und die geschätzte Position — bewusst als grobe Schätzung dargestellt, das Gelände dominiert LoRa-Links. Der Layer hängt in der Traceroute-Legende und ist dort per ✕ entfernbar. Mindestens 2 Anker mit Position und Signalwert nötig; mit Unit-Tests abgesichert.
- **Triangulation: selbstkalibrierendes Distanzmodell.** Das Signal→Distanz-Modell kalibriert sich bei jeder Triangulation automatisch aus den eigenen Mesh-Daten: Alle HF-Links der letzten 30 Tage, bei denen *beide* Enden eine GPS-Position haben, liefern (echte Distanz, Signalwert)-Paare; daraus werden RSSI@1 km und Pfadverlust-Exponent per Regression gefittet (statt der Schreibtischwerte −90 dBm / 2,5). Schutzmechanismen: mindestens 8 Stichproben, ausreichende Distanz-Spreizung, physikalische Plausibilität, Begrenzung auf sinnvolle HF-Bereiche — sonst Fallback aufs Standardmodell. Das verwendete Modell steht im Log.
- **Triangulation: Modell-Wahl vor jeder Schätzung.** Sobald eine Selbstkalibrierung verfügbar ist, fragt der Client, ob das selbstkalibrierte oder das Standardmodell verwendet werden soll — beide Parametersätze werden im Dialog angezeigt. Hintergrund: Die gesammelten Links sind anfangs survivorship-verzerrt (nur funktionierende Links landen in der Datenbank), der Fit kann daher zunächst ungenauer sein als das Standardmodell und wird mit wachsender Datenlage besser.
- **Triangulation: Unschärfe-Region, Alternativposition & ehrlichere Modelle.** (1) Um die geschätzte Position erscheint jetzt eine **Unschärfe-Region** (gefüllter Kreis, ±km im Label), berechnet aus der tatsächlichen Kostenlandschaft — bei einseitiger Anker-Geometrie (z. B. Router ohne Abdeckung nach Norden) wird sie automatisch groß und zeigt ehrlich, wie wenig die Daten die Position festlegen. (2) Hat die Lösung **zwei plausible Regionen** (klassische Spiegel-Mehrdeutigkeit bei einseitigen/kollinearen Ankern), wird die **Alternativposition** als zweiter, halbtransparenter Marker angezeigt. (3) Die Selbstkalibrierung hat ein **Qualitäts-Gate** bekommen: Fits ohne echte Distanz-Korrelation (R² &lt; 0,2) werden verworfen statt absurde Mini-Kreise zu erzeugen; die Rest-Streuung des Fits (σ in dB) wird als **Unschärfe-Band** (zweiter, gepunkteter Kreis) an jedem Anker dargestellt — nie besser als ±50 % Distanz.
- **Triangulation: klare optische Trennung & Modell-Wahl mit Qualitätsangabe.** Ziel-Elemente sind jetzt eindeutig von den HF-Anker-Kreisen unterscheidbar: Anker = dünne gestrichelte/gepunktete Ringe, Unschärfe-Region = **durchgezogene, dickere Umrandung mit Füllung**, geschätzte Position = **großes Dreieck** mit weißem Rand und fetter Beschriftung, Alternativposition = kleineres halbtransparentes Dreieck. Die Modell-Abfrage erscheint wieder **immer**, wenn ein Fit existiert — das R²-Gate verwirft nicht mehr still, sondern der Dialog zeigt Fit-Qualität (R², Streuung σ) samt Empfehlung, und du entscheidest.
- **Triangulation: Vektorkarte zieht optisch gleich.** Auf der Vektorkarte waren alle Kreise durchgezogen und die Schätzung nur ein Punkt — jetzt gelten dort dieselben Stile wie auf der Rasterkarte: Anker-Kreise gestrichelt, Unschärfe-Region gefüllt mit durchgezogener Umrandung (neuer Polygon-Fill-Layer im Vektor-Renderer), Schätzung und Alternative als Dreiecks-Marker (getönter SDF-Pfeil).
- **Triangulation: gemeldete Positions-Unschärfe fließt ein (precision_bits).** Meshtastic-Positionen tragen eine Genauigkeitsangabe (viele Nodes senden bewusst vergröbert, z. B. 13 Bit ≈ ±2,9 km) — der Client erfasst sie jetzt pro Node und nutzt sie dreifach: (1) Anker mit unscharfer Position bekommen im Kostenmodell eine **Dead-Zone** (innerhalb des Positionsfehlers keine Strafe) und weniger Gewicht, ihr Unschärfe-Band auf der Karte wird entsprechend breiter (Label zeigt „±x km"). (2) Meldet der **Ziel-Node selbst** eine (grobe) Position, geht sie als stark gewichteter **Prior** ein — die Funk-Anker verfeinern dann nur noch innerhalb/nahe der gemeldeten Zelle, die als eigener Kreis erscheint. (3) Die **Selbstkalibrierung** verwendet nur noch Links zwischen präzise positionierten Nodes als Ground Truth (vergröberte Positionen verfälschen die Distanz-Stichproben).
- **Triangulation: getrimmter Median statt „letzter Messwert".** Die Signal-Werte pro Link-Richtung sind jetzt robuste Mediane über die Mess-Historie der letzten 30 Tage (5 %-Percentile oben/unten getrimmt) statt des zufällig zuletzt gemessenen Werts: eigener Direktempfang aus allen 0-Hop-Paketen (`packet_rx`, SNR *und* RSSI), Traceroute-/NeighborInfo-Beweise aus einer neuen Beobachtungs-Historie (`rf_link_obs`, füllt sich ab jetzt mit jeder Messung; bis dahin Fallback auf den letzten Wert). Nebenbei behoben: Lieferten mehrere Quellen (z. B. `rx` und `traceroute`) einen SNR für dieselbe Richtung, war bisher **unbestimmt**, welcher Wert in der Nachbar-Tabelle und Triangulation gewann — jetzt gewinnt deterministisch der neueste.
- **Triangulation: robuste Schätzung — ein Ausreißer-Anker kann nicht mehr dominieren.** Zwei Änderungen am Kostenmodell: (1) Residuen werden durch die **eigene 1-σ-Unschärfe der Distanzschätzung** geteilt (aus der dB-Streuung des Modells) — ein 5-km-Kreis darf kilometerweise danebenliegen, ein 1-km-Kreis nicht; vorher zählte jeder km-Fehler gleich. (2) **Huber-Verlust** statt reinem Quadrat: Jenseits von 1,5 σ steigen die Kosten nur noch linear — ein einzelner grob falscher Anker (Modell-Müll, unbekanntes Relay, falsche Position) zieht die Lösung nicht mehr kilometerweit weg, die konsistente Mehrheit gewinnt (mit Test abgesichert). (3) **Evidenzqualität skaliert die σ-Breite**: direkt gemessener RSSI = scharf (×1,0), SNR aus beiden Richtungen = weicher (×1,3), Einzel-Richtungs-SNR = weich (×1,7), plus Aufschlag (×+0,3) wenn der Noise Floor nur der −105-Default war — eine lügende Einzel-SNR-Distanz kann damit die falsche Seite einer Spiegel-Mehrdeutigkeit nicht mehr „bestätigen". Das Log zeigt die Evidenz pro Kreis (`[rssi]`, `[snr2]`, `[snr1+nf?]`). Außerdem: Läuft die Selbstkalibrierung gegen ihre physikalischen Grenzwerte (z. B. Exponent am Anschlag 1,8 — degenerierter Fit mit absurden Mini-Kreisen), wird das im Modell-Dialog als ⚠ ausgewiesen, die Empfehlung ist dann das Standardmodell **und Enter wählt die Empfehlung** (vorher war Ja/gefittet immer der Default-Knopf).
- **Triangulation: Geländemodell (DEM) fließt ein.** Der Client lädt Terrain-RGB-Höhenkacheln vom eigenen Tile-Server (Mapbox-Encoding, SRTM 90 m, z5–10) über die bestehende Vektor-Kachel-Infrastruktur (Cache in `vectortiles/`, funktioniert offline für einmal geladene Gebiete) und dekodiert sie in ein zusammenhängendes Höhen-Grid. Für jede Kandidatenzelle × Anker wird das Geländeprofil abgetastet (4/3-Erdkrümmung, 60 % Fresnel-Zone bei 868 MHz): Steht ein Höhenzug im Weg, wird der Knife-Edge-Mehrverlust über den Pfadverlust-Exponenten in Signal-Distanz umgerechnet — verdeckte Kandidaten passen dann nicht mehr zur gemessenen Distanz und verlieren. Wichtig: Es zählt nur der **Mehrverlust gegenüber einer flachen Strecke** — die typische Bodennähe-Dämpfung steckt bereits im empirischen Distanzmodell und würde sonst doppelt zählen (Deckel 25 dB; SRTM-90-Analyse rechtfertigt nicht mehr). Antennenhöhen: Standard **2,5 m über Grund** (Nodes hängen typisch 2–3 m hoch), pro Node überschreibbar im Experten-Dialog (neue Spalte). Ohne Höhendaten (offline ohne Cache, außerhalb der Abdeckung > 52° N) läuft alles wie bisher flach.
- **Triangulation (Experte): Sendeleistung & Antennengewinn aller beteiligten Nodes fließen ein.** Zweiter Button neben der Triangulation (gleiches Easteregg): Ein Dialog fragt Sendeleistung (dBm, mit Live-Watt-Anzeige) und Antennengewinn (dBi, Standard 1) **für jeden Node in einem Fenster ab — den Ziel-Node und jeden Nachbarn (Anker)**, denn die analysierten Funkstrecken laufen zwischen beiden Enden. Voreingestellt anhand des Hardware-Modells (RAK4631 22 dBm, RAK WisMesh 27 dBm = 0,5 W → überschreibbar auf 30 dBm = 1 W, Heltec V3 21 dBm, Station 30 dBm, …), immer überschreibbar — im schlechtesten Fall kennt man vom Ziel-Node nur das Gerätemodell, und genau dafür reichen die Presets; Eingaben werden pro Node für die Sitzung gemerkt. Die Werte gehen pro Link als **richtungsbewusstes Link-Budget-Delta** zur Modell-Referenz (22 dBm + 1 dBi je Ende) in die Distanzschätzung ein: Pro Richtung zählen Sendeleistung + Antenne des *sendenden* Endes und der Antennengewinn des *empfangenden* Endes. Mit Referenzwerten ist der Experten-Modus identisch zum normalen. Nebenbei präzisiert: Die SNR→Pseudo-RSSI-Umrechnung nutzt jetzt pro Richtung den Noise Floor des tatsächlichen Empfängers. Außerdem pro Zeile: Antennenhöhe über Grund (Standard 2,5 m, für die Gelände-Analyse) und ein Haken **„Pos. exakt"** — ist die Node-Position exakt bekannt, wird die gemeldete Positions-Unschärfe (precision_bits) ignoriert, und über den **📍-Knopf** lässt sich die exakte Position **per Hand markieren/verifizieren**: Es öffnet sich ein kleines Karten-Popup (gleiche Kachel-Quelle/Cache wie die Rasterkarte) mit orangem Marker an der bisher bekannten Position — ein Klick setzt die Koordinaten, Ziehen/Zoomen bleibt normal nutzbar, Abbrechen/Esc verwirft; gewählte Positionen und Haken werden pro Sitzung gemerkt. Das Fenster ist breiter und hat jetzt Spaltenüberschriften.

### ⛰ Gelände (DEM)
- **3D-Gelände auf der Vektorkarte:** Neuer Haken „⛰ 3D-Gelände" im Layer-Popup der Karte. Der Client hängt die Terrain-RGB-Quelle zur Laufzeit als `raster-dem` in MapLibre ein (`setTerrain` + Hillshade-Layer, Strg+Ziehen zum Neigen) — der Karten-Style selbst bleibt unangetastet, alte Clients sehen weiter 2D. Kacheln laufen über den normalen Interceptor (Cache/Offline/Auth wie jede andere Kachel); außerhalb der DEM-Abdeckung (> 52° N) antwortet der Server mit 204 → flach, kein Fehler-Spam. Einstellung wird gespeichert. Sobald die Karte gedreht oder geneigt ist, erscheint ein **Nordpfeil** (Klick = wieder einnorden; bei Nord-oben blendet er sich aus).
- **Höhenprofil pro Link:** Rechtsklick auf einen Nachbarn im Telemetriefenster oder auf einen Hop im Traceroute-Fenster → „⛰ Höhenprofil des Links". Zeigt das SRTM-Geländeprofil (mit 4/3-Erdkrümmung), die Sichtlinie zwischen den Antennen und die 60-%-Fresnel-Zone bei 868 MHz, dazu das Verdikt: Sichtverbindung frei (mit Reserve in m) oder Gelände im Weg (mit Knife-Edge-Zusatzdämpfung in dB und Position der engsten Stelle). Antennenhöhen aus dem Experten-Dialog, sonst 2,5 m.

### 🐛 Behoben / ✨ Verbessert
- **Traceroute: ehrliche MQTT-Kennzeichnung pro Teilstrecke.** Bisher stempelte das Traceroute-Fenster *jeden* Hop mit dem via-MQTT-Flag des Antwort-Pakets — absurd bei Routen, deren Hinweg nachweislich rein über Funk lief (echte per-Hop-SNRs, aber überall MQTT-Badges). Jetzt gilt: Das Kopfzeilen-Badge „via MQTT" sagt nur noch „die *Antwort* hat uns über MQTT erreicht" (Tooltip erklärt das; den Rückweg bestimmt der antwortende Node über sein eigenes OK-to-MQTT-Bit, das können wir nicht steuern). Pro Hop erscheint das MQTT-Badge nur noch, wenn für die Teilstrecke **keine Funk-Messung** existiert — und das heißt neben dem Sentinel −128 auch **SNR genau 0**: Die Firmware hängt bei MQTT-/API-Empfang ungefiltert `rx_snr = 0` an (TraceRouteModule), eine echte 0,0-dB-Messung ist dagegen extrem selten. Gleiche Logik jetzt auch für die Blitz-Zickzack-Segmente auf Raster- und Vektorkarte.
- **rf_links: MQTT-Brücken flogen als falsche „0-dB-Funklinks" in die Datenbank.** Folgefund derselben Analyse: Die Traceroute-Beweiskette übersprang nur Sentinel-SNRs, nicht die 0-Werte gebrückter Segmente — solche Paare haben sich u. U. nie über Funk gehört, standen aber als RF-Nachbarn mit 0 dB in `rf_links` (und damit in Nachbar-Tabelle, Triangulation und Selbstkalibrierung). Jetzt: 0-SNR-Segmente erzeugen keine Traceroute-Links mehr, SNR 0 wird generell als „keine Messung" gespeichert (Link-Beweis bleibt, Wert wird NULL), und eine idempotente Startup-Bereinigung entfernt bereits vergiftete Altdaten (Traceroute-0-Links gelöscht, übrige 0-Werte genullt).
- **Encoding-Altschäden repariert + Canary-Test:** Ein Commit vom Juni (b2c1ada) hatte `MainWindow.xaml.cs`/`Map.cs` einmal durch einen ANSI-Encoder gespeichert — alle Nicht-cp1252-Zeichen wurden still zu `?`: der ✕-Button der Traceroute-Legende, der 📡-Live-Indikator, 🚨-Notruf-/🔔-Update-Texte, 📍-Wegpunkt-Symbole und →-Pfeile. Alle Glyphen aus der Historie wiederhergestellt (der Legenden-Tooltip ist dabei auch lokalisiert). Damit das nicht zum dritten Mal passiert, wacht jetzt ein **Encoding-Canary-Test** über die Quellen: schlägt an bei Mojibake-Sequenzen (kaputt dekodierte Umlaute/Gedankenstriche), bei Fragezeichen-Resten zerstörter Emojis und wenn definierte Schlüssel-Glyphen aus Dateien verschwinden.
- **Heatmap-Overlay auf der Vektorkarte war manchmal hart abgeschnitten** (senkrechte Kante mitten in der Fläche, Konturen/Labels liefen weiter): MapLibres `updateImage` behält die Texturgröße des ersten Bildes — ändert sich die Pixelgröße des Heatmap-Bilds mit der Node-Bounding-Box, wurde das breitere Bild an der alten Kante geclippt. Der Client schickt jetzt die Bildmaße mit und legt die Image-Source bei Größenwechsel gezielt neu an (unterhalb der Konturlinien). Zweite Ursache: Bei aktivem **3D-Gelände** rendert MapLibre Image-Sources pro Gelände-Kachel (Render-to-Texture) und schneidet sie an Kachelgrenzen hart ab — solange die Heatmap angezeigt wird, wird die 3D-Drapierung daher automatisch ausgesetzt (die Hillshade-Optik bleibt) und danach wieder aktiviert.
- **Telemetriefenster-Schließen minimierte manchmal den ganzen Client:** Win32-Eigenheit — wird das aktive „owned" Fenster zerstört, sucht Windows das nächste zu aktivierende Fenster und schickte dabei gelegentlich das Hauptfenster in die Taskleiste (besonders mit besitzerlosen Geschwister-Fenstern wie dem Dashboard). Das Hauptfenster wird jetzt vor dem Schließen aktiviert.
- **Node-Info: „Zuerst/Zuletzt gesehen".** Das Node-Info-Fenster zeigt jetzt, wann ein Node zum ersten und zum letzten Mal gehört wurde — „Zuerst gesehen" kommt aus der Telemetrie-Datenbank (frühestes je empfangenes Paket) und überlebt damit Client-Neustarts.

---

## [1.6.4.6] - 2026-10-19

### 📡 Funk-Analyse: Nachbarn, Overlays, LocalStats, HF-only-Traceroute
- **Nachbar-Ansicht dynamisch (zeitbasiert):** Das Telemetriefenster wechselt automatisch von der alten „gehörte Nodes"-Liste auf die echten HF-Nachbarn — sofort ab 3 belegten Links, bei 1–2 Links sobald die Link-Sammlung insgesamt eine Woche läuft (abgelegene Nodes haben legitim wenige Nachbarn — das ist dann ein Befund, kein Datenmangel). Nur Nodes ganz ohne Link-Beweise bleiben bei der alten Liste. Der Sammlungs-Start wird in der DB markiert.
- **SNR-Farben zeigten Falsches an (rote Nodes in der Liste):** Drei Lücken behoben — (1) der Farbwert `SnrValue` wurde von *jedem* Paket gesetzt, auch Multi-Hop (= SNR des letzten Relays!) und MQTT; jetzt nur noch bei Direktempfang. (2) Die Avatar-Farbe der Listenansicht prüfte die Hop-Zahl nicht (die Kachel-Ansicht tat es). (3) Der SNR-Restore aus der Datenbank beim Start lud Werte ohne Hop-Filter — genau die vermuteten „Alt-Daten". Multi-Hop-/MQTT-Nodes sind jetzt neutral grau.
- **SNR-/RSSI-Zeitreihen generell nur noch aus Direktempfang** (Hop 0): betrifft Dashboard-Charts, Telemetrie-Plots und Heatmaps — vorher mischten sich Relay-SNRs in die Kurven ferner Nodes.
- **Umwelt-Overlay-Karte kann jetzt Funkqualität:** Die Metrik-Auswahl enthält zusätzlich **SNR (Empfang)**, **RSSI (Empfang)** und **Noise Floor** — als interpolierte Fläche im Wetterdienst-Stil über dem Gebiet, aus den Direktempfangs-Daten bzw. den Noise-Floor-Meldungen der Nodes.
- **LocalStats-Telemetrie wird erfasst** (neuere Firmware): Noise Floor, RX gesamt/fehlerhaft/Duplikate, TX gesamt/verworfen, Relay gesendet/abgebrochen, Nodes online/gesamt. Sichtbar im Telemetriefenster (neuer Abschnitt, erscheint sobald der Node meldet — aktiv abfragbar über „Info anfordern → LocalStats"), als Dashboard-Metriken (Noise Floor, RX fehlerhaft, RX Duplikate, TX Relay u. a.) und als Karten-Overlay (Noise Floor).
- **Kanal-Hash für unbenannte Kanäle korrigiert:** Die Firmware hasht bei Kanälen ohne Namen den Preset-Namen („ShortSlow", „LongFast", „LongMod" …) — der Client hashte den Leerstring. Dadurch war das Hash-Byte für jeden Default-benannten Kanal falsch: Der HF-only-Traceroute wurde vom Empfänger verworfen, und die clientseitige Kanal-Entschlüsselung (ChanDec-Fallback) konnte Pakete solcher Kanäle nie per Hash zuordnen. Traceroute-Fenster außerdem verbreitert (Start-Button/Statuszeile waren abgeschnitten).
- **Automatische Telemetrie-Abfragen (Polling-Plan):** Neuer „📥 Abfragen"-Button im Dashboard. Ausgewählte Nodes werden aktiv nach Werten gefragt, die sie nicht von selbst senden (LocalStats, Umwelt-/Geräte-/Luftqualitäts-/Strom-Metriken). Mesh-schonende Limits fest verdrahtet: max. 4 Nodes, LocalStats höchstens 1×/Stunde, andere Werte höchstens alle 30 min, nie mehr als eine Abfrage pro Minute (Scheduler verteilt automatisch). Läuft app-weit, solange verbunden — auch bei geschlossenem Dashboard; Plan in `telemetry_polling.json`, Zeitstempel überleben Neustarts (kein Abfrage-Burst nach Start).
- **Delta-Raten aus LocalStats-Zählern:** Aus den kumulativen Zählern (RX, TX, fehlerhaft, Duplikate, Relay) werden zwischen den Abfragen automatisch Raten in Pakete/h abgeleitet — Reboot-Resets werden erkannt und übersprungen. Als Dashboard-Metriken (RX-Rate, TX-Rate, RX fehlerhaft/h …) und live im Telemetriefenster (≈-Wert neben den Zählern).
- **Traceroute „Nur HF":** Neuer Haken im Traceroute-Fenster. Die Anfrage wird dann als **vorverschlüsseltes Paket** mit gelöschtem OK-to-MQTT-Bit gesendet — die Firmware erzwingt das Bit nur bei Klartext-Paketen von der App, bei vorverschlüsselten lässt sie die Payload unangetastet. MQTT-Gateways (auf öffentlichen Brokern, auch Meshhessen) leiten das Paket dann nicht ins Internet weiter: Die Route läuft ausschließlich über Funk. Relays können sich weiterhin eintragen (Kanal-Schlüssel). Falls das Paket nicht gebaut werden kann (kein Kanal-Schlüssel o. ä.), fällt der Client automatisch auf den normalen Traceroute zurück.

### 📡 Telemetrie: Nachbarn = echte HF-Nachbarn
- **Die „Nachbarn"-Tabelle im Telemetriefenster zeigt jetzt nur noch direkte HF-Verbindungen** des gewählten Nodes — vorher listete sie schlicht alle *gehörten* Absender (inkl. Multi-Hop und MQTT) und schrieb dabei dem Ursprungs-Node die SNR der *letzten Relay-Teilstrecke* zu.
- **Drei Beweisquellen speisen eine neue Link-Datenbank (`rf_links`):** (1) eigener **Direktempfang** (0 Hops, kein MQTT), (2) **Traceroute-Teilstrecken** — benachbarte Nodes im Pfad haben sich per Definition direkt gehört, inkl. der per-Hop-SNR aus `snr_towards`/`snr_back` (MQTT-Brücken-Segmente ohne Funk-SNR werden verworfen), (3) **NeighborInfo-Pakete** (Portnum 71, wurde bisher gar nicht ausgewertet) — ein Node meldet darin selbst, wen er direkt hört. Die Liste füllt sich dadurch mit der Zeit, auch für entfernte Nodes.
- **Beide Richtungen sichtbar:** „Hört mit (SNR)" (der gewählte Node hört den Partner) und „Wird gehört mit (SNR)" (der Partner hört ihn), dazu Quelle(n) und letzter Nachweis; der Zeitraum-Filter des Fensters gilt mit.
- Die Nachbar-LED der Signal-Analyse und die Mesh-Health-Bewertung bleiben bewusst unverändert (basieren weiter auf den Empfangs-Trends).

### 🔔 Toast-Benachrichtigungen überarbeitet
- **Klick auf einen Toast holt die Anwendung in den Vordergrund** — bisher passierte beim Anklicken schlicht nichts (die Aktivierung war nie verdrahtet).
- **Nachrichten-Fluten werden gebündelt:** Trafen viele Nachrichten auf einmal ein (z. B. nachgeladener Chat-Verlauf), erschien pro Nachricht ein eigener Toast, sekundenweise nacheinander, nicht abbrechbar. Jetzt: Nachrichten innerhalb eines kurzen Sammel-Fensters werden zu **einem** Sammel-Toast zusammengefasst („N neue Nachrichten" mit Absender-Liste), und alle Toasts teilen sich einen Tag — ein neuer **ersetzt** den vorherigen im Info-Center statt sich zu stapeln.
- **Nachgeladene/alte Nachrichten toasten gar nicht mehr:** Nachrichten, die älter als ein paar Minuten sind (Backlog-Replay, Store-and-Forward), erzeugen keine Einzel-Benachrichtigungen.

### ✨ Telemetrie-Dashboard — Charts & Bedienung (Ausbau-Paket)

#### Charts
- **Zoom bleibt beim Auto-Refresh erhalten:** Charts lassen sich per Mausrad/Ziehen zoomen und pannen — bisher sprang die Ansicht bei jedem Refresh-Zyklus auf den Gesamtbereich zurück. Die Nutzer-Ansicht wird jetzt über den Refresh hinweg wiederhergestellt (Reset wie in OxyPlot üblich mit Taste „A").
- **Zeitachsen zeigen Lokalzeit statt UTC** — die Timestamps kamen als UTC aus der DB und wurden unkonvertiert geplottet (1–2 h Versatz). Betrifft Linien-, Flächen-, Scatter- und Candlestick-Charts sowie Stat-Zeitstempel und CSV-Export.
- **Tag/Nacht-Schattierung:** Linien-, Flächen- und Scatter-Charts hinterlegen die Nachtstunden (Sonnenuntergang→-aufgang, eigene Position) dezent — nächtliche SNR-/Temperaturmuster sind sofort erkennbar (bei Zeiträumen bis ~1 Monat).
- **Downsampling:** Linien-/Flächen-Serien nutzen OxyPlots Decimator — „Gesamt"-Charts mit zehntausenden Punkten rendern flüssig.
- **Adaptive Zeitachse:** 1 Tag → `HH:mm`, bis 7 Tage → `dd.MM HH:mm`, darüber → `dd.MM` statt immer demselben gequetschten Format.
- **Gleitender Durchschnitt ist jetzt zeitbasiert** (Fenster = 1/24 des sichtbaren Bereichs, 30 min–24 h) statt „letzte 12 Messpunkte", was je nach Messdichte mal Stunden, mal Tage glättete.
- **Leere Charts** zeigen einen „Keine Daten"-Hinweis statt eines nackten Koordinatensystems.
- **Stat-Widget:** zeigt den Zeitstempel des Werts; Werte älter als 24 h werden ausgegraut — ein toter Node sieht nicht mehr kerngesund aus.

#### Bedienung & Fenster
- **Node-Liste: Kontextmenü-Aktionen (Telemetrie, Node-Info, …) schlagen nicht mehr sporadisch fehl.** Zusätzlich zum Rechtsklick-Selektions-Fix (unten): Die Node-Liste sortiert sich bei eintreffenden Updates laufend neu und konnte die Auswahl **zwischen Menü-Öffnen und Klick** wieder verwerfen; der Menüpunkt tat dann still nichts. Der geklickte Node wird jetzt beim Öffnen als Objektreferenz eingefangen (wie in der Kachel-Ansicht) und übersteht Listen-Neuaufbauten.
- **Schwellwert-Alarme als Windows-Toast:** Über-/unterschreitet der letzte Messwert eines Nodes den Widget-Schwellwert, kommt eine Benachrichtigung (nur bei Zustandswechsel, kein Spam beim Öffnen).
- **Dashboard-Export/-Import als JSON** (Toolbar) — Dashboards lassen sich in der Community teilen; beim Import werden Widget-IDs neu vergeben und Namenskonflikte aufgelöst.
- **Vollbild-Modus** per F11 oder ⛶-Button (Esc beendet) — fürs Mesh-Monitoring am Wandmonitor.
- **Fensterposition, -größe und zuletzt aktives Dashboard werden gemerkt** und beim nächsten Öffnen wiederhergestellt.
- **Widget duplizieren** (Kontextmenü) — erspart beim Aufbau ähnlicher Widgets den Dialog.
- **Widget als PNG speichern / Bild kopieren** (Kontextmenü, 2×-Auflösung) — funktioniert für alle Widget-Typen.
- **CSV-Export in zwei Varianten:** „Excel (Semikolon, Dezimal-Komma)" und „international (Komma, Dezimal-Punkt)" — vorher erzeugte der Export auf deutschen Systemen kaputte Spalten (Dezimal-Komma in Komma-CSV); Felder werden jetzt korrekt ge-quotet.
- **Doppelklick auf den Karten-Header** öffnet den Bearbeiten-Dialog.
- **Theme-Wechsel zur Laufzeit:** Wechselt Windows zwischen Hell/Dunkel, färbt sich das offene Dashboard sofort um (vorher bis zum Neuöffnen im alten Look).

### 🐛 Behoben / ✨ Verbessert — Telemetrie-Dashboard (Komplett-Review)
- **Timer-Leck beim Schließen:** Auto-Refresh- und Uhr-Timer liefen nach dem Schließen des Dashboards für die restliche App-Laufzeit weiter (inkl. DB-Abfragen alle 30 s) und hielten das Fenster im Speicher — jedes Öffnen/Schließen stapelte einen weiteren Satz. Beim Schließen werden jetzt alle Timer gestoppt.
- **Timer-Leck beim Auto-Refresh:** Der In-Place-Refresh erzeugte für Uhr-Widgets bei jedem Zyklus einen neuen Sekunden-Timer, ohne den alten zu stoppen (~120 verwaiste Timer/Stunde). Der alte Timer wird jetzt vor dem Ersetzen gestoppt.
- **UI blockiert nicht mehr beim Laden:** Alle Widget-Datenabfragen (SQLite) laufen jetzt in einem Hintergrund-Prefetch; der UI-Aufbau bedient sich aus dem Cache. Vorher fror die Oberfläche beim Öffnen und bei jedem Auto-Refresh spürbar ein, sobald DB und Widget-Anzahl wuchsen. Überholende Refreshes werden über einen Generationszähler verworfen.
- **„Gesamt" bedeutet jetzt überall dasselbe:** Gauge/Stat/Ranking/MultiStat interpretierten Zeitraum 0 still als 7 Tage, der CSV-Export als 30 Tage — jetzt heißt „Gesamt" konsistent „alle Daten" (Heatmap/Stateline als Raster-Widgets: 30/14 Tage, Mesh-Health: 7-Tage-Analysefenster, jeweils dokumentiert).
- **Y-Achsen schneiden keine Ausreißer mehr ab:** Liegen Messwerte oder die Schwellwert-Linie außerhalb des festen Achsenbereichs (z. B. Temperatur über 60 °C), wird die Achse automatisch aufgeweitet statt die Daten unsichtbar abzuschneiden.
- **Heatmap: „keine Daten" ≠ „Minimum":** Leere Zellen wurden bisher auf den Minimalwert gemappt und sahen aus wie echte Messwerte — sie erscheinen jetzt in neutralem Grau außerhalb der Farbskala.
- **Auto-Refresh-Intervall wird gespeichert** (dashboards.json) statt bei jedem Öffnen auf 30 s zurückzuspringen.
- **MultiStat:** doppelte DB-Abfrage pro Node entfernt.
- **CSV-Export:** Widget-Titel mit Sonderzeichen (`:`, `/` …) ließen den Datei-Dialog abstürzen — Dateiname wird jetzt bereinigt.
- **i18n nachgezogen:** Mesh-Health-Detailzeile („Ø Pfad/Routen/Empfang/Kanal"), „Export fehlgeschlagen" und die Einheit „Pakete/h" waren hartkodiert deutsch; das Uhr-Datum folgt jetzt der Systemsprache.
- **Kosmetik:** Der Refresh-Punkt leuchtet, während tatsächlich geladen wird (vorher davor); Charts mit nur einer Serie zeigen keine überflüssige Legende mehr über den Daten.

### 🐛 Behoben / ✨ Verbessert
- **Node-Liste: Rechtsklick-Menü zielt jetzt zuverlässig auf die geklickte Zeile.** WPF-ListViews selektieren beim Rechtsklick nicht — „Node Info" & Co. arbeiteten daher mit einer veralteten oder fehlenden Auswahl und öffneten manchmal gar nichts (oder den falschen Node). Die rechtsgeklickte Zeile wird jetzt beim Öffnen des Menüs selektiert (gleicher Fix wie im Nachrichten-Chat). Alle übrigen Kontextmenüs wurden auditiert: Dasselbe Problem steckte auch in der **Kanal-Liste**, wo „PSK kopieren" still den PSK eines anderen (zuvor selektierten) Kanals kopieren konnte — ebenfalls behoben. Kachel-Ansicht, DM-Sidebar, Karte, Nachrichten-Chat und DM-Fenster waren bereits korrekt.
- **Dashboard: Toolbar-Texte im Dark-Mode lesbar (Issue #47).** Die Buttons „Neu/Umbenennen/Löschen/Aktualisieren" und die Stift-Icons nutzten eigene Templates ohne `Foreground` und fielen damit auf das WPF-Standard-Schwarz zurück — im Dark-Mode unlesbar. Die Styles beziehen Vorder- und Rahmenfarbe jetzt aus theme-abhängigen Ressourcen, die das Fenster beim Start passend zu Hell/Dunkel setzt.
- **Node-Info-Fenster komplett überarbeitet (Issue #46).** Neues Layout mit Avatar in Node-Farbe, Rollen-Badge und Info-Karten (Gerät / Status / Position) statt der alten Label-Tabelle. Zeigt jetzt auch **Rolle**, **Akku** (inkl. 🔌 bei externer Versorgung), **Signal** (SNR/RSSI bzw. Hops/MQTT). Alle Beschriftungen sind lokalisiert (de/en) und theme-fest — vorher waren sie hartkodiert englisch.
- **Info-Tab zeigt die volle Versionsnummer** (z. B. v1.6.4.6) — Titelzeile und Statusbar behalten die Kurzversion (v1.6.4).
- **Node-Rolle wurde intern nie als Infrastruktur erkannt:** Die Rolle wird als C#-Enum-Name gespeichert („RouterLate"), die Prüfungen verglichen aber mit „ROUTER_LATE" — `IsInfrastructure` und die hübschen Rollen-Namen griffen daher für die gespeicherte Schreibweise nicht. Beide Schreibweisen werden jetzt normalisiert (mit Unit-Tests abgesichert). Die Rolle `CLIENT_BASE` wird als „Client Base" angezeigt.

---

## [1.6.4.5] - 2026-10-03

### 🐛 Behoben / ✨ Verbessert
- **Remote Admin: Ablehnungen (NAK) werden jetzt gemeldet statt still in den Timeout zu laufen.** Lehnte der Ziel-Node oder die Route eine Admin-Anfrage ab (z. B. `ADMIN_PUBLIC_KEY_UNAUTHORIZED` = unser Key ist drüben nicht als Admin-Key eingetragen, `PKI_FAILED` = Schlüsselproblem am lokalen Node, `MAX_RETRANSMIT` = Paket kam über Funk nicht durch), wurde der Routing-NAK bisher nur ins Log geschrieben — das Fenster zeigte nach 30 s kommentarlos „Timeout". Jetzt bricht die Anfrage sofort ab und das Fenster nennt den konkreten Fehlergrund samt Hinweis, was zu prüfen ist.
- **Remote Admin: Pakete werden explizit als PKI markiert (Android-Parität).** Wenn der Public Key des Ziel-Nodes bekannt ist, wird `pki_encrypted` + Key gesetzt — wie in der Android-App. Vorteil: Kann die Firmware nicht per PKI verschlüsseln (Key fehlt in der NodeDB des lokalen Nodes oder stimmt nicht überein), gibt sie sofort einen sichtbaren NAK zurück, statt das Paket still kanalverschlüsselt zu senden, das der Ziel-Node dann kommentarlos verwirft.
- **Remote Admin: funktionierte nur zu direkt erreichbaren Nodes (hop_limit=0).** Die Remote-Admin-Pakete (Requests **und** Writes) wurden ohne `hop_limit` und ohne `want_ack` gesendet. Die Firmware füllt das Standard-Hop-Limit bei Paketen von der App aber **nur** auf, wenn `want_ack` gesetzt ist (`Router.cpp`, `sendLocal`) — die Pakete gingen also mit **0 Hops** auf die Luft und wurden nie weitergeleitet. Folge: Remote Admin klappte nur bei Nodes in direkter Funkreichweite, über Multi-Hop- oder MQTT-Strecken lief jede Anfrage in den Timeout (die Android-App setzt `hop_limit`/`want_ack` explizit und war deshalb nicht betroffen). Der Client sendet Admin-Pakete jetzt wie alle anderen Remote-Pakete mit `hop_limit=7`, `want_ack=true` und Priorität *Reliable* (inkl. Firmware-Retransmits auf verlustreichen Strecken).

---

## [1.6.4.3] - 2026-09-18

### 🐛 Behoben / ✨ Verbessert
- **Mesh-Hessen-Button: reboot-sicher, gestaged, mit Rücklesung + automatischem Nachziehen.** Der Button schreibt das Profil jetzt in einer **einzigen Edit-Transaktion** (`BeginEditSettings`/`CommitEditSettings`), sodass das Gerät nur **einmal am Ende** neu startet — vorher rebootete es schon nach dem Modem-Preset/Region-Wechsel, wodurch der anschließende Kanal-Write verloren ging. Reihenfolge: **1.** Modem-Preset ShortSlow + Region EU868, **2.** Kanal „Mesh Hessen" (Uplink+Downlink), **3.** MQTT + Hop-Limit 7. Die LoRa-Config wird dabei **zweimal** geschrieben (vor und nach dem Kanal): Der erste Write löst die Firmware-Coercion aus (EU868 mit Duty-Cycle → `ignore_mqtt` wird zwangsweise `true`), der zweite Write lässt die Region unverändert → keine Coercion → `ok_to_mqtt=1` / `ignore_mqtt=0` bleiben stehen (Fund aus `ignore-mqtt-eu868-coercion.md`, jetzt auch im Full-Client). **Vorher wurden `ok_to_mqtt`/`ignore_mqtt` gar nicht gesetzt.** Nach dem Commit wartet der Client reboot-sicher auf die automatische Neuverbindung und **liest alle Werte zurück** (Region/Preset/Hop/MQTT/Kanal). Fehlt danach noch etwas, wird es in einem **zweiten Durchlauf automatisch nachgezogen**; das Endergebnis wird mit ✓/✗ angezeigt. Der Button ist außerdem nutzbar, solange das Profil noch nicht vollständig gesetzt ist (z. B. zum Nachbessern der MQTT-Werte).

---

## [1.6.4.2] - 2026-09-17

### 🐛 Behoben
- **Virtueller Node: Verbindung mit der Meshtastic-Android-App stabilisiert.** Die App nutzt einen **zweistufigen** `want_config`-Handshake mit zwei festen Spezial-Nonces: `69420` = „nur Config" (my_info, Metadata, Config, ModuleConfig, Kanäle) und `69421` = „nur Nodes" (ausschließlich NodeInfos). Der vNode hat bisher **beide** Anfragen mit dem vollständigen Dump inkl. `my_info` beantwortet. Dadurch setzte Androids `handleMyInfo` in Stufe 2 den Handshake-Zustand zurück, das `config_complete` von Stufe 2 wurde verworfen, die App wurde nie „verbunden" und lief in eine Reconnect-Schleife (2 Replays pro Versuch, 4 Versuche, dann Abbruch). Der vNode **verzweigt jetzt nach Nonce**: auf `69420` nur die Config (ohne Nodes), auf `69421` nur die Nodes (ohne my_info/Config/Kanäle); jede andere Nonce erhält weiterhin den vollständigen Einstufen-Dump. Zusätzlich loggt der vNode jetzt Nonce und Stufe pro Replay.

---

## [1.6.4] - 2026-09-10

### ✨ Hinzugefügt

#### 💬 Nachrichten-Übersicht mit Seitenleiste (WhatsApp-Stil)
- Neue **Seitenleiste** im Nachrichten-Tab: links die Auswahl (Kanäle **und** Direktnachrichten), rechts der Chat. Das bewährte **Kontextmenü** und die farbigen Bubbles bleiben unverändert — die **Node-Farbe** wird weiterhin je Bubble berücksichtigt.
- **Ungelesen-Badge** (grüne Blase mit Zähler) an Kanälen und DMs, die nicht gerade geöffnet sind.

#### ✅ Zustellstatus & erneutes Senden
- Gesendete Nachrichten zeigen jetzt **Statushaken**: … (wird gesendet) → ✓ (gesendet) → ✓✓ (zugestellt, grün) bzw. ⚠ (fehlgeschlagen, rot). Basiert auf `want_ack` und der Routing-Bestätigung der Firmware (Android-Parität).
- **Erneut-senden-Button** (↻) an fehlgeschlagenen Nachrichten.

#### 🔔 Direktnachrichten wahlweise inline oder im Fenster
- In den Einstellungen umschaltbar: DMs **wie bei WhatsApp direkt im Client** (inline in der Übersicht) **oder** im separaten Fenster (Standard bleibt Fenster).

#### 🔔 Toast-Benachrichtigungen
- Optionale **Windows-Toast-Benachrichtigungen** bei neuen Nachrichten (in den Einstellungen aktivierbar), nur wenn das Fenster nicht im Vordergrund ist.

#### 🚀 Verbindung
- **BLE-Durchsatz optimiert**: bevorzugte Verbindungsparameter (`ThroughputOptimized`) werden angefordert — spürbar schnellerer Datenaustausch über Bluetooth.
- **BLE-Reconnect** stabilisiert: GATT-Session/-Service und Verbindungsparameter werden beim Trennen sauber freigegeben, sodass ein erneutes Verbinden zuverlässig funktioniert.

> ℹ️ Diese Version hebt das Windows-SDK-Ziel auf `10.0.22621` an (für die BLE-Durchsatz-API). Mindest-Windows-Version bleibt unverändert.

### 🐛 Behoben
- **Toast-Spam bei verschlüsselten Nachrichten**: Für unentschlüsselbare Nachrichten (Platzhalter „Verschlüsselte Nachricht – PSK erforderlich") wird **kein** Toast mehr angezeigt.
- **„DM senden" springt jetzt in den Chat**: Der „DM senden"-Button an der Node-Liste respektiert nun den Inline/Fenster-Schalter — im Inline-Modus wird direkt in den Nachrichten-Tab gesprungen statt zusätzlich das DM-Fenster zu öffnen; im Fenster-Modus wird der passende Chat direkt geöffnet und aktiviert.
- **„Info anfordern" im Chat**: Das Nachrichten-Kontextmenü (Rechtsklick auf eine Nachricht) hat jetzt das vollständige **„Info anfordern"**-Untermenü (Nutzer-Info, Position, Geräte-/Umwelt-/Luftqualitäts-/Power-Metriken, LocalStats, HostMetrics, PaxCounter) — zielt auf den Absender. Funktioniert auch bei **noch unbekannten Nodes** (nur die Node-ID ist nötig — genau so lernt man einen unbekannten Node kennen). Zusätzlich hat die **Chatliste selbst** (Direktnachrichten-Einträge) jetzt ein Rechtsklick-Menü mit „Node-Info", „Auf Karte zeigen" und „Info anfordern".
- **Kanalauswahl-Dropdown neben dem Eingabefeld entfernt**: Der Sende-Kanal folgt jetzt der Auswahl in der Seitenleiste — das separate Dropdown war doppelt und ist weg. Mehr Platz fürs Eingabefeld.
- **Chat startet unten (neueste Nachricht) statt oben**: Beim Start bzw. Kanal-/DM-Wechsel wird jetzt zuverlässig ans Ende gescrollt. Zuvor landete man im Back-Scroll ganz oben, weil das Nachladen älterer Nachrichten schon beim ersten Layout-Durchlauf (Scroll-Position 0) auslöste; das ist jetzt bis nach dem ersten Scroll-to-Bottom gesperrt.
- **Chatliste und angezeigte Nachrichten stimmen beim Start überein**: Beim Start war in der Seitenleiste zwar ein Kanal (z. B. „short slow") ausgewählt, angezeigt wurden aber die Nachrichten eines anderen Kanals — erst ein erneuter Klick schaltete um. Ursache: der versteckte alte Kanal-Filter (ComboBox) überschrieb beim Befüllen die Auswahl der Seitenleiste mit „Alle Kanäle". Die Seitenleiste ist jetzt alleinige Filterquelle; der versteckte ComboBox kann sie nicht mehr überschreiben.
- **Serielle Verbindung hängt nicht mehr beim Trennen**: Verband man sich versehentlich mit einem COM-Port, der kein Meshtastic-Node ist, und trennte sofort, konnte der Client einfrieren. Ursache: unendlicher Write-Timeout ließ Schreibvorgänge auf dem toten Port ewig blockieren, wodurch `SerialPort.Close()` beim Trennen deadlockte. Jetzt: finiter Write-Timeout (2 s), das Schließen läuft zeitlich begrenzt auf einem eigenen Thread, der Trenn-Vorgang ist reentranz-sicher, die Config-Wartephase bricht beim Trennen sofort ab, und die Oberfläche gibt nach spätestens 8 s garantiert wieder frei.

---

## [1.6.3] - 2026-08-31

### ✨ Hinzugefügt

#### 🌡️ Umweltdaten auf der Karte
- Neues, in den Einstellungen aktivierbares Feature: Nodes, die Umwelt-Telemetrie liefern, zeigen ihre Messwerte direkt auf der Karte. Funktioniert für **Raster- und Vektor-Karte**.
- **Messwert-Boxen** unter dem jeweiligen Node-Marker (leicht gelbe Box mit themenabhängigem Rand – weiß bei Nacht, schwarz bei Tag) mit allen gemeldeten Werten plus Datum/Uhrzeit der Messung.
- **Heatmap / interpolierte Wertefläche** (Vektor **und** Raster-Karte) für die gewählte Metrik — im **Wetterdienst-Stil**: die Farbe entspricht dem **tatsächlichen Messwert** auf einer kalibrierten, saturierten Skala (nicht der Punktdichte), mit **Legende** am Kartenrand. Zwischen den Sensoren wird per IDW (inverse Distanzgewichtung, ~30 km Reichweite) interpoliert; jenseits der Reichweite bleibt die Fläche transparent. Weiche, ineinander verlaufende Kanten (bilinear hochgerechnetes Bild) plus **Isobaren** an den Bandgrenzen und einer **Außenumrandung** des Gebiets. Ältere Messwerte zählen weniger (exponentieller Zeit-Abfall), veraltete Sensoren verlieren an Gewicht. Funktioniert für **alle Metriken** (Temperatur, IAQ, Feuchte, …). Die Karte bleibt darunter sichtbar.
- **Voller Sensorsatz**: Temperatur, Feuchte, Luftdruck, IAQ, Gas-Widerstand, Beleuchtung/Weißlicht/UV, Wind (Geschw./Richtung/Böe), Regen (1 h/24 h), Bodenfeuchte/-temperatur, Strahlung, Distanz, Gewicht. Die Telemetrie-DB wird dafür automatisch migriert (bisher wurden nur Temperatur/Feuchte/Luftdruck/IAQ gespeichert).
- **Einzelne Nodes abwählbar** (falls einer Unsinn misst): über den 🌡️-Button an der Karte, wirkt auf Boxen **und** Heatmap.
- Steuerung komplett über den neuen 🌡️-Button (Box-Modus, Heatmap an/aus, Metrik-Auswahl, Node-Liste); Einstellungen bleiben erhalten.
- **Box-Modus konfigurierbar**: Aus / Immer sichtbar / Beim Überfahren (Hover). Nodes, die Umweltdaten liefern, sind am **grünen Marker-Ring** erkennbar.
- **Abgestufte Legende** (z. B. 5-°C-Schritte bei Temperatur) mit diskreten Farbbändern statt weichem Verlauf — die Band-Grenzen sind als sichtbare Kontur in der Fläche erkennbar.
- **Z-Reihenfolge** korrigiert: eigener Standort liegt über den Node-Markern, Messwert-Boxen über den Markern.

---

## [1.6.2.5] - 2026-08-30

### 🐛 Behoben

#### 🗺️ Tile-Download ignorierte den Karten-Modus (nutzte immer die Tile-Server-URL)
- Beim Offline-Laden von Kacheln wurde **immer** die im Feld „Tile-Server URL" stehende Adresse verwendet — unabhängig vom gewählten Karten-Modus und der Quell-Auswahl (osm/topo/dark). In Meshhessen-/Offline-Modus lud der Download so von der Custom-URL statt vom quellenrichtigen offiziellen Server.
- Jetzt gilt: Die Custom-URL ist **nur** im Modus „Eigener Tile-Server" (`online-custom`) aktiv — dort weiterhin auch der ungespeicherte Wert aus dem Textfeld, damit man ohne Speichern laden kann. In allen anderen Modi nutzt der Download den zum Quell-Dropdown passenden Server (`online-own`/offline → offizielle Meshhessen-Server, `online-osm` → public OSM, für Bulk ohnehin gesperrt). Damit sind Download und Kartenabfrage konsistent.

---

## [1.6.2.4] - 2026-08-22

### 🐛 Behoben

#### 🔢 Kanal-Anzeige: Backlog zeigte „Kanal N" statt Kanalname
- Nachrichten aus dem Backlog (DB) zeigten teils „Kanal 0"/„Kanal 1" statt „Mesh Hessen" o.ä., während Live-Nachrichten den Namen zeigten. Ursache: der Backlog nutzt den **gespeicherten** Kanalnamen — der stand als „Kanal N" fest, wenn die Nachricht empfangen/gespeichert wurde, **bevor** die Kanäle vom Gerät kamen. Jetzt wird `ChannelName` einheitlich aus dem **aktuellen Kanal-Index** aufgelöst (observable) und beim Eintreffen von Kanälen sowie nach dem Backlog-Laden neu berechnet.

#### ↩️ Reply wechselt jetzt auf den Kanal der Nachricht
- „Antworten" auf eine Kanalnachricht schaltete den aktiven Kanal nicht mehr um — die Antwort ging auf dem gerade gewählten Kanal raus. Jetzt wird beim Reply der aktive Kanal auf den umgestellt, auf dem die Nachricht empfangen wurde (Live **und** Backlog, da Live-Nachrichten jetzt auch den `ChannelIndex` tragen).

#### 🖱️ Debug-Log Auto-Scroll sprang nach oben
- Bei aktivem Auto-Scroll sprang das Debug-Log nach oben statt dem Ende zu folgen: Ab 10000 Zeilen wurde `DebugLogTextBox.Text` neu gesetzt (Trim), was die Scroll-Position auf den Anfang zurücksetzt — und das passierte **nach** `ScrollToEnd()`. Bei viel Traffic (ständiges Trimmen) wurde die Ansicht so bei jeder Zeile nach oben gerissen. Jetzt wird erst getrimmt, dann als letzte Aktion ans Ende gescrollt.

---

## [1.6.2.3] - 2026-08-16

### ✨ Hinzugefügt / 🔧 Geändert

#### 🗄️ NodeDB-Reset: Abfrage-Dialog mit Optionen
- Der Button „NodeDB zurücksetzen" (Geräte-Einstellungen **und** Remote-Admin) öffnet jetzt einen Dialog mit zwei Häkchen statt einer simplen Ja/Nein-Abfrage:
  - **Favoriten ebenfalls löschen** — steuert das `nodedb_reset`-Bool: Standard behält Favoriten (`true`), angehakt löscht sie (`false`). Firmware-Abgleich ergab: der Bool-Wert ist genau `keepFavorites` in `resetNodes()`. Hinweis im Dialog: Router/Client-Base behalten Favoriten firmwareseitig immer.
  - **Interne Node-DB dieses Clients ebenfalls zurücksetzen** — leert zusätzlich die lokale Node-Liste (In-Memory) + Karten-Pins; Nachrichten-/Telemetrie-History bleibt.
- **Korrektur der Beschreibungen:** Texte sagten „alle bekannten Nodes werden vergessen", obwohl Favoriten erhalten blieben — jetzt korrekt formuliert. Der Geräte-Reset löst wie zuvor einen **Reboot** aus (kein Shutdown).
- Der hartcodierte Dialogtext wurde durch i18n-Strings (DE/EN) ersetzt.

### 🐛 Behoben

#### 🗺️ „Auf Karte zeigen" zentrierte die Vector-Map nicht
- Bei aktiver **Vector-Map** (MapLibre/WebView2) öffnete „Auf Karte zeigen" zwar die Karte, sprang aber **nicht** auf die Node-Position — die Zentrierung lief nur über die Mapsui-**Rastermap**. Jetzt zentriert ein gemeinsamer Helfer (`CenterMapOnNode`) **beide** Karten (Raster via `CenterOnAndZoomTo`, Vector via `setCenter`). Betrifft alle drei Einstiege (Node-Kontextmenü, Nachrichten-Kontextmenü, Alert-Button). Öffnet der Klick die Vector-Map erstmalig, wird das Ziel gepuffert und nach dem Laden angewandt.

#### 🔔 Alert Bell: ASCII-BEL (0x07) wird wieder gesendet
- Der Alert-Bell-Button hat den **Hardware-Alert nie ausgelöst**: gesendet wurde nur das 🔔-Emoji, die Firmware triggert aber ausschließlich über das **ASCII-Steuerzeichen `0x07`** (`ExternalNotificationModule`: `ASCII_BELL = 0x07`, scannt den Payload). Zusätzlich war im Kanal-Fenster das Emoji durch eine Nicht-UTF-8-Speicherung zu `??` zerstört.
- Jetzt senden Kanal **und** DM `0x07` + `🔔` (zentral in `EmojiPalette.AlertPrefix`, codepunkt-/char-basiert → korruptionssicher). Empfangserkennung und Anzeige-Stripping nutzen dieselben Konstanten. Abgesichert durch 4 neue Tests.
- Zusätzlich: Der Kanal-Alert-Bell tauchte im eigenen Chat gar nicht auf — er hat die gesendete Nachricht (anders als der normale Sendepfad) nie lokal angezeigt. Jetzt erscheint sie als eigene Nachricht mit Glocken-Markierung (0x07 für die Anzeige entfernt, 🔔 bleibt).
- Der sofortige client-seitige PKI-Entschlüsselungsversuch war an `packet.PkiEncrypted` gekoppelt. Die Firmware setzt dieses Flag aber auf `false`, sobald der **eigene** Node nicht entschlüsseln konnte (Router.cpp:814) — z.B. nach einem **NodeDB-Reset**, wenn dessen nodedb den Sender-Key nicht mehr hat. Dadurch wurden PKI-DMs gepuffert und nicht entschlüsselt, **obwohl der Client den Key besitzt**.
- Jetzt versucht der Client PKI-Entschlüsselung für **jede DM an uns**, sobald Private- und Sender-Key vorliegen — unabhängig vom Flag (sicher, da AES-CCM tag-verifiziert ist). Telemetrie/DMs von Nodes, deren Key wir kennen (der Node aber nach Reset nicht), gehen so sofort auf statt im Puffer zu landen.

#### 📨 Request-Antworten werden nach Portnum verarbeitet (nicht mehr als „verschlüsselte DM")
- Antworten auf `want_response`-Anfragen (UserInfo/Position/Telemetrie/PaxCounter) kommen teils verschlüsselt zurück: **Telemetrie/Pax** PKI-verschlüsselt (der Node hat unseren Key aus der Anfrage, uns fehlt seiner → Henne-Ei), **NodeInfo/Position** kanal-verschlüsselt (Firmware schließt diese Portnums von PKI aus, Router.cpp:1053). Konnte unser Node sie nicht entschlüsseln, landeten sie pauschal als „[Encrypted message]" im Chat und wurden beim Entschlüsseln nur akzeptiert, wenn es Text war.
- Jetzt: ein gepuffertes Paket wird nach Entschlüsselung **nach Portnum geroutet** (`DispatchLateDecrypted` → `RouteDecodedData`) — NodeInfo/Position/Telemetrie/… gehen in die normale Verarbeitung (Node/Key/Telemetrie werden übernommen), nur echter Text (Portnum 1) erscheint als DM.
- **Zusätzlich:** Kommt eine verschlüsselte Antwort kurz nach einer **expliziten** Info-Anfrage an denselben Node (≤30 s), wird sie als Request-Antwort erkannt, gepuffert und **nicht** mehr als Chat-Platzhalter angezeigt (und löst keine weitere NodeInfo-Anfrage aus). Hinweis: `pki=False` im Log ist irreführend — die Firmware setzt das Flag zurück, sobald der eigene Node nicht entschlüsseln konnte (Router.cpp:814).

#### 📏 Node-Namen-Spalte folgt jetzt der Spaltenbreite
- Die Namensspalte in der Node-Tabelle war hart auf 155 px gedeckelt (`MaxWidth`) und saß in einem `StackPanel` — beim Verbreitern der Spalte wurde der Name trotzdem abgeschnitten. Das Cell-Template nutzt jetzt ein `Grid` (Auto + `*`) ohne `MaxWidth`, und die Zeilen strecken sich auf die Spaltenbreite (`HorizontalContentAlignment=Stretch`). Der Name folgt jetzt der Spaltenbreite (Ellipsis) und zeigt per Tooltip den vollen Namen.

---

## [1.6.2.1] - 2026-08-13

### ✨ Hinzugefügt

#### 🌈 Bunte Emoji in Node-Namen und Chat
- Emoji in Node-Namen werden jetzt **farbig** dargestellt — in der Node-Kachelansicht, der Node-Tabelle, der Kanal-Tabelle sowie als Absendername im Kanal-Chat und in DM-Blasen (via `Emoji.Wpf.TextBlock` statt einfachem `TextBlock`).

### 🐛 Behoben

#### 😀 Emoji-Picker zeigte „??" statt Emojis
- Beim Rechtsklick → Reagieren erschienen im Emoji-Picker nur noch „??"-Kästchen. Ursache war eine Nicht-UTF-8-Speicherung, die die Emoji-Literale im Quelltext zerstörte. Die Emoji-Liste liegt jetzt zentral in `Helpers/EmojiPalette.cs` und wird aus **Unicode-Codepoints** aufgebaut (reiner ASCII-Quelltext) — sie kann so nicht mehr kaputtgehen. Genutzt vom Picker im Kanal-Chat **und** im DM-Fenster.

---

## [1.6.2] - 2026-08-11

### 🐛 Behoben

#### 🪪 DM-Absendername übersteht Neustart
- Wurde ein DM-Absender nach Empfang auf den echten Node-Namen aufgelöst, stand nach einem Neustart wieder nur die Node-ID da: Die Auflösung landete nur im Speicher, nicht in der DB. Beim Laden der DM-History wird der Absender jetzt aus der **aktuellen Node-Liste** aufgelöst (der DB-Name ist nur Fallback) — Absender **und** Tab-Name stimmen sofort.

#### 🖱️ DM-Fenster: Scroll-Position
- Beim **Tab-Wechsel** springt die Nachrichtenliste jetzt ans **Ende** (neueste Nachricht) statt oben zu bleiben.
- Auto-Scroll folgt nur noch, wenn die **neueste** Nachricht dazukommt — das Einfügen älterer Nachrichten (History/Sortierung) reißt die Ansicht nicht mehr nach oben.

#### 📡 MQTT-Nachrichten client-seitig per Kanal-PSK entschlüsseln
- **Über MQTT relayte Pakete** (Kanal-Chat **und** schlüssellose DMs) reicht das Gerät **noch verschlüsselt** an den Client weiter — es entschlüsselt nur seinen eigenen Funkverkehr. Der Client entschlüsselt sie jetzt **selbst per Kanal-PSK** (AES-CTR), wenn er einen Kanal mit passendem Hash hat. Zuvor blieben diese Nachrichten als „[Encrypted message]" stehen, obwohl der Kanalschlüssel vorlag.
- Kanal-Hash + PSK-Expansion 1:1 aus der Firmware nachgebaut (`Channels::generateHash`/`getKey`); greift live (exakter Hash-Match) wie beim 🔑-Retry gepufferter/aus der DB restaurierter DMs (dort per Brute-Force über alle Kanäle mit Parse-Validierung, da DMs den Kanal bisher als 0 speicherten). DMs persistieren ab jetzt den echten Kanal-Hash.
- Abgesichert durch 2 neue Tests (AES-CTR-Roundtrip mit passendem Kanal; bleibt verschlüsselt bei unbekanntem Kanal).

#### 🔐 PKC-Entschlüsselung korrekt implementiert (AES-CCM statt CTR)
- **Wurzelursache gefunden:** Unser `PkiDecryptionService` nutzte **AES-CTR** mit fester Null-Nonce und behandelte das ganze Paket als Ciphertext. Meshtastics echte Public-Key-Crypto ist aber **AES-CCM** (L=2, 8-Byte-Auth-Tag, kein AAD) mit einem **übertragenen 4-Byte-`extraNonce`** am Paketende. Damit konnte client-seitige PKI-Entschlüsselung **reale Firmware-Pakete nie** entschlüsseln (Ergebnis war Müll → „parse failed").
- Jetzt exakt nach Firmware (`CryptoEngine::decryptCurve25519` + `aes-ccm`) umgesetzt: `key = SHA256(X25519(privat, öffentlich))`, Blob = `[ciphertext][tag 8B][extraNonce 4B]`, 13-Byte-CCM-Nonce = `[packetId low32][extraNonce][fromNode]`. Ein Treffer ist **tag-verifiziert** — kein Müll/Fehlparsen mehr, nur garantiert korrekter Klartext.
- Betrifft die nachträgliche DM-Entschlüsselung (auch MQTT-relayte PKI-DMs).

#### 🔓 Verschlüsselte MQTT-Direktnachrichten erkennen
- Eine **über MQTT relayte** verschlüsselte DM an uns trägt oft **kein** `pki_encrypted`-Flag — dadurch griff die nachträgliche Entschlüsselung nicht. Jetzt wird **jede verschlüsselte DM an den eigenen Node** als PKI-Kandidat behandelt (Ciphertext puffern + NodeInfo anfordern); Nicht-PKI-Ciphertext scheitert bei der Tag-Prüfung → sicher. Broadcasts bleiben ausgeschlossen. Plus Diagnose-Zeile beim Empfang.

### ✨ Hinzugefügt

#### 🏷️ „Meshtastic abcd" statt „Unknown"
- Ein noch unbekannter Absender heißt im Chat jetzt **„Meshtastic abcd"** (abcd = letzte 4 Hex-Stellen der Node-ID) statt „Unknown"/„!hex" — wie in den Meshtastic-Apps. Gilt für Kanal-Chat, DM-Blasen, DM-Tabtitel und Reaction-Tooltips; sobald die echte NodeInfo eintrifft, wird der Platzhalter durch den echten Namen ersetzt.

#### 🪪 Unbekannte Node in Chat → NodeInfo still anfordern
- Erscheint ein Absender im Chat als „Unknown", fordert der Client dessen NodeInfo jetzt **automatisch und still** an (löst Name auf und – bei PKI – den öffentlichen Schlüssel). DM an uns: sofort. Channel/Broadcast: mit globalem Burst-Schutz (min. 8 s Abstand) plus Rate-Limit pro Node, damit ein voller Channel das Mesh nicht flutet.
- **Nachträgliche Namensauflösung:** Sobald die NodeInfo eintrifft, werden bereits angezeigte „Unknown"-Nachrichten (Kanal **und** DM) **in place** auf den echten Absendernamen aktualisiert – inkl. Kurzname/Farbe und DM-Titel.
- **Auf dem richtigen Kanal (gültiger Index, kein Hash):** Die NodeInfo-Anfrage geht auf dem Kanal raus, auf dem wir den Node zuletzt **entschlüsselt** gehört haben (Index 0–7). Ein nicht entschlüsselbares Paket trägt nur den Kanal-**Hash** (z. B. 117) — den als Index zu senden ließ das eigene Gerät mit `NAK NoChannel` ablehnen. Jetzt wird ein Hash (>7) durch den letzten gültigen Kanal-Index des Nodes ersetzt (sonst Primär 0). So erreicht die Anfrage auch nur über **MQTT** gehörte Nodes.
- **Eigener Public Key wird mitgesendet:** Die Anfrage enthält unsere NodeInfo inkl. Public Key (aus SecurityConfig, sonst aus dem Private Key abgeleitet), damit der Zielnode uns direkt PKI-verschlüsselte DMs schicken kann.
- **Nachvollziehbarkeit:** NodeInfo-Anfragen werden geloggt (`[NodeInfoReq] sent → !… (ch=…, reason: …)`); übersprungene/aufgeschobene Anfragen (Rate-Limit, Burst-Schutz) erscheinen im Debug-Log (`DebugDevice`). Retroaktive Auflösung: `[Chat] Resolved N message(s) from !… → "Name"`.

#### 😀 Reaction-Tooltip zeigt Absender
- Der Tooltip auf einer Reaction listet jetzt **wer** womit reagiert hat (z. B. „👍  Anna, Max") statt nur das Emoji zu wiederholen.

#### 🖱️ Debug-Log: Auto-Scroll abschaltbar
- Checkbox „Auto-Scroll" im Debug-Tab: abgeschaltet bleibt die Ansicht stehen (auch die Zeilen-Begrenzung reißt sie dann nicht mehr nach unten), sodass man in Ruhe hochscrollen und lesen kann.

### 🐛 Behoben

#### 🕒 Chat-Sortierung beim Verbinden/History-Laden
- Beim Connect mischten sich Live-Nachrichten und nachgeladene History durcheinander (neue oben, alte drunter). `MessageItem` hat jetzt einen echten `SortTime`; Kanal **und** DM werden konsequent **chronologisch** einsortiert (neueste unten) — egal in welcher Reihenfolge live/History eintreffen.

---

## [1.6.1.3] - 2026-08-11

### ✨ Hinzugefügt

#### 📍 Positionen unbekannter Nodes puffern statt verwerfen
- Kommt eine Position von einem Node, zu dem wir noch keine NodeInfo haben, wird sie jetzt **zwischengespeichert** (statt „position discarded") — inkl. Persistenz in der Positions-DB. Sobald die NodeInfo eintrifft, wird die gepufferte Position automatisch übernommen und der Node erscheint sofort mit Standort auf der Karte (TTL 6 h, Limit 500 Nodes).

### 🐛 Behoben / 🔍 Diagnose

#### 🔑 „Schlüssel anfordern"-Button klarer
- Der Button erscheint jetzt **nur noch**, wenn wirklich ein Ciphertext zum Entschlüsseln vorliegt (`CanRetryDecrypt`) — bei channel-verschlüsselten oder alt-importierten Nachrichten ohne gespeicherten Ciphertext taucht er nicht mehr auf (er hätte dort ohnehin nichts tun können).
- **Explizites Logging**: Klick schreibt jetzt immer eine `[PKI] Manual key request clicked …`-Zeile (inkl. `hasCipher`), und ein Skip am Guard wird als `[PKI] Retry skipped …` protokolliert — so ist sofort sichtbar, ob der Klick ankam und warum ggf. nichts passiert. Zusätzlich kurze Status-Rückmeldung im DM-Fenster.

---

## [1.6.1.2] - 2026-08-11

### ✨ Hinzugefügt / 🐛 Behoben

#### 🔓 Nachträgliche DM-Entschlüsselung: robuster (Persistenz + manueller Anstoß)
- **Übersteht jetzt Neustarts:** Der Ciphertext einer unentschlüsselbaren PKI-DM wird mit der Nachricht **persistiert** (neue `cipher`-Spalte in der Nachrichten-DB, automatische Migration). Beim nächsten Öffnen des DM-Fensters werden solche Nachrichten automatisch erneut angestoßen — entschlüsselt, sobald der Schlüssel bekannt ist, sonst wird die NodeInfo erneut angefragt.
- **Manueller Anstoß:** Verschlüsselte DM-Sprechblasen haben jetzt einen 🔑-Button, der den Schlüssel des Absenders **sofort** anfordert (ohne Rate-Limit) und die Nachricht bei Erfolg direkt entschlüsselt.
- **Ordering-Robustheit:** Kommt der Schlüssel des Absenders **vor** unserem eigenen Private Key an, wird nach dem Laden des Private Keys automatisch ein Nachzieh-Durchlauf für alle gepufferten DMs mit bereits bekanntem Schlüssel gemacht.
- **Fix:** Der verschlüsselte Platzhalter setzte bisher keine Paket-`Id` — dadurch wäre die PKI-Nonce beim Nachentschlüsseln falsch gewesen; jetzt korrekt gesetzt (wichtig auch für Dedup/Persistenz).
- Abgesichert durch zwei zusätzliche Tests (Sofort-Entschlüsselung bei bekanntem Schlüssel; Schlüssel-Anforderung bei unbekanntem Schlüssel).

---

## [1.6.1.1] - 2026-08-11

### ✨ Hinzugefügt

#### 🔓 Verschlüsselte Direktnachrichten nachträglich entschlüsseln
- Eine per PKI verschlüsselte **Direktnachricht**, für die uns noch der öffentliche Schlüssel des Absenders fehlt (oder er rotiert wurde), wird jetzt erkannt und **gepuffert** statt nur als „[Encrypted message]" abgelegt.
- Der Client **fordert aktiv die NodeInfo des Absenders an** (`NODEINFO_APP` mit `want_response`, rate-limitiert pro Node), um dessen Public Key zu bekommen.
- Sobald der Schlüssel eintrifft, wird die Nachricht **nachträglich entschlüsselt** und die bereits angezeigte Sprechblase **in place** auf den Klartext aktualisiert (`MessageItem.Message`/`IsEncrypted` sind jetzt beobachtbar); der gespeicherte DB-Eintrag wird per `UpdateDmMessage(packetId, …)` korrigiert.
- **Nur für Direktnachrichten** (`packet.To == eigener Node`, `PkiEncrypted`). Kanal-/Broadcast-Nachrichten sind bewusst ausgenommen — PKI ist 1:1.
- Absicherung durch einen End-to-End-Test mit echtem X25519-Roundtrip (RFC-7748-Testvektoren): unentschlüsselbare DM → NodeInfo-Anforderung geprüft → nach Schlüsselempfang entschlüsselt und Event ausgelöst.

---

## [1.6.1.0] - 2026-08-08

### ✨ Hinzugefügt

#### 📝 Text-Formatierung in Nachrichten (Meshtastic-Markup)
- Nachrichten unterstützen jetzt das Meshtastic-Formatierungs-Subset — **im Rendering** für Kanal-Chat *und* DMs (empfangene wie eigene Nachrichten): `**fett**`, `*kursiv*`, `~~durchgestrichen~~`, `` `monospace` `` und `[Linktext](https://…)`. Die Marker-Zeichen sind Teil des gesendeten Textes und zählen weiter zum Längenlimit — nur die Darstellung ändert sich.
- **Format-Leiste am Eingabefeld** (Kanal + DM): Buttons **B** / *I* / ~~S~~ / `</>` / 🔗 umschließen den markierten Text mit den passenden Markern (oder fügen sie am Cursor ein). Respektiert `MaxLength` (kein Überschreiten des Zeichenlimits).
- Parser verschachtelt Formatierungen (z. B. fett + kursiv) und lässt Marker in Monospace-Spannen wörtlich; nackte http(s)-URLs bleiben klickbar.

---

## [1.6.0.1] - 2026-07-31

### 🐛 Behoben

#### 🔀 Virtuelle Node: Node-/Kanalliste erreicht verbundene Apps zuverlässig
- **BLE lieferte an die vNode dauerhaft 0 Kanäle / 0 Nodes.** Der Replay-Cache wurde nur aus live mitlaufenden, *gerahmten* Rohframes befüllt — und diese wurden ausschließlich auf Serial/TCP gefeuert, nie auf BLE. Über BLE blieb der Cache damit für immer leer.
- **Auch auf Serial füllte sich der Cache erst verspätet** (erster Client `0/0`, vollständige `8/80` erst nach einem Watchdog-Recovery-Reconnect): der einmalige Init-Config-Strom wurde verpasst, wenn die vNode nicht exakt währenddessen schon lauschte.
- **Fix:** Der `MeshtasticProtocolService` führt jetzt einen **transportunabhängigen Snapshot** (my_info, metadata, configs, module configs, channels, nodes) über *jedes* geparste `FromRadio` — Serial, BLE **und** TCP, im Init wie bei späteren Live-Updates — und hält ihn für die gesamte Verbindung vor. Die virtuelle Node replayt beim Client-Connect diesen Snapshot, unabhängig davon, **wann** sie aktiviert wurde. Späteres Einschalten funktioniert damit; ist noch kein vollständiger Gerätestand erfasst, kommt ein klarer Hinweis (ggf. neu verbinden).
- BLE feuert nun ebenfalls `RawFrameReceived`, sodass auch der Live-Broadcast (Nachrichten nach dem Connect) an verbundene Apps über BLE funktioniert.
- **Eigener Node im Replay abgesichert:** Liefert das Gerät keinen `NodeInfo`-Eintrag für sich selbst, synthetisiert der Snapshot ihn aus `my_info` + bekannten Geräteinfos. Android braucht den eigenen Node in der DB, um Node-Liste und Senden freizuschalten — greift nur, wenn er tatsächlich fehlt, und wird durch den echten Eintrag ersetzt, sobald er eintrifft (mögliche Ursache für „Nodes (0)/kein Senden" trotz vollem Replay).
- Abgesichert durch 6 neue Tests (Snapshot-Befüllung Serial/TCP **und** BLE, BLE-Frame-Fire, Node-Dedup, Eigen-Node-Synthese vorhanden/fehlend).

---

## [1.6.0] - 2026-07-30

### 🔧 Geändert (intern)

#### 📦 Offizielle Meshtastic-Protobufs statt selbstgebauter
- **Umstieg von den handgeschriebenen Proto-Dateien auf die offiziellen Meshtastic-Protobufs**, eingebunden als **git-Submodule** (`protobufs/`, gepinnt auf `v2.7.26-140-g6ceceae`). Beseitigt die wiederkehrende Fehlerklasse falscher Feldnummern/Typen an der Wurzel und hält uns im Gleichschritt mit der Firmware; Version wird über den Submodule-Commit festgehalten und bewusst gebumpt.
- `Data.portnum` ist jetzt korrekt das `PortNum`-Enum (war `uint32`); Config-/ModuleConfig-Untertypen sind wie im Original verschachtelt (`Config.Types.LoRaConfig` usw.). Aufrufseiten bleiben über globale Alias-Usings (`GlobalUsings.cs`) weitgehend unverändert.
- Absicherung durch die neuen Protokoll-Decode-Tests (Text/Reaktion/NodeInfo/Framing) + CI, die jetzt gegen die offiziellen Protos laufen.

#### 🧪 Tests & CI
- **Neues Testprojekt `MeshhessenClient.Tests`** (xUnit, 35 Tests): Protokoll-Decode über einen Fake-Transport, **Gerätekonfig-Write über den Draht** (LoRa-Config serialisieren → entframen → zurückparsen, prüft die Enum-Werte), Settings-Round-Trip, Tile-Mathematik, Overlay-Registry, Vektor-Cache-Pfade, AppSettings-Defaults.
- **CI führt Tests bei jedem Push/PR aus**; Release-Build hängt per `needs: test` daran (kein Release ohne grüne Tests).

#### 🧱 Aufräumen
- `MainWindow` in partial classes aufgeteilt (Karte, Kiosk, NodeList, NodeCommands); `AppSettings` auf init-Properties umgestellt (behebt u. a. einen latenten „Speichern während des Ladens"-Bug bei der Sprache).

---

## [1.5.14] - 2026-07-15

### ✨ Hinzugefügt

#### 🗺️ Vektorkarten (Vorschau)
- **Neue Kartendarstellung „Vektor"** in den Einstellungen umschaltbar – Raster bleibt Standard und wird voll weiter unterstützt; Beschreibung der Vor-/Nachteile direkt in den Einstellungen
- **Rendering mit MapLibre GL JS** im eingebetteten WebView2: gestochen scharf in jeder Zoomstufe, alle drei Kartenstile (OSM, OpenTopo mit Höhenlinien+Relief, Dark) vom Meshhessen-Vektorserver; Stil-Updates kommen ohne Client-Update an
- **Alle vier Karten-Modi** wie bei Raster: Offline / Meshhessen-Server / eigener Server (Style-URL konfigurierbar) – im OSM-Online-Modus wird automatisch die Rasterkarte verwendet (kein öffentlicher Vektorserver)
- **Automatischer Offline-Cache:** online betrachtete Gebiete werden unter `vectortiles/` gespeichert (Tiles, Styles, Schriften, Symbole, Relief) und sind ohne Internet weiter nutzbar; deutlich kleinere Datenmengen als Raster
- **🚒 Feuerwehr-/Rettungs-Layer** per Checkbox in der Karten-Toolbar zuschaltbar: Hydranten (ab Zoom 15, Überflur/Unterflur unterscheidbar), Wachen, Sirenen, Löschteiche, Saugstellen, Rettungspunkte, Defibrillatoren u. v. m. (ab Zoom 13). Solange ausgeschaltet, wird **kein Byte** dafür geladen. Architektur vorbereitet für weitere Fach-Layer (z. B. THW, Krankenhäuser)
- **Klick auf ein Feuerwehr-Objekt** öffnet ein Detail-Popup: Bauart, Kupplungen, Durchfluss, Druck, Wasserquelle, Betreiber, Standort, Erfassungsdatum u. a. (zweisprachig DE/EN)
- **Voller Karten-Funktionsumfang auch im Vektor-Modus:** Node-Pins (Farben, Notizen, Emoji-Labels), eigener Standort, Waypoints, Traceroutes mit allen Linientypen (Richtungspfeile, MQTT-Blitz-Zickzack, unbekannte Route gestrichelt mit „?", klickbare Segment-Punkte mit SNR-Popup), Nachbar-Linien mit SNR-/Alter-Farbverlauf, Positionsverläufe mit Richtungspfeilen sowie das komplette Rechtsklick-Kontextmenü (Position setzen, Waypoint anlegen, DM/Info/Farbe/Traceroute/Telemetrie je Node)
- **🧭 Legende auf der Vektorkarte:** eigener Legende-Button in der Karten-Toolbar öffnet die bekannte Legende inkl. Nachbar-Linien-Schalter und Traceroute-Liste (WPF-Overlays sind über der Vektorkarte technisch unsichtbar – daher als Popup)
- **Layer-Auswahl:** Zusatz-Layer in den Einstellungen an-/abwählbar und zusätzlich über den 🗂️-Button direkt auf der Karte (Auswahl-Popup); Registry vorbereitet für weitere abonnierbare Layer
- **Vektor-Offlinepaket-Downloader:** Bundesland-Presets oder eigene Bounding-Box, wählbarer Detail-Zoom (12–17), OpenTopo-Extras (Höhenlinien + Relief) und Zusatz-Layer als Opt-in; lädt Styles, Schriften und Symbole automatisch mit; bereits vorhandene Kacheln werden übersprungen (Wiederaufnahme möglich); Abbruch stoppt sauber ohne die Oberfläche zu blockieren
- **Automatischer Rückfall auf Raster** mit Hinweis, falls die WebView2-Runtime fehlt
- **Pflicht-Attribution** „© OpenMapTiles © OpenStreetMap contributors" in der Vektorkarte; neue Lizenz-Einträge im Info-Tab: OpenMapTiles (CC-BY/ODbL), MapLibre GL JS (BSD-3-Clause), Noto Sans (SIL Open Font License 1.1)

---

## [1.5.13] - 2026-06-11

### ✨ Hinzugefügt

#### 🗺️ Direkte Nachbar-Linien auf der Karte
- **Aktivierbar im Legenden-Feld:** zieht Linien von der eigenen Position zu allen Nodes, die wir **direkt über HF empfangen** haben (0 Hops, kein MQTT)
- **Farbverlauf** wählbar nach **SNR** (rot → gelb → grün, −20…+10 dB) oder nach **Alter** der letzten direkten Verbindung (cyan → violett, jetzt…24 h) – echter Gradient statt Stufen
- **Option „Dauerhaft"** zeigt alle je direkt gehörten Nachbarn statt nur der letzten 24 h; historische 0-Hop-Kontakte werden aus der Telemetrie-DB (`packet_rx`, `hop_count=0`) wiederhergestellt
- Dunkler Umriss unter jeder Linie für gute Sichtbarkeit auch auf der Topo-Karte
- Eigener Layer unterhalb der Node-Pins; aktualisiert sich live bei neuen Paketen

#### 🔧 Node-Kachelansicht (Fancy View)
- **Optionale Kachel-Oberfläche** statt Tabelle (Einstellungen → Darstellung): responsive, Spaltenanzahl wächst/schrumpft mit der Fensterbreite
- **Pro Kachel:** ShortName-Badge in Node-Farbe, 🔑/🔓 PKI-Status, voller Name, „vor X min", ⭐-Favoritenstern (klickbar), 📡 Infrastruktur- und ☁ MQTT-Symbol
  - Stromzeile: extern versorgt **oder** Batterie % + Spannung
  - Entfernung + Höhe ü. MSL, Hop-Anzahl, RSSI/SNR mit Farbverlauf + Qualitätslabel
  - Umwelt-Telemetrie (🌡 Temp / 💧 Feuchte / 🌬 Druck), Hardware-Modell · Geräterolle · Node-ID
- **Eigener Node immer ganz oben**, virtualisiertes Pixel-Scrolling auch bei 1000+ Nodes (kein Einfrieren)
- Eigener Node erscheint jetzt zuverlässig in der Liste und ist anheftbar

#### 📥 Informationen anfordern (wie Android-App)
- **Rechtsklick-Untermenü** in Node-Liste, Kachel und Karte: fordert gezielt Daten von einem Node an (`want_response`)
- Typen: Benutzer-Info, Position austauschen, Geräte-/Umwelt-/Luftqualität-/Strom-/Host-Metriken, Signalqualität/Mesh-Statistik, PAX-Zähler
- Proto erweitert um `AirQualityMetrics`, `PowerMetrics`, `LocalStats`, `HealthMetrics`, `HostMetrics`, `Paxcount` + `Telemetry`-oneof

#### 🔧 Erweiterte Node-Listen-Filter
- Filter nach **zuletzt gesehen** (5 Min / 15 Min / 30 Min / 1 Std / 6 Std / 24 Std / 7–60 Tage)
- **MQTT-Nodes ausblenden**, **nur Favoriten**, **SNR-Einfärbung** an/aus
- **Sortier-Dropdown** im Kachelmodus (Name, SNR, Entfernung, Batterie, zuletzt gesehen)

#### 💬 Signalwerte in Nachrichten
- **SNR + RSSI direkt in der Nachrichten-Bubble** bei Direktempfang (0 Hops, kein MQTT) – farbig gemäß Signalqualitäts-Gradient (rot → gelb → grün)

#### 🔒 Kiosk-/Trainingsmodus
- **Für geteilte Stationen** (Vereinslokal, Schulung, Veranstaltung): sperrbare UI als Versehens-Schutz
- **Aktivierung in den Einstellungen** mit Passwort (PBKDF2-Hash, kein Klartext in der INI)
- **Konfigurierbar, was im gesperrten Zustand ausgeblendet wird:** Tabs (Nodes, Kanäle, Einstellungen, Info, Tools, Debug), Node-Konfiguration, Fernverwaltung, Telemetrie-Dashboard, SOS-Button, Meshhessen-Schnellkonfiguration – Nachrichten- und Karten-Tab bleiben immer sichtbar
- **🔒-Schloss in der Fußleiste** zum Sperren/Entsperren; nur sichtbar wenn ein Passwort gesetzt ist; App startet im Kiosk-Modus immer gesperrt
- **VNode-Härtung:** bei aktiver Sperre werden Admin-Befehle von Virtual-Node-Clients erzwungen blockiert
- **Passwort vergessen:** `KioskModeEnabled=False` in `meshhessen-client.ini` setzen (dokumentiert in der README)

### 🐛 Behoben

#### 🧩 Protobuf-Definitionen mit Original abgeglichen
- **`Position.ground_speed` / `ground_track`** lagen auf Feld 14/15 statt **15/16** → GPS-Geschwindigkeit/Kurs wurden falsch dekodiert
- **`Region`-Enum** ab Wert 13 verschoben (UA_868/MY_919/SG_923/LORA_24 falsch) → korrigiert + `EU_433` und vollständige Liste ergänzt; ComboBox-Tags angepasst
- **`MQTTConfig` Feld 11** war `uint32 map_report_precision` statt der verschachtelten `MapReportSettings`-Message → Map-Report-Genauigkeit lesen/schreiben repariert
- **`TelemetryConfig`** Felder 3–9 waren verschoben → Telemetrie-Modul-Konfig (Mess-/Anzeige-Flags, Intervalle) jetzt korrekt
- **`CannedMessageConfig`** Felder verschoben (`inputbroker_pin_a/b` fehlten) → `send_bell` u.a. jetzt auf korrekter Feldnummer
- **`ModemPreset`** um `SHORT_TURBO`, `LONG_TURBO`, `LITE_FAST/SLOW`, `NARROW_FAST/SLOW` (8–13) ergänzt

#### 🔡 Sonstige Fixes
- **Signal-Anzeige:** RSSI/SNR werden nur noch bei direktem Empfang gezeigt (0 Hops, kein MQTT); bei Relay-Nodes zeigt die Spalte die Hop-Anzahl. RSSI/SNR erscheinen jetzt konsistent zusammen
- **Encoding:** ~90 kaputte Umlaute (U+FFFD) in Log-/Dialog-Texten repariert; Logging-Pipeline durchgängig UTF-8
- **DM-Freeze behoben:** Node-Refresh bei Paket-/DM-Verkehr wird jetzt immer gebündelt (700 ms), nicht mehr nur im Kachelmodus → kein UI-Einfrieren beim Schreiben von DMs
- LED-Tooltip „Wetter-Effekt": literaler `\n` durch echten Zeilenumbruch ersetzt
- Karten-Legende „Dauerhaft" jetzt mehrsprachig; Alter-Gradient-Grau kollidiert nicht mehr mit dem Lora-Hop-Grau
- Map-Topbar aufgeräumt (Trenner + deskriptive Button-Namen); PKI-Schlüssel-Spalte verbreitert; Startfenster breiter
- **Tile-Downloader:** Rate-Limit (2 Anfragen/s) für eigene/benutzerdefinierte Tile-Server entfernt; Massen-Downloads von öffentlichen OSM-/OpenTopoMap-Servern werden stattdessen mit klarer Fehlermeldung abgelehnt (Tile-Usage-Policy) – gilt für Tile-Downloader und T-Deck-Assistent
- **Neuer vierter Karten-Modus „Online – eigener Tile-Server":** lädt Kacheln on-demand von den selbst konfigurierten Tile-URLs (mit dauerhaftem lokalem Cache); der Meshhessen-Modus nutzt jetzt immer fest die offiziellen Server. Öffentliche OSM-/OpenTopoMap-URLs werden im Custom-Modus abgelehnt
- **Nachrichten:** Scrollbar überdeckt die Chat-Bubbles nicht mehr (Innenabstand rechts)
- **Kachelmodus:** „Farbe setzen"-Kontextmenü zeigte zwei leere Einträge (fehlende Resource-Keys) → Farbliste an Tabellen-Menü angeglichen (Braun/Pink/Cyan + „Farbe entfernen")
- Einstellungen: Kachelansicht-Option deskriptiver benannt + Performance-Hinweis ergänzt

---

## [1.5.12] - 2026-06-09

### ✨ Hinzugefügt

#### 🏷️ Per-Node-Stationsname
- **Pro verbundenem Node ein eigener Stationsname** – ✏-Button neben dem Verbinden-Button öffnet einen Eingabedialog
- **Auflösungsreihenfolge:** globaler Name (Einstellungen) → node-spezifischer Name → ShortName des Nodes; Label-Farbe zeigt die Quelle (rot/orange/grau)
- Persistenz pro Node in `meshhessen-client.ini` (`NodeStationName_<id>`)

### 🐛 Behoben
- **MQTT-Proxy-Statusleiste:** zeigte dauerhaft „MQTT Proxy gestoppt" auch wenn der Proxy nie gestartet war → Status wird nur noch gemeldet, wenn der Proxy tatsächlich lief

---

## [1.5.11] - 2026-05-24

### ✨ Hinzugefügt

#### 💬 Nachrichten – Kanal-Filter synct Send-Dropdown
- **Kanal filtern** → **Senden-Dropdown** werden jetzt synchronisiert: wählt man im Filter-Dropdown einen bestimmten Kanal, springt der Sende-Kanal automatisch auf denselben Kanal mit; „Alle Kanäle" lässt den Sende-Kanal unverändert

#### 🔧 Remote Admin – Lazy Loading & Tab-Reload
- **Lade-Modus-Dialog** beim Öffnen des Remote-Admin-Fensters: Benutzer wählt ob alle Einstellungen sofort geladen werden sollen (~30–60 s) oder seitenweise
  - **Seitenweise-Modus:** Jeder Reiter wird erst beim ersten Anklicken geladen (kein unnötiger Funkverkehr)
  - Bereits geladene Reiter werden nicht erneut abgerufen
- **Neuer Button „↻ Tab neu laden"** in der Buttonleiste – lädt nur den aktuell sichtbaren Reiter komplett neu (ohne den Rest anzufassen)

#### 🔧 Lokaler Admin – Sequentielles Laden
- **Echtes sequentielles Config-Loading** in der Node-Konfiguration (lokaler Admin):
  - Bisher: alle 17 Konfigurations-Requests feuern und auf Events warten (fire-and-forget)
  - Jetzt: senden → auf Antwort-Event warten → nächste senden; 8 s Timeout pro Konfiguration
  - Verhindert Queue-Overflow auf langsamen Boards (z.B. Heltec) – vorher Abbruch bei 4–5/17
  - Falls eine Konfiguration nicht antwortet (z.B. nicht unterstütztes Modul), wird nach 8 s automatisch weitergemacht; fehlende Configs werden am Ende angezeigt

#### 🖧 Virtual Node (Tools-Tab)
- **Virtual Node TCP-Proxy-Server** – wandelt den Meshhessen Client in einen Meshtastic-kompatiblen TCP-Server um
  - Meshtastic-Apps (Android, iOS, andere Clients) können sich mit dem Client verbinden, als wäre er ein echtes Gerät
  - Konfigurierbar in **Tools → Virtual Node**: Port (Standard: 4404), aktivieren/deaktivieren, Admin-Befehle blockieren (optional)
  - Startet automatisch beim Verbinden mit dem physischen Node (wenn aktiviert); stoppt bei Disconnect
  - **Config-Replay**: Verbindende Apps erhalten sofort MyNodeInfo, alle Kanäle, Gerätekonfig und bekannte Nodes
  - **Bidirektionale Nachrichtenweiterleitung**: In der App geschriebene Nachrichten erscheinen in den verbundenen Apps und umgekehrt (über den physischen Node)
  - **Multi-Client-Support**: Beliebig viele Clients gleichzeitig verbindbar
  - Message-Queue mit 10 ms Delay zwischen Paketen schützt den physischen Node vor Überflutung
  - Status-Anzeige im Tools-Tab: läuft/gestoppt, Client-Anzahl, verbundene IPs
  - Einstellungen werden sofort in INI gespeichert
  - Neue INI-Keys: `VirtualNodeEnabled`, `VirtualNodePort`, `VirtualNodeBlockAdmin`

#### 🔧 T-Deck Karten-Assistent (neuer Tools-Tab)
- **Neuer Reiter „Tools"** im Hauptfenster mit zwei Funktionen:
  - **T-Deck Karten-Assistent** – geführter 6-Schritt-Wizard zur Vorbereitung einer SD-Karte mit Offline-Karten
  - **Tile-Export** – lokale Tiles als ZIP exportieren (je nach Kartentyp oder alle)
- **Schritt 1 – Willkommen:** Erklärung des Workflows, Hinweis dass nur Deutschland abgedeckt ist
- **Schritt 2 – SD-Karte wählen:** Listet nur Wechseldatenträger auf, zeigt Gesamtgröße, freier Speicher und Dateisystem
- **Schritt 3 – Formatierung prüfen:**
  - Prüft ob das Laufwerk exFAT oder FAT32 ist
  - Empfiehlt exFAT mit 4096-Byte-Zuordnungseinheiten (erklärt warum: kleine Tiles ~100 Bytes, große AU = Platzverschwendung)
  - Bietet Formatierung per PowerShell `Format-Volume` mit UAC-Elevation an
  - Doppelte Bestätigungsdialoge vor dem unwiderruflichen Formatieren
  - Auswahl: exFAT 4096 Bytes (Standard/empfohlen) oder FAT32 512 Bytes (Ältere Geräte)
  - Formatierung kann übersprungen werden
- **Schritt 4 – Bereich auswählen** (drei Modi):
  - **Nach Bundesland:** alle 16 Bundesländer als Checkboxen mit Alles/Keine-Schnellauswahl; kombinierte BBox wird berechnet
  - **Ganz Deutschland:** feste BBox N 55.10° S 47.27° W 5.87° E 15.04°
  - **Freestyle:** Mapsui-Kartenfenster, Klicken-und-Ziehen zum Zeichnen eines Rechteck-Auswahlbereichs
- **Schritt 5 – Zoom & Kartentyp:**
  - Maximale Zoom-Stufe: 8 / 10 / 12 / 14 (empfohlen) / 16 / 17 (Maximum)
  - Warnhinweise bei Zoom ≥ 14 und ≥ 16 (Laufzeit, Speicherplatz)
  - Kartentyp: OSM Standard / OSM Dark / OpenTopoMap
  - Live-Schätzung: Tile-Anzahl und Speicherplatzbedarf; Warnung wenn SD-Speicher knapp
- **Schritt 6 – Download & Übertragung:**
  - Zusammenfassung aller Einstellungen
  - **Additiver Transfer:** SD-Tile vorhanden → überspringen; lokal vorhanden → kopieren; sonst → herunterladen vom Tile-Server und gleichzeitig lokal cachen
  - Fortschrittsbalken mit Tile-Zähler und aktueller Zoom/X/Y-Angabe
  - Abbrechen jederzeit möglich
- **SD-Karten-Verzeichnisstruktur:** `{Laufwerk}:\maps\OSM\{z}\{x}\{y}.png` (bzw. `OpenTopo`, `OSMDark`)
- **Tile-Export-Dialog:** Exportiert lokale Tiles (alle oder gefiltert nach Kartentyp) als ZIP
- Vollständig mehrsprachig (Deutsch / Englisch) über bestehende i18n-Infrastruktur

#### Remote-Verwaltung – Sicherheits-Tab & Favoriten
- **Neuer „Sicherheit"-Reiter** in der Fernverwaltung (Remote Admin) für `Config.SecurityConfig`
  - **Öffentlicher Schlüssel** (Public Key) read-only als Base64 (wie Meshtastic-App)
  - **Admin-Schlüssel 1–3** (Base64) editierbar — autorisierte Admin-Geräte
  - **Flags:** Legacy Admin Channel, Managed Mode, Serial Console, Debug Log API
  - Wird automatisch beim Öffnen mit geladen (GetConfigRequest = SecurityConfig)
  - Wird mit „Speichern" an das Remote-Gerät übertragen
- **Favoriten auf Remote-Knoten verwalten** – neuer Abschnitt im Steuerung-Reiter:
  - ComboBox mit allen bekannten Knoten
  - „Als Favorit setzen" / „Favorit entfernen" – sendet `set_favorite_node` / `remove_favorite_node` an den Remote-Node
  - Bestätigung per MessageBox nach erfolgtem Senden

#### Telemetrie-Dashboard
- **Dashboard-Button in der Hauptleiste** (📊 Toolbar) – Dashboard direkt ohne Umweg über das Telemetrie-Kontextmenü öffnen
- **Dashboard ist jetzt ein unabhängiges Fenster** – schließt sich nicht mehr mit dem Telemetrie-Fenster; kann auf einem zweiten Monitor platziert werden

#### Persistente Nachrichten-Datenbank
- **SQLite-Nachrichtenspeicher** für Kanal- und DM-Nachrichten (optional, in Einstellungen aktivierbar)
  - Je Kanal eine eigene DB-Datei (`messages/channel_{index}_{name}.db`), DMs in `messages/dm.db`
  - WAL-Modus, Insert/LoadSince/LoadBefore/ClearAll/ClearOlderThan, per `partner_id` filterbar
- **Automatisches Laden** der letzten 24 h nach dem Verbinden; älteres Nachladen per Hochscrollen (Lazy Load)
- **DM-History:** Beim Öffnen des DM-Fensters werden alle gespeicherten Konversationen der letzten 24 h automatisch als Tabs wiederhergestellt
- **Pro-Kanal-Löschung** im Kanäle-Tab: neue Spalte „Nachrichten-DB leeren" mit Button pro Zeile
  - Klick öffnet Dialog mit Zeitraum-Auswahl (Alle / Älter als 30 / 90 / 365 Tage)
- **Bestätigung** vor dem Leeren einer DM-Konversation (versehentliches Löschen verhindert)
- **Aufbewahrungsdauer** konfigurierbar (30 / 90 / 365 Tage), Retention wird beim Start angewendet
- Neue Einstellungen: `EnableMessageDb`, `MessageDbRetentionDays`

#### Verbindung merken
- **Letzte Verbindungsart** (Serial / Bluetooth / WiFi) wird in der INI gespeichert und beim Start automatisch vorausgewählt
- **Letztes BT-Gerät** wird gespeichert; nach BT-Scan wird es automatisch in der Geräteliste vorausgewählt
- Neue Einstellungen: `LastConnectionType`, `LastBtDevice`

#### Alert Bell Support
- **🚨 Notruf-Funktion** integriert (Meshtastic Alert Bell Character)
  - SOS-Button in Hauptchat und DM-Fenstern
  - Emoji-basiert (🔔) für Kompatibilität mit Android/Web-Apps
  - **Visuelle Benachrichtigung**: Rote blinkende Umrandung (6 Blinks über 3 Sekunden)
  - **Akustische Benachrichtigung**: Sirenen-Sound (WAV-generiert, funktioniert auch bei stummen System-Sounds)
  - **Notification-Bar**: Erscheint oben im Fenster mit Absender-Name
  - **"Zur Karte springen" Button**: Springt direkt zur Node-Position auf der Karte (Zoom Level 12)
    - Button wird nur angezeigt wenn Position des Nodes bekannt ist
    - Wechselt automatisch zum Karten-Tab und zentriert auf Node
  - Notification verschwindet automatisch nach 30 Sekunden oder manuell schließbar
  - 🔔 Icon in Nachrichtenlisten für empfangene Alert Bells

#### Karten-Erweiterungen
- **OSM Dark Mode**: Dunkle Kartenansicht für bessere Sicht bei Nacht
- **OpenTopoMap**: Topografische Karte mit Höhenlinien
- **Drei Kartentypen** wählbar in Einstellungen: OSM Standard, OSM Dark, OpenTopoMap
- **Eigener Tile-Server**: Umstellung auf eigenen Server (tile.schwarzes-seelenreich.de)
  - OSM-Policy verbietet Offline-Downloads für unsere Nutzung
  - Eigener Server erlaubt explizit Offline-Downloads
  - Tile-Server-URL individuell konfigurierbar in Einstellungen
- **Rate-Limiting** nur für externe Server (nicht für eigene Server)
- **Copyright-Hinweise** auf der Karte (unten rechts)
  - Dynamischer Text je nach Kartenquelle (OSM, OpenTopoMap)
  - Verlinkung zu Datenquellen
- **Support für weitere Bundesländer**: Offline-Tiles für ganz Deutschland und angrenzende Gebiete

### 🐛 Behoben

#### 🔧 Remote Admin – Channel-Ladefehler
- **`GetChannelRequest` ist 1-basiert** (1 = Kanal 0, 2 = Kanal 1 …): War 0-basiert → Kanal 0 lud nie (Timeout), alle anderen Kanäle um eins verschoben
- **Channel-Reihenfolge:** Stale-Response-Validierung via `ch.Index` entfernt – Firmware setzt das Feld oft nicht (Protobuf-Default 0), was alle Kanäle außer 0 als "falsche Antwort" klassifizierte und dreifach neu anforderte

#### 🔧 Remote Admin – Favoriten (Proto-Feldnummern falsch)
- `add_favorite_node = 36` war veraltet; aktuelle Firmware erwartet `set_favorite_node = 39`
  - Feld 36 ist in aktueller Firmware `set_canned_message_module_messages` → Favorites-Anfragen landeten still beim falschen Handler
- `remove_favorite_node` korrigiert von Feld 37 auf Feld 40
- `admin.proto` im Projekt auf aktuelle Feldnummern aktualisiert; alle C#-Referenzen auf `SetFavoriteNode` umgestellt
- **FavoriteButton Fire-and-forget behoben:** Fehler beim Setzen/Entfernen wurden still ignoriert; jetzt proper `async void` mit Fehlermeldung und UI-Revert bei Fehler

#### 🔧 Admin – Key-Anzeige
- **Public Key und Private Key** wurden als Hex angezeigt; Meshtastic-App und Firmware nutzen Base64 → beide Admin-Fenster (lokal & remote) zeigen Keys jetzt in Base64

#### 🗺️ Karte – Emoji-Node-Namen
- **Emoji-Kurzname (z.B. 🔥, 📶)** wurden auf der Karte als □ angezeigt, weil SkiaSharp/Mapsui kein Emoji-Fallback hat
- Alle vier `LabelStyle`-Stellen in MainWindow auf `Font { FontFamily = "Segoe UI Emoji" }` gesetzt

#### 🖧 Virtual Node – Traceroute-Telemetrie
- **Traceroute-Requests des VNode-Clients** wurden fälschlicherweise in die eigene Telemetrie eingespeist: Android-App löst Traceroute aus → Request-Paket (`WantResponse=true`, Portnum=70) wurde als Traceroute-Ergebnis mit `DestinationNodeId = myNodeId` gespeichert
- Fix: `ProcessExternalPacket` filtert ausgehende Traceroute-Requests heraus; die Antwort kommt ohnehin über den physischen Funkweg

#### ⚙️ Sonstige Bugfixes
- **Zombie-Prozess beim Beenden**: App beendet sich jetzt sauber mit `Application.Current.Shutdown()`
  - Synchroner Disconnect statt asynchron
  - Keine hängenden Prozesse mehr nach Fenster-Schließen
- **Tab-Navigation**: "Zur Karte" Button springt jetzt korrekt zum Karten-Tab (nicht zur Node-Liste)

### 🔄 Geändert

- **Alert Bell Format**: Umstellung von ASCII Control Character (0x07) auf Emoji (🔔)
  - Kompatibel mit Android und Web-Apps
  - Emoji wird beim Empfang automatisch aus Nachrichtentext entfernt
  - Unterstützt beide Varianten beim Empfang (ASCII + Emoji)

---

## [1.0-Beta] - 2026-02-08

### ✨ Hinzugefügt

#### Direct Messages (DMs)
- **Separates DM-Fenster** mit Tab-System für verschiedene Konversationen
- **Automatische Benachrichtigungen** bei neuen DMs:
  - Fenster wird automatisch angezeigt und in den Vordergrund gebracht
  - Taskbar blinkt bei neuer Nachricht (Windows API Integration)
  - System-Sound Benachrichtigung
- **Orange Hervorhebung** und **fette Schrift** für ungelesene Nachrichten in Tabs
- **Rechtsklick-Menü** in Knoten-Tab: "💬 DM senden" öffnet direkt Chat
- **Gesendete DM-Nachrichten** werden jetzt korrekt im Chat angezeigt
- DM-Button in Toolbar mit Bold-Markierung bei neuen ungelesenen Nachrichten

#### Message-Logging
- **Automatisches Logging** aller Nachrichten (Kanal + DMs)
- **Separate Log-Dateien** pro Kanal: `logs/Channel_[Index]_[Name].log`
- **Separate Log-Dateien** pro DM-Partner: `logs/DM_[NodeID]_[Name].log`
- **Speicherort**: Direkt neben der EXE im Ordner `logs/`
- **Log-Format**: `[yyyy-MM-dd HH:mm:ss] Absender: Nachricht`
- **UTF-8 Encoding** für korrekte Umlaute und Sonderzeichen
- **Thread-sicheres Schreiben** mit Lock-Mechanismus
- **Automatische Sanitisierung** von Dateinamen (ungültige Zeichen werden ersetzt)
- MessageLogger Service für zentralisierte Logging-Logik

#### UI-Verbesserungen
- **Dark Mode**: Umschaltbar in Einstellungen-Tab
  - Vollständige Unterstützung durch ModernWPF Theme
  - Einstellung wird live angewendet
- **Kanalfilter mit Buffering**:
  - Dropdown über Nachrichtenliste zum Filtern nach Kanal
  - "Alle Kanäle" zeigt alle Nachrichten
  - Nachrichten werden in separater Liste gepuffert und bleiben beim Filterwechsel erhalten
- **Kanalnamen statt Nummern**:
  - Nachrichten zeigen Kanalnamen (z.B. "Primary", "LongFast")
  - Fallback auf "Kanal [X]" wenn Name nicht verfügbar
- **Intuitivere Kanalauswahl**:
  - Kanal-Dropdown jetzt direkt neben Senden-Button
  - Logischer Workflow: Kanal wählen → Nachricht eingeben → Senden
  - Toolbar aufgeräumt (Kanalauswahl entfernt)
- **Verbindungsstatus-Anzeige**:
  - **Grau**: Nicht verbunden
  - **Gelb**: Verbinde...
  - **Orange**: Initialisiere... (Config wird geladen)
  - **Grün**: Verbunden und bereit
- **Quietscheentchen-Icon** 🦆: Freies Icon für EXE
- **Meshhessen.de Branding**: Im Footer mit Link-Farbe hervorgehoben

#### Technische Features
- DirectMessagesWindow.xaml/.cs für DM-Verwaltung
- DirectMessageConversation Model für Konversationsverwaltung
- MessageLogger Service mit thread-sicherer Datei-Verwaltung
- Windows API Integration für Taskbar-Benachrichtigungen (FlashWindow)
- Verbesserte ConnectionStatus Enum mit mehr Zuständen

### 🐛 Behoben

- **Filter-Buffering**: Nachrichten verschwinden nicht mehr beim Filtern
  - Separate `_allMessages` Liste speichert alle Nachrichten ungefiltert
  - `_messages` zeigt nur gefilterte Ansicht
  - Beim Filterwechsel werden Nachrichten aus `_allMessages` neu gefiltert
- **DM gesendete Nachrichten**: Eigene DMs werden jetzt im Chat angezeigt
- **Thread-Sicherheit**: Alle Message-Logs sind thread-sicher implementiert
- **Verbindungsstatus**: Stabilere und aussagekräftigere Status-Anzeige

### 🔄 Geändert

- **Kanalauswahl** von Toolbar nach unten zu Nachrichten-Eingabe verschoben
- **Log-Speicherort** von `%LocalAppData%` zu `[EXE-Verzeichnis]/logs/`
- **Toolbar-Layout**: Reduziert auf 2 Spalten (Connection Controls | Status)
- **MessageItem Model**: Neue Felder `ChannelName`, `FromId`, `ToId`, `IsEncrypted`
- **Ready-Status Farbe**: Von LightGreen zu LimeGreen (kräftigeres Grün)

### 📝 Dokumentation

- README komplett überarbeitet mit allen neuen Features
- Neue Sektion "Message-Logging" mit Beispielen
- Erweiterte "Verwendung" Sektion mit DM-Anleitung
- Detaillierter Changelog (diese Datei)
- Aktualisierte Projekt-Struktur in README
- Screenshots-Beschreibungen hinzugefügt

---

## [0.1-Alpha] - 2025-12-XX

### ✨ Hinzugefügt

#### Basis-Funktionalität
- **Serielle USB-Verbindung**
  - Automatische COM-Port-Erkennung
  - Framing-Protokoll (0x94 0xC3)
  - Protobuf-Parsing (FromRadio/ToRadio)
- **Nachrichten senden/empfangen**
  - Text-Nachrichten über TEXT_MESSAGE_APP
  - Broadcast-Nachrichten
  - Verschlüsselte Nachrichten Erkennung
- **Multi-Channel-Support**
  - Automatisches Laden aller Channels (0-7)
  - Channel-Auswahl in Toolbar
  - PRIMARY/SECONDARY Rollen
  - PSK-Anzeige (Base64)
- **Knoten-Übersicht**
  - Liste aller Nodes im Mesh
  - Node-ID, Name, SNR, Distanz
  - Batteriestatus
  - Letzte Aktivität
- **Geräteeinstellungen**
  - LoRa-Config auslesen (Region, Modem Preset)
  - Device-Info anzeigen
  - Hardware-Model und Firmware-Version
- **Debug-Modus**
  - Intensives Logging
  - DebugView-Kompatibilität
  - Live-Log im Debug-Tab
  - Log-Export Funktionen

#### Technische Basis
- WPF Application mit ModernWPF UI
- SerialPortService für Low-Level Serial I/O
- MeshtasticProtocolService für Protobuf-Handling
- Logger Service für Debug-Ausgaben
- Models: MessageItem, NodeInfo, ChannelInfo, DeviceInfo
- Protobuf-Definitionen (mesh.proto, portnums.proto, admin.proto)

---

## Geplante Features

### v1.1 - Persistence
- SQLite-Datenbank für Message-History
- History beim Start laden
- Message-Suche
- Export/Import von Nachrichten

### v1.2 - Config-Management
- Config-Bearbeitung und Speichern
- Kanal hinzufügen/bearbeiten/löschen
- PSK-Generator
- Device-Remote-Config

### v1.3 - Erweiterte Features
- PSK-Entschlüsselung
- Karten-Ansicht (GPS-Positionen)
- Mesh-Visualisierung (Graph)
- Waypoints
- Telemetrie-Dashboard

### v2.0 - Pro Features
- Firmware-Update über Client
- Multi-Device-Support
- Remote-Node-Verwaltung
- Erweiterte Statistiken
- Mehrsprachigkeit

---

## Legende

- ✨ **Hinzugefügt** - Neue Features
- 🐛 **Behoben** - Bug-Fixes
- 🔄 **Geändert** - Änderungen an bestehenden Features
- ⚠️ **Veraltet** - Bald zu entfernende Features
- 🗑️ **Entfernt** - Entfernte Features
- 🔒 **Sicherheit** - Sicherheits-relevante Änderungen
- 📝 **Dokumentation** - Dokumentations-Änderungen

---

**Projekt**: Meshtastic Windows Client
**Lizenz**: GNU General Public License v3.0 (GPL-3.0)
**Website**: Meshhessen.de
**Entwickelt mit**: Claude AI
