<p align="center"><img src="Assets/logo.png" width="128" alt="Perialls logo"></p>

# Perialls

[Português](README.md) · **English**

A lightweight app for Windows and Linux that shows your peripherals in one place: what is connected,
the battery, the RGB color and the mouse DPI.

It does not replace the brands' apps (G Hub, Synapse etc.). It is just a simple screen for everyday use.

- **Lightweight:** a single screen. It does not start with the system and leaves nothing running in the background.
- **Generic:** recognizes anyone's devices, using what the system itself reports.
  Unknown devices show up too, with just their name and status.

## What it does

| Feature | Works with |
|---|---|
| Device list and status | Any USB, HID, Bluetooth or audio device |
| Battery | Bluetooth devices that report battery, controllers (XInput on Windows; the ones the kernel knows on Linux) and Logitech (HID++) |
| RGB color | Everything [OpenRGB](https://openrgb.org) recognizes (motherboard, fans, strips, peripherals) |
| DPI | Logitech mice with HID++ |
| Effect, color, brightness, speed and polling rate | Akko/MonsGeek FUN60 PRO keyboard |
| Extra features through drivers | Devices with a driver in [`drivers/`](drivers), downloaded by the app itself |
| Sync RGB | The same color on every RGB device, in one click |

## How to use

1. Download it from the [Releases](../../releases) page and open it. Nothing to install.
   - **Windows:** `Perialls.exe`.
   - **Linux:** `Perialls`. Make it executable (`chmod +x Perialls`) and open it.

   > **"Windows protected your PC"?** This warning shows up because the `.exe` is not digitally
   > signed (the certificate costs money). Click **More info → Run anyway**. All the code is here in
   > the repository, if you want to check it or build it yourself.

2. **Linux, for Logitech and Akko/MonsGeek:** Linux only lets root talk directly to these devices.
   Install the udev rule once (it only grants access to whoever is using the PC):
   ```
   sudo curl -L -o /etc/udev/rules.d/70-perialls.rules https://raw.githubusercontent.com/arthurgdelima-dev/perialls/main/linux/70-perialls.rules
   sudo udevadm control --reload && sudo udevadm trigger
   ```
   (Or, with the repository downloaded: `sudo cp linux/70-perialls.rules /etc/udev/rules.d/`.)
   Then unplug the device and plug it back in. Without the rule they show up in the list, but without
   battery, DPI and lighting (the app's footer tells you).
3. **To control RGB:** install [OpenRGB](https://openrgb.org) and keep it open with the
   **SDK Server** on (*SDK Server* tab → *Start Server*). If OpenRGB is installed, Perialls shows a
   button in the sidebar that opens it with the server already on.
4. Click **Refresh** whenever you connect or disconnect a device. The app does not keep checking on
   its own.

**The app never changes anything on its own.** When it opens, it only reads the devices. Color, DPI,
polling rate and the rest are only sent when you change something on screen. Your last choice for each
device is kept in `%AppData%\Perialls\settings.json` (on Linux, `~/.config/Perialls/settings.json`), only to
show on screen what you picked when the device does not report its own state. It is not reapplied on
startup. When the device has its own memory and OpenRGB or the driver know how to write to it, the
setting is also saved on the device.

**Double-click a device's name** to rename it (Enter saves, Esc cancels, empty restores the original
name). Names are kept in `%AppData%\Perialls\nomes.txt` (on Linux, `~/.config/Perialls/nomes.txt`).

The **drop** button in the sidebar **syncs RGB**: the chosen color goes to every RGB device at once. If
one fails, the footer says which one, and the others get the color normally.

The **language** (Portuguese or English) is at the bottom of the sidebar (`PT`/`EN`). The first time,
the app uses the system language.

Hover over a card to **hide** a device you don't want to see. The eye button in the sidebar shows the
hidden ones again. The hidden list is kept in `%AppData%\Perialls\ocultos.txt` (on Linux, `~/.config/Perialls/ocultos.txt`).

### Drivers

A small icon shows up in the corner of each card:

| Icon | Meaning |
|---|---|
| ⬇ | There is a driver for the device. Click to download it: the features show up right away. |
| ⟳ | There is a new version of the driver. |
| ✓ | Driver installed. |
| Fan | [OpenRGB](https://openrgb.org) controls this device's RGB. Click to open its website. |
| ➕ | No support yet. Click to open a request on GitHub, already filled in with the device's details. |

Drivers are just JSON files that describe the commands. The app never downloads programs, checks each
driver's hash against the index and blocks dangerous commands (firmware, reset). They are kept in
`%LocalAppData%\Perialls\drivers` (on Linux, `~/.local/share/Perialls/drivers`).

Want to write a driver for a device? See [how to add a device](docs/como-adicionar-um-aparelho.md)
(in Portuguese).

### Akko/MonsGeek FUN60 PRO keyboard
- Close the official software (MonsGeek Driver and the `iot_driver` that runs in the background) and the
  web driver before using it, so the two don't fight over the keyboard.
- On Linux, it needs the udev rule (step 2 above).
- The app checks the model by the ID the keyboard itself reports. If it is not a known model, the
  controls don't show up.
- **Polling rate:** the official software only offers this option on newer firmware. Perialls offers it
  on any firmware, so use it at your own risk. If the keyboard stops responding, unplug it and plug it
  back in.
- How the protocol works: [docs/protocolo-monsgeek.md](docs/protocolo-monsgeek.md) (in Portuguese).

## Building

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet run --project Perialls.csproj
```

To build the single executable (about 45 MB, because .NET goes with it). You can build both from
either system:

```
dotnet publish Perialls.csproj -p:PublishProfile=win-x64     # goes to publish\Perialls.exe
dotnet publish Perialls.csproj -p:PublishProfile=linux-x64   # goes to publish/linux/Perialls
```

## Adding a device photo

1. Put a PNG in `Assets/Devices/`.
2. Add a line to `Assets/Devices/devices.json` linking the device's USB code to the file:
   ```json
   "VID_046D&PID_4093": "logitech-pro-x-superlight.png"
   ```
3. Build again. Devices without a photo show their type icon instead.

The USB code (VID/PID) shows up in Device Manager: *Properties* → *Details* → *Hardware Ids*.
On Linux, in the `lsusb` command (e.g. `ID 046d:4093` becomes `VID_046D&PID_4093`).

## Libraries

- [Avalonia](https://avaloniaui.net): the screen, the same on Windows and Linux.
- [HidSharp](https://www.zer7.com/software/hidsharp): talks to HID devices (Logitech and Akko).
- [OpenRGB.NET](https://github.com/diogotr7/OpenRGB.NET): OpenRGB SDK client over the network.
  OpenRGB itself is a separate program (GPL) and does not ship with Perialls.
- System.Management: lists devices on Windows (WMI). On Linux the list comes from `/sys` and
  Bluetooth from BlueZ (through `busctl`, which ships with systemd).

## License

[MIT](LICENSE): use, copy and modify freely, as long as you keep the copyright notice.
