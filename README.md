# SlayTheSpire2.LAN.Multiplayer.Reforged

![LAN Multiplayer](mod_image.png)

A community-maintained fork of **SlayTheSpire2.LAN.Multiplayer** for **Slay the Spire 2**.

This project updates the original LAN Multiplayer mod to remain compatible with newer versions of Slay the Spire 2 while preserving its original gameplay and networking experience.

It replaces the standard online transport path with a direct ENet connection, allowing players on the same local network—or connected through a virtual LAN—to host and join multiplayer games without relying on Steam lobbies or external matchmaking services.

> This is an unofficial community project and is not affiliated with Mega Crit.

* [English](README.md)
* [简体中文](README.zh-CN.md)

---

> [!WARNING]
> Do not enable the original `SlayTheSpire2.LAN.Multiplayer` mod and LAN Multiplayer: Reforged at the same time. Remove or disable the original mod before installing this fork.

## Features

* Host multiplayer games over a local network
* Join multiplayer sessions using an IP address and port
* Automatically discover joinable games on the local network
* Display live discovery latency, rolling packet loss and connection quality
* Supports Standard, Daily and Custom multiplayer runs
* Continue existing LAN multiplayer saves
* Uses separate LAN multiplayer save data
* Displays available local and public network addresses
* Click an address to copy it
* Supports physical LAN and virtual LAN connections
* Uses the game's existing multiplayer synchronization systems
* Includes translated LAN settings and interface text
* Supports configuration through ModConfig when installed

---

## Requirements and Compatibility

Required:

* Slay the Spire 2
* Windows
* BaseLib
* The same game version on every computer
* The same version of LAN Multiplayer: Reforged on every computer
* The same gameplay-changing mods, mod versions and load order on every computer

Optional:

* ModConfig

Tested with:

```text
Slay the Spire 2 stable (assembly set supplied 2026-08-24)
Slay the Spire 2 beta (assembly set supplied 2026-08-24)
Godot 4.5.1 Mono
.NET 9
```

Version 2 uses one DLL for both stable and beta. Known API differences are resolved at runtime and individual Harmony patch groups fail independently, so an unrelated game API change no longer prevents the entire mod from loading.

The maximum lobby size is intentionally limited to the game's native four slots. This keeps LAN traffic on the vanilla multiplayer wire format and avoids fragile custom lobby serialization.

BaseLib is a required dependency. The mod will not load correctly if BaseLib is missing or disabled.

Because Slay the Spire 2 is still being actively updated, future game versions may change internal APIs and require this mod to be rebuilt or updated.

---

## Installation

1. Install BaseLib and confirm that it is enabled.
2. Download the latest LAN Multiplayer: Reforged release.
3. Extract the archive.
4. Copy the included `mods` folder into the Slay the Spire 2 installation directory.

Example:

```text
Slay the Spire 2/
├─ SlayTheSpire2.exe
└─ mods/
   ├─ BaseLib/
   └─ SlayTheSpire2.LAN.Multiplayer.Reforged/
      ├─ mod_manifest.json
      ├─ SlayTheSpire2.LAN.Multiplayer.Reforged.dll
      └─ SlayTheSpire2.LAN.Multiplayer.Reforged.pck
```

5. Start the game.
6. Confirm that both BaseLib and LAN Multiplayer: Reforged are loaded.
7. Confirm that the game reports that it is running modded.

Do not keep the original LAN Multiplayer mod enabled alongside the Reforged version.

---

## Hosting a LAN Game

1. Start Slay the Spire 2.
2. Open the multiplayer menu.
3. Select the LAN hosting option.
4. Choose Standard, Daily or Custom mode.
5. Share the displayed address and port with the other players.

The host must keep the game open while other players connect and throughout the multiplayer session.

---

## Joining a LAN Game

1. Start Slay the Spire 2.
2. Open the multiplayer join screen.
3. Locate the LAN connection panel.
4. Enter the host's address.
5. Press the LAN join button.

Supported address formats include:

```text
192.168.1.100
192.168.1.100:33771
localhost
```

When no port is entered, the configured default LAN port is used.

### Nearby LAN rooms

While the join screen is open, the mod sends a small discovery probe once per second. Hosts answer directly and appear in the nearby-room list with occupancy, game mode, latency, rolling packet loss and an overall quality rating. Select an available room to join it immediately.

Discovery uses UDP port `33770`. If discovery is blocked by a firewall or unsupported by the current virtual network, manual address entry continues to work normally. The discovery responder only accepts probes originating from a locally attached IPv4 subnet.

---

## Playing Through a Virtual LAN

The mod can also work through virtual LAN software, provided that every player can directly reach the host's virtual IP address.

Common options include:

* Tailscale
* ZeroTier
* Radmin VPN
* Hamachi

Enter the host's virtual LAN address in the join screen exactly as you would enter a normal local IP address.

A virtual LAN service does not automatically guarantee connectivity. Firewall rules, network profiles and routing settings may still need to be adjusted.

---

## Configuration

BaseLib is required for this mod.

ModConfig is optional. When ModConfig is installed, the following LAN options are available through its configuration interface:

* Default LAN port
* Maximum player count
* Connection timeout
* Default server address
* Remember last joined address
* Player name
* Preferred Net ID

Without ModConfig, LAN Multiplayer: Reforged continues to use its original in-game settings interface.

Configuration values continue to use the mod's existing LAN settings storage, regardless of whether they are changed through ModConfig or the fallback settings interface.

ModConfig may have its own dependencies and installation requirements.

---

## Mod Compatibility

All players should use the same:

* Game version
* LAN Multiplayer: Reforged version
* BaseLib version
* Gameplay-changing mods
* Mod versions
* Mod load order

Cosmetic differences may work, but identical installations are strongly recommended.

Differences in cards, relics, characters, events, enemies, acts or synchronization-related patches can cause connection failures or desynchronization.

For troubleshooting, first test with only these mods enabled:

```text
BaseLib
LAN Multiplayer: Reforged
```

Add other mods back only after confirming that the basic LAN connection works.

---

## Localization

The Reforged resource package includes localized interface entries for the LAN multiplayer menus and settings.

Localization resources, paths and keys have been migrated to the Reforged project identifier:

```text
SlayTheSpire2.LAN.Multiplayer.Reforged
```

The included PCK uses the Godot 4.5.1 Pack V3 format.

If raw localization keys are displayed instead of translated text, verify that the Reforged PCK is installed beside the DLL and that an older LAN Multiplayer PCK is not being loaded.

---

## Building from Source

### Prerequisites

* .NET 9 SDK
* Git
* An installed copy of Slay the Spire 2
* BaseLib or the required BaseLib assembly references
* Required game assemblies from a local Slay the Spire 2 installation

Clone the repository:

```powershell
git clone https://github.com/PageSecOnd/SlayTheSpire2.LAN.Multiplayer.Reforged.git
cd SlayTheSpire2.LAN.Multiplayer.Reforged
```

Build the project by running:

```bat
build.bat
```

Set `STS2_DIR` to the directory containing `sts2.dll`, `GodotSharp.dll` and `0Harmony.dll` when the game is not installed at the project default path. Maintainers can validate both channels with:

```powershell
./scripts/verify-dual-version.ps1 -StableDir <stable-data-dir> -BetaDir <beta-data-dir>
```

See [Compatibility architecture](docs/COMPATIBILITY.md) before adapting the mod to a future game update.

The compiled DLL will normally be located at:

```text
SlayTheSpire2.LAN.Multiplayer.Reforged/bin/Release/net9.0/
```

The project references assemblies from a locally installed copy of Slay the Spire 2. These game assemblies are not included in the repository and must not be redistributed.

---

## Troubleshooting

### The mod does not appear in the game

Verify that:

* The mod folder is placed directly inside the game's `mods` directory
* The folder contains `mod_manifest.json`
* BaseLib is installed and enabled
* The required DLL and PCK files are present
* The original LAN Multiplayer mod is disabled

### The mod reports a missing dependency

Confirm that BaseLib is installed, enabled and compatible with the current game version.

BaseLib is required. LAN Multiplayer: Reforged cannot be used as a standalone mod without it.

### Settings display untranslated localization keys

For example:

```text
settings_ui.SlayTheSpire2.LAN.Multiplayer.Reforged.HOST_PORT
```

Verify that:

* `SlayTheSpire2.LAN.Multiplayer.Reforged.pck` is installed
* The PCK is located beside the mod DLL
* The PCK uses the Godot 4.5.1 Pack V3 format
* The original LAN Multiplayer PCK is not being loaded
* The DLL and PCK come from the same release

### The client cannot connect

Verify that:

* The host has already opened a LAN lobby
* The IP address is correct
* The port is correct
* Both computers are on the same LAN or virtual LAN
* Windows Firewall allows the game
* The host and client use the same game version
* The host and client use the same mod version
* The host and client use compatible BaseLib versions

### Connection timed out

A timeout usually indicates that the client cannot reach the host.

Check that:

* The host is still in the multiplayer lobby
* The host address is reachable
* The selected port is not blocked
* The virtual LAN connection is active
* A VPN is not routing the connection through the wrong interface
* The connection timeout value is not set too low

### Players disconnect or desynchronize

Temporarily disable every other gameplay-changing mod and test again.

If the problem disappears, re-enable mods in small groups until the incompatible mod is identified.

All participants should use matching game versions, mod versions, BaseLib versions and mod load order.

---

## Screenshots

![Screenshot 1](Screenshot/screenshot1.png)

![Screenshot 2](Screenshot/screenshot2.png)

![Screenshot 3](Screenshot/screenshot3.png)

![Screenshot 4](Screenshot/screenshot4.png)

![Screenshot 5](Screenshot/screenshot5.png)

![Screenshot 6](Screenshot/screenshot6.png)

---

## Contributing

Bug reports and pull requests are welcome.

When reporting a problem, include:

* Game version
* LAN Multiplayer: Reforged version
* BaseLib version
* ModConfig version, when installed
* Full game log
* Host and client operating systems
* Installed mod list
* Steps required to reproduce the issue

Source repository:

https://github.com/PageSecOnd/SlayTheSpire2.LAN.Multiplayer.Reforged

---

## Acknowledgements

This project is a fork of **SlayTheSpire2.LAN.Multiplayer**, originally created by **kmyuhkyuk**.

Original repository:

https://github.com/kmyuhkyuk/SlayTheSpire2.LAN.Multiplayer

The original LAN multiplayer implementation was created by kmyuhkyuk.

This fork builds upon that work with compatibility fixes, maintenance updates, BaseLib integration, optional ModConfig support and migrated Reforged localization resources.

The original project is licensed under GPL-3.0, and this fork continues to be distributed under the same license.

---

## License

This project is licensed under the GNU General Public License v3.0.

See [LICENSE](LICENSE) for details.
