# ⬆️ Firmware Update

[← Handbook home](index.md) · [🇩🇪 Deutsche Version](https://meshhessenclient.de/handbuch/de/firmware-update/)

The client updates your node's firmware directly – no web flasher, no command line – with a guided assistant in the **Tools tab** ("⬆️ Start firmware update …"). It works like the Meshtastic Android app: firmware from the official releases, a device catalog for validation, four transfer methods. Nothing is sent to the device until the pre-flight page shows no red item.

## Transfer methods

| Method | Devices | Connection to the client | How it works |
|---|---|---|---|
| **📶 Wi-Fi OTA** | ESP32 (Heltec V3/V4, T-Beam, T-Deck, Station G2 …) | any; Wi-Fi must be configured on the node | The node reboots into its OTA loader and receives the firmware on TCP port 3232. The loader's IP is found via UDP beacon (PC on the same network). The loader verifies the SHA-256 hash and activates the image only on a match. Fastest method. |
| **🔵 Bluetooth OTA** | ESP32 | Bluetooth | Same loader, but over Bluetooth LE: one packet → one acknowledgement. Takes a few minutes, needs no network – ideal with an external dongle and a directional antenna. |
| **🔵 Bluetooth DFU** | nRF52 (RAK4631, T-Echo, Heltec T114, Nano G2 …) | Bluetooth | Nordic DFU through the Adafruit bootloader. The client detects automatically whether the bootloader speaks **Legacy DFU** (the default on RAK & co.) or **Secure DFU**, resumes/retries after a link drop and clears a stuck DFU session. If interrupted the device stays in the bootloader – nothing is broken, just start again. |
| **💾 USB drive (UF2)** | nRF52, RP2040 | any + USB cable | The node reboots as a USB drive (bootloader); the client copies the `.uf2` automatically, the bootloader checks the chip family and reboots. The safest method. |

**Not supported:** flashing ESP32 over serial. Enable Wi-Fi on the device instead (then Wi-Fi OTA) or use the official [web flasher](https://flasher.meshtastic.org) – the assistant links to it.

!!! note "Requirement for ESP32 OTA"
    The OTA loader protocol (`ota_request`) exists since firmware **2.7.17**. Update older ESP32 nodes once via the web flasher; after that everything works from the client.

## The six steps

1. **Device & catalog** – shows hardware model, architecture, current firmware, build identifier (`pio_env`) and connection. The **device catalog** (api.meshtastic.org) is loaded and cached locally; offline the assistant works from the cache and says so when no validation is possible. Warnings: device no longer actively supported, bootloader upgrade required before the first BLE update (with a link to the guide). Below that the **active Bluetooth adapter** – see the box below.
2. **Transfer method** – only the methods possible for device and connection are selectable; all others show the reason.
3. **Choose firmware** – **Stable** (recommended), **Alpha** (pre-releases, with a warning) or a **local file**. Release notes, version comparison (update / same version / ⚠ downgrade) and a check whether the release contains a build for your device at all. Local files: `.bin` (ESP32), `-ota.zip` (nRF52 DFU), `.uf2` (USB).
4. **Pre-flight & start** – the firmware is downloaded (or read) and checked: manifest MD5/size, ESP image header with chip id against the device architecture, app descriptor (no factory image), UF2 family id, DFU manifest, file name against build identifier, battery level, transport hints. Optionally the **device configuration is backed up as JSON** first (`firmware\backups\` – contains channel keys, treat accordingly). Red items block the start.
5. **Transfer** – progress, phase and log. The client releases the mesh connection on purpose (no auto-reconnect) and connects to the loader or bootloader.
6. **Done** – result; "Reconnect" restores the connection and shows the new firmware version the device reports.

## Bluetooth adapter & directional antenna

Windows always uses exactly **one** adapter for Bluetooth LE; selecting one inside an application is technically impossible. The assistant shows the active adapter (name and address). If several Bluetooth adapters are built in or plugged in, a hint appears: **disable the unwanted adapter in Device Manager**, toggle Bluetooth off and on, and reopen the assistant. This way an external dongle with a directional antenna can be used deliberately for distant nodes.

## Files & cache

Everything lives in the `firmware\` folder next to the EXE: `deviceHardware.json` and `releases.json` (catalog cache), one sub-folder per version with the downloaded images (prefetch – a release downloaded once can be flashed offline later) and `backups\` with the configuration backups. "Firmware folder" in the assistant opens it.

## Recovery

If an update is interrupted, an OTA/UF2 device simply keeps running the old firmware (the image is only activated after successful verification). An nRF52 stays in its bootloader after an interrupted DFU: open the assistant **without** a mesh connection – it then offers Bluetooth DFU (last Bluetooth address) or the UF2 drive with a local file. On nRF52/RP2040 a UF2 drive also appears manually by double-tapping the RESET button.

## Typical errors

* **"Bootloader not found"** (nRF52): the device did not enter DFU mode – usually a stale Windows pairing blocks the trigger. Remove the device in Windows Bluetooth settings, pair again, retry.
* **"OTA loader unreachable"** (Wi-Fi): PC and node on the same network? Firewall for UDP/TCP 3232? On first use Windows may ask to allow network access for the client.
* **"The device refuses OTA mode"**: the firmware reports the reason (e.g. missing OTA partition or a loader without BLE support). The mesh connection stays up.
* **Hash rejected / hash verification failed**: the image was **not** activated, the old firmware keeps running. Try again.
