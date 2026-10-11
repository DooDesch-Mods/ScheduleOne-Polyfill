using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI.Stations;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>Preserves ProduceMore's drying display without constructing Singleton&lt;DryingRackCanvas&gt;.</summary>
    internal sealed class ProduceMoreDryingRack : Fix
    {
        internal override string Id => "producemore-drying-rack";
        internal override string Mod => "ProduceMore";
        internal override string ModVersions => "1.2.0";
        internal override string GameVersions => "0.4.6*,0.4.7*";
        internal override string What => "the drying rack shows ProduceMore's progress and remaining time again";
        internal override string StandsDownBecause => "ProduceMore's drying display uses a renamed station screen as a self-referencing singleton";

        private static MethodInfo _speed, _originalTime;
        private static MelonLogger.Instance _log;
        private static int _failures;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            var utils = AccessTools.TypeByName("ProduceMore.Utils");
            var patches = AccessTools.TypeByName("ProduceMore.DryingRackPatches");
            var old = patches == null ? null : AccessTools.Method(patches, "UpdatePositionPostfix");
            var target = AccessTools.DeclaredMethod(typeof(DryingOperationUI), "UpdatePosition");
            _speed = utils == null ? null : AccessTools.Method(utils, "GetStationSpeed", new[] { typeof(string) });
            _originalTime = utils == null ? null : AccessTools.Method(utils, "AddOriginalStationTime", new[] { typeof(GridItem), typeof(int) });
            if (old == null || target == null || _speed == null || _originalTime == null)
            {
                log.Warning("[fix] producemore-drying-rack: the display patch or its speed helpers are missing; the display was left unchanged.");
                return false;
            }
            if (HarmonyLib.Harmony.GetPatchInfo(target)?.Postfixes.Any(p => p.PatchMethod == old) != true)
            {
                log.Warning("[fix] producemore-drying-rack: ProduceMore's display patch is not registered; the display was left unchanged.");
                return false;
            }

            _log = log;
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            // The old body cannot be JIT-compiled because its generic argument violates Singleton<T>'s
            // constraint. Patch the callable game method instead of detouring that body.
            harmony.Unpatch(target, old);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ProduceMoreDryingRack), nameof(After)));
            if (HarmonyLib.Harmony.GetPatchInfo(target)?.Postfixes.Any(p => p.PatchMethod == old) == true)
                throw new InvalidOperationException("ProduceMore's broken drying display patch is still registered");
            return true;
        }

        private static void After(DryingOperationUI __instance)
        {
            try
            {
                var rack = Singleton<DryingRackInterface>.Instance.Rack;
                if (rack != null)
                    _originalTime.Invoke(null, new object[] { rack, DryingRack.DRY_MINS_PER_TIER });
                float speed = (float)_speed.Invoke(null, new object[] { "DryingRack" });
                float tierMinutes = DryingRack.DRY_MINS_PER_TIER / speed;
                float progress = Mathf.Clamp01(__instance.AssignedOperation.Time / tierMinutes);
                int remaining = (int)Mathf.Clamp((int)tierMinutes - __instance.AssignedOperation.Time, 0f, (int)tierMinutes);
                __instance.Tooltip.text = remaining / 60 + "h " + remaining % 60 + "m until next tier";
                __instance.Rect.anchoredPosition = new Vector2(Mathf.Lerp(-62.5f, 62.5f, progress), 0f);
                if (!_said)
                {
                    _said = true;
                    _log.Msg("[fix] producemore-drying-rack: progress and remaining time updated with ProduceMore's speed setting.");
                }
                if (_failures > 0)
                {
                    _log.Msg($"[fix] producemore-drying-rack: display recovered after {_failures} failed update(s).");
                    _failures = 0;
                }
            }
            catch (Exception e)
            {
                _failures++;
                if (_failures == 1 || _failures % 60 == 0)
                    _log.Error($"[fix] producemore-drying-rack: display update failed ({_failures}): {e}; the game's own display remains.");
                Fixes.Record("producemore-drying-rack", "display update failed: " + e.GetBaseException().Message);
            }
        }
    }
}
