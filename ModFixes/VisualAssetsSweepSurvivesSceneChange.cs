using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>Visual Assets Overhaul keeps replacing icons after a save is loaded.</summary>
    /// <remarks>
    /// The mod's icon sweep walks the scene's <c>UI</c> root one element per frame. Loading a save starts a
    /// sweep in the menu (<c>OnPreLoad</c>), the menu's UI root is destroyed while it is still walking, and the
    /// next <c>GetComponentsInChildren</c> on that root throws. The exception ends the coroutine before
    /// <c>RefreshRoutine</c> clears <c>_refreshRunning</c>, and with that flag stuck every later refresh only
    /// queues itself and never runs: no icon is replaced for the rest of the session.
    ///
    /// A finalizer on the sweep's <c>MoveNext</c> ends that one sweep instead of the routine around it. The
    /// routine then sees the sweep finish, runs the refresh that was queued meanwhile against the new UI root,
    /// and clears the flag itself.
    /// </remarks>
    internal sealed class VisualAssetsSweepSurvivesSceneChange : Fix
    {
        internal override string Id => "vao-sweep-scene-change";
        internal override string Mod => "Visual Assets Overhaul";
        internal override string ModVersions => "*";
        internal override string GameVersions => "*";
        internal override string What => "icons keep being replaced after loading a save instead of stopping for the session";
        internal override string StandsDownBecause => "a scene change in the middle of its icon sweep stops every later icon refresh";

        private static MelonLogger.Instance _log;
        private static int _ended;

        internal override bool Apply(MelonLogger.Instance log)
        {
            var manager = AccessTools.TypeByName("VisualAssetsOverhaul.IconReplacementManager");
            if (manager == null)
            {
                log.Warning("[fix] vao-sweep-scene-change: VisualAssetsOverhaul.IconReplacementManager is not there; the "
                          + "icon sweep was left as it is.");
                return false;
            }
            var sweep = manager.GetNestedTypes(AccessTools.all)
                               .FirstOrDefault(t => t.Name.StartsWith("<SweepUI>", StringComparison.Ordinal));
            var moveNext = sweep == null ? null : AccessTools.DeclaredMethod(sweep, "MoveNext");
            if (moveNext == null)
            {
                log.Warning("[fix] vao-sweep-scene-change: the mod's SweepUI coroutine is not where it was; the icon "
                          + "sweep was left as it is.");
                return false;
            }

            _log = log;
            new HarmonyLib.Harmony("doodesch.polyfill.fixes")
                .Patch(moveNext, finalizer: new HarmonyMethod(typeof(VisualAssetsSweepSurvivesSceneChange), nameof(EndSweep)));
            return true;
        }

        private static Exception EndSweep(Exception __exception, ref bool __result)
        {
            if (__exception == null) return null;
            _ended++;
            __result = false;
            _log.Warning($"[fix] vao-sweep-scene-change: an icon sweep lost its UI ({__exception.GetType().Name}: "
                       + $"{__exception.Message}); it ended there and the next refresh runs ({_ended} so far).");
            return null;
        }
    }
}
