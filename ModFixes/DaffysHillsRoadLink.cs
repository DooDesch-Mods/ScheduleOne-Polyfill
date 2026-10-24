using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;
using UnityEngine;
using UnityEngine.AI;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// NPCs walk across the road link DaffysHills adds instead of standing on it with an error every frame.
    /// </summary>
    /// <remarks>
    /// DaffysHills joins its Clifftop road to the town with <c>NavMesh.AddLink</c>, a link with no
    /// <c>OffMeshLink</c> component behind it. <c>NPCMovement.Update</c> treats every off-mesh link as a ladder
    /// and calls <c>TraverseLadder(_agent.currentOffMeshLinkData.offMeshLink.GetComponent&lt;Ladder&gt;())</c>
    /// (NPCMovement.cs:262-268 on 0.4.7f12, the same on 0.4.6f13). For a code link <c>offMeshLink</c> is null,
    /// so that line throws a NullReferenceException every frame and the NPC stands still until DaffysHills'
    /// own watchdog warps it across after 2.5 seconds.
    ///
    /// The prefix walks such an NPC to the link's end at its own speed and completes the link, which is what
    /// the agent would do on its own with automatic traversal. A ladder link (one with a component) and an NPC
    /// off any link run vanilla. Update does nothing else, so skipping it while on a code link loses nothing.
    /// RVRepairVan carries the same prefix for its door link; when another prefix already moved the NPC this
    /// frame (<c>__runOriginal</c> is false), this one leaves it alone.
    ///
    /// DaffysHills' watchdog (<c>ClifftopWorker.Worker.LinkHelper</c>) warps an agent to the far side once it
    /// has spent 2.5 seconds within 6 m of a link end, counted in <c>Worker.LinkStall</c>. The link is about
    /// 15 m long, so a walking NPC would be warped twice on the way. While the prefix walks an NPC across, it
    /// drops that agent from <c>LinkStall</c>, so the watchdog keeps restarting its count for it. An agent that
    /// is stuck beside the link, and not walking on it, is still warped by the watchdog.
    /// </remarks>
    internal sealed class DaffysHillsRoadLink : Fix
    {
        internal override string Id => "daffyshills-road-link";
        internal override string Mod => "DaffysHills";
        internal override string ModVersions => "1.0.0";
        internal override string GameVersions => "*";

        internal override string What
            => "NPCs walk across DaffysHills' road link without an error every frame";

        internal override string StandsDownBecause
            => "NPCs that reach DaffysHills' road link stop on it for a few seconds and log an error every frame.";

        // A slow walk for an NPC whose agent reports no speed, so it still gets across.
        private const float FallbackSpeed = 1.5f;

        private static MelonLogger.Instance _log;
        private static int _crossed;
        private static System.Collections.IDictionary _stall;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var target = AccessTools.Method(typeof(NPCMovement), "Update");
            if (target == null)
            {
                log.Warning($"[fix] {Id}: NPCMovement.Update was not found, so NPCs keep stopping on the road link.");
                return false;
            }

            var worker = AccessTools.TypeByName("ClifftopWorker.Worker");
            var stall = worker == null ? null : AccessTools.Field(worker, "LinkStall");
            _stall = stall?.GetValue(null) as System.Collections.IDictionary;
            if (_stall == null)
                log.Warning($"[fix] {Id}: DaffysHills' LinkStall was not found (Worker {worker != null}, field "
                          + $"{stall != null}); NPCs walk onto the link but its watchdog may still warp them across.");

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, prefix: new HarmonyMethod(typeof(DaffysHillsRoadLink), nameof(Cross)));
            return true;
        }

        private static bool Cross(NPCMovement __instance, bool __runOriginal)
        {
            if (!__runOriginal) return false;
            try
            {
                NavMeshAgent agent = __instance._agent;
                if (agent == null || !agent.enabled || !agent.isOnOffMeshLink) return true;
                OffMeshLinkData link = agent.currentOffMeshLinkData;
                if (link.offMeshLink != null) return true;

                _stall?.Remove(agent.GetInstanceID());
                Transform t = agent.transform;
                Vector3 end = link.endPos + Vector3.up * agent.baseOffset;
                float speed = agent.speed > 0.1f ? agent.speed : FallbackSpeed;
                t.position = Vector3.MoveTowards(t.position, end, speed * Time.deltaTime);
                Vector3 flat = new Vector3(end.x - t.position.x, 0f, end.z - t.position.z);
                if (flat.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(flat);
                if ((t.position - end).sqrMagnitude < 0.0025f)
                {
                    agent.CompleteOffMeshLink();
                    if (++_crossed == 1)
                        _log?.Msg($"[fix] daffyshills-road-link: {__instance.gameObject.name} walked across "
                                + "a road link without a ladder.");
                }
                return false;
            }
            catch (System.Exception e)
            {
                _log?.Warning("[fix] daffyshills-road-link: could not move an NPC across a road link; the game "
                            + "handles it as a ladder: " + e.Message);
                return true;
            }
        }
    }
}
