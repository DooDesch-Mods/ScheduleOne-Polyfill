using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A person dazzled by Solar Flare or scared by a glowing Moon Hopper stops for the flinch again.
    /// </summary>
    /// <remarks>
    /// On 0.4.6 a heavy <c>AvatarAnimation.Flinch</c> raised onHeavyFlinch, and the NPC's behaviours answered with
    /// HeavyFlinchBehaviour.Flinch (AvatarAnimation.cs:393-395, NPCBehaviour.cs:84 on 0.4.6f13), which holds it still.
    /// 0.4.7 removed the event, and NPC.ProcessImpactForce now calls both itself (NPC.cs:845-846 on 0.4.7f12). The mod
    /// still calls only the animation, so the NPC plays it while it keeps walking.
    ///
    /// The second call is added at the mod's two heavy flinches. A patch on Flinch itself would run for the game's
    /// own impacts too, which would then hold an NPC for twice as long.
    /// </remarks>
    internal sealed class MoonRocksHeavyFlinch : Fix
    {
        internal override string Id => "moonrocks-heavy-flinch";
        internal override string Mod => "Moon Rocks";

        /// <summary>Written against 1.1.0. A later build may call the behaviour itself.</summary>
        internal override string ModVersions => "<=1.1.0";

        /// <summary>0.4.7f5 is the first build without the event.</summary>
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "people dazzled or scared by the moon's drugs stop for the flinch";

        internal override string StandsDownBecause
            => "People flinching at Solar Flare or a glowing Moon Hopper keep walking through the animation.";

        private static MelonLogger.Instance _log;
        private static FieldInfo _glaringNpc;
        private static bool _reportedFlinch, _reportedFailure;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var scare = AccessTools.TypeByName("MoonRocks.Features.Mounts.MountBuffs") is { } buffs
                ? AccessTools.DeclaredMethod(buffs, "Scare") : null;
            var glare = AccessTools.TypeByName("MoonRocks.Features.SolarFlare.GlareReaction");
            var begin = glare == null ? null : AccessTools.DeclaredMethod(glare, "Begin");
            _glaringNpc = glare == null ? null : AccessTools.DeclaredField(glare, "_npc");
            if (scare == null || begin == null || _glaringNpc == null)
            {
                log.Warning($"[fix] {Id}: something it needs is not here (Scare {scare != null}, Begin {begin != null}, "
                          + $"_npc {_glaringNpc != null}).");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Patch(scare, postfix: new HarmonyMethod(typeof(MoonRocksHeavyFlinch), nameof(Scared)));
            harmony.Patch(begin, postfix: new HarmonyMethod(typeof(MoonRocksHeavyFlinch), nameof(Dazzled)));
            return true;
        }

        private static void Scared(NPC npc) => Hold(npc);

        private static void Dazzled(object __instance) => Hold(_glaringNpc.GetValue(__instance) as NPC);

        /// <summary>The mod's own test for a heavy flinch: nobody on a ladder or in a seat gets one.</summary>
        private static void Hold(NPC npc)
        {
            try
            {
                var flinch = npc == null || npc.Behaviour == null ? null : npc.Behaviour.HeavyFlinchBehaviour;
                if (flinch == null || npc.Movement.IsOnLadder || npc.Avatar.Animation.IsSeated) return;

                flinch.Flinch();
                if (_reportedFlinch) return;
                _reportedFlinch = true;
                _log?.Msg($"[fix] moonrocks-heavy-flinch: {npc.FullName} stops for the flinch.");
            }
            catch (Exception e)
            {
                if (_reportedFailure) return;
                _reportedFailure = true;
                _log?.Warning("[fix] moonrocks-heavy-flinch: an NPC walks through its flinch: " + e);
            }
        }
    }
}
