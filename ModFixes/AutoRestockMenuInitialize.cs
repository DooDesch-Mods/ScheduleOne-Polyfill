using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>AutoRestock waits for a save instead of starting in the main menu and failing.</summary>
    /// <remarks>
    /// AutoRestock starts its manager from a postfix on LoadingScreen.Close and stops it from a prefix on
    /// LoadManager.ExitToMenu. The game closes the loading screen again once the menu scene is up after an
    /// exit (LoadManager.cs:999 on 0.4.7f12, the same call since 0.4.5f2), and after a failed join
    /// (LoadManager.cs:712). So the mod starts in the menu, where no TimeManager exists, and its first
    /// read, <c>timeManager.CurrentDay</c>, throws NullReferenceException, which the mod logs as a warning
    /// in every session that ever left a save.
    ///
    /// In the menu there is nothing to start: no clock, no money, no Oscar. The prefix skips Initialize
    /// while the game has no TimeManager, and the mod starts as before when the next save's loading screen
    /// closes, because isInitialized stays false.
    /// </remarks>
    internal sealed class AutoRestockMenuInitialize : Fix
    {
        private const string FixId = "autorestock-menu-initialize";

        internal override string Id => FixId;
        internal override string Mod => "AutoRestock";
        internal override string ModVersions => "<=1.1.5";
        internal override string GameVersions => "*";
        internal override string What => "AutoRestock waits in the main menu for the next save instead of failing to start there";

        internal override string StandsDownBecause
            => "AutoRestock logs a NullReferenceException each time a save is left for the main menu; it still starts "
             + "with the next save.";

        private static MelonLogger.Instance _log;
        private static int _skipped, _failures;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var manager = AccessTools.TypeByName("AutoRestock.Manager");
            var initialize = manager == null ? null : AccessTools.DeclaredMethod(manager, "Initialize", Type.EmptyTypes);
            if (initialize == null)
            {
                log.Warning($"[fix] {FixId}: AutoRestock.Manager.Initialize is not in this AutoRestock (type {manager != null}); nothing was changed.");
                return false;
            }
            new HarmonyLib.Harmony("doodesch.polyfill.fixes")
                .Patch(initialize, prefix: new HarmonyMethod(typeof(AutoRestockMenuInitialize), nameof(OnlyInASave)));
            return true;
        }

        private static bool OnlyInASave()
        {
            try
            {
                if (NetworkSingleton<TimeManager>.InstanceExists) return true;
            }
            catch (Exception e)
            {
                _failures++;
                if (_failures == 1 || _failures % 20 == 0)
                    _log.Error($"[fix] {FixId}: reading the game clock failed ({_failures}): {e}; AutoRestock starts as it would without the fix.");
                Fixes.Record(FixId, "reading the game clock failed: " + e.GetBaseException().Message);
                return true;
            }

            _skipped++;
            if (_skipped == 1 || _skipped % 20 == 0)
                _log.Msg($"[fix] {FixId}: AutoRestock waits for a save; the loading screen closed without one ({_skipped}).");
            return false;
        }
    }
}
