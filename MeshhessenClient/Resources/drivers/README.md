# Mitgelieferte Treiber

## CH341PAR-WIN1X-2.6.2025.4.zip

WCH-Treiberpaket **CH341PAR** (USB-Bridge CH341A im Parallel-/SPI-/I²C-Modus, Geräteklasse „WCH“) für Windows 10/11 x64,
wie es der Elecrow MeshStick braucht. Inhalt = Ordner `WIN 1X` aus `CH341PAR.EXE` von wch-ic.com (Stand 14.04.2025,
DriverVer 2.6.2025.04, Katalog von Microsoft WHQL-signiert), unverändert:

| Datei | Zweck |
|---|---|
| `CH341WDM.INF`, `CH341WDM.CAT` | Treiberpaket (Hardware-IDs `USB\VID_1A86&PID_5512`, `USB\VID_4348&PID_5512`, CH347/CH339) |
| `CH341W64.SYS`, `CH341M64.SYS`, `CH341WDM.SYS` | Kernel-Treiber (64-Bit WDM/Mini, 32-Bit) |
| `CH341DLLA64.DLL`, `CH341DLL.DLL` | Anwendungsbibliothek 64/32 Bit – meshtasticd lädt `CH341DLLA64.DLL` zur Laufzeit |
| `CH347DLLA64.DLL`, `CH347DLL.DLL` | Bibliothek für CH347-Geräte (im selben Paket, vom INF mit abgedeckt) |

Der MeshStick-Assistent (`Services/MeshStick/Ch341DriverSetup.cs`) entpackt die ZIP nach `%TEMP%` und installiert mit
Administratorrechten `pnputil /add-driver CH341WDM.INF /install`; die DLLs werden wie vom WCH-Setup nach
`System32` (64 Bit) und `SysWOW64` (32 Bit) kopiert. Die ZIP ist als eingebettete Ressource
`MeshhessenClient.Drivers.CH341PAR.zip` in der EXE enthalten (siehe `MeshhessenClient.csproj`).

Neuere Versionen: https://www.wch-ic.com/downloads/CH341PAR_EXE.html – der Assistent kann wahlweise auch eine dort
heruntergeladene `CH341PAR.EXE`/`.ZIP` installieren.
