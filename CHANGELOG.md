# Changelog

All notable changes to this project are documented here.

## [0.5.0] - 2026-08-12

### Fixed
- After a Schedule I update Polyfill starts from the game's new files. It used to keep writing its copy of
  the old ones back over them, which broke mods that had been fine.
- Repairs keep working when the game reaches 0.5.0 or 1.0. Version numbers were compared in a way that read
  0.5.0 as older than 0.4.6f5, which would have switched the rename history off.
- A repair a person wrote by hand is used before one guessed from spelling. Two similar-looking names could
  stop the better answer from being used at all.

### Added
- The report says what Polyfill DID, not only what it found. `polyfillexport` gains a REFUSED section for
  every repair it had a candidate for and did not trust.
- `polyfillregen` has MelonLoader build the game's generated files again, for when Polyfill reports that it
  has no untouched copy of one left.

## [0.4.3] - 2026-08-12

### Fixed
- OverTheCounter's co-op sync is no longer switched off by a version number. It demanded exactly the
  SteamNetworkLib it was built with, and the newer one carries everything it calls.

## [0.4.2] - 2026-08-12

### Fixed
- The supplier's meeting greeting and its reply work again. 0.4.6 stopped keeping either in a field, and
  a mod reaching for them was throwing six times a minute.

## [0.4.1] - 2026-08-12

### Fixed
- Trees no longer stand inside the buildings a mod places. 0.4.6 draws them from a baked texture rather
  than from the terrain, so clearing the terrain had only been taking their collision away.

## [0.4.0] - 2026-08-12

### Added
- Renames the game made in any build since 0.4.4 are followed on their own. Polyfill carries the game's
  own history, chained over 14 versions, and asks it when the installed game cannot answer.
- That includes a FishNet RPC whose hash moved with its signature, which nothing on the installed game
  can work out by looking.

## [0.3.0] - 2026-08-12

### Added
- Per-mod fixes: one small module per mod, for breakage that has no name in the metadata to repair.
  `polyfillfixes` lists them and switches one off.
- Mods built on S1MAPI get their doors, switches and counters back. 0.4.6 stopped listing those as
  network-spawnable and the lookup only ever searched that list, so they came out empty.
  - The game keeps no loose copy of those, so what gets cloned is one already standing in the world.
    It looks right and may behave like the one it came from. `polyfillfixes off s1mapi-prefab-lookup`.
- The same mods also follow prefabs the game renamed, such as the `_Built` suffix 0.4.6 put on placeable
  furniture.

## [0.2.0] - 2026-08-12

### Added
- Mods that build their own NPC run again: 0.4.6 moved the NPC's name, ID, mugshot and inventory
  settings, and writing them killed the mod.
- More names get matched. A member renamed from `CustomerSlots` to `_customerSlots` is found now, which
  brings back the weather reading and the handover screen for older mods.
- Two more kinds of break are repaired: a type that only changed namespace, and a method that grew an
  argument. That is OverTheCounter's contacts app and its storage screen.
- T.H.M - The Hitman Mod now has a repair for all 6 things it asks for, OverTheCounter Dispensary for 29
  of 40. It was 5 and 4.

- `polyfillprefab <name>` says whether the game still has a prefab a mod spawns by name, and what is
  spelled close to it. Renamed prefabs used to fail in the Unity log where nobody looks.

### Fixed
- Boots that died with no message. A mod asking Harmony for a type by name made the game load every type
  in every assembly, and one of them takes the process down.

## [0.1.3] - 2026-08-11

### Fixed
- The store pages said your files are never touched and then offered a command to undo things, which made
  no sense. It patches the game, not your mods, and now the text says so.

## [0.1.2] - 2026-08-11

### Changed
- The store pages are shorter and say what the mod does in one line. The previous text still read like
  documentation instead of a mod page.

## [0.1.1] - 2026-08-11

### Changed
- The store pages now say what the mod does in plain words. The old text explained the mechanism instead
  of the problem, so nobody could tell whether it was for them.

## [0.1.0] - 2026-08-11

### Added
- Mods built for an older Schedule I keep working: the names an update took away are put back into the
  game's generated interop assemblies, pointing at wherever the thing lives now.
- Your mod files are never touched. Repairs go into MelonLoader's own generated assemblies, and the
  untouched copy stays beside them as `<name>.dll.polyfill-orig`.
- `polyfill` in the console says what every installed mod is missing and what it was matched to, and
  `polyfillexport` writes all of it to one file you can send on, with no paths in it.
- `polyfillrestore` undoes every repair on the next launch. It cannot happen sooner, because the
  assemblies are in use while the game runs.
- `DryRun` works out what is missing and changes nothing, so you can see what it would do first.
