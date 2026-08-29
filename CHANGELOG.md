# Changelog

All notable changes to this project are documented here.

## [0.11.4] - 2026-08-29

### Fixed
- Another fix for overlay mods: 0.11.3 stopped the crash but drew nothing, so a clock or a crosshair was
  simply missing. The image is on screen now.
- A repair that cannot do the job says so and steps aside, instead of leaving a mod loaded and silent.
  `polyfillfixes` shows it as failed and the log gives the reason.

## [0.11.3] - 2026-08-29

### Fixed
- Overlay mods draw again. ClockOverlay and EZCrosshair threw an error every frame instead of drawing,
  which filled the log and could take the game down with it.
- Mods that open or close the sleep screen work again. 0.4.6 replaced the call they use, and Polyfill
  now opens and closes it the way the game does.
- Mods that close the pause menu close it. They loaded without complaining and then did nothing.

## [0.11.2] - 2026-08-28

### Changed
- The sharing question now names everything that is sent: mod authors, how long you played, and any
  errors a mod threw. It said "mod names and versions only" and that was less than the truth.
- `polyfillshare on` says the same, and points at `polyfillshare show` so you can read the exact text.

## [0.11.1] - 2026-08-28

### Fixed
- polyfill.doomods.com counted every launch as a separate player. Your PC rolled a new id each time
  the game started, so six sessions looked like six people.

## [0.11.0] - 2026-08-28

### Added
- polyfill.doomods.com now says whether a mod actually works, not just whether it starts. Polyfill
  watches your session for errors and reports what it saw when you leave the game.
- The list shows how long people played before answering, so a mod nobody has really used says so
  instead of looking fine.

### Changed
- Reports go out when you quit, not while the game loads. A mod that starts and then breaks was
  passing the old check.
- Only the error type and the line it came from are sent. Never the error text, which can contain your
  save name or a folder path.

## [0.10.1] - 2026-08-28

### Fixed
- The sharing question stops coming back. It appeared on every launch and no button could stop it -
  the answer was written but never read back.

### Changed
- The compatibility list now says a mod "starts" rather than "works". Polyfill checks what a mod asks
  the game for while it loads, which is not the same as playing it.
- Reports say which Polyfill wrote them, so polyfill.doomods.com can show when a mod was fixed by an
  update rather than by a game change.

## [0.10.0] - 2026-08-28

### Added
- Check whether a mod still works before you install it: polyfill.doomods.com lists what other players
  reported for your game version.
- Polyfill asks once, on your first launch, whether it may send what it found. Say no and nothing ever
  leaves your PC.
  - `polyfillshare show` prints exactly what would be sent, `polyfillshare on` and `off` change your mind.
- Only mod names, versions and what Polyfill repaired are sent. Never your name, your save, your folders,
  or which mods you run together.

### For mod authors
- `polyfill-check YourMod.dll --game 0.4.6f13` lists what a game update broke in your mod, with no game
  and no MelonLoader installed.
  - It agreed with the in-game report on all 44 mods the two were run against.
- Your mod's page on the index names every member the game no longer has, and whether Polyfill bridged
  it, refused it, or found nothing to point at.

## [0.9.26] - 2026-08-26

### Fixed
- Absorbent Soil and NACops are no longer listed as broken. Both work on this version - the report was
  wrong about them, not the mods.

## [0.9.25] - 2026-08-26

### Fixed
- Your Over The Counter manager stops turning into a townsperson - their face, their name, their stock.
  If a manager is missing after you load a save, rejoin and they come back.

## [0.9.24] - 2026-08-26

### Fixed
- The Tweakables app opens on your phone again instead of sitting there dead.
- Suppliers show you their stock at the meetup. They used to just greet you until you shoved them.
- GraphicsMOD's lighting option turns the sun off again.

## [0.9.23] - 2026-08-21

### Fixed
- A patch a mod cannot bind no longer blocks every later patch of that same method. Harmony keeps the
  failed entry, and 0.9.19 left it there.
- Tweakables can raise how often a customer orders again, and GraphicsMOD's lighting option switches the
  sun instead of doing nothing.

## [0.9.22] - 2026-08-21

### Fixed
- Deal Optimizer re-evaluates a counteroffer after you change the quantity. The method kept its name and
  swapped its argument type, which Harmony can neither choose nor bind.
- Tweakables can patch the handover price box and the blackjack bet slider again. Polyfill's stand-ins
  carried the new argument names, and a patch binds by name.

### Added
- The blackjack bet slider, its handler and the bet label are reachable on the screen again, after 0.4.6
  moved them into the panel every casino game shares.

## [0.9.21] - 2026-08-21

### Fixed
- Enhanced PD stops logging an error every second. 0.4.6 moved the player lookups off Player onto
  PlayerManager, and the mod asks the old address once per raid tick.

## [0.9.20] - 2026-08-19

### Fixed
- The GreenTab app comes back after a trip to the main menu instead of failing to register, which took
  the whole app down rather than its icon.
- Unicorn's Custom Seeds can put its seeds in a supplier's shop again. Reading the list was bridged and
  writing it back was not.
- Installing through Vortex puts the plugin in the game's Plugins folder instead of bepinex/plugins.

## [0.9.19] - 2026-08-18

### Fixed
- The Heisenberg mod delivers Heisenberg again instead of a standard chemist: the flag that says an NPC
  has a last name moved in 0.4.6 and nothing had put it back.
- Deal Optimizer works on counteroffers and street deals again, and no longer throws at startup.
- One patch a mod cannot bind no longer costs it every patch after that one - Deal Optimizer lost seven
  working ones to a single dead target.

### Added
- A method that kept its name and now hands back a renamed type is repaired instead of only listed.
- The report names a patched method the game split into two overloads, which Harmony refuses to choose
  between.

## [0.9.18] - 2026-08-17

### Fixed
- Despawning an NPC stops filling your log with errors. The game asks one list of schedule actions
  whether they should start without checking they still exist.

## [0.9.17] - 2026-08-17

### Fixed
- The Over The Counter manager panel goes away when you put the clipboard on another employee, instead
  of staying up and drawing over theirs.

## [0.9.16] - 2026-08-17

### Fixed
- Instant Pack works again. The packaging and mixing screens renamed the property that hands out the
  station they belong to, and mods that ask for the old name got nothing.

## [0.9.15] - 2026-08-16

### Changed
- The log names which character each Over The Counter customer was built from, for the first eight. Ask
  for it if your customers all look alike.

## [0.9.14] - 2026-08-16

### Fixed
- Suppliers open their sales page again and the GreenTab app takes input after you return from the main
  menu. Both died in the same call Polyfill had written wrong.

## [0.9.13] - 2026-08-16

### Fixed
- Over The Counter's manager panel goes away when you put the clipboard down, instead of staying up and
  overlapping the next employee's panel.

## [0.9.12] - 2026-08-15

### Fixed
- You can move again after putting the manager clipboard away. It was hidden without being taken off the
  input stack, so the game still had it open while the screen said otherwise.

## [0.9.11] - 2026-08-15

### Fixed
- Ultimate Mod Menu starts. Its third way of looking a type up killed the game too, and so did the way
  Polyfill answered the first two.

## [0.9.10] - 2026-08-15

### Fixed
- Ultimate Mod Menu gets past its clipboard setup. 0.9.8 covered one of its two type searches, so the
  crash moved from the police patches to the next feature instead of going away.

## [0.9.9] - 2026-08-15

### Fixed
- Over The Counter can dress a drifter again. It writes a field 0.4.6 deleted, and the whole routine
  stopped before its own error handling could run.

## [0.9.8] - 2026-08-15

### Fixed
- Over The Counter's customers stop all being the same person. They were cloned from one prefab; now
  every spawnable civilian gets a turn, and the log names which ones your game has.
- Giving an Over The Counter manager a route works again. The button did nothing because the panel
  named a UI class 0.4.6 removed, which stopped the whole method from running.
- Ultimate Mod Menu no longer kills the game on startup with no message. It searched for game types in a
  way that loads every type there is, which the game does not survive.

### Changed
- `polyfillprobe` now calls static members too, so a repair like `Singleton<T>.Instance` can be
  checked rather than only listed.

## [0.9.7] - 2026-08-15

### Fixed
- More Foot Patrols staffs its routes again. It cloned a police prefab this build no longer has; the
  officers now come out of the police station, two held back for callouts.

## [0.9.6] - 2026-08-14

### Fixed
- Deal Optimizer reads and sets the handover price again, and asks a dealer how much of a product it has.
  All three kept their meaning and lost their names.
- Always Show Distances On Compass finds what a compass marker points at. 0.4.6f5 renamed it to
  `TargetTransform`.

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
