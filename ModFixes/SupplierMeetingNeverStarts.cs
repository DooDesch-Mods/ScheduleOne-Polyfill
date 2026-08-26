using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.NPCs.Schedules;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A supplier who arrived but never opened his shop opens it.
    /// </summary>
    /// <remarks>
    /// The reported symptom is that a supplier only greets you: the meetup happens, he stands at the
    /// spot, and the "Yes" that opens his stock is not there. Pushing him a step makes it appear, which
    /// is the clue.
    ///
    /// WHERE THE CHOICE COMES FROM. Supplier.Start() builds it and leaves it disabled
    /// (Supplier.cs:176-183); the meetup enables the schedule action and warps him over
    /// (Supplier.cs:896-900); and only NPCEvent_LocationDialogue.StartAction enables the choice
    /// (NPCEvent_LocationDialogue.cs:277-291). Vanilla calls StartAction from Started, from LateStarted
    /// and from the walk callback - never from the tick.
    ///
    /// WHICH IS FINE UNTIL SOMETHING MOVES THE NPC. OverTheCounter intercepts MeetAtLocation and pulls
    /// the supplier out of its S1MAPI warehouse interior at the same moment
    /// (SupplierWarehousePatch.cs:122-152). Take the walk callback away from an action that has already
    /// started walking and nothing is left to start it: OnActiveTick sees him standing at the
    /// destination and only turns him to face the right way (NPCEvent_LocationDialogue.cs:82-88). He is
    /// there, he is done, and IsActionStarted is still false - forever. Pushing him makes him move,
    /// which ends in a walk callback, which starts the action.
    ///
    /// So the tick is given the case vanilla never needed: at the destination, not moving, not started.
    /// It calls the game's own StartAction, so the choice, the network propagation and the later
    /// EndAction cleanup are all vanilla's - this only says when.
    ///
    /// HOST ONLY, and that is not caution: OnActiveTick returns early on a client
    /// (NPCEvent_LocationDialogue.cs:71), StartAction is an ObserversRpc, and calling it from a client
    /// would either do nothing or send an RPC that client has no right to send.
    /// </remarks>
    internal sealed class SupplierMeetingNeverStarts : Fix
    {
        internal override string Id => "supplier-meeting-never-starts";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a supplier who arrived but never started the meetup starts it";

        internal override string StandsDownBecause
            => "a supplier can greet you without ever offering his stock, and only a shove fixes it.";

        private static MelonLogger.Instance _log;
        private static MethodInfo _isAtDestination;
        private static MethodInfo _startAction;
        private static PropertyInfo _started;
        private static int _rescued;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var tick = AccessTools.Method(typeof(NPCEvent_LocationDialogue), "OnActiveTick");
            _isAtDestination = AccessTools.Method(typeof(NPCEvent_LocationDialogue), "IsAtDestination");
            _startAction = AccessTools.Method(typeof(NPCEvent_LocationDialogue), "StartAction");

            // A field on the game side, a property on this one - Il2CppInterop turns every game field
            // into one, protected included (NPCEvent_LocationDialogue.cs:187 in the interop branch).
            _started = AccessTools.Property(typeof(NPCEvent_LocationDialogue), "IsActionStarted");

            if (tick == null || _isAtDestination == null || _startAction == null || _started == null)
            {
                log.Warning($"[fix] {Id}: NPCEvent_LocationDialogue is not the shape this knows on this "
                          + "build, so a stuck meetup still needs a shove.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.suppliermeeting").Patch(
                tick, postfix: new HarmonyMethod(typeof(SupplierMeetingNeverStarts), nameof(StartWhatArrived)));
            return true;
        }

        /// <summary>Standing at the destination with nothing left to wait for means the action can start.</summary>
        private static void StartWhatArrived(NPCEvent_LocationDialogue __instance)
        {
            try
            {
                if (__instance == null || __instance.Destination == null) return;
                if ((bool)_started.GetValue(__instance)) return;                  // already running
                if (__instance.npc?.Movement == null || __instance.npc.Movement.IsMoving) return;
                if (!(bool)_isAtDestination.Invoke(__instance, null)) return;

                _startAction.Invoke(__instance, new object[] { null });

                if (_rescued++ == 0)
                {
                    _log?.Msg("[fix] supplier-meeting-never-starts: a meetup that had arrived without "
                            + "starting was started, which is where the missing shop choice comes from.");
                }
            }
            catch (Exception e)
            {
                if (_rescued++ == 0)
                    _log?.Warning("[fix] supplier-meeting-never-starts: could not start an arrived "
                                + "meetup: " + (e.InnerException ?? e).Message);
            }
        }
    }
}
