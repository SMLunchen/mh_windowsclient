# Meshhessen Client – Roadmap & Geplante Features

## Persistence

- [ ] Message-Suche

## Erweiterte Features

- [ ] Mesh-Visualisierung (Graph)
- [ ] Waypoints
- [ ] Telemetrie-Dashboard

## Ideen / Backlog

* Export/Import von Nachrichten
* Einstellung: Chatbubbles mit eckigen ecken und runden ecken auswählbar.

## Bugfixes

## Technische Schulden / Architektur

Bewertung Stand v1.5.14. Fundament ist gesund (saubere `IConnectionService`-Abstraktion,
getrennte Service-Schicht, richtige Kopplungsrichtung). Baustellen sind Größe und
Duplizierung, nicht falsche Schichtung. Reihenfolge nach Nutzen/Aufwand.

- [x] **`MainWindow` aufteilen** (war 7.605 Zeilen) – per partial classes ausgelagert:
  `MainWindow.Map.cs`, `.Kiosk.cs`, `.NodeList.cs`, `.NodeCommands.cs`. MainWindow.xaml.cs jetzt
  ~4.670 Zeilen (−39 %). Reine Umsortierung, kein Logikwechsel; Build + App-Smoke-Test grün.
- [x] **`AppSettings` auf `init`-Properties umgestellt** (war 44 positionale Parameter) – Konstruktion
  jetzt per Objekt-Initializer, Defaults leben einmal am Record, `SaveSettings` nutzt `with`
  (neue Felder werden automatisch erhalten statt still zurückgesetzt). **Bonus:** dabei einen
  latenten Bug gefixt – `LanguageComboBox_SelectionChanged` speicherte während `LoadSettings`
  den halb-initialisierten Platzhalter (jetzt via `_suppressDirtyTracking` unterdrückt).
  Verifiziert per Settings-Round-Trip (INI vorher/nachher identisch).
- [x] **Testprojekt angelegt** (`MeshhessenClient.Tests`, xUnit, 25 Tests grün): Settings-Round-Trip
  inkl. Dictionaries + `EnableMessageDb=false`-Regressionsguard, Tile-Mathe (`LatLonToTile`,
  `EstimateTileCount`, Public-Server-Erkennung), Overlay-Registry-Parsing, Vektor-Cache-Pfad-Mapping,
  AppSettings-Defaults/`with`. **Noch offen** (brauchen eine Test-Naht): `MeshtasticProtocolService`-Decode
  (instanz-/event-basiert) und `PkiDecryptionService` (braucht Krypto-Testvektoren).
- [ ] **Vektor/Raster-Duplizierung auflösen** – jedes Karten-Feature existiert zweimal (Pins,
  Linien, Push-Logik), beides in MainWindow. `IMapView` mit zwei Implementierungen
  (`MapsuiMapView`, `VectorMapView`), MainWindow redet gegen eine Schnittstelle. Größere OP –
  lohnt, weil die Karte weiter wächst.
- [ ] **Service-Wiring zentralisieren** (niedrige Prio) – Connection wird an 3 Stellen neu gebaut;
  eine kleine `ConnectionFactory` würde es bündeln.

Bewusst NICHT geplant: MVVM-Vollumbau (Code-behind ist dokumentierte Entscheidung) und
DI-Container (bei dieser Größe Overkill).

