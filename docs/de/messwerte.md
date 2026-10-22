<!-- Inhaltlich bewusst dupliziert von docs/index.html (#telemetrie) — die Hilfe
     soll auf Website UND im Handbuch stehen. Änderungen bitte an beiden Stellen
     nachziehen. -->
# 📈 Messwerte verstehen

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](https://meshhessenclient.de/handbuch/en/metrics/)

Der Meshhessen Client speichert alle empfangenen Telemetrie-Daten in einer lokalen SQLite-Datenbank und wertet sie automatisch aus. Über das Node-Kontextmenü → *Telemetrie* öffnet sich das Analyse-Fenster für jeden Node; die LED-Ampeln im Hauptfenster zeigen den Gesamtzustand auf einen Blick. Dieses Kapitel erklärt, **was die Zahlen bedeuten** — wie du sie im Client findest, steht in [Telemetrie & Funkanalyse](telemetrie.md).

## 📶 Signal-Analyse: SNR & RSSI

### SNR – Signal-Rausch-Abstand

Gibt an, wie stark das Nutzsignal gegenüber dem Rauschen ist. LoRa dekodiert dank Spreizung auch Signale unterhalb des Rauschpegels – wie weit, hängt vom Modem-Preset ab (kurze Presets ≈ −7 dB, Long Slow bis ≈ −20 dB). Die folgenden Schwellen sind eine praxisnahe Orientierung für die hierzulande üblichen Presets, keine festen Grenzen:

* 🟢 **▲ > 5 dB** – Sehr gut, stabile Verbindung
* 🟢 **● 0 – 5 dB** – Gut, zuverlässige Verbindung
* 🟡 **● −5 – 0 dB** – Grenzwertig, Paketverluste möglich
* 🔴 **▼ < −5 dB** – Schlecht, häufige Paketverluste

### RSSI – Empfangspegel

Absoluter Empfangspegel in dBm. Gibt an, wie laut das Signal am Empfänger ankommt – unabhängig vom Rauschen. In Kombination mit SNR ergibt sich ein vollständiges Bild der Verbindungsqualität.

* 🟢 **> −100 dBm** – Gut
* 🟡 **−100 bis −115 dBm** – Grenzwertig
* 🔴 **< −115 dBm** – Schwach

### Signal-Trend (Regression)

Der Client berechnet den linearen Trend über das eingestellte Zeitfenster (Standard: 6 Stunden). Ein negativer Trend deutet auf eine sich verschlechternde Verbindung hin – z. B. durch wechselnde Wetterbedingungen oder Hindernisse.

* 🟢 **▲ Steigend** – Signal verbessert sich
* 🟡 **→ Stabil** – Kein signifikanter Trend
* 🔴 **▼ Fallend** – Signal verschlechtert sich

## 🔋 Batterie & Spannung

### Batteriestand (%)

* 🟢 **> 50 %** – Gut
* 🟡 **20 – 50 %** – Mittel
* 🔴 **< 20 %** – Niedrig, bald laden

### Nachtabfall

Gibt an, wie viel % Batterie der Node in der Nacht (kein Solar) verbraucht. Hoher Nachtabfall zeigt erhöhten Ruhestrom oder fehlende Deep-Sleep-Konfiguration.

* 🟢 **< 5 %** – Normal (Ruhestrom)
* 🟡 **5 – 15 %** – Leicht erhöht
* 🔴 **> 15 %** – Kritisch, Ursache prüfen

### Spannung (V)

Rohspannung des Akkus in Volt. Typische LiPo-Werte: 4,2 V = voll geladen, 3,7 V = Nennspannung, 3,2 V = Tiefentladungsgrenze. Solarnodes: Spannung tagsüber deutlich höher als nachts.

## 📡 Kanalauslastung (Channel Utilization)

Die Kanalauslastung gibt an, wie viel Prozent des LoRa-Zeitfensters auf dem gewählten Kanal belegt ist – durch eigene Pakete und empfangene Pakete fremder Nodes. Hohe Kanalauslastung erhöht die Wahrscheinlichkeit von Kollisionen und Paketverlusten. Der Mesh-Health-Tooltip zeigt die Top-5-Nodes nach Kanalauslastung.

| Auslastung | Bewertung | Empfehlung |
|---|---|---|
| 0 – 10 % | 🟢 Sehr gut | Viel Kapazität frei, keine Maßnahmen nötig |
| 10 – 25 % | 🟢 Gut | Normales, gesundes Mesh |
| 25 – 50 % | 🟡 Erhöht | Position-Update-Rate reduzieren, Short Slow prüfen |
| > 50 % | 🔴 Kritisch | Viele Paketverluste; Telemetrie-Intervalle und Broadcasts reduzieren |

## 🔀 Routing & Pfad-Stabilität

### Hop-Anzahl

Wie oft ein Paket von Relay-Nodes weitergeleitet wurde (Meshtastic-Zählweise: **direkt = 0 Hops**; die Zahl der Funkstrecken ist Hops + 1). Jede Weiterleitung erhöht Latenz und Airtime-Verbrauch.

* 🟢 **0 Hops** – Direktverbindung, optimal
* 🟢 **1–2 Hops** – Normal
* 🟡 **3–5 Hops** – Hoch, hoher Airtime-Verbrauch
* 🔴 **6–7 Hops** – Maximum, sehr ineffizient

### Pfad-Wechselrate

Gibt an, wie häufig sich der Routing-Pfad zu einem Node ändert. Häufige Pfadwechsel deuten auf instabile Zwischenverbindungen hin – z. B. Relay-Nodes an schlechten Standorten.

* 🟢 **Stabil** – Gleichbleibender Pfad
* 🟡 **Mäßig** – Gelegentliche Wechsel
* 🔴 **Instabil** – Häufige Wechsel, Relay-Nodes prüfen

### Hop-Kosten

Gewichteter Durchschnitt der Hop-Anzahl über den Analysezeitraum. Niedrige Hop-Kosten bedeuten kurze, effiziente Pfade; hohe Hop-Kosten erhöhen Latenz und Kanalauslastung im gesamten Mesh.

## 👥 Nachbarn (Neighbors)

Zeigt alle Nodes, die der ausgewählte Node direkt (0 Hops, ohne Relay) hört. Pro Nachbar wird der SNR der direkten Verbindung und der zeitliche Trend angezeigt. Viele stabile Nachbarn bedeuten eine gute Einbindung ins Mesh; wenige oder instabile Nachbarn zeigen Reichweitenprobleme.

## ⏱️ Airtime

Gibt an, wie viel Sendezeit (TX-Airtime) und Empfangszeit (RX-Airtime) der Node belegt. Nodes mit hoher TX-Airtime erhöhen die Kanalauslastung für alle anderen Nodes im Bereich. Telemetrie-Intervalle und Broadcast-Häufigkeit sollten entsprechend angepasst werden.

## 🔧 LED-Ampel „Netz:“ – Bedeutung auf einen Blick

Die Leiste **„Netz:“** im Hauptfenster bewertet das gesamte sichtbare Mesh:

| LED | 🟢 Grün | 🟡 Gelb | 🔴 Rot | ⚪ Grau |
|---|---|---|---|---|
| Wetter/KT *(Signal-Wetter, Kurzzeit-SNR-Trend, 6 h)* | SNRs stabil oder steigend | Leichter gemeinsamer Abfall | Deutlicher gemeinsamer Abfall (Wetter-Effekt) | Keine Daten |
| LZ-SNR *(Antennen-Gesundheit, 7 Tage)* | Langzeittrend stabil/steigend | Schleichende Verschlechterung | Langfristig fallend — Antenne/Position prüfen | Keine Daten |
| NB *(Nachbar-Qualität)* | ≥ 2 stabile Nachbarn | 1 Nachbar oder sinkende SNRs | Keine Direktverbindungen | Keine Daten |
| PS *(Routen-Stabilität)* | Stabiler Pfad | Gelegentliche Wechsel | Häufige Pfadwechsel | Kein Traceroute |
| Mesh Health *(Score 0–100 %)* | Guter Gesamtzustand | Einzelne Problembereiche | Hohe Kanalauslastung oder viele instabile Nodes | Keine Daten |

## 🛠️ Maßnahmen zur Fehlerbehebung

* **Schlechtes Signal (SNR/RSSI niedrig):** Antenne auf festen Sitz und Beschädigung prüfen. Standort erhöhen – freies Sichtfeld verbessert LoRa-Reichweite erheblich. Antennen-Polarisation beachten (vertikal). Relay-Node als Zwischenstation aufstellen.
* **Fallender Signal-Trend:** Wetterbedingungen beobachten (Feuchtigkeit, Regen dämpfen das Signal). Antenne auf Wassereintritt prüfen (Outdoor-Nodes). Frequenz-Offset des Nodes überprüfen.
* **Hohe Kanalauslastung (> 25 %):** Modem-Preset auf *Short Slow* setzen (höhere Datenrate durch niedrigeren Spreading Factor = kürzere Airtime pro Paket). Position-Update-Rate in der Firmware reduzieren (z. B. auf alle 10–30 Minuten). Telemetrie-Intervalle (Gerät, Umwelt) erhöhen. Unnötige Broadcasts (z. B. häufige Nachrichten) minimieren.
* **Pfad-Instabilität (häufige Pfadwechsel):** Relay-Nodes an schlechten Standorten identifizieren und verbessern. Für stationäre Relay-Nodes eine feste Position setzen – spart GPS-Strom und verhindert springende Kartenpositionen (das Next-Hop-Routing steuert es nicht direkt). SNR der betroffenen Zwischenverbindungen über [Traceroute](traceroute.md) analysieren.
* **Hoher Nachtabfall der Batterie:** Stromsparoptionen rollenabhängig prüfen: Clients/Tracker profitieren von Power-Saving/Deep-Sleep, Router sollen dauerhaft empfangsbereit bleiben. Display-Timeout reduzieren. Netzteil / Solar-Panel prüfen. Ladecontroller auf korrekte Funktion überprüfen.
* **Wenige oder keine Nachbarn:** Kanal-Einstellungen (Frequenz, Modem-Preset) mit Nachbar-Nodes abgleichen. Antenne und Standort verbessern. Mesh-Dichte in der Region erhöhen (weiteren Node aufstellen).

> 📖 Kurzdefinitionen aller Begriffe stehen im [Glossar](glossar.md); wie du diese Werte als Dashboard überwachst, zeigt das [How-to im Telemetrie-Kapitel](telemetrie.md).
