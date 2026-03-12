# ATF Wyvern Mod

A quality-of-life mod for Nuclear Option that enhances gameplay with several useful features for improved target management, weapon tracking, and situational awareness.

## Features

- **Smart Laser Deconfliction**: Automatically distributes unique targets among multiple players' laser designators to prevent conflicts when multiple players select the same targets. Ensures each player's lasers get assigned different targets for optimal coordination.

- **Time-To-Impact Readout**: Displays real-time estimated time until impact for bombs and missiles on the HUD. Calculates TTI based on projectile type (ballistic or guided) and shows weapon type for better mission planning.

- **Master Safe Slot**: Allows gathering target information (range, bearing, speed) on friendly units without requiring a weapon lock. Perfect for coordination and situational awareness when working with friendly forces.

- **Enhanced Laser Reticle Visibility**: Makes the laser reticle on the target camera significantly more visible in night vision/IR mode. Uses bright cyan coloring to ensure the reticle remains clearly visible in low-light conditions.

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) for Nuclear Option
2. Place `ATFWyvernMod.dll` in your `BepInEx/plugins` folder
3. Launch the game - the mod will load automatically

## Configuration

All features can be toggled individually via the BepInEx configuration file located at `BepInEx/config/com.atf.wyvernmod.cfg`. Features are enabled by default but can be disabled if desired.

## Requirements

- Nuclear Option
- BepInEx 5.x or later
- HarmonyLib (included with BepInEx)
