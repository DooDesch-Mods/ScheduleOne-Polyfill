# Changelog

All notable changes to this project are documented here.

## [0.9.5] - 2026-08-14

### Changed
- Schedule I 0.4.6f13 is a build these repairs have been read against, so they stop reporting themselves
  as unchecked on it. Nothing 0.4.6f13 changed is anything they touch.

## [0.9.4] - 2026-08-14

### Fixed
- The manager clipboard in Over The Counter answers the interact key again. It was listening on the key
  0.4.6 moved its button to, so the prompt appeared and nothing happened.
- NPCs stop going unresponsive after you trade with them. The storage window lost the event a mod uses to
  put them back, so its cleanup never ran.

## [0.9.3] - 2026-08-14

### Fixed
- Custom Commands Framework stops killing the game on startup. It reads every type in every loaded
  assembly, and doing that to the game's generated ones ends the process with no error at all.

## [0.9.2] - 2026-08-14

### Fixed
- Over The Counter's hired manager stops throwing every tick, which is what froze the clipboard when you
  tried to give one a locker. It was being built out of a cleaner, same as the drifters were.

## [0.9.1] - 2026-08-14

### Fixed
- Trees stop growing every time you reload. With Bigger Trees installed, each load multiplied the size
  again instead of setting it, so dying and loading gave 2x, then 4x, then 8x.

### Added
- `HelpHarmonyFind` in MelonPreferences switches off the one layer that sits in front of another mod's
  code, for ruling it out when something misbehaves. The repairs themselves stay.

## [0.9.0] - 2026-08-14

### Fixed
- Mods stop throwing thousands of errors a session. A type Polyfill put back had its own members skipped,
  so one ATM reading alone threw once a frame for as long as the game ran.
- OG Backpack saves and loads its contents again, and its station panels work. One dead patch target was
  taking the whole class with it, five times over.
- Lithium's shop prices refresh again. 0.4.6 renamed an argument, and Harmony matches those by name.
- Lithium's drying rack and ATM screens work again.
- A nested type Polyfill put back is nested again. It was written at the top level under a name nothing
  asks for, so the repair reported success and the mod threw on the type.

### Added
- `polyfillfixes` gains `split-screen-patches`: where 0.4.6 split a station screen's `SetIsOpen` into
  `Open` and `Close`, a patch aimed at the old name runs again from both.
- The report says when a patch will be moved rather than left dead, so a working mod stops being listed
  as broken.

## [0.8.1] - 2026-08-14

### Fixed
- Hiring a manager in Over The Counter works again. It sets the NPC's pickpocket target, which 0.4.6 kept
  under the name its private field carries now.
- More Foot Patrols spawns its officers again. `ApplyShapeKeys` lost a flag, and what the flag switched off
  is what 0.4.6 does either way.

## [0.8.0] - 2026-08-14

### Fixed
- OG Backpack opens with B again. The storage window has three `Open` methods, all three grew an argument
  in 0.4.6, and the repair answered for the wrong one.
- Mods that patch the mixing or chemistry station screen work again. Those two and two more were renamed in
  0.4.6, and a patch aimed at the old name now reaches the method the game calls.
- StackPro talks to other players again. 0.4.6 took the Steam ids off the lobby, and all three are still
  derivable from what it kept.
- Lithium reads a dealer's type and an NPC's walking speed again, and its ATM screen patches apply.
- DealOptimizer finds the counteroffer price box again. It became an `AmountSelector`, and the input field
  inside it is the same control.

### Changed
- The report stops naming members that were never gone. 0.4.6 moved eight of them onto base classes, where
  the game finds them anyway; four mods in one report were called blocked over that alone.
- One repair that fails no longer costs the other fifty. An emitter that threw took the whole assembly with
  it, and the log said only that it had been left alone.

### Added
- When a member moved to a base class under a new name, the report says which class and which name, so a
  mod author has an answer rather than a dead end.
- `polyfillfixes` gains `patches-on-grown-overloads`, which moves a mod's patch onto the method the game
  calls when the old signature only exists because Polyfill put it back.

## [0.7.4] - 2026-08-14

### Fixed
- Another fix for the management clipboard: the stand-in for the deleted NPC selector screen was missing
  the one property the mod reads off it.

## [0.7.3] - 2026-08-14

### Added
- `polyfillprefab tree <name>` walks a UI object in debug builds, for mods that find their way by a
  spelled-out path and break when a screen is redesigned.

## [0.7.2] - 2026-08-13

### Fixed
- Another fix for Bella's handover screen: it reads the customer's favourite drug with no count check,
  and hers has none.

## [0.7.1] - 2026-08-13

### Fixed
- Bella's handover screen shows your options again. It reads the customer's preferences straight out, and
  OverTheCounter's own customer has none, so the screen never filled in.

## [0.7.0] - 2026-08-13

### Fixed
- OverTheCounter keeps its manager panel AND the game keeps its clipboard. 0.4.6 deleted the NPC selector
  screen, which killed both; it now answers that it is not open, which is true.

## [0.6.2] - 2026-08-13

### Fixed
- OverTheCounter's route picker closes again without choosing. Right-click, which is what the mod's own
  community repatch picked after 0.4.6 removed the Escape and Back buttons.

## [0.6.1] - 2026-08-13

### Fixed
- The management clipboard works again with OverTheCounter installed. Its patch asks for a screen 0.4.6
  removed, which killed the whole clipboard and filled the console every frame.

## [0.6.0] - 2026-08-13

### Fixed
- T.H.M's syringe and garrote kill again. They were repaired as far as the killing blow, which then never
  landed and said nothing.
- A type the game moved to another assembly AND renamed is put back properly. The old repair crashed any
  mod method that mentioned it, past that mod's own error handling.
- Mods built before 0.4.6 press the key they mean to press. The game deleted 14 entries from the middle of
  its button list, so `Interact` became the vehicle-lights key.

### Added
- `polyfillprefab thm` and `polyfillprefab doors` report why a mod's action did nothing, in debug builds.

## [0.5.2] - 2026-08-13

### Fixed
- Doors a mod puts up in a property you already own can be opened again. They locked themselves and waited
  for a purchase that had already happened, showing no prompt at all.
- Counters, switches and doors a mod spawns come out visible. Half the copies Polyfill could hand over are
  switched off, because the game deactivates a building's interior while you are away from it.

### Added
- Bigger Trees makes the trees bigger again. The game stopped drawing terrain trees in 0.4.6f5 and the
  mod's setting reached nothing. `TreeScale` in MelonPreferences sets the size, 1 leaves them alone.

## [0.5.1] - 2026-08-13

### Added
- OverTheCounter's drifters stop throwing an error every tick. The mod asks for a character the game no
  longer has and fell back to an employee, which crashes without a workplace.

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
