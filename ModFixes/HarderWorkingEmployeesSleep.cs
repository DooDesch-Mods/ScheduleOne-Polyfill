using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>Harder Working Employees tidies up its employees around a night's sleep again.</summary>
    /// <remarks>
    /// The mod releases route and station locks when the sleep starts and puts employees that ended up off the
    /// NavMesh back after the clock skipped. It hooks both moments on 0.4.6's TimeManager: a postfix on its
    /// StartSleep RPC and one on RpcLogic___SetHostSleepDone, the host's word that its sleep events were over
    /// (TimeManager.cs:411-415, 802-841 on 0.4.6f13). 0.4.7 moved the sleep to SleepController
    /// (SleepController.cs:121-126, 217-298 on 0.4.7f12). The bridge for TimeManager.StartSleep lets the first
    /// patch bind, but nothing calls it; SetHostSleepDone has no counterpart a mod may call.
    ///
    /// So the mod's two handlers are called from where 0.4.7 sleeps: the start after SleepController's StartSleep
    /// logic began a sleep, the end after the host skipped the clock to the wake time, which only happens inside
    /// the sleep routine (SleepController.cs:264-268). Both handlers check for the host themselves.
    /// </remarks>
    internal sealed class HarderWorkingEmployeesSleep : Fix
    {
        private const string FixId = "harderworkingemployees-sleep";

        internal override string Id => FixId;
        internal override string Mod => "Harder Working Employees";

        /// <summary>Written against 2.2.3. A later build may hook SleepController itself.</summary>
        internal override string ModVersions => "<=2.2.3";

        /// <summary>0.4.7f5 is the first build with SleepController.</summary>
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "employees release their locks before a night's sleep and get back on their feet after it";

        internal override string StandsDownBecause
            => "Employees keep the shelves and stations they had locked through the night, and one left off its path "
             + "after sleeping stays stuck.";

        private static MethodInfo _started, _ended;
        private static MelonLogger.Instance _log;
        private static bool _saidStart, _saidEnd;
        private static int _failures;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var patches = AccessTools.TypeByName("HarderWorkingEmployees.Patches.SleepPatches");
            _started = patches == null ? null : AccessTools.DeclaredMethod(patches, "TimeManager_StartSleep_Postfix", Type.EmptyTypes);
            _ended = patches == null ? null : AccessTools.DeclaredMethod(patches, "TimeManager_EndSleep_Postfix", new[] { typeof(bool) });
            var start = AccessTools.DeclaredMethod(typeof(SleepController), nameof(SleepController.RpcLogic___StartSleep_2166136261));
            var skip = AccessTools.DeclaredMethod(typeof(TimeManager), nameof(TimeManager.SkipToTimeAndSync), new[] { typeof(int) });

            if (_started == null || _ended == null || start == null || skip == null)
            {
                log.Warning($"[fix] {FixId}: something it needs is not here (mod start {_started != null}, mod end "
                          + $"{_ended != null}, SleepController start {start != null}, SkipToTimeAndSync {skip != null}).");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(HarderWorkingEmployeesSleep), nameof(BeforeStart)),
                                 postfix: new HarmonyMethod(typeof(HarderWorkingEmployeesSleep), nameof(AfterStart)));
            harmony.Patch(skip, postfix: new HarmonyMethod(typeof(HarderWorkingEmployeesSleep), nameof(AfterSkip)));
            return true;
        }

        /// <summary>Whether this call begins a sleep. A second StartSleep during one is ignored by the game.</summary>
        private static void BeforeStart(SleepController __instance, out bool __state)
        {
            __state = false;
            try
            {
                __state = !__instance.IsSleepInProgress;
            }
            catch (Exception e)
            {
                Fail("reading whether a sleep runs", e);
            }
        }

        private static void AfterStart(bool __state)
        {
            if (!__state) return;
            Call(_started, null, "the sleep start", ref _saidStart);
        }

        /// <summary>The wake-up skip, the only skip that runs while a sleep is in progress.</summary>
        private static void AfterSkip(int newTime)
        {
            try
            {
                var sleep = NetworkSingleton<SleepController>.Instance;
                if (sleep == null || !sleep.IsSleepInProgress) return;
            }
            catch (Exception e)
            {
                Fail("reading whether a sleep runs", e);
                return;
            }
            Call(_ended, new object[] { true }, $"the wake-up at {newTime}", ref _saidEnd);
        }

        private static void Call(MethodInfo handler, object[] args, string moment, ref bool said)
        {
            try
            {
                handler.Invoke(null, args);
                if (said) return;
                said = true;
                _log.Msg($"[fix] {FixId}: Harder Working Employees handled {moment}.");
            }
            catch (Exception e)
            {
                Fail("calling the mod at " + moment, e);
            }
        }

        private static void Fail(string what, Exception e)
        {
            _failures++;
            if (_failures == 1 || _failures % 20 == 0)
                _log.Error($"[fix] {FixId}: {what} failed ({_failures}): {e}; the mod's sleep cleanup does not run.");
            Fixes.Record(FixId, what + " failed: " + e.GetBaseException().Message);
        }
    }
}
