# ATF Wyvern Mod

A quality-of-life mod for Nuclear Option that enhances gameplay with several useful features for improved target management, weapon tracking, and situational awareness.

## Features

- **Smart Laser Deconfliction**: Automatically distributes unique targets among multiple players' laser designators to prevent conflicts when multiple players select the same targets. Ensures each player's lasers get assigned different targets for optimal coordination.

- **Time-To-Impact Readout**: Displays real-time estimated time until impact for bombs and missiles on the HUD. Calculates TTI based on projectile type (ballistic or guided) and shows weapon type for better mission planning.

- **Master Safe Slot**: Allows gathering target information (range, bearing, speed) on friendly units without requiring a weapon lock. Perfect for coordination and situational awareness when working with friendly forces. **How to use**: Simply select an empty weapon slot (or safe weapon slot) and point at a friendly unit - you'll see target information displayed even without a weapon lock. No additional key press required - it works automatically when enabled.

- **Enhanced Laser Reticle Visibility**: Makes the laser reticle on the target camera significantly more visible in night vision/IR mode. Uses bright cyan coloring to ensure the reticle remains clearly visible in low-light conditions.

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) for Nuclear Option
2. Place `ATFWyvernMod.dll` in your `BepInEx/plugins` folder
3. Launch the game - the mod will load automatically

## Configuration

All features can be toggled individually via the BepInEx configuration file located at `BepInEx/config/com.atf.wyvernmod.cfg`. Features are enabled by default but can be disabled if desired.

### In-Game Toggle

Press **F9** (default) to toggle all mod features on/off during gameplay. When toggled, you'll see a log message in the BepInEx console showing the status of each feature. This is useful for quickly disabling the mod if needed without restarting the game.

**Note**: The toggle affects all features at once. Individual features can still be enabled/disabled via the config file.

## Requirements

- Nuclear Option
- BepInEx 5.x or later
- HarmonyLib (included with BepInEx)
