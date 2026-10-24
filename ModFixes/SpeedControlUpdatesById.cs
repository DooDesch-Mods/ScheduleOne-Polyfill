using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod that hands an NPC a speed control under an id it already has changes that control again.
    /// </summary>
    /// <remarks>
    /// 0.4.6's public <c>AddSpeedControl(SpeedControl)</c> looked the id up first and, when it was there,
    /// copied the new speed and priority onto the old control (NPCSpeedController.cs:56-65 on 0.4.6f13).
    /// Mods relied on that to change a speed: Cartel Enforcer slows its boss and its goons this way, NACops
    /// its raiders and its investigator, each time with the same id.
    ///
    /// 0.4.7 made that overload private and turned the duplicate into a warning that changes nothing
    /// (NPCSpeedController.cs:79-90 on 0.4.7f12). The interop assembly still exposes it, so the call
    /// binds and the NPC keeps whatever speed the first control set.
    ///
    /// The game's own path never reaches the duplicate case: its public <c>AddSpeedControl(id, speed, ...)</c>
    /// updates an existing control itself before it calls the private one (NPCSpeedController.cs:46-59).
    /// So the prefix only ever changes what a mod's call does.
    /// </remarks>
    internal sealed class SpeedControlUpdatesById : Fix
    {
        internal override string Id => "speed-control-updates-by-id";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "a mod that changes an NPC's speed with a control it already added gets the new speed";

        internal override string StandsDownBecause
            => "a mod that changes an NPC's speed a second time under the same name leaves the NPC at the "
             + "first speed.";

        private static MelonLogger.Instance _log;
        private static int _updated;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var target = AccessTools.Method(typeof(NPCSpeedController), "AddSpeedControl",
                                            new[] { typeof(SpeedControl) });
            var find = AccessTools.Method(typeof(NPCSpeedController), "GetSpeedControl", new[] { typeof(string) });
            if (target == null || find == null)
            {
                log.Warning($"[fix] {Id}: NPCSpeedController.AddSpeedControl(SpeedControl) "
                          + $"({target != null}) or GetSpeedControl(string) ({find != null}) is not here.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, prefix: new HarmonyMethod(typeof(SpeedControlUpdatesById), nameof(Update)));
            return true;
        }

        /// <summary>The 0.4.6 answer to a known id: take the new speed and priority, add nothing.</summary>
        private static bool Update(NPCSpeedController __instance, SpeedControl control)
        {
            try
            {
                if (__instance == null || control == null || string.IsNullOrEmpty(control.Id)) return true;

                var existing = __instance.GetSpeedControl(control.Id);
                if (existing == null) return true;

                existing.SetSpeed(control.Speed, control.Type);
                existing.SetPriority(control.Priority);
                if (++_updated == 1)
                    _log?.Msg($"[fix] speed-control-updates-by-id: '{control.Id}' "
                            + $"now runs at {control.Speed} (priority {control.Priority}) instead of keeping "
                            + "the speed it was first added with.");
                return false;
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] speed-control-updates-by-id: could not update a speed control; the game "
                            + "keeps the old one: " + e.Message);
                return true;
            }
        }
    }
}
