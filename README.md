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

| | |
|---|---|
| [MelonLoader](https://github.com/LavaGang/MelonLoader) | 0.7.3+ - the mod loader |
| Anything else | Nothing |

## Install

Mod manager: install and it places both files for you.

By hand:

1. `Polyfill.Boot.dll` into `Plugins/`
2. `Polyfill.dll` into `Mods/` (optional - it only adds the console commands)

The plugin does the work and has to be a plugin, because it runs before any mod is read from disk. A copy
of it in `Mods/` is ignored by MelonLoader.

## Console

Needs the developer console enabled in the game's settings.

```
polyfill                 what it found in your mods at startup
polyfilllist             every mod, with its verdict
polyfillshow <mod>       everything one mod asks for that is missing
polyfillunfixed <mod>    only what cannot be pointed at anything
polyfillexport           write one file with everything, ready to send
polyfillprobe <type>     ask the runtime whether a name resolves
polyfillrestore          undo every repair, takes effect on the next launch
polyfillregen            have MelonLoader build the game's generated assemblies again
```

After a game update Polyfill notices that MelonLoader rebuilt those assemblies and starts from the new
ones. `polyfillregen` is for the case where it says it cannot find an untouched copy of something: it
asks MelonLoader to build the whole set again, which takes a few minutes on the next launch.

The full report is written to `UserData/Polyfill/last-run.txt`, and the untouched copy of anything it
changed sits next to it as `<name>.dll.polyfill-orig`.

## Settings

`MelonPreferences.cfg`, category `Polyfill`:

- `Enabled` - off means nothing is read and nothing is changed
- `DryRun` - work out what is missing, write the report, leave the game alone

## What it cannot fix

- **Behaviour behind an unchanged signature.** A method that kept its name and now does something else.
- **Reordered enum values used as plain numbers.** The compiler erased the type; a `12` is not
  distinguishable from an array index. It is reported, never rewritten - guessing wrong here writes into
  your save.
- **A type that moved AND was renamed.** .NET type forwarding matches on the name, so it cannot follow
  `ScheduleOne.Weather.X` to `ScheduleOne.Core.Weather.X`.
- **Removed with no successor.** A forwarder needs something to point at.
- **Scene and prefab paths.** `transform.Find("UI/HUD/...")` compiles forever and returns null. No rule
  can find these: they are strings, so nothing reports them and the mod simply does nothing. A named
  repair for one mod is possible where the old effect is unambiguous - GraphicsMOD's lighting toggle is
  one - but that is a hand-written fix per mod, never a rule that keeps paths working.

Everything in that list shows up as a line in the report rather than as a crash.

## Planned

- **A compatibility index.** Right now finding out that a mod is still broken depends on somebody
  running `polyfillexport` and sending the file. A small opt-in service would collect the same thing
  automatically and publish which mods run on which game version, so the gaps are visible without
  anyone doing anything. Design and the open questions: `Workspace/docs/Polyfill/COMPAT-INDEX.md`.

## Building

```
dotnet build Boot/Polyfill.Boot.csproj -c Release
dotnet build Report/Polyfill.csproj -c Release
```

Both need the Schedule I workspace libraries; point `WorkspaceLibPath` at them.

## Credits

Schedule I by TVGS. Built on [MelonLoader](https://github.com/LavaGang/MelonLoader) and
[Mono.Cecil](https://github.com/jbevain/cecil).

## License

MIT - see [LICENSE.md](LICENSE.md).
