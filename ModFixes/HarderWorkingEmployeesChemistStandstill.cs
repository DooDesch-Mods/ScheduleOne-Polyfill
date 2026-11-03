using System.Collections.Generic;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using MelonLoader;
using UnityEngine;
using NpcBehaviour = Il2CppScheduleOne.NPCs.Behaviour.Behaviour;

namespace Polyfill.ModFixes
{
    /// <summary>A chemist that stopped short of a lab oven or cauldron is put at it instead of standing there.</summary>
    /// <remarks>
    /// The three station behaviours walk the chemist to the station whenever it is not moving and not at it
    /// (FinishLabOvenBehaviour.cs:54-68, StartLabOvenBehaviour.cs:56-70, StartCauldronBehaviour.cs:76-101 on
    /// 0.4.7f12). When the stand point cannot be reached, the walk ends at the closest point as Partial, and Partial
    /// resets the pathing failure count (Behaviour.cs:279-289), so the game's own warp after five failures
    /// (Behaviour.cs:263-276) never comes. The chemist walks nowhere every tick and never starts. Harder Working
    /// Employees sees it as "stuck navigating (Unknown phase)" and resets the behaviour, which walks it back to the
    /// same spot.
    ///
    /// The fix does what the game's warp would have done: after the chemist stood still for StillSeconds, not at
    /// the station and not working, it is warped to the stand point. Two tries per spot; a chemist the warp cannot
    /// place is named in the log with its distance and left alone, so a station off the NavMesh does not turn into
    /// a teleport loop.
    /// </remarks>
    internal sealed class HarderWorkingEmployeesChemistStandstill : Fix
    {
        private const string FixId = "harderworkingemployees-chemist-standstill";
        private const float StillSeconds = 12f;
        private const float MovedMetres = 0.5f;
        private const int TriesPerSpot = 2;

        internal override string Id => FixId;
        internal override string Mod => "Harder Working Employees";
        internal override string ModVersions => "<=2.2.3";
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "chemists that stop short of a lab oven or cauldron are put at it instead of standing there";

        internal override string StandsDownBecause
            => "A chemist that cannot reach the front of a lab oven or cauldron stands next to it until the day ends.";

        private sealed class Still
        {
            internal Vector3 Spot;
            internal float Since;
            internal int Tries;
            internal bool GaveUp;
        }

        private static readonly Dictionary<int, Still> _still = new Dictionary<int, Still>();
        private static MelonLogger.Instance _log;
        private static int _failures;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var finish = AccessTools.DeclaredMethod(typeof(FinishLabOvenBehaviour), nameof(FinishLabOvenBehaviour.OnActiveTick));
            var start = AccessTools.DeclaredMethod(typeof(StartLabOvenBehaviour), nameof(StartLabOvenBehaviour.OnActiveTick));
            var cauldron = AccessTools.DeclaredMethod(typeof(StartCauldronBehaviour), nameof(StartCauldronBehaviour.OnActiveTick));

            if (finish == null || start == null || cauldron == null)
            {
                log.Warning($"[fix] {FixId}: something it needs is not here (FinishLabOven {finish != null}, "
                          + $"StartLabOven {start != null}, StartCauldron {cauldron != null}).");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Patch(finish, postfix: new HarmonyMethod(typeof(HarderWorkingEmployeesChemistStandstill), nameof(AfterFinishLabOven)));
            harmony.Patch(start, postfix: new HarmonyMethod(typeof(HarderWorkingEmployeesChemistStandstill), nameof(AfterStartLabOven)));
            harmony.Patch(cauldron, postfix: new HarmonyMethod(typeof(HarderWorkingEmployeesChemistStandstill), nameof(AfterStartCauldron)));
            return true;
        }

        private static void AfterFinishLabOven(FinishLabOvenBehaviour __instance)
        {
            try
            {
                if (__instance.targetOven == null) return;
                Check(__instance, "lab oven", __instance.actionRoutine != null, __instance.IsAtStation(),
                      __instance.GetStationAccessPoint());
            }
            catch (Exception e)
            {
                Fail("checking a chemist at a lab oven", e);
            }
        }

        private static void AfterStartLabOven(StartLabOvenBehaviour __instance)
        {
            try
            {
                if (__instance.targetOven == null) return;
                Check(__instance, "lab oven", __instance.cookRoutine != null, __instance.IsAtStation(),
                      __instance.GetStationAccessPoint());
            }
            catch (Exception e)
            {
                Fail("checking a chemist at a lab oven", e);
            }
        }

        private static void AfterStartCauldron(StartCauldronBehaviour __instance)
        {
            try
            {
                var station = __instance.Station;
                if (station == null || station.StandPoint == null) return;
                Check(__instance, "cauldron", __instance.WorkInProgress, __instance.IsAtStation(),
                      station.StandPoint.position);
            }
            catch (Exception e)
            {
                Fail("checking a chemist at a cauldron", e);
            }
        }

        private static void Check(NpcBehaviour behaviour, string station, bool working, bool atStation, Vector3 standPoint)
        {
            if (!InstanceFinder.IsServer || !behaviour.Active) return;
            var npc = behaviour.Npc;
            if (npc == null || npc.Movement == null) return;

            int key = npc.GetInstanceID();
            if (working || atStation)
            {
                _still.Remove(key);
                return;
            }
            if (npc.Movement.IsMoving) return;

            Vector3 here = npc.transform.position;
            if (!_still.TryGetValue(key, out var still))
            {
                _still[key] = new Still { Spot = here, Since = Time.time };
                return;
            }
            if (Vector3.Distance(here, still.Spot) > MovedMetres)
            {
                still.Spot = here;
                still.Since = Time.time;
                still.GaveUp = false;
                still.Tries = 0;
                return;
            }
            if (still.GaveUp || Time.time - still.Since < StillSeconds) return;

            float distance = Vector3.Distance(here, standPoint);
            if (still.Tries >= TriesPerSpot)
            {
                still.GaveUp = true;
                _log.Warning($"[fix] {FixId}: {npc.FullName} still stands {distance:F1} m from the {station} after "
                           + $"{TriesPerSpot} warps; the stand point is likely off the walkable floor. Left alone until it moves.");
                Fixes.Record(FixId, $"{station} stand point not reachable even by warp ({distance:F1} m)");
                return;
            }

            still.Tries++;
            npc.Movement.Warp(standPoint);
            Vector3 after = npc.transform.position;
            still.Spot = after;
            still.Since = Time.time;
            _log.Msg($"[fix] {FixId}: {npc.FullName} stood {distance:F1} m from the {station} for {StillSeconds:F0}s "
                   + $"without starting; warped to it, now {Vector3.Distance(after, standPoint):F1} m away (try {still.Tries}).");
        }

        private static void Fail(string what, Exception e)
        {
            _failures++;
            if (_failures == 1 || _failures % 20 == 0)
                _log.Error($"[fix] {FixId}: {what} failed ({_failures}): {e}; a chemist stuck there stays stuck.");
            Fixes.Record(FixId, what + " failed: " + e.GetBaseException().Message);
        }
    }
}
