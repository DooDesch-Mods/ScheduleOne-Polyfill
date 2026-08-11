# Polyfill - Keep Older Mods Working After a Game Update

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/polyfill](https://support.doodesch.de/polyfill).

> A mod built against an older Schedule I asks for names the update took away, and dies the moment you
> use the feature. Polyfill puts those names back - not into your mods, into the interop assemblies
> MelonLoader generates from your own copy of the game - pointing at wherever the thing lives now.

![Version](https://img.shields.io/badge/version-0.1.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Status](https://img.shields.io/badge/status-early%20beta-orange)

**Early beta.** It repairs renames, casing changes and FishNet RPC hashes today, and reports everything
it cannot. Read the report before assuming a mod is fixed.

## Features

- Old mods keep working across a game update, with nothing for the mod author to do
- One repair serves every installed mod - it goes into the game, not into anyone's DLL
- **Your mod files are never written to.** The untouched copy of anything it changes is kept beside it
- `polyfill` in the console says exactly what each mod is missing and what it was matched to
- `polyfillrestore` undoes everything on the next launch
- `DryRun` works out what is missing and changes nothing

## Requirements

- [MelonLoader](https://github.com/LavaGang/MelonLoader) 0.7.3+ - the mod loader
- Nothing else

## Installation

Mod manager: install and it places both files for you.

By hand:

- `Polyfill.Boot.dll` into `Plugins/`
- `Polyfill.dll` into `Mods/`

The plugin does the work and has to be a plugin, because it runs before any mod is read from disk - a
copy in `Mods/` is ignored. The mod only adds the console commands and can be left out.

## Console

Needs the developer console enabled in the game's settings.

```
polyfill                 what it found in your mods at startup
polyfilllist             every mod, with its verdict
polyfillshow <mod>       everything one mod asks for that is missing
polyfillunfixed <mod>    only what cannot be pointed at anything
polyfillprobe <type>     ask the runtime whether a name resolves
polyfillrestore          undo every repair, takes effect on the next launch
```

The full report is written to `UserData/Polyfill/last-run.txt`.

## Settings

`UserData/MelonPreferences.cfg`, category `Polyfill`:

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | Off means nothing is read and nothing is changed |
| `DryRun` | `false` | Work out what is missing, write the report, leave the game alone |

## What it cannot fix

- Behaviour behind an unchanged signature - a method that kept its name and now does something else
- Reordered enum values used as plain numbers, which it reports but never rewrites
- A type that moved and was renamed, which .NET type forwarding cannot follow
- Anything removed with no successor
- Scene and prefab paths

All of it shows up in the report rather than as a crash.

## Credits

Schedule I by TVGS. Built on [MelonLoader](https://github.com/LavaGang/MelonLoader) and
[Mono.Cecil](https://github.com/jbevain/cecil).

## License

MIT
