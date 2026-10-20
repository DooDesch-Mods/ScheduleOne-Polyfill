# Polyfill

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/polyfill](https://support.doodesch.de/polyfill).

**Your old mods stopped working after a Schedule I update? Polyfill gets a lot of them running again.**

![Version](https://img.shields.io/github/v/release/DooDesch-Mods/ScheduleOne-Polyfill?label=version&color=blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
[![Polyfill](https://polyfill.doomods.com/badge/polyfill.svg)](https://polyfill.doomods.com/mod/polyfill)

[![MLVScan Polyfill.Boot.dll](https://mlvscan.com/attestations/att_PbTI7z1DtvkZ3F7OxHQa3AVp/badge.svg)](https://mlvscan.com/attestations/att_PbTI7z1DtvkZ3F7OxHQa3AVp) [![MLVScan Polyfill.dll](https://mlvscan.com/attestations/att_LE6tAJ4mKjKwYHw1mSC-zSzm/badge.svg)](https://mlvscan.com/attestations/att_LE6tAJ4mKjKwYHw1mSC-zSzm)

> ### Do your mods still work?
>
> Look them up: **[polyfill.doomods.com](https://polyfill.doomods.com)**
>
> Over 1,300 mods. Pick your game version and see if a mod runs, which version to use and what players
> wrote about it.
>
> Still broken? Drop your `Latest.log` on the mod's page there.

The game gets an update. Mods made before it stop working. Polyfill patches the game so they run again.

It patches the game, not your mods. Your own files are never changed, and `polyfillrestore` takes the
patch back out.

It cannot save every mod. The index tells you which ones.

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
| `polyfillshare on` / `off` | send reports to the index, or stop |
| `polyfillrestore` | take the patch back out |

## Good to know

- Made for Schedule I 0.4.6 and 0.4.7
- Asks once if it may send anonymous reports: your mods, what broke, how long you played. Never your name,
  your save or your folders
- Only changes things on your own PC. Your save is untouched
- In multiplayer, everyone using the old mod needs it too
- To remove it: `polyfillrestore`, restart, delete the two files

## For mod authors

Your mod probably has a page on [polyfill.doomods.com](https://polyfill.doomods.com) already:

- what it is missing on each game version, by name
- errors from players' sessions, with the method they came from
- a live badge for your README or mod page
- all of it as JSON, no key: [API](https://polyfill.doomods.com/about/api)

Want your mod off the list? [Ask here](https://polyfill.doomods.com/about#withdraw).

Schedule I by TVGS. Built on [MelonLoader](https://github.com/LavaGang/MelonLoader) and
[Mono.Cecil](https://github.com/jbevain/cecil). MIT licence.

This mod is written with help from generative AI tools (Anthropic Claude, OpenAI Codex).

If you have fun with it, a like on the [Thunderstore page](https://thunderstore.io/c/schedule-i/p/DooDesch/Polyfill/) really helps other people find it (thumbs up next to Download, you need to be logged in) - ty either way, and lemme know if anything breaks or you've got ideas

The compatibility index runs on a server I pay for. Tips: [ko-fi.com/doodesch](https://ko-fi.com/doodesch)
