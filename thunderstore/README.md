# Polyfill

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/polyfill](https://support.doodesch.de/polyfill).

**Your old mods stopped working after a Schedule I update? Polyfill gets a lot of them running again.**

![Version](https://img.shields.io/badge/version-0.1.1-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Status](https://img.shields.io/badge/status-early%20beta-orange)

> ### ⚠ Early beta
>
> Still got a mod that will not run? Open the console, type `polyfillexport`, and send me the file it
> makes: [support.doodesch.de/polyfill](https://support.doodesch.de/polyfill)

The game gets an update. Mods made before it stop working.

Polyfill patches the game so those mods run again. It never touches the files in your Mods folder, and it
only adds things, so nothing you already have can break because of it.

It cannot save every mod. Some are too far gone. The console tells you which ones.

## Features

- Old mods work again
- Nothing to set up - install it and forget it
- Your mod files are never changed
- One command puts everything back the way it was

## Requirements

- [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.3 or newer
- Nothing else

## Installation

A mod manager does it for you. By hand:

1. `Polyfill.Boot.dll` goes in the `Plugins` folder
2. `Polyfill.dll` goes in the `Mods` folder

Two files, two different folders. The first one has to be in `Plugins` or it does nothing at all.

Using Vortex? Check that `Polyfill.Boot.dll` really ended up in `Plugins`. If not, move it there
yourself.

## Still not working?

Switch the developer console on in the game's settings, then type:

| | |
|---|---|
| `polyfill` | what it did |
| `polyfilllist` | every mod you have |
| `polyfillexport` | makes the file to send me |
| `polyfillrestore` | undo everything |

**Send me that file.** It says exactly what each mod is missing, and that is what gets them fixed in the
next version.

## Good to know

- Made for Schedule I 0.4.6f12
- It only changes things on your own PC
- In multiplayer, everyone using the old mod needs it too
- Your save is not touched
- To remove it: type `polyfillrestore`, restart, delete the two files

## Credits

Schedule I by TVGS. Built on [MelonLoader](https://github.com/LavaGang/MelonLoader) and
[Mono.Cecil](https://github.com/jbevain/cecil).

MIT licence.
