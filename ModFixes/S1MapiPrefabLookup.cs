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
    /// What this cannot promise is replication. FishNet will only spawn a prefab it has registered, so an
    /// object found this way appears for whoever ran the code and may not travel to other players. That is
    /// a judgement - a door that is there for the host beats a doorway that is empty for everyone - and it
    /// is the kind of judgement a fix is allowed to make and a rule is not. `polyfillfixes off
    /// s1mapi-prefab-lookup` takes it back.
    /// </remarks>
    internal sealed class S1MapiPrefabLookup : Fix
    {
        internal override string Id => "s1mapi-prefab-lookup";
        internal override string Mod => "S1MAPI";
        internal override string ModVersions => "*";
        internal override string GameVersions => "0.4.6*";
        internal override string What => "prefabs the game loads but does not replicate can be found again";

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

            var loaded = Loaded(name);
            if (loaded == null) return;

            _found[name] = loaded;
            __result = loaded;
            _log?.Msg($"[fix] s1mapi-prefab-lookup: '{name}' is loaded but not spawnable - handed it over.");
        }

        /// <summary>
        /// The loaded object of that exact name, preferring a template over something already in the world.
        /// </summary>
        /// <remarks>
        /// A prefab asset belongs to no scene, so <c>scene.IsValid()</c> separates the original from copies
        /// that are standing in the map. Cloning the original is what the caller meant; cloning a placed one
        /// would carry whatever has happened to it.
        /// </remarks>
        private static GameObject Loaded(string name)
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<UnityEngine.Object> all;
            try { all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<GameObject>()); }
            catch { return null; }
            if (all == null) return null;

            GameObject placed = null;
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

                try { if (!candidate.scene.IsValid()) return candidate; } catch { }
                placed ??= candidate;
            }
            return placed;
        }
    }
}
