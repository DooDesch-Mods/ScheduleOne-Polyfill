using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Moon Rocks' bosses, hawks and couriers fight with the health the mod gives them.
    /// </summary>
    /// <remarks>
    /// The mod writes the new maximum into the NPC's data and then restores its health
    /// (ConquestActors.SetMaxHealth, LoversActors.SetMaxHealth, PuppetActors.SetMaxHealth, HawkFight). On 0.4.6
    /// <c>NPCHealth.MaxHealth</c> read that data each time (NPCHealth.cs:82 on 0.4.6f13). 0.4.7 copies it once, in
    /// SetHealthData (NPCHealth.cs:71, :115-117 on 0.4.7f12), so the write changes nothing and the boss keeps the
    /// 275 hp it spawned with.
    ///
    /// Restoring health is the mod's next step at every one of those places, so the maximum is taken over from the
    /// data there. Game NPCs hold the same number in both places, which leaves them unchanged.
    /// </remarks>
    internal sealed class MoonRocksMaxHealth : Fix
    {
        internal override string Id => "moonrocks-max-health";
        internal override string Mod => "Moon Rocks";

        /// <summary>Written against 1.1.0. A later build may set the maximum itself.</summary>
        internal override string ModVersions => "<=1.1.0";

        /// <summary>0.4.7f5 is the first build that copies the maximum.</summary>
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "the moon's bosses and couriers fight with their full health";

        internal override string StandsDownBecause
            => "Moon Rocks' bosses, hawks and couriers keep their normal health and go down early.";

        private static MelonLogger.Instance _log;
        private static bool _reportedChange, _reportedFailure;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var restore = AccessTools.DeclaredMethod(typeof(NPCHealth), "RestoreHealth");
            if (restore == null)
            {
                log.Warning($"[fix] {Id}: NPCHealth.RestoreHealth is not here.");
                return false;
            }
            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(restore,
                prefix: new HarmonyMethod(typeof(MoonRocksMaxHealth), nameof(Restore)));
            return true;
        }

        private static void Restore(NPCHealth __instance)
        {
            try
            {
                var npc = __instance.GetComponent<NPC>();
                var data = npc == null ? null : npc.NPCData;
                var health = data?.Health;
                if (health == null || health.MaxHealth <= 0f || health.MaxHealth == __instance.MaxHealth) return;

                if (!_reportedChange)
                {
                    _reportedChange = true;
                    _log?.Msg($"[fix] moonrocks-max-health: {npc.FullName} goes from {__instance.MaxHealth:0} to "
                            + $"{health.MaxHealth:0} hp.");
                }
                __instance.MaxHealth = health.MaxHealth;
            }
            catch (Exception e)
            {
                if (_reportedFailure) return;
                _reportedFailure = true;
                _log?.Warning("[fix] moonrocks-max-health: an NPC keeps the maximum it spawned with: " + e);
            }
        }
    }
}
