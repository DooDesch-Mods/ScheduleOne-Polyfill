# Polyfill

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/polyfill](https://support.doodesch.de/polyfill).

Keeps older Schedule I mods working after a game update, without the mod author having to do anything.

## What it does

Every update renames things, moves types into other assemblies, and changes FishNet RPC hashes. A mod
built against an older version asks for names that are not there any more and dies - usually at the
moment you use the feature, so it reads as a broken mod rather than a version mismatch.

Polyfill puts those names back. Not into your mods - into the interop assemblies MelonLoader generates
from your own copy of the game - pointing at wherever the thing lives now. One repair serves every mod
that wants it, and **the files in your Mods folder are never touched**.

It only ever adds. Nothing is renamed, changed or removed, so a mod that already works cannot be
affected by it.

## Install

1. [MelonLoader](https://melonloader.co/) 0.7.x
2. `Polyfill.Boot.dll` into your `Plugins` folder
3. `Polyfill.dll` into your `Mods` folder (optional - it only adds the console commands)

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
- **Scene and prefab paths.** `transform.Find("UI/HUD/...")` compiles forever and returns null.

Everything in that list shows up as a line in the report rather than as a crash.

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
