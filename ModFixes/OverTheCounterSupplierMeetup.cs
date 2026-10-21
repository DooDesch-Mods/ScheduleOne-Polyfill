using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.Economy;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A supplier parked in the OverTheCounter warehouse stays at the meetup spot instead of walking back.
    /// </summary>
    /// <remarks>
    /// OverTheCounter parks idle suppliers in its warehouse, and S1MAPI's interior navigator moves them
    /// around in there: it switches the nav agent off and sets the position itself, every frame, for each
    /// NPC it tracks. When a meetup starts, the mod's prefix on <c>Supplier.MeetAtLocation</c> asks S1MAPI
    /// to let him go (<c>RecallNPC</c>), which starts an exit walk from where he stands in the warehouse
    /// (<c>InteriorNavigatorCore.BeginExit</c>). Then vanilla warps him to the meetup spot.
    ///
    /// S1MAPI still tracks him, and the exit path is still the one from inside the warehouse. So the next
    /// frame it walks him from the meetup spot back towards the warehouse door, in a straight line,
    /// at walking speed. Measured with Albert Hoover and the spot next to the mayor's house: 0.8 m from
    /// the stand point one second after the warp, 100 m away fifty seconds later, status still Meeting.
    /// The mod waits 15 s for the release and then gives up
    /// ("S1MAPI release timed out after 15s"), so it never warps him back. He returns only when S1MAPI
    /// lets go at the door and the game walks him to the meetup again, minutes later.
    ///
    /// He is already where he should be when vanilla is done, so the fix finishes the release S1MAPI
    /// started: after <c>MeetAtLocation</c> it drops him from the navigator's table and lets S1MAPI's own
    /// <c>ReleaseNPC</c> switch the agent and collider back on, without its warp to the door. The mod's
    /// own wait then sees him released and warps him to the spot, which is where he is.
    ///
    /// A supplier S1MAPI does not track is not touched.
    /// </remarks>
    internal sealed class OverTheCounterSupplierMeetup : Fix
    {
        internal override string Id => "otc-supplier-meetup-release";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a supplier from the warehouse stays at the meetup spot instead of walking back";

        internal override string StandsDownBecause
            => "a supplier who was waiting in the OverTheCounter warehouse walks back there right after the "
             + "meetup starts, so he is not at the spot for minutes.";

        private const string CoreType = "S1MAPI.Building.InteriorNavigatorCore";

        private static MelonLogger.Instance _log;
        private static FieldInfo _buildings;
        private static FieldInfo _tracked;
        private static MethodInfo _release;
        private static int _released;
        private static bool _warned;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            Type core = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                core = assembly.GetType(CoreType, false);
                if (core != null) break;
            }
            if (core == null)
            {
                log.Msg("[fix] otc-supplier-meetup-release: S1MAPI's interior navigator is not loaded, so no "
                      + "supplier is held in a warehouse.");
                return false;
            }

            _buildings = AccessTools.Field(core, "_activeBuildings");
            _tracked = AccessTools.Field(core, "_tracked");
            _release = AccessTools.Method(core, "ReleaseNPC");
            var meet = AccessTools.Method(typeof(Supplier), nameof(Supplier.MeetAtLocation));
            if (_buildings == null || _tracked == null || _release == null || _release.GetParameters().Length != 3
                || meet == null)
            {
                log.Warning("[fix] otc-supplier-meetup-release: S1MAPI's interior navigator or "
                          + "Supplier.MeetAtLocation is not the shape this knows, so a supplier from the "
                          + "warehouse still walks back after the meetup starts.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.otcsuppliermeetup").Patch(
                meet, postfix: new HarmonyMethod(typeof(OverTheCounterSupplierMeetup), nameof(Met)));
            return true;
        }

        /// <summary>After the warp to the meetup, S1MAPI lets go of the supplier where he stands.</summary>
        private static void Met(Supplier __instance)
        {
            try
            {
                var movement = __instance?.Movement;
                if (movement == null) return;
                IntPtr wanted = movement.Pointer;

                if (!(_buildings.GetValue(null) is IList buildings)) return;
                foreach (var building in buildings)
                {
                    if (building == null || !(_tracked.GetValue(building) is IDictionary tracked)) continue;

                    object key = null, data = null;
                    foreach (DictionaryEntry entry in tracked)
                    {
                        if (entry.Key is Il2CppObjectBase k && k.Pointer == wanted) { key = entry.Key; data = entry.Value; break; }
                    }
                    if (key == null) continue;

                    // Out of the table first: ReleaseNPC only queues the removal, and the queue is cleared at
                    // the start of the navigator's next update, so outside that update it never happens.
                    tracked.Remove(key);

                    // No resume walk: the saved destinations point into the warehouse, and the meetup
                    // already set where he stands.
                    var type = data.GetType();
                    type.GetField("PendingExteriorDestination")?.SetValue(data, null);
                    type.GetField("SavedChasePosition")?.SetValue(data, null);

                    _release.Invoke(building, new[] { key, data, (object)false });

                    _released++;
                    if (_released == 1 || _released % 50 == 0)
                        _log?.Msg($"[fix] otc-supplier-meetup-release: {__instance.FullName} let go at the meetup "
                                + $"spot instead of walking back to the warehouse ({_released} so far).");
                    return;
                }
            }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                _log?.Warning("[fix] otc-supplier-meetup-release: could not release a supplier at the meetup: "
                            + (e.InnerException ?? e).Message);
            }
        }
    }
}
