# 🔌 MeshStick assistant

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/meshstick/)

The **Elecrow MeshStick** is a USB stick with an SX1262 radio and TCXO – but no processor of its own. The Meshtastic node therefore runs on the PC: as **meshtasticd**, the native Meshtastic firmware for Linux, macOS and Windows. On Windows, meshtasticd is a service that starts automatically at boot; the Meshhessen Client connects to it like to any Wi-Fi node over TCP – just locally (`127.0.0.1:4403`).

The assistant on the **Tools tab** ("🔌 Set up MeshStick …") walks through every step from "stick plugged in" to "node on air with the Meshhessen profile".

!!! note "Requirements"
    * **Internet** – the driver and meshtasticd are downloaded.
    * **Administrator rights** – installing the driver and setting up the Windows service each show a Windows security prompt (UAC). Without admin rights on the PC it cannot work.
    * The stick sits **directly** in a USB port of the PC (USB hubs can upset the CH341 bridge).
    * 64-bit Windows 10/11.

## The six steps

| Step | What happens |
|---|---|
| **1 · Requirements & driver** | The assistant checks the internet connection and looks for the stick in the device tree (USB id `VID_1A86&PID_5512`). Without a driver Windows reports error code 28. The stick needs the **CH341PAR** driver from the chip maker WCH – *not* the serial CH340/CH341 driver. The client **ships the package** (version 2.6.2025.4, WHQL-signed) and installs it with one click and no further dialogs – only the Windows security prompt. For a newer version download `CH341PAR.EXE` or `.ZIP` from the WCH page; the assistant picks the file up from your Downloads folder (for the EXE, click "Install" in the WCH window). It then re-checks every 3 seconds until the stick reports "detected, driver installed". |
| **2 · Get meshtasticd** | Shows whether meshtasticd is already installed (version, service state). Otherwise choose a source: **Meshhessen build** (GitHub `SMLunchen/mh_firmware`, default), **official Meshtastic releases** (once they carry a Windows MSI) or a **local MSI file**. The download goes to the folder `meshtasticd\` next to the client EXE. |
| **3 · Configuration & service** | Shows the `config.yaml` that will be written (MeshStick pins, TCP port, default 4403). "Install & set up" does everything with **one** security prompt: write the configuration to `%ProgramData%\Meshtastic\config.yaml`, install the MSI, set the service rights (see below), start the service – and waits until the port answers. The log is shown in the window and saved as `meshtasticd\setup.log`. **"Test daemon"** runs meshtasticd in the foreground for 12 seconds with exactly this configuration and shows its output – before the installation (the EXE is pulled from the MSI without admin rights) or when the service does not come up. Still running after 12 seconds means the configuration works. |
| **4 · Connect** | The client connects over TCP to `127.0.0.1:<port>` and waits until configuration and channels are loaded. This connection is stored as the last used one and pre-selected on the next start. |
| **5 · LoRa settings** | Region, modem preset, hop limit, TX power – or, with one click, the **Meshhessen profile** (EU868, ShortSlow, hop 7, MQTT allowed, channel "Mesh Hessen"), identical to the Meshhessen button on the Channels tab. After saving, the node restarts, the client reconnects automatically and reads the values back for verification. |
| **6 · Done** | Summary and folder link. From now on: PC on → service runs → connect the client. |

## What the assistant changes on the system

* **Driver:** WCH CH341PAR, installed with `pnputil /add-driver CH341WDM.INF /install` (device class "WCH", service `CH341_A64`). The INF places `CH341DLLA64.DLL` in `System32` and `CH341DLL.DLL` in `SysWOW64` – meshtasticd loads the 64-bit DLL at runtime, without it the node does not start. The bundled package is the unchanged `WIN 1X` folder from WCH's `CH341PAR.EXE` (see `Resources/drivers/README.md` in the source tree).
* **meshtasticd:** installed to `%ProgramFiles%\Meshtastic\meshtasticd.exe` (MSI from the Meshtastic build pipeline, `packaging/windows/meshtasticd.wxs`), registered as service **meshtasticd** ("Meshtastic Daemon"), automatic start, LocalSystem account.
* **Configuration:** `%ProgramData%\Meshtastic\config.yaml` – the pin map from the firmware template `lora-usb-meshstick-1262.yaml` (SX1262 over `spidev: ch341`, DIO2 as RF switch, TCXO) plus `General.APIPort`. The node database lives in `%ProgramData%\Meshtastic\data`.
* **Service rights:** the assistant allows logged-in users to start and stop the service (`sc sdset`). Reason: a Meshtastic "reboot" (after every settings save) ends the node's process. The client then starts the service itself on the next connect – without these rights it would need admin rights every time. Automatic restarts after crashes are configured as well.

## Day to day

* The service runs independently of the client – the node is in the mesh even with the client closed.
* Connecting: connection type **TCP/WiFi**, host `127.0.0.1`, port `4403` (pre-selected after the assistant). If the port does not answer and the service is stopped, the client starts it and waits up to 25 seconds.
* All further settings (name, position, modules, channels) via **Node configuration**, like for any other node.
* The port can be changed in the INI (`MeshStickApiPort`), the download source as well (`MeshStickMirrorRepo`, default `SMLunchen/mh_firmware`).

## Where does meshtasticd for Windows come from?

Since 2026 the Meshtastic firmware contains a native Windows build with MSI and Windows service – the build pipeline runs on every release, but the **official releases do not attach the MSI yet**. The Meshhessen build comes from exactly this pipeline in the fork `SMLunchen/mh_firmware`: a daily workflow checks the newest upstream release, builds the MSI from it with upstream's unchanged build steps and publishes it as release `meshtasticd-v<version>` with the asset `meshtasticd-<version>-x64.msi` plus a SHA-256 file. The assistant downloads it from there. Once the official releases carry the MSI, it shows up under "Official Meshtastic releases".

**Updates:** run the assistant again – step 2 shows the installed version and the available builds; a newer MSI replaces the installation in place (configuration and node database are kept). The configuration itself is rewritten and the service restarted.

## Troubleshooting

| Symptom | Cause / remedy |
|---|---|
| Stick "not found" | Not plugged in, bad cable/hub, or a different CH341 USB device – look for `VID_1A86&PID_5512` in Device Manager. Try directly on the PC without a hub. |
| "found, but without a driver (error code 28)" | CH341PAR not installed yet, or only the serial driver CH341SER. Click "Install driver" in the assistant (bundled package). |
| "driver present, but CH341DLLA64.DLL is missing" | Driver installed some other way, DLL missing in `System32`. Click "Install driver" in the assistant again – it puts the DLL in place. |
| Microsoft Update Catalog offers a CH341A driver | That is a 2009 version without the DLL – do not use it, the assistant ships the current one. |
| "Installation failed (MSI error)" | The MSI starts the service right after copying; if the node does not come up (MSI log: *Error 1920, Service 'Meshtastic Daemon' failed to start*, result 1603), Windows Installer rolls everything back. That is why the assistant writes the configuration **before** the MSI. If it persists: "Test daemon" shows meshtasticd's output – typically an unplugged stick, a missing DLL or a port in use. |
| "Setup failed (21)" | `meshtasticd.exe` is missing under `%ProgramFiles%\Meshtastic` after the installation – broken MSI or wrong architecture (x64 only). |
| Port does not answer | Service started, but the node does not come up: stick unplugged? Driver/DLL missing? Another process on the port? The daemon's output is in the Windows Event Viewer (Application). Check the state with `sc query meshtasticd`; the assistant can be repeated at any time. |
| Connection drops after saving settings | Normal: the node restarts, the process ends briefly. The client reconnects automatically (attempts for up to 7 minutes) and starts the service if needed. |
| Security prompt declined | The step was cancelled, nothing happened – just click again. |

## Uninstalling

Remove meshtasticd via **Apps & Features** (entry "meshtasticd"). Configuration and node database in `%ProgramData%\Meshtastic` are kept on purpose; the CH341PAR driver is removed through Device Manager.

## Further reading

* [🚀 Getting started & connection](getting-started.md) · [🔧 Node management & remote admin](node-management.md)
* [Meshtastic docs: meshtasticd](https://meshtastic.org/docs/meshtasticd/) · [Elecrow MeshStick](https://meshtastic.org/docs/hardware/devices/elecrow/meshstick/)
