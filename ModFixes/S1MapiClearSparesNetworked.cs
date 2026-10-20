using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppFishNet.Object;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Keep S1MAPI's site clearing from destroying the game's own networked objects.
    /// </summary>
    /// <remarks>
    /// A building mod asks S1MAPI to clear the ground around its building
    /// (<c>TerrainClearer.ClearAroundBuilding</c>). Besides the terrain trees, <c>ClearSceneObjects</c>
    /// destroys every scene object with a renderer inside the room, and it destroys the LOD ROOT of each
    /// one: <c>ResolveLodRoot</c> climbs to the first ancestor that carries a <c>LODGroup</c>.
    ///
    /// On 0.4.7 a whole business can be that root. <c>@Businesses/PostOffice</c> has its
    /// <c>LODGroup</c> on the same object as its <c>Business</c> and FishNet <c>NetworkObject</c>, with 362
    /// renderers below it (ripped Main scene). One of them inside a mod's room is enough to destroy the post
    /// office, laundering included, and only on the machine that ran the clear.
    ///
    /// This adds one rule to the clear's own filter: an object that is, or holds, a FishNet
    /// <c>NetworkObject</c> is kept. Such an object is game state that the server and every client must
    /// agree on, and a local <c>Object.Destroy</c> is never a correct way to remove it. Scenery, rocks and
    /// vegetation carry no <c>NetworkObject</c> and are cleared as before.
    /// </remarks>
    internal sealed class S1MapiClearSparesNetworked : Fix
    {
        internal override string Id => "s1mapi-clear-spares-networked";
        internal override string Mod => "S1MAPI";
        internal override string ModVersions => "*";
        internal override string GameVersions => "*";
        internal override string What
            => "clearing a building site no longer destroys the game's networked objects, such as the post office";

        internal override string StandsDownBecause
            => "A building mod clearing its site through S1MAPI can destroy a whole vanilla business, such as "
             + "the post office and its laundering, when one of its renderers is inside the room.";

        private static MelonLogger.Instance _log;
        private static PropertyInfo _filter;
        private static readonly List<string> _spared = new();
        private static int _kept;
        private static readonly Dictionary<int, bool> _networked = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            System.Type clearer = null, options = null;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    clearer ??= assembly.GetType("S1MAPI.Building.Structural.TerrainClearer", false);
                    options ??= assembly.GetType("S1MAPI.Building.Structural.ClearingOptions", false);
                }
                catch (System.Exception e)
                {
                    log.Warning($"[fix] s1mapi-clear-spares-networked: could not read {assembly.GetName().Name}: "
                              + e.Message);
                }
                if (clearer != null && options != null) break;
            }
            if (clearer == null || options == null)
            {
                log.Warning("[fix] s1mapi-clear-spares-networked: TerrainClearer or ClearingOptions is not where "
                          + "it was.");
                return false;
            }

            _filter = options.GetProperty("Filter", BindingFlags.Public | BindingFlags.Instance);
            if (_filter == null || _filter.PropertyType != typeof(System.Func<GameObject, bool>))
            {
                log.Warning("[fix] s1mapi-clear-spares-networked: ClearingOptions.Filter is gone or changed shape.");
                return false;
            }

            var target = AccessTools.Method(clearer, "ClearSceneObjects");
            if (target == null)
            {
                log.Warning("[fix] s1mapi-clear-spares-networked: ClearSceneObjects is gone.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(target,
                prefix: new HarmonyMethod(typeof(S1MapiClearSparesNetworked), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(S1MapiClearSparesNetworked), nameof(Postfix)));
            return true;
        }

        /// <summary>
        /// Put the keep rule in front of whatever filter the mod passed.
        /// </summary>
        /// <remarks>
        /// The options object belongs to this one call: ClearAroundBuilding builds a fresh one with the
        /// building as Preserved before it gets here. So wrapping its filter changes nothing for the next
        /// clear, and the mod's own filter still runs for everything this rule does not keep.
        /// </remarks>
        private static void Prefix(object options)
        {
            _spared.Clear();
            _networked.Clear();
            _kept = 0;
            if (options == null) return;

            try
            {
                var theirs = _filter.GetValue(options) as System.Func<GameObject, bool>;
                System.Func<GameObject, bool> ours = go => Keep(go) || (theirs != null && theirs(go));
                _filter.SetValue(options, ours);
            }
            catch (System.Exception e)
            {
                _log?.Error("[fix] s1mapi-clear-spares-networked: could not add the keep rule, this clear runs "
                          + "unguarded: " + e.Message);
            }
        }

        private static void Postfix()
        {
            if (_kept == 0) return;
            _log?.Msg($"[fix] s1mapi-clear-spares-networked: kept {_kept} networked object(s) out of a "
                    + $"site clear: {string.Join(", ", _spared)}{(_kept > _spared.Count ? ", ..." : "")}.");
            _spared.Clear();
        }

        /// <summary>True when the object is, or holds, a FishNet NetworkObject.</summary>
        private static bool Keep(GameObject go)
        {
            if (go == null) return false;

            int key;
            try { key = go.GetInstanceID(); }
            catch (System.Exception e)
            {
                _log?.Warning("[fix] s1mapi-clear-spares-networked: could not identify a clear candidate: "
                            + e.Message);
                return false;
            }
            if (_networked.TryGetValue(key, out bool known)) return known;

            bool networked;
            try { networked = go.GetComponentInChildren<NetworkObject>(true) != null; }
            catch (System.Exception e)
            {
                _log?.Warning($"[fix] s1mapi-clear-spares-networked: could not inspect '{go.name}', left it "
                            + "to the clear: " + e.Message);
                networked = false;
            }

            _networked[key] = networked;
            if (networked)
            {
                _kept++;
                if (_spared.Count < 8) _spared.Add(go.name);
            }
            return networked;
        }
    }
}
