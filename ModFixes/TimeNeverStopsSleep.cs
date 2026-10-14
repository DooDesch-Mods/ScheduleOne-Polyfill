using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.UI;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>Time Never Stops' night ends again on 0.4.7: no stuck sleep, no extra day.</summary>
    /// <remarks>
    /// Two things changed under the mod that a bridge cannot carry.
    ///
    /// THE SUMMARY IT HIDES NOW HOLDS THE NIGHT. With the phone app installed, the mod skips
    /// DailySummary.Open and Close so the summary goes to the app instead. On 0.4.6 a skipped Open left
    /// IsOpen false (it was set inside Open, DailySummary.cs:72 on 0.4.6f13), so the sleep canvas moved
    /// on. 0.4.7 sets IsInProgress BEFORE Open (DailySummary.cs:73-77 on 0.4.7f12), clears it only in
    /// Close (:117-127), and the sleep routine waits for it (SleepController.cs:149). Skipping both left
    /// every night waiting forever. So a skipped Open clears the flag, as 0.4.6 effectively did.
    ///
    /// THE WAKE-UP SKIP LOST ITS NAME. The mod stops the wake-up fast-forward once the clock is already
    /// past 07:00, by a prefix on 0.4.6's private SkipForwardToTime (TimeManager.cs:424, called only from
    /// the sleep at :822-827 on 0.4.6f13). 0.4.7 wakes through SkipToTimeAndSync (SleepController.cs:266
    /// on 0.4.7f12), and a skip that goes backwards there counts a whole extra day (TimeManager.cs:790-834).
    /// So the mod's own prefix is asked again, from the new call and only while a sleep runs.
    /// </remarks>
    internal sealed class TimeNeverStopsSleep : Fix
    {
        private const string FixId = "timeneverstops-sleep";

        internal override string Id => FixId;
        internal override string Mod => "Time Never Stops";
        internal override string ModVersions => "1.5.0";
        internal override string GameVersions => "0.4.7*";
        internal override string What => "nights with Time Never Stops end again, without an extra day";
        internal override string StandsDownBecause
            => "Time Never Stops hides the daily summary in a way that holds the 0.4.7 sleep, and its wake-up "
             + "guard patches a method 0.4.7 no longer calls";

        private static MethodInfo _wakeGuard, _setInProgress;
        private static MelonLogger.Instance _log;
        private static bool _saidSummary, _saidWake;
        private static int _failures;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var guard = AccessTools.TypeByName("Time_Never_Stops.Patch_TimeManager_SkipForwardToTime");
            _wakeGuard = guard == null ? null : AccessTools.Method(guard, "Prefix", new[] { typeof(TimeManager) });
            _setInProgress = AccessTools.PropertySetter(typeof(DailySummary), nameof(DailySummary.IsInProgress));
            var open = AccessTools.DeclaredMethod(typeof(DailySummary), nameof(DailySummary.Open), Type.EmptyTypes);
            var skip = AccessTools.DeclaredMethod(typeof(TimeManager), nameof(TimeManager.SkipToTimeAndSync), new[] { typeof(int) });

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            int wired = 0;

            if (open == null || _setInProgress == null)
                log.Warning($"[fix] {FixId}: DailySummary.Open or the IsInProgress setter is missing; a hidden summary still holds the night.");
            else
            {
                harmony.Patch(open, postfix: new HarmonyMethod(typeof(TimeNeverStopsSleep), nameof(AfterOpen)));
                wired++;
            }

            if (_wakeGuard == null || skip == null)
                log.Warning($"[fix] {FixId}: the mod's wake-up guard or TimeManager.SkipToTimeAndSync is missing; waking after 07:00 still counts an extra day.");
            else
            {
                harmony.Patch(skip, prefix: new HarmonyMethod(typeof(TimeNeverStopsSleep), nameof(BeforeSkip)));
                wired++;
            }

            if (wired == 0) return false;
            log.Msg($"[fix] {FixId}: {wired} of 2 repairs in place.");
            return true;
        }

        /// <summary>A summary the mod hid has nothing left to show, so the night may go on.</summary>
        private static void AfterOpen(DailySummary __instance, bool __runOriginal)
        {
            if (__runOriginal) return;
            try
            {
                if (!__instance.IsInProgress) return;
                _setInProgress.Invoke(__instance, new object[] { false });
                if (!_saidSummary)
                {
                    _saidSummary = true;
                    _log.Msg($"[fix] {FixId}: the hidden daily summary released the night.");
                }
            }
            catch (Exception e)
            {
                Fail("releasing the hidden summary", e);
            }
        }

        /// <summary>The mod's own wake-up guard, asked from the call 0.4.7 wakes through.</summary>
        private static bool BeforeSkip(TimeManager __instance, int newTime)
        {
            try
            {
                var sleep = NetworkSingleton<SleepController>.Instance;
                if (sleep == null || !sleep.IsSleepInProgress) return true;

                bool run = (bool)_wakeGuard.Invoke(null, new object[] { __instance });
                if (!_saidWake || !run)
                {
                    _saidWake = true;
                    _log.Msg($"[fix] {FixId}: wake-up skip to {newTime} at {__instance.CurrentTime} "
                           + (run ? "runs." : "held by Time Never Stops."));
                }
                return run;
            }
            catch (Exception e)
            {
                Fail("asking the wake-up guard", e);
                return true;
            }
        }

        private static void Fail(string what, Exception e)
        {
            _failures++;
            if (_failures == 1 || _failures % 20 == 0)
                _log.Error($"[fix] {FixId}: {what} failed ({_failures}): {e}; the game's own behaviour remains.");
            Fixes.Record(FixId, what + " failed: " + e.GetBaseException().Message);
        }
    }
}
