using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A body knocked down on Moon Rocks' moon falls at the moon's gravity.
    /// </summary>
    /// <remarks>
    /// The mod scales the ConstantForce components a ragdoll falls with, from a postfix on
    /// <c>NPCMovement.SetGravityMultiplier</c> and <c>Player.SetGravityMultiplier</c>, through the permanent
    /// <c>ragdollForceComponents</c> list. 0.4.7 has neither that list nor that route: each knock-down builds a
    /// new Ragdoll, and the avatar sets its multiplier directly (Avatar.cs:281-303, NPCMovement.cs:1453-1454 on
    /// 0.4.7f12), so a postfix on the movement component does not run. Left alone, the body falls at Earth's
    /// gravity on the moon.
    ///
    /// <c>Ragdoll.SetGravityMultiplier</c> is the point every route ends in. It writes each force absolutely
    /// (Ragdoll.cs on 0.4.7f12), so scaling after it cannot add up over repeated calls. The bridged list is
    /// empty, so the mod's own postfix scales nothing on top of this.
    /// </remarks>
    internal sealed class MoonRocksRagdollGravity : Fix
    {
        internal override string Id => "moonrocks-ragdoll-gravity";
        internal override string Mod => "Moon Rocks";

        /// <summary>Written against 1.1.0. A later build may scale the 0.4.7 ragdoll itself.</summary>
        internal override string ModVersions => "<=1.1.0";

        /// <summary>0.4.7f5 is the build the per-knock-down Ragdoll arrived in.</summary>
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "bodies knocked down on the moon fall at the moon's gravity";

        internal override string StandsDownBecause
            => "Bodies knocked down on Moon Rocks' moon may fall at Earth's gravity.";

        private static MelonLogger.Instance _log;
        private static Func<Vector3, float> _scaleAt;
        private static PropertyInfo _forces, _length, _item, _force;
        private static bool _reportedScale, _reportedFailure;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var ragdoll = AccessTools.TypeByName("Il2CppScheduleOne.AvatarFramework.Ragdoll");
            var set = ragdoll == null ? null : AccessTools.DeclaredMethod(ragdoll, "SetGravityMultiplier", new[] { typeof(float) });
            _forces = ragdoll == null ? null : AccessTools.Property(ragdoll, "_constantForces");
            _length = _forces?.PropertyType.GetProperty("Length");
            _item = _forces?.PropertyType.GetProperty("Item");
            _force = AccessTools.Property(AccessTools.TypeByName("UnityEngine.ConstantForce"), "force");
            var api = AccessTools.TypeByName("MoonRocks.Shared.MoonApi");
            var scale = api == null ? null : AccessTools.Method(api, "GravityScaleAt", new[] { typeof(Vector3) });

            if (set == null || _forces == null || _length == null || _item == null || _force == null || scale == null)
            {
                log.Warning($"[fix] {Id}: something it needs is not here (Ragdoll.SetGravityMultiplier {set != null}, "
                          + $"_constantForces {_forces != null}, its Length {_length != null}, its Item {_item != null}, "
                          + $"ConstantForce.force {_force != null}, MoonApi.GravityScaleAt {scale != null}).");
                return false;
            }

            _scaleAt = (Func<Vector3, float>)Delegate.CreateDelegate(typeof(Func<Vector3, float>), scale);
            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(set,
                postfix: new HarmonyMethod(typeof(MoonRocksRagdollGravity), nameof(Scale)));
            return true;
        }

        private static void Scale(object __instance)
        {
            try
            {
                var forces = _forces.GetValue(__instance);
                if (forces == null) return;

                int count = (int)_length.GetValue(forces);
                for (int i = 0; i < count; i++)
                {
                    var item = _item.GetValue(forces, new object[] { i });
                    if (item is not Component component || component == null) continue;

                    float scale = _scaleAt(component.transform.position);
                    if (Mathf.Approximately(scale, 1f)) continue;

                    var force = (Vector3)_force.GetValue(item);
                    _force.SetValue(item, force * scale);

                    if (_reportedScale) continue;
                    _reportedScale = true;
                    _log?.Msg($"[fix] moonrocks-ragdoll-gravity: a ragdoll at {component.transform.position} falls at "
                            + $"x{scale:0.###} of Earth's gravity ({force.y:0.##} to {(force * scale).y:0.##}).");
                }
            }
            catch (Exception e)
            {
                if (_reportedFailure) return;
                _reportedFailure = true;
                _log?.Error("[fix] moonrocks-ragdoll-gravity: a ragdoll keeps Earth's gravity: " + e);
            }
        }
    }
}
