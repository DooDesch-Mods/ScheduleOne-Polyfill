# Changelog

All notable changes to this project are documented here.

## [0.2.0] - 2026-08-11

### Added
- Mods that build their own NPC run again: 0.4.6 moved the NPC's name, ID, mugshot and inventory
  settings, and writing them killed the mod.
- More names get matched. A member renamed from `CustomerSlots` to `_customerSlots` is found now, which
  brings back the weather reading and the handover screen for older mods.
- T.H.M - The Hitman Mod now has a repair for all 6 things it asks for, OverTheCounter Dispensary for 18
  of 40. It was 5 and 4.

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
