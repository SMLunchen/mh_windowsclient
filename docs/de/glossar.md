# 📖 Glossar — Meshtastic-Begriffe kurz erklärt

[← Handbuch-Übersicht](../index.md) · [🇬🇧 English version](../en/glossary.md)

**SNR (Signal-Rausch-Abstand, dB)**
: Wie weit das Nutzsignal über dem Rauschen liegt. LoRa dekodiert dank Spreizung auch Signale *unter* dem Rauschen — je nach Modem-Preset bis etwa −7 dB (kurze Presets) bzw. −20 dB (Long Slow). Faustregel: > 5 dB sehr gut, um 0 dB solide, deutlich negativ = grenzwertig.

**RSSI (Received Signal Strength Indicator, dBm)**
: Absoluter Empfangspegel. Typische LoRa-Werte liegen zwischen ≈ −60 dBm (sehr nah/stark) und ≈ −125 dBm (Empfangsgrenze). Anders als SNR unabhängig vom Rauschpegel — beide zusammen ergeben das Bild.

**Noise Floor (dBm)**
: Grundrauschen am Empfänger (Umgebungsstörungen + Empfängerrauschen). Näherung: RSSI ≈ Noise Floor + SNR. Neuere Firmware meldet ihn in den LocalStats.

**Hop**
: Eine Weiterleitung durch einen Relay-Node. Jeder Hop erhöht Latenz und Airtime-Verbrauch; das Hop-Limit (Standard 3, max. 7) begrenzt die Reichweite einer Nachricht.

**Airtime**
: Die Zeit, die eine Aussendung den Funkkanal belegt. Gemeinschaftsressource: Je länger das Modem-Preset und je mehr Pakete/Hops, desto weniger Kapazität bleibt für alle anderen.

**Channel Utilization (Kanalauslastung, %)**
: Anteil der Zeit, in der der Kanal belegt ist (eigene + fremde Aussendungen). Dauerhaft über ~25–40 % wird kritisch — Pakete kollidieren und gehen verloren.

**Modem-Preset (z. B. Short Slow, Long Fast)**
: Kombination aus Bandbreite und Spreading Factor. „Short"-Presets = höhere Datenrate, kürzere Airtime, weniger Reichweite; „Long"-Presets umgekehrt. Alle Nodes eines Meshes müssen dasselbe Preset nutzen.

**MQTT**
: Internet-Brücke zwischen Mesh-Inseln: Gateways heben Pakete auf einen Broker und zurück. Praktisch für Reichweite — aber ein „Nachbar via MQTT" ist kein Funk-Nachbar. Der Client kennzeichnet MQTT-Strecken deshalb getrennt.

**Direct Neighbors / echte HF-Nachbarn**
: Nodes, die nachweislich *direkt über Funk* gehört wurden (0 Hops, kein MQTT) — belegt durch eigenen Direktempfang, Traceroute-Teilstrecken oder NeighborInfo-Pakete.

**NeighborInfo**
: Paket-Typ, mit dem ein Node selbst meldet, wen er direkt hört — eine der drei Beweisquellen für echte HF-Nachbarn.

**Traceroute**
: Diagnose-Anfrage, die den Pfad zu einem Ziel-Node aufzeichnet: jede Station trägt sich mit per-Hop-SNR ein (Hin- und Rückweg).

**PKI (Public-Key-Infrastruktur)**
: Schlüsselpaare (X25519) für verschlüsselte Direktnachrichten und Admin-Zugriff. Der Client entschlüsselt PKI-DMs client-seitig (AES-256-CCM) und verwaltet bekannte Public Keys in `node_keys.csv`.

**NodeDB**
: Die Node-Datenbank eines Geräts: alle je gehörten Nodes mit Namen, Position, Telemetrie-Ständen. Der Client lädt sie beim Verbinden und reichert sie mit eigener Telemetrie-Historie an.

**LocalStats**
: Statistik-Telemetrie neuerer Firmware: Noise Floor, RX gesamt/fehlerhaft/Duplikate, TX/Relay-Zähler, Nodes online. Grundlage für Raten (Pakete/h) und die Funk-Overlays.

**precision_bits (Positions-Genauigkeit)**
: Meshtastic kann Positionen bewusst vergröbert senden (z. B. 13 Bit ≈ ±2,9 km). Der Client zeigt und verrechnet diese Unschärfe (etwa bei der Standort-Schätzung).

**Fresnel-Zone**
: Ellipsenförmiger Raum um die Sichtlinie einer Funkstrecke, der für sauberen Empfang weitgehend frei sein muss (Faustregel: 60 % der 1. Zone). Ragt Gelände hinein, entsteht Zusatzdämpfung — das Höhenprofil des Clients zeigt genau das.
