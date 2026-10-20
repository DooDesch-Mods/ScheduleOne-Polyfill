using HarmonyLib;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.Quests;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>Loading a save keeps the deals that were tracked when it was saved on the HUD and compass.</summary>
    /// <remarks>
    /// MultiplayerTrackerFix hides deals another player accepts: its prefix on
    /// <c>QuestManager.CreateContract_Local</c> sets <c>tracked</c> to false unless the local player has just
    /// picked a delivery window in the messages app. The save loader creates every saved contract through the
    /// same method, with the saved <c>IsTracked</c> (QuestsLoader.cs:94, :193 on 0.4.7f12), and no window
    /// button was picked during a load, so every loaded deal came back untracked.
    ///
    /// While <c>QuestsLoader.Load</c> runs, a first-priority prefix keeps the value the loader passed and a
    /// last-priority prefix puts it back after the mod's own prefix. Deals accepted by another player in
    /// co-op still go through the mod's rule.
    /// </remarks>
    internal sealed class TrackerFixKeepsSavedTracking : Fix
    {
        internal override string Id => "trackerfix-saved-tracking";
        internal override string Mod => "MultiplayerTrackerFix";
        internal override string ModVersions => "1.0.1";
        internal override string GameVersions => "0.4.7*";
        internal override string What => "deals tracked when the game was saved stay on the HUD and compass after loading";
        internal override string StandsDownBecause => "MultiplayerTrackerFix untracks every contract the save loader creates";

        private static MelonLogger.Instance _log;
        private static int _loading;
        private static bool _saved;
        private static int _restored;

        internal override bool Apply(MelonLogger.Instance log)
        {
            var modPatch = AccessTools.TypeByName("MultiplayerTrackerFix.Patch_CreateContract_Local");
            var modPrefix = modPatch == null ? null : AccessTools.Method(modPatch, "Prefix");
            var create = AccessTools.DeclaredMethod(typeof(QuestManager), nameof(QuestManager.CreateContract_Local));
            var load = AccessTools.DeclaredMethod(typeof(QuestsLoader), nameof(QuestsLoader.Load), new[] { typeof(string) });
            if (modPrefix == null)
            {
                log.Warning("[fix] trackerfix-saved-tracking: MultiplayerTrackerFix's CreateContract_Local prefix is not "
                          + "where it was; loaded deals were left as the mod sets them.");
                return false;
            }
            if (create == null || load == null)
            {
                log.Warning("[fix] trackerfix-saved-tracking: QuestManager.CreateContract_Local or QuestsLoader.Load is "
                          + "missing; loaded deals were left as the mod sets them.");
                return false;
            }
            if (HarmonyLib.Harmony.GetPatchInfo(create)?.Prefixes.Any(p => p.PatchMethod == modPrefix) != true)
            {
                log.Warning("[fix] trackerfix-saved-tracking: MultiplayerTrackerFix's prefix is not registered; nothing "
                          + "to correct.");
                return false;
            }

            _log = log;
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Patch(load,
                prefix: new HarmonyMethod(typeof(TrackerFixKeepsSavedTracking), nameof(LoadStarts)),
                finalizer: new HarmonyMethod(typeof(TrackerFixKeepsSavedTracking), nameof(LoadEnds)));
            harmony.Patch(create,
                prefix: new HarmonyMethod(typeof(TrackerFixKeepsSavedTracking), nameof(Keep)) { priority = Priority.First });
            harmony.Patch(create,
                prefix: new HarmonyMethod(typeof(TrackerFixKeepsSavedTracking), nameof(Restore)) { priority = Priority.Last });
            return true;
        }

        private static void LoadStarts() => _loading++;

        private static Exception LoadEnds(Exception __exception)
        {
            if (_loading > 0) _loading--;
            if (_loading == 0 && _restored > 0)
            {
                _log.Msg($"[fix] trackerfix-saved-tracking: {_restored} loaded deal(s) kept their tracking.");
                _restored = 0;
            }
            return __exception;
        }

        private static void Keep(bool tracked)
        {
            if (_loading > 0) _saved = tracked;
        }

        private static void Restore(ref bool tracked)
        {
            if (_loading == 0 || tracked == _saved) return;
            tracked = _saved;
            if (_saved) _restored++;
        }
    }
}
