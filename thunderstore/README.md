# Polyfill

> 🛟 **Need help or found a bug?** [support.doodesch.de/polyfill](https://support.doodesch.de/polyfill)

**Your old mods stopped working after a Schedule I update? Polyfill gets a lot of them running again.**

![Version](https://img.shields.io/badge/version-0.1.3-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Status](https://img.shields.io/badge/status-early%20beta-orange)

> ### ⚠ Early beta
>
> Still got a mod that will not run? Open the console, type `polyfillexport`, and send me the file it
> creates: [support.doodesch.de/polyfill](https://support.doodesch.de/polyfill)

The game gets an update. Mods made before it stop working. Polyfill patches the game so they run again.

It patches the game, not your mods. Your own files are never changed, and `polyfillrestore` takes the
patch back out.

It cannot save every mod. The console tells you which ones.

## Tested on

| | |
|---|---|
| T.H.M - The Hitman Mod 5.0.2 | runs |
| OverTheCounter Dispensary 2.0.10 | runs; 29 of 40 names repaired, and two doors it looks up by name stay missing |

## Requirements

[MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.3 or newer. Nothing else.

## Install

1. `Polyfill.Boot.dll` into the `Plugins` folder
2. `Polyfill.dll` into the `Mods` folder

Two files, two folders. The first one has to be in `Plugins` or nothing happens.

## Console

Switch the developer console on in the game's settings.

| | |
|---|---|
| `polyfill` | what it did |
| `polyfillexport` | creates the file to send me |
| `polyfillrestore` | take the patch back out |

## Good to know

- Made for Schedule I 0.4.6f12
- Only changes things on your own PC. Your save is untouched
- In multiplayer, everyone using the old mod needs it too
- To remove it: `polyfillrestore`, restart, delete the two files

Schedule I by TVGS. Built on [MelonLoader](https://github.com/LavaGang/MelonLoader) and
[Mono.Cecil](https://github.com/jbevain/cecil). MIT licence.
