# Sineus Arena - DPS Meter

A sleek, highly customizable BepInEx Damage & DPS Meter mod for **Sineus Arena** (inspired by *Details! Damage Meter* for World of Warcraft).

## ⚔️ Features

- 📊 **Real-Time Multiplayer Combat Tracking**: Tracks total damage, DPS, total kills, and percentage share (`%`) of party damage across all players in multiplayer matches.
- 🖼️ **Equipped Hero Skin Icons**: Displays each player's active character skin preview icon at the start of their row.
- ⏱️ **HUD Timer Sync**: Synchronizes seamlessly with `SineusArena.SessionTimerService` for 1:1 match timer sync and accurate DPS calculations.
- 🎨 **Subtle Modern UI Aesthetics**: Premium dark glassmorphism design with rank color indicators (Gold 🥇, Silver 🥈, Bronze 🥉), thin blue border frame, and subtle damage bar fills.
- ⚙️ **Configurable BepInEx Settings**: Customize toggle hotkey (Default: `Delete`), bar opacity, row height, window width, font size, and position auto-saving via `com.github.antigravity.dpsmeter.cfg`.
- ⚡ **Zero-Lag Performance**: Optimized caching with zero scene-wide object scans during combat.

## 📥 Installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx) for Sineus Arena.
2. Download `DpsMeter.dll` from the latest [Release](https://github.com/Snack-tacular/DPSMeter/releases).
3. Place `DpsMeter.dll` into your `Sineus Arena/BepInEx/plugins/` directory.
4. Launch the game! Press **Delete** to toggle the DPS meter window on/off.

## 🛠️ Building from Source

Requirements: [.NET Standard 2.1 SDK](https://dotnet.microsoft.com/download)

```bash
dotnet build DpsMeter.csproj -c Release
```

## 📜 License

MIT License
