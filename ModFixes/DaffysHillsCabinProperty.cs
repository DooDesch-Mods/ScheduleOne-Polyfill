using HarmonyLib;
using Il2CppScheduleOne.Property;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// DaffysHills' cabin becomes a property without the game throwing while it is added.
    /// </summary>
    /// <remarks>
    /// DaffysHills makes its cabin a property with <c>AddComponent&lt;Property&gt;()</c> and fills in the
    /// bounds, contents container, spawn points and the rest afterwards. Unity runs <c>Awake</c> inside
    /// <c>AddComponent</c>, while every field is still empty, and its first line reads
    /// <c>BoundingBox.GetComponentsInChildren</c> (Property.cs:878-880 on 0.4.7f12, the same on 0.4.6f13).
    /// That throws a NullReferenceException before any of Awake's own setup has run. The mod catches nothing
    /// there, but its setup does not depend on Awake: it builds everything Awake would have built itself.
    ///
    /// The prefix reproduces what the exception leaves behind, without the exception: for a property that has
    /// no <c>BoundingBox</c> yet it runs FishNet's early network setup, which Awake does before the throwing
    /// line, and skips the rest. Every property the game places has a bounding box and runs vanilla.
    /// </remarks>
    internal sealed class DaffysHillsCabinProperty : Fix
    {
        internal override string Id => "daffyshills-cabin-property";
        internal override string Mod => "DaffysHills";
        internal override string ModVersions => "1.0.0";
        internal override string GameVersions => "*";

        internal override string What
            => "DaffysHills' cabin becomes a property without an error";

        internal override string StandsDownBecause
            => "DaffysHills' cabin logs a NullReferenceException in Property.Awake on every load.";

        private static MelonLogger.Instance _log;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var target = AccessTools.Method(typeof(Property), nameof(Property.Awake));
            if (target == null)
            {
                log.Warning($"[fix] {Id}: Property.Awake was not found, so DaffysHills' cabin keeps throwing on load.");
                return false;
            }
            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, prefix: new HarmonyMethod(typeof(DaffysHillsCabinProperty), nameof(Bare)));
            return true;
        }

        private static bool Bare(Property __instance)
        {
            try
            {
                if (__instance == null || __instance.BoundingBox != null) return true;

                __instance.NetworkInitialize___Early();
                if (!_said)
                {
                    _said = true;
                    _log?.Msg($"[fix] daffyshills-cabin-property: {__instance.gameObject.name} was added without "
                            + "bounds; the mod sets it up itself, so Awake was skipped.");
                }
                return false;
            }
            catch (System.Exception e)
            {
                _log?.Warning("[fix] daffyshills-cabin-property: could not check a new property; the game sets "
                            + "it up as it is: " + e.Message);
                return true;
            }
        }
    }
}
