using Il2CppFishNet;
using Il2CppFishNet.Managing.Object;
using UnityEngine;

namespace Polyfill.Report
{
    /// <summary>
    /// What the game's spawnable prefab list is called TODAY.
    /// </summary>
    /// <remarks>
    /// A mod does not only ask for types and members. It also asks for prefabs, by name, out of FishNet's
    /// spawnable registry - and an update renames those too. When it happens the mod gets null and usually
    /// says nothing a player would see: OverTheCounter's two missing doors were only in the Unity log, which
    /// nobody reads, under a line that says a name was not found and not which names exist.
    ///
    /// This is deliberately a REPORT and not a repair, and the line is not squeamishness:
    ///
    /// For a type or a member, both sides are in metadata. Cecil reads what the mod asks for, the index reads
    /// what the game has, and a repair only happens when exactly one candidate matches - no inference at all.
    /// For a prefab there is no such link. The old name is a string constant inside the mod and the new one
    /// is a string on an asset; nothing connects them but their spelling. Choosing on spelling alone would
    /// mean spawning a guessed object into the player's world and their save, which is the same trade the
    /// project already refuses for reordered enum values: a wrong repair runs, a missing one does not.
    ///
    /// So this answers the question instead of pre-empting it. The registry only exists once FishNet is up,
    /// which is why it lives in the mod rather than the plugin.
    /// </remarks>
    internal static class PrefabLookup
    {
        /// <summary>Every spawnable prefab name, or null when the registry is not up yet.</summary>
        internal static List<string> Names()
        {
            try
            {
                var manager = InstanceFinder.NetworkManager;
                if (manager == null) return null;

                var objects = manager.GetPrefabObjects<PrefabObjects>(0, false);
                if (objects == null) return null;

                var names = new List<string>();
                int count = objects.GetObjectCount();
                for (int i = 0; i < count; i++)
                {
                    var networkObject = objects.GetObject(true, i);
                    var gameObject = networkObject == null ? null : networkObject.gameObject;
                    if (gameObject != null) names.Add(gameObject.name);
                }
                return names;
            }
            catch { return null; }
        }

        /// <summary>
        /// Report on one name: is it there, and if not, what is close.
        /// </summary>
        /// <remarks>
        /// The near matches are ordered by how little was changed, not scored into a verdict. Casing and
        /// spacing first, because those are the renames that happen, then anything sharing a word. The
        /// caller reads the list; nothing here decides.
        /// </remarks>
        internal static void Explain(string wanted)
        {
            var names = Names();
            if (names == null)
            {
                Core.Log.Warning("the prefab list is not up yet - load a save first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(wanted))
            {
                Core.Log.Msg($"{names.Count} spawnable prefab(s) registered. "
                           + "Name one to check it: `polyfillprefab Basic Metal Glass Door`, "
                           + "or `polyfillprefab list` for all of them.");
                return;
            }

            wanted = wanted.Trim();

#if DEBUG
            if (wanted.StartsWith("clone ", StringComparison.Ordinal))
            { Clone(wanted.Substring(6).Trim()); return; }
            if (wanted == "doors") { Doors(); return; }
            if (wanted == "thm") { Polyfill.ModFixes.ThmGateProbe.Arm(); return; }
            if (wanted.StartsWith("equip ", StringComparison.Ordinal))
            { Polyfill.ModFixes.ThmRig.Equip(wanted.Substring(6).Trim()); return; }
            if (wanted.StartsWith("thm run ", StringComparison.Ordinal))
            { Polyfill.ModFixes.ThmRig.Run(wanted.Substring(8).Trim()); return; }
#endif

            if (wanted == "list")
            {
                names.Sort(StringComparer.OrdinalIgnoreCase);
                Core.Log.Msg($"the {names.Count} prefabs a mod can spawn by name:");
                foreach (string name in names) Core.Log.Msg("  " + name);
                return;
            }
            foreach (string name in names)
                if (name == wanted) { Core.Log.Msg($"'{wanted}' is registered - the game still has it."); return; }

            // Collection 0 and no more, because that is the list a mod's own lookup reads. Saying "the game
            // does not have it" off a wider search would be a different sentence than the one that matters.
            Core.Log.Warning($"'{wanted}' is not among the {names.Count} spawnable prefabs a mod can ask for.");

            var close = new List<string>();
            foreach (string name in names)
                if (Squashed(name) == Squashed(wanted)) close.Add(name + "   [only casing or spacing]");

            if (close.Count == 0)
                foreach (string name in names)
                    if (SharesAWord(name, wanted)) close.Add(name);

            if (close.Count == 0) Core.Log.Msg("  nothing in that list shares a word with it.");
            else
            {
                Core.Log.Msg("  that list has these, which is what the mod would have to ask for instead:");
                for (int i = 0; i < close.Count && i < 12; i++) Core.Log.Msg("    " + close[i]);
                if (close.Count > 12) Core.Log.Msg($"    ... and {close.Count - 12} more");
            }

            Elsewhere(wanted);
        }

        /// <summary>
        /// Is the object loaded at all, outside the spawnable list?
        /// </summary>
        /// <remarks>
        /// The spawnable list is what FishNet can replicate, and it is a fraction of what is loaded: 108
        /// entries against tens of thousands of objects. A name missing from it is therefore two different
        /// findings, and only one of them is fatal. Gone entirely means a mod asking for it can never have
        /// it. Loaded but not spawnable means the object is right there and the LOOKUP is aimed at the wrong
        /// list - which is a repair somebody can actually make, in the mod or in a fix.
        ///
        /// One sweep, only when a name has already failed, because the sweep is not cheap.
        /// </remarks>
        private static void Elsewhere(string wanted)
        {
            UnityEngine.Object[] loaded;
            try { loaded = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<GameObject>()); }
            catch (Exception e) { Core.Log.Msg("  (could not sweep loaded objects: " + e.GetType().Name + ")"); return; }
            if (loaded == null) return;

            string shape = Squashed(wanted);
            var exact = new List<GameObject>();
            var shaped = new List<string>();

            foreach (var one in loaded)
            {
                string name;
                try { name = one?.name; } catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;
                if (name == wanted)
                {
                    GameObject asObject = null;
                    try { asObject = one.TryCast<GameObject>(); } catch { }
                    if (asObject != null && exact.Count < 8) exact.Add(asObject);
                    continue;
                }
                if (Squashed(name) == shape && shaped.Count < 6) shaped.Add(name);
            }

            if (exact.Count > 0)
            {
                Core.Log.Msg($"  BUT it IS loaded, {exact.Count}x, under exactly that name - it is simply not "
                           + "in the spawnable list. A lookup that searched loaded objects would find it.");
                foreach (var one in exact) Core.Log.Msg("    " + State(one));
                return;
            }
            if (shaped.Count > 0)
            {
                Core.Log.Msg("  it is not loaded under that name either, but these are loaded and differ only "
                           + "in case or spacing:");
                foreach (string one in shaped) Core.Log.Msg("    " + one);
                return;
            }
            Core.Log.Msg($"  and nothing loaded anywhere in the game is called '{wanted}'.");
        }

#if DEBUG
        /// <summary>
        /// Every property door in the map, and whether a player can get through it.
        /// </summary>
        /// <remarks>
        /// The one measurement that answers the reported failure on a real save rather than on a clone made
        /// to order: which doors are on ExitOnly, what property each is bound to, whether that property is
        /// owned, and where the door hangs - a mod's copy does not hang under <c>Map/</c>.
        ///
        /// Three things measured with it, all worth not rediscovering, on a 145-day save on 0.4.6f12:
        ///
        /// <c>Property.DoBoundsContainPoint</c> answers false for a door of its OWN property, 22 of 22 owned
        /// doors, so "is this door standing in the property it names" cannot be asked that way.
        ///
        /// A PropertyDoorController with no property at all is normal vanilla: the gas station staff rooms
        /// are built that way and are meant to be one-way.
        ///
        /// A save whose quest log names an OverTheCounter dispensary held 68 doors and every one of them
        /// hangs under the map. The mod asks the lookup for its four door and switch names during the load
        /// and places none of them as a door, so the reported dispensary cannot be reproduced by owning one.
        /// </remarks>
        private static void Doors()
        {
            // FindObjectsOfTypeAll and not FindObjectsOfType: the latter skips switched-off objects, which
            // is exactly the set worth looking at when a building has come out dead.
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> all;
            try
            {
                all = Resources.FindObjectsOfTypeAll(
                    Il2CppInterop.Runtime.Il2CppType.Of<Il2CppScheduleOne.Doors.DoorController>());
            }
            catch (Exception e) { Core.Log.Error("sweep failed: " + e.Message); return; }
            if (all == null || all.Count == 0) { Core.Log.Msg("no doors loaded."); return; }

            int shut = 0;
            Core.Log.Msg($"{all.Count} door(s), switched-off ones included:");
            foreach (var one in all)
            {
                var door = one?.TryCast<Il2CppScheduleOne.Doors.DoorController>();
                if (door == null) continue;

                string access;
                try { access = door.PlayerAccess.ToString(); } catch { continue; }
                try { if (!door.gameObject.activeInHierarchy) access = "OFF/" + access; } catch { }
                if (!access.EndsWith("Open", StringComparison.Ordinal)) shut++;

                // The base type covers both, and the difference is the point: only the property kind locks
                // itself in Awake, so a plain one that will not open was built that way or was cloned that way.
                var asProperty = door.TryCast<Il2CppScheduleOne.Building.Doors.PropertyDoorController>();
                var property = asProperty?.Property;
                string where = asProperty == null ? "plain door" : "no property";
                if (property != null)
                    where = property.PropertyName + (property.IsOwned ? " (owned)" : " (NOT owned)");
                Core.Log.Msg($"  {access,-9} {Path(door.transform)}   {where}");
                try
                {
                    var at = door.transform.position;
                    Core.Log.Msg($"            at {at.x:0.#}, {at.y:0.#}, {at.z:0.#}");
                }
                catch { }
            }
            Core.Log.Msg($"{shut} of {all.Count} will not open from the outside.");
        }

        /// <summary>Where an object hangs, root first. What a mod placed does not hang where the map does.</summary>
        private static string Path(Transform transform)
        {
            var parts = new List<string>();
            try
            {
                for (var step = transform; step != null && parts.Count < 8; step = step.parent)
                    parts.Add(step.name);
            }
            catch { }
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>
        /// Do to a name exactly what a mod does with it, and report what came out.
        /// </summary>
        /// <remarks>
        /// Reproduces the reported failure without needing the mod that reported it: find the loaded object
        /// under that name, clone it, and read the clone. A cloned PropertyDoorController locks itself to
        /// ExitOnly in Awake and subscribes to an acquisition that has already happened, so a clone that
        /// comes out Open is the `s1mapi-cloned-doors` repair having run, and one that comes out ExitOnly is
        /// the bug.
        ///
        /// Debug builds only. It puts an object in the world, which is a thing a report command has no
        /// business doing on a player's save.
        /// </remarks>
        private static void Clone(string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) { Core.Log.Warning("name a prefab to clone."); return; }

            GameObject source = null;
            try
            {
                foreach (var one in Resources.FindObjectsOfTypeAll(
                             Il2CppInterop.Runtime.Il2CppType.Of<GameObject>()))
                {
                    if (one == null || one.name != wanted) continue;
                    var candidate = one.TryCast<GameObject>();
                    if (candidate == null) continue;
                    var itsDoor = candidate.GetComponentInChildren<
                        Il2CppScheduleOne.Building.Doors.PropertyDoorController>(true);
                    if (itsDoor != null && itsDoor.Property != null) { source = candidate; break; }
                    source ??= candidate;
                }
            }
            catch (Exception e) { Core.Log.Error("sweep failed: " + e.Message); return; }

            if (source == null) { Core.Log.Warning($"nothing loaded is called '{wanted}'."); return; }
            Core.Log.Msg("source: " + State(source));

            GameObject clone;
            try { clone = UnityEngine.Object.Instantiate(source); }
            catch (Exception e) { Core.Log.Error("clone failed: " + e.Message); return; }

            Core.Log.Msg("clone:  " + State(clone));
            var door = clone.GetComponentInChildren<
                Il2CppScheduleOne.Building.Doors.PropertyDoorController>(true);
            if (door == null) Core.Log.Msg("  no door controller on it, so there is nothing to lock.");
            else Core.Log.Msg($"  access {door.PlayerAccess} - Open means a player can walk in, ExitOnly "
                            + "means the door shows no prompt at all from the outside.");

            UnityEngine.Object.Destroy(clone);
        }
#endif

        /// <summary>
        /// What one loaded copy would actually give a caller that cloned it.
        /// </summary>
        /// <remarks>
        /// "It is loaded" was the whole answer until a player reported a building where nothing could be
        /// interacted with. Two properties of the copy decide whether cloning it produces something usable,
        /// and neither is visible from the name:
        ///
        /// Switched off - a property deactivates its interior while you are away, so the copy that gets
        /// cloned can be an invisible one.
        ///
        /// The property a door is bound to - a PropertyDoorController locks itself to ExitOnly on the way up
        /// and waits for that property to be acquired, an event that has already fired. The clone waits
        /// forever, and a door with no access shows no prompt at all rather than a locked one.
        /// </remarks>
        private static string State(GameObject one)
        {
            var text = new System.Text.StringBuilder();
            try { text.Append(one.activeInHierarchy ? "switched on " : "SWITCHED OFF "); } catch { }
            try { text.Append(one.scene.IsValid() ? "in " + one.scene.name : "a prefab template"); } catch { }

            try
            {
                var door = one.GetComponentInChildren<Il2CppScheduleOne.Building.Doors.PropertyDoorController>(true);
                if (door != null)
                {
                    var property = door.Property;
                    text.Append("   door bound to ")
                        .Append(property == null ? "no property" : property.PropertyName)
                        .Append(property == null ? "" : (property.IsOwned ? " (owned)" : " (NOT owned)"));
                }
            }
            catch { }
            return text.ToString();
        }

        /// <summary>Lower case with every space and separator taken out, so only the letters are compared.</summary>
        private static string Squashed(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }

        private static bool SharesAWord(string name, string wanted)
        {
            foreach (string word in wanted.Split(new[] { ' ', '_', '-', '.' },
                                                 StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length < 4) continue;              // "the", "of" and friends match everything
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
