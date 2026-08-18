# Changelog

Alle nennenswerten Änderungen an diesem Projekt werden in dieser Datei dokumentiert.

Das Format basiert auf [Keep a Changelog](https://keepachangelog.com/de/1.0.0/),
und dieses Projekt folgt [Semantic Versioning](https://semver.org/lang/de/).

---

## [Unreleased]

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
