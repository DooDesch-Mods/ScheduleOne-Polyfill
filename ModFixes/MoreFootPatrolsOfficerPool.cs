using System.Collections;
using HarmonyLib;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.Police;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// More Foot Patrols staffs its routes from the police station instead of cloning an officer.
    /// </summary>
    /// <remarks>
    /// The mod builds every patrol by cloning a prefab it looks up by name:
    /// <code>
    /// for (i..) { var o = spawnable.GetObject(true, i); if (o.gameObject.name == "PoliceNPC") val = o; }
    /// var clone = Object.Instantiate(val);            // Core.cs:166-174
    /// ...
    /// officer.StartFootPatrol(group, warpToStart);    // Core.cs:192
    /// </code>
    /// Since 0.4.6 that prefab is not in the spawnable list, so <c>val</c> stays null and the whole thing
    /// ends at <c>NullReferenceException at UnityEngine.Object.Instantiate[T]</c>. No patrol is created.
    ///
    /// THE FIRST ATTEMPT WAS TO SUBSTITUTE THE PREFAB, AND IT MUST NOT BE REPEATED. Ten officers stand in
    /// Main, each with PoliceOfficer and NetworkObject, so handing one of those to the mod looks obvious -
    /// and measured, it produces twelve officers, six routes reported built, and then:
    ///
    /// - FishNet throws on eleven of the twelve spawns, and <c>ServerObjects.SpawnWithoutChecks</c> adds to
    ///   <c>_spawnCache</c> BEFORE the throw, so the cache is never cleared again;
    /// - a save load afterwards hung at "Spawning player";
    /// - every NPC registers itself as saveable in Awake and re-registers its COPIED guid, so twelve clones
    ///   are twelve duplicate identities in the save;
    /// - and no client could resolve the spawn either way: with SceneId cleared FishNet looks the object up
    ///   in the spawnable prefabs, where no officer exists, and with SceneId kept it resolves to the scene
    ///   officer that was copied.
    ///
    /// So the clone is dropped and the mod is given what the game itself uses. <c>LawManager.StartFootpatrol</c>
    /// (LawManager.cs:42-64) pulls officers out of <c>PoliceStation.OfficerPool</c> and calls the same
    /// <c>StartFootPatrol</c> the mod already calls. Nothing is created, nothing is spawned, nothing is
    /// written to the save.
    ///
    /// TWO COSTS, BOTH REAL. There are ten officers in the game and the routes ask for twelve, so some
    /// routes end up with one officer instead of two - which is why they are handed out one per route per
    /// pass rather than filling the first route first. And <see cref="ReservedForVanilla"/> officers are
    /// left in the pool, because an empty pool means the police cannot answer a crime at all
    /// (PoliceStation.Dispatch reads the same list).
    ///
    /// The pool is not full when the mod runs. An officer enters it by walking into the station
    /// (<c>PoliceStation.NPCEnteredBuilding</c>), so right after a load it can be empty and fill over the
    /// next seconds. The requests are therefore parked and handed out once the pool has stopped growing.
    /// </remarks>
    internal sealed class MoreFootPatrolsOfficerPool : Fix
    {
        internal override string Id => "morefootpatrols-officer-pool";
        internal override string Mod => "MoreFootPatrols";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What => "the extra patrol routes are staffed from the police station";

        internal override string StandsDownBecause
            => "More Foot Patrols clones a prefab called PoliceNPC that this build does not offer mods, so "
             + "every route it tries to staff ends in a NullReferenceException and no patrol is created.";

        /// <summary>Officers left in the pool so the police can still answer a crime.</summary>
        private const int ReservedForVanilla = 2;

        /// <summary>How long to wait for officers to reach the station before handing out what there is.</summary>
        private const float WaitForPoolSeconds = 30f;

        private static MelonLogger.Instance _log;
        private static readonly List<Request> Pending = new();
        private static bool _draining;

        private sealed class Request
        {
            internal IntPtr Route;
            internal PatrolGroup Group;
            internal float Speed;
            internal bool Warp;
        }

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var core = AccessTools.TypeByName("BogsMod.Core");
            if (core == null)
            { log.Warning("[fix] morefootpatrols-officer-pool: BogsMod.Core is not where it was."); return false; }

            var spawn = AccessTools.Method(core, "spawnOfficer");
            var load = AccessTools.Method(core, "LoadPatrolRoutesFromJson");
            if (spawn == null || load == null)
            {
                log.Warning("[fix] morefootpatrols-officer-pool: the mod no longer has both spawnOfficer and "
                          + "LoadPatrolRoutesFromJson, so it was left alone.");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Patch(spawn, prefix: new HarmonyMethod(typeof(MoreFootPatrolsOfficerPool), nameof(Park)));
            harmony.Patch(load, postfix: new HarmonyMethod(typeof(MoreFootPatrolsOfficerPool), nameof(HandOut)));
            return true;
        }

        /// <summary>Take the request and drop the clone. Always.</summary>
        /// <remarks>
        /// The original is skipped even when something later goes wrong, because the first thing it does is
        /// the lookup that returns null - there is no half of it worth running.
        /// </remarks>
        private static bool Park(PatrolGroup group, float movementSpeedMult, bool warpToStart)
        {
            try
            {
                if (group?.Route == null) return false;

                Pending.Add(new Request
                {
                    Route = group.Route.Pointer,
                    Group = group,
                    Speed = movementSpeedMult,
                    Warp = warpToStart,
                });
            }
            catch (Exception e) { _log?.Warning("[fix] morefootpatrols-officer-pool: " + e.Message); }

            return false;
        }

        private static void HandOut()
        {
            if (_draining || Pending.Count == 0) return;
            _draining = true;
            MelonCoroutines.Start(WhenTheStationHasOfficers());
        }

        /// <summary>
        /// Wait for the officer pool to settle, then hand the officers out.
        /// </summary>
        /// <remarks>
        /// Settled, not full: officers arrive at the station over several seconds after a load, and there is
        /// no event for "that was the last one". Two readings of the same count one interval apart is the
        /// cheapest stopping point that does not cut the pool short.
        /// </remarks>
        private static IEnumerator WhenTheStationHasOfficers()
        {
            float deadline = Time.realtimeSinceStartup + WaitForPoolSeconds;
            int previous = -1;

            while (Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSeconds(0.5f);

                int count = Available();
                if (count > ReservedForVanilla && count == previous) break;
                previous = count;
            }

            Distribute();
        }

        private static int Available()
        {
            try
            {
                var station = Station();
                return station?.OfficerPool?.Count ?? 0;
            }
            catch { return 0; }
        }

        private static PoliceStation Station()
        {
            var stations = PoliceStation.PoliceStations;
            return stations == null || stations.Count == 0 ? null : stations[0];
        }

        /// <summary>
        /// One officer per route, then a second for whoever is left over.
        /// </summary>
        /// <remarks>
        /// The routes ask for more officers than the game has, so the order decides who goes empty. Filling
        /// the request list front to back would staff the first three routes twice over and leave the last
        /// three with nobody; a pass per route means every route gets somebody first.
        /// </remarks>
        private static void Distribute()
        {
            _draining = false;

            try
            {
                var station = Station();
                if (station == null)
                {
                    _log?.Warning("[fix] morefootpatrols-officer-pool: there is no police station in this "
                                + "scene, so the routes stay empty.");
                    Pending.Clear();
                    return;
                }

                var queues = new List<List<Request>>();
                var whichQueue = new Dictionary<IntPtr, int>();
                foreach (var request in Pending)
                {
                    if (!whichQueue.TryGetValue(request.Route, out int at))
                    {
                        at = queues.Count;
                        whichQueue[request.Route] = at;
                        queues.Add(new List<Request>());
                    }
                    queues[at].Add(request);
                }
                Pending.Clear();

                int staffed = 0;
                var staffedRoutes = new HashSet<IntPtr>();

                for (bool handedOut = true; handedOut;)
                {
                    handedOut = false;

                    foreach (var queue in queues)
                    {
                        if (queue.Count == 0) continue;
                        if (station.OfficerPool.Count <= ReservedForVanilla) goto done;

                        var request = queue[0];
                        queue.RemoveAt(0);

                        var officer = station.PullOfficer();
                        if (officer == null) goto done;

                        // The mod's own two lines, on an officer the game already owns.
                        officer.Movement.MoveSpeedMultiplier = request.Speed;
                        officer.StartFootPatrol(request.Group, request.Warp);

                        staffedRoutes.Add(request.Route);
                        staffed++;
                        handedOut = true;
                    }
                }

                done:
                int routes = staffedRoutes.Count;
                _log?.Msg($"[fix] morefootpatrols-officer-pool: 'PoliceNPC' is not among the prefabs this "
                        + $"build lets a mod spawn, so no officer can be cloned. Staffed {staffed} patrol "
                        + $"slot(s) across {routes} route(s) from the police station instead; "
                        + $"{station.OfficerPool.Count} officer(s) left for callouts.");
            }
            catch (Exception e)
            {
                Pending.Clear();
                _log?.Warning("[fix] morefootpatrols-officer-pool: " + e.Message);
            }
        }
    }
}
