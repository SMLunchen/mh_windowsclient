# TODO: Protobuf-v2.8-Integration

Stand 2026-11-09. Submodule `protobufs/` ist auf **v2.8.2** (`ffd5ad4`) gezogen — baut ohne
Fehler, alle 222 Tests grün. Diese Liste sammelt alles, was wir aus dem Update inhaltlich
mitnehmen wollen, damit nichts verloren geht.

**Referenz-Checkouts (beide am 2026-11-09 gefetcht, Code aus den origin-Refs lesen):**
- Android `Documents/meshtastic/Meshtastic-Android`: `origin/main` = 2026-11-09. Achtung:
  Working Tree enthält einen alten unversionierten Source-Drop (1165 Änderungen) — zum Lesen
  `git grep/show … origin/main` nutzen; Aufräumen (reset --hard) nur mit User-Go.
- Firmware `Documents/meshtastic/firmware`: AEAD-/2.8-Code liegt auf `origin/develop`
  (2026-11-08), nicht auf master — ebenfalls per `git grep/show … origin/develop` lesen.

## Runde A — Quick Wins (keine 2.8-Firmware nötig)

**Stand 2026-11-09: Runde A KOMPLETT umgesetzt** (249 Tests grün, Smoke-Launch ok).
Bewusst zurückgestellt: Multi-Kanal-ADC/One-Wire-Kanäle und `pm_status_flags` (seltene
DIY-Hardware — nachrüsten wenn gefragt), optionale Dashboard-Metrik „direkt hörbare
Nodes" (A6), LONG_SLOW-Deprecation-Markierung im Preset-Combo (→ C4).

- [x] **A1 Hardware-Namen prüfen:** Neue Modelle 144–152 (Heltec Mesh Pocket V2, T-Connect Pro,
      MeshPager X2, Nodara, …) kommen per Enum automatisch rein — einmal prüfen, wie die Namen
      formatiert angezeigt werden (Node-Liste, Node-Info, Remote Admin `HwModel.ToString()`).
- [x] **A2 Telemetriefenster — neue Messwerte anzeigen:**
      - `SoilWaterMetrics` (neue Telemetry-Variante 11): Boden-pH, pH, Leitfähigkeit, Salinität,
        NPK, gelöster O₂, ORP, CSB/BSB, Trübung, Nitrat, Ammonium, Solar-Irradianz
      - Blitzsensor AS3935: `lightning_strike_count_1h`, `lightning_distance_km`
      - Multi-Kanal: `adc_voltage_ch0..7`, `one_wire_temperature_ch0..7`
      - `pm_status_flags` (SEN6X-Fehlerbits: Lüfter/RHT/Gas/CO₂/HCHO/PM) — als Warnhinweis dekodieren?
- [x] **A3 Funk-Echo — SoilWater als Umweltdaten:** Variante 11 (`soil_water_metrics`) im
      `RequestEchoClassifier.ExtractTelemetryVariant` auf **Environment („Umweltdaten")**
      mappen — kein eigener Typ (Entscheidung User 2026-11-09); Blitz steckt ohnehin in
      EnvironmentMetrics. Test ergänzen.
- [x] **A4 Dashboard-Metriken:** `lightning_strike_count_1h`, `lightning_distance_km` (Achtung:
      Gefahr = NIEDRIGER Wert → AlarmLow-Zone „Gewitter näher als x km"), **Radiation** (µR/h —
      Feld existiert länger, fehlt aber als Dashboard-Metrik; in `EnvironmentMetricInfo` schon
      registriert), ggf. Boden-pH/Leitfähigkeit. Prüfen, was die Telemetrie-DB bereits generisch
      speichert; MetricRange + AddWidgetDialog + Query-Serie ergänzen.
- [x] **A5 Karten-Overlay:** Blitz (Distanz/Häufigkeit) und Radiation ins Umweltdaten-Heatmap-
      Overlay aufnehmen (prüfen, ob der Overlay-Picker alle `EnvironmentMetricInfo`-Metriken
      listet — dann fehlt nur Blitz als neue EnvMetric-Einträge).
- [x] **A6 `NodeInfo.heard_on_current_lora`:** Ins Node-Modell übernehmen (Merge-Pfad
      `MergeNodeInfo_NoLock` beachten: Geräte-Flag wie IsFavorite behandeln!), in der Node-Liste
      als „direkt per Funk gehört"-Kennzeichnung nutzen, in die RF-Nachbar-/SNR-Logik als
      zusätzliches hartes Signal integrieren, optional Dashboard-Metrik „direkt hörbare Nodes".
      *Android-Referenz:* `core/data/.../NodeManagerImpl.kt`, `MeshMessageProcessorImpl.kt`,
      `NodeRepositoryImpl.kt` (Schreib-/Reset-Logik des Flags).
- [x] **A7 LoRaAirtime-Tabelle vervollständigen:** `ForPreset` kennt nur 8 Presets — es fehlen
      VeryLongSlow, LongTurbo, LiteFast/LiteSlow, NarrowFast/NarrowSlow, TinyFast/TinySlow,
      MediumTurbo → Parameter aus Firmware `MeshRadio.h` (Checkout vorhanden) + Tests.
- [x] **A8 `ChannelSettings.use_aead` — Badge UND Crypto:** AEAD-Kanäle (AES-CCM, 12-Byte-Tag,
      experimentell) in Kanalliste/-editor als Badge zeigen. Zusätzlich **Decrypt + Encrypt
      implementieren** (Entscheidung User): AES-CCM-Primitive haben wir aus der PKI-Arbeit
      (BouncyCastle); Voraussetzung: **Firmware-Checkout auf 2.8.x ziehen** (Stand 2026-08-25
      enthält `use_aead` noch nicht) und das exakte Schema verifizieren (Nonce-Aufbau,
      Tag-Länge, Key) → dann ChannelDecrypt/Send-Pfad + Tests wie bei CTR. Der #69-Clone-Fix
      erhält das Flag beim Schreiben bereits automatisch.
      *Firmware-Fakten (origin/develop, gefetcht):* `CryptoEngine.h`: `AEAD_TAG_SIZE = 12`,
      `AEAD_AAD_SIZE = 2*sizeof(uint32)`; `Channels.cpp`: AEAD nur mit PSK (sonst wird das
      Flag gelöscht), AEAD-Kanäle überschreiben die Signatur-Policy (Pakete nie signiert,
      werden nicht gedroppt).
- [x] **A9 Status-Meldungen (NODE_STATUS_APP, Port 36):** Empfangen + dekodieren
      (`StatusMessage { status }`, Broadcast bei Änderung und per Timer, seit FW 2.7.20) →
      **Anzeige in der Node-Liste und im Node-Info-Fenster (Fancy View)**. Eigenen Status
      setzen über einen **Button in der Kopfzeile neben dem Funk-Echo-Icon** (kleiner Dialog/
      Flyout, max 80 Byte, UTF-8-Clamp), geschrieben via
      `ModuleConfig.StatusMessageConfig.node_status`. i18n + Persistenz (letzter Status je
      Node) prüfen.
      *Android-Referenz:* Anzeige `feature/node/.../NodeDetailsSection.kt` (`node.nodeStatus`),
      Setzen `feature/settings/.../UserConfigItemList.kt` + `RadioSetupScreen.kt` (Prefill aus
      Config oder letztem empfangenen Status).
- [x] **A10 Namens-Limits durchsetzen (wie Kanalnamen, #Byte ≠ #Zeichen):** Firmware 2.8
      klemmt Long Name auf **24 UTF-8-Byte** (kam mit dem NodeInfoLite-Refactor, Commit
      41727ea 2026-06-11; Decode bleibt 40 nur für Alt-Kompatibilität), Short Name 4 Byte.
      **Firmware-abhängig erzwingen (User-Entscheid):** verbundenes Gerät ≥ 2.8 → 24 Byte,
      ältere Firmware → wie bisher 39 Byte. Owner-Editor in der Node-Konfiguration
      (LongName/ShortName) und Remote Admin entsprechend validieren/kappen —
      `ChannelEdit.ClampName` zu einem allgemeinen UTF-8-Clamp-Helper verallgemeinern.

## Runde B — 2.8-Firmware-Features (Buttons/Anzeigen nur bei unterstütztem Gerät)

**Stand 2026-11-09: Runde B KOMPLETT umgesetzt** (259 Tests grün). Hinweis: Die Firmware
origin/develop enthält die Display-Mirror-Verbs selbst noch nicht (nur die Protos) — unsere
Seite ist wie die Android-App forward-ready; der Button erscheint erst, wenn ein Gerät
DisplayInfo in den Metadaten meldet. Zusätzlich umgesetzt: Radiation wahlweise in µSv/h
(Einstellung, 1 µSv/h = 100 µR/h) und Signing-Policy auch im Remote Admin.

- [x] **B1 Feature-Gating zentral:** `DeviceMetadata.firmware_version` parsen + Capability-Check
      („Gerät kann X") als gemeinsamen Helper; Buttons/Spalten erscheinen nur, wenn das
      verbundene Gerät das Feature hat. Bei der Android-App spicken
      (`Documents/meshtastic/Meshtastic-Android`).
- [x] **B2 Authentifizierte ACKs:** `Routing.ack_proof` / `MeshPacket.ack_proof_status`
      auswerten → Zustellstatus erweitern: „Zugestellt 🛡 (kryptografisch bestätigt)" bei VALID,
      Warn-Darstellung bei INVALID, neutral bei ABSENT/NO_KEY. Chat-Bubble-Status + DM-Fenster.
      **Grundsatz (User): nur flaggen, nie blocken** — Nachrichten/ACKs werden immer angezeigt,
      Auffälligkeiten nur markiert.
      *Android-Referenz:* `feature/messaging/.../MessageItem.kt` (`isAckProofForged` — nur
      INVALID wird an der Bubble markiert) + `MessageActionsBottomSheet.kt` (Details beim
      Long-Press: VALID/INVALID/NO_KEY mit Icon+Farbe, ABSENT wird gar nicht erwähnt).
- [x] **B3 Signing-Policy konfigurierbar:** `SecurityConfig.packet_signature_policy`
      (Compatible/Balanced/Strict) ins Security-Panel der Node-Konfiguration (Combo + i18n).
      **Default bleibt Compatible**; der Client selbst verwirft nichts, Policy-Durchsetzung
      ist Sache der Firmware.
      *Android-Referenz:* `feature/settings/.../PacketAuthenticitySetting.kt`.
- [x] **B4 Display-Mirroring:** `DeviceMetadata.display` (DisplayInfo: Auflösung, Panel-Klasse,
      Touch) auswerten → Button „Geräte-Display spiegeln" nur zeigen, wenn DisplayInfo vorhanden
      und Verbindung lokal. `AdminMessage.set_display_mirror` + `FromRadio.display_frame`-Chunks
      zusammensetzen und rendern (MONO_VLSB + RGB565, `display_palette`-Einfärbung für
      Mono-Panels). Einzelbild per `get_display_frame_request`.
      *Android-Referenz:* `core/data/.../DisplayMirrorManagerImpl.kt` +
      `MirrorRectCompositor.kt` (Chunk-/Rect-Reassembly) + `AdminControllerImpl.kt`.

## Beobachten / Backlog

- [ ] **C1 PAGING_APP (Port 38) aktiv beobachten:** Quittierbare Alarmierung („gesehen" statt nur
      „zugestellt"). In v2.8.2 existiert nur der Portnum — das `PagingPacket`-Proto fehlt noch.
      Bei Firmware-Release prüfen: passt thematisch zu Funk-Echo/BOS-Szenarien.
- [ ] **C2 Store&Forward `original_id`:** Für Backlog-Dedupe nutzen (Duplikate zwischen
      Live-Empfang und S&F-History), sobald Server im Netz das Feld liefern.
- [ ] **C3 `field_metadata` nutzen:** Die Protos tragen jetzt offizielle Labels, Beschreibungen,
      Einheiten und Min/Max je Config-Feld — längerfristig für Tooltips/Validierung im
      NodeConfig-Fenster auswerten statt eigener Texte.
- [ ] **C4 Deprecations nachziehen:** `LONG_SLOW`/`VERY_LONG_SLOW` im Preset-Combo als veraltet
      kennzeichnen; `PowerMetrics.ch4..ch8_voltage/current` sind deprecated → prüfen, ob
      Dashboard-Metriken darauf zeigen (Ersatz: `adc_voltage_ch0..7`).
- [ ] **C5 Sensor-Admin (nur bei Bedarf):** SEN6X (Lüfter-Reinigung, CO₂-Kalibrierung),
      AS3935 (Antennen-Tuning), DS248X — Admin-Messages existieren, UI nur falls Nutzer fragen.
