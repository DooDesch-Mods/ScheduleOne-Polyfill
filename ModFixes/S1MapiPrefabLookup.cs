using HarmonyLib;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Let S1MAPI's prefab lookup see the objects that are loaded but not network-spawnable.
    /// </summary>
    /// <remarks>
    /// <c>PrefabRef.Find()</c> searches one list: FishNet's spawnable prefab collection, 108 entries on
    /// 0.4.6f12. That is what the game can REPLICATE, not what it has. Measured against the loaded object
    /// set:
    /// <code>
    /// Basic Metal Glass Door   not spawnable   loaded 3x under exactly that name
    /// Classical Wooden door    not spawnable   loaded 3x under exactly that name
    /// ModularSwitch            not spawnable   loaded 3x under exactly that name
    /// </code>
    /// So the objects were never gone. Thirty of S1MAPI's sixty-nine names fail the same way, which makes
    /// this one lookup rather than thirty renames - and it is why the earlier reading of "no successor
    /// exists" was too narrow: it only ever asked the spawnable list.
    ///
    /// The repair infers nothing. The name matches exactly; the only question was where to look. A prefab
    /// template is preferred over an instance already placed in the world, so what gets cloned is the
    /// original rather than whatever state some copy is in.
    ///
    /// Two things it cannot promise, both worth stating rather than discovering.
    ///
    /// It does not promise replication. FishNet only spawns a prefab it has registered, so an object found
    /// this way appears for whoever ran the code and may not travel to other players.
    ///
    /// It does not promise a clean copy. Measured on 0.4.6f12, none of these is loaded as a prefab
    /// template - every one of them exists only as an instance already standing in the world, so that is
    /// what gets handed over and cloned. A door carries its PropertyDoorController, which points at the
    /// property it was copied from. It looks right and may behave like its original.
    ///
    /// Both are judgements: a door that is there beats a doorway that is empty, and a caller that gets
    /// something beats one that gets null. That is the kind of judgement a fix is allowed to make and a
    /// rule is not, which is why it says in the log which of the two it handed over, and why
    /// `polyfillfixes off s1mapi-prefab-lookup` takes it back.
    /// </remarks>
    internal sealed class S1MapiPrefabLookup : Fix
    {
        internal override string Id => "s1mapi-prefab-lookup";
        internal override string Mod => "S1MAPI";
        internal override string ModVersions => "*";
        internal override string GameVersions => "0.4.6*";
        internal override string What => "prefabs the game loads but does not replicate can be found again";

        internal override string StandsDownBecause
            => "Doors, switches and counters a mod spawns by name may come out empty again.";

        private static MelonLogger.Instance _log;
        private static readonly Dictionary<string, GameObject> _found = new(StringComparer.Ordinal);

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            Type reference = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { reference = assembly.GetType("S1MAPI.Core.PrefabRef", false); } catch { }
                if (reference != null) break;
            }
            if (reference == null) { log.Warning("[fix] s1mapi-prefab-lookup: PrefabRef is not where it was."); return false; }

            var find = AccessTools.Method(reference, "Find");
            if (find == null) { log.Warning("[fix] s1mapi-prefab-lookup: PrefabRef.Find is gone."); return false; }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                find, postfix: new HarmonyMethod(typeof(S1MapiPrefabLookup), nameof(FindPostfix)));
            return true;
        }

        /// <summary>Only ever fills in a null. A lookup that already worked is not touched.</summary>
        private static void FindPostfix(object __instance, ref GameObject __result)
        {
            if (__result != null) return;

            string name = null;
            try { name = __instance?.GetType().GetProperty("Name")?.GetValue(__instance) as string; }
            catch { }
            if (string.IsNullOrEmpty(name)) return;

            if (_found.TryGetValue(name, out var cached))
            {
                if (cached != null) { __result = cached; return; }
                _found.Remove(name);                       // it was destroyed since; look again
            }

            var loaded = Loaded(name, out bool template, out bool active);
            if (loaded == null) return;

            // A switched-off answer is not remembered. Which copies are switched on depends on where the
            // player is standing, so the same question asked from somewhere else can have a better answer,
            // and caching the bad one would make that impossible for the rest of the session.
            if (template || active) _found[name] = loaded;
            __result = loaded;
            // Which of the two it was matters when something comes out wrong: a template clones clean, a
            // copy taken out of the map carries whatever has already happened to it.
            _log?.Msg($"[fix] s1mapi-prefab-lookup: '{name}' is loaded but not spawnable - handed over "
                    + (template ? "the prefab template." : "a copy standing in the map; there is no template."));
            if (!template && !active)
                _log?.Warning($"[fix] s1mapi-prefab-lookup: every copy of '{name}' is switched off right now, "
                            + "so what gets cloned is switched off too. Property interiors are deactivated "
                            + "while you are away from them.");
        }

        /// <summary>
        /// The loaded object of that exact name, preferring a template over something already in the world.
        /// </summary>
        /// <remarks>
        /// A prefab asset belongs to no scene, so <c>scene.IsValid()</c> separates the original from copies
        /// that are standing in the map. Cloning the original is what the caller meant; cloning a placed one
        /// would carry whatever has happened to it.
        ///
        /// Among placed copies, an ACTIVE one wins. <c>Resources.FindObjectsOfTypeAll</c> returns switched-off
        /// objects too, and a property switches its whole interior off while nobody is near it
        /// (`ScheduleOne.Property/Property.cs:372-378` calls <c>SetActive(!culled)</c> on every object it
        /// culls). Cloning one of those hands the caller something invisible, which is a worse answer than
        /// the identical object two rooms away that happens to be switched on.
        /// </remarks>
        private static GameObject Loaded(string name, out bool template, out bool active)
        {
            template = false;
            active = false;
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> all;
            try { all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>()); }
            catch { return null; }
            if (all == null) return null;

            GameObject placed = null, placedActive = null;
            foreach (var one in all)
            {
                GameObject candidate = null;
                try
                {
                    if (one == null || one.name != name) continue;
                    candidate = one.TryCast<GameObject>();
                }
                catch { continue; }
                if (candidate == null) continue;

                try { if (!candidate.scene.IsValid()) { template = true; return candidate; } } catch { }

                placed ??= candidate;
                try { if (placedActive == null && candidate.activeInHierarchy) placedActive = candidate; }
                catch { }
            }

            active = placedActive != null;
            return placedActive ?? placed;
        }
    }
}
