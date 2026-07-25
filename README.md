# Sineus Arena - DPS Meter

A World of Warcraft (Details! style) damage meter mod for Sineus Arena.

<p align="center">
  <img src="preview.png" alt="DPS Meter Preview" width="380" />
</p>

## Features

- Real-time multiplayer tracking (Total Damage, DPS, Kills, Damage %)
- Shows selected character skin icons in player rows
- Draggable overlay window with position auto-saving
- Configurable via BepInEx (`com.github.antigravity.dpsmeter.cfg`) for hotkeys, sizes, and opacity

## Installation

1. Make sure [BepInEx 5](https://github.com/BepInEx/BepInEx) is installed for Sineus Arena.
2. Download `DpsMeter.dll` from [Releases](https://github.com/Snack-tacular/DPSMeter/releases).
3. Drop `DpsMeter.dll` into `Sineus Arena/BepInEx/plugins/`.
4. Press **Delete** in-game to toggle the overlay on/off.

## Building from Source

Requires .NET Standard 2.1 SDK.

```bash
dotnet build DpsMeter.csproj -c Release
```

## License

MIT
