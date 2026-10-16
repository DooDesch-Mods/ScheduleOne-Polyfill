using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// AutoRestock finds Oscar on 0.4.7 and starts restocking, instead of stopping at an NPC the game has
    /// not set up.
    /// </summary>
    /// <remarks>
    /// AutoRestock starts its manager when the loading screen closes, and the first thing it does is look
    /// for Oscar, who sends the restock receipt:
    ///
    /// <code>
    /// oscar = Object.FindObjectsOfType&lt;NPC&gt;(true).FirstOrDefault(npc => npc.ID == "oscar_holland");
    /// </code>
    ///
    /// The <c>true</c> takes inactive NPCs too. On 0.4.7 <c>NPC.ID</c> reads <c>NPCData.BasicInfo.ID</c>
    /// while the game runs (NPC.cs:173-188), and an NPC that was never initialised has no NPCData, so the
    /// getter throws NullReferenceException. The exception leaves Initialize before <c>isInitialized</c> is
    /// set, every patch of the mod checks that flag first, and the mod does nothing for the whole session -
    /// the log shows one warning and the player sees no restock at all.
    ///
    /// The guard sits on the lambda and nowhere else: an NPC whose ID cannot be read is not Oscar, so the
    /// search moves on to the next one. <c>NPC.ID</c> itself is left alone, because it is a getter the game
    /// calls constantly and a detour on it would cost every caller for the sake of one search.
    /// </remarks>
    internal sealed class AutoRestockOscarLookup : Fix
    {
        internal override string Id => "autorestock-oscar-lookup";
        internal override string Mod => "AutoRestock";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";
        internal override string What => "AutoRestock starts after a save loads and restocks again";

        private static MelonLogger.Instance _log;
        private static int _skipped;

        internal override bool Apply(MelonLogger.Instance log)
        {
            var manager = AccessTools.TypeByName("AutoRestock.Manager");
            if (manager == null)
            {
                log.Warning("[fix] autorestock-oscar-lookup: AutoRestock.Manager is not in this AutoRestock; nothing was changed.");
                return false;
            }

            // The compiler names the lambda <Initialize>b__N_M on the closure class <>c. N moves with every
            // edit of the mod, so it is found by where it comes from and by its shape: NPC in, bool out.
            var lambdas = manager.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                .Where(m => m.Name.StartsWith("<Initialize>", StringComparison.Ordinal)
                         && m.ReturnType == typeof(bool)
                         && m.GetParameters().Length == 1
                         && m.GetParameters()[0].ParameterType.Name == "NPC")
                .ToList();
            if (lambdas.Count != 1)
            {
                log.Warning($"[fix] autorestock-oscar-lookup: expected one NPC search in AutoRestock's Initialize, found {lambdas.Count}; nothing was changed.");
                return false;
            }

            _log = log;
            new HarmonyLib.Harmony("doodesch.polyfill.fixes")
                .Patch(lambdas[0], finalizer: new HarmonyMethod(typeof(AutoRestockOscarLookup), nameof(NotOscar)));
            return true;
        }

        private static Exception NotOscar(Exception __exception, ref bool __result)
        {
            if (__exception == null)
                return null;

            __result = false;
            _skipped++;
            if (_skipped == 1)
                _log.Warning("[fix] autorestock-oscar-lookup: skipped an NPC without game data while AutoRestock looked for Oscar ("
                           + __exception.GetBaseException().Message.Split('\n')[0].Trim() + ").");
            Fixes.Record("autorestock-oscar-lookup", "skipped an NPC without game data");
            return null;
        }
    }
}
