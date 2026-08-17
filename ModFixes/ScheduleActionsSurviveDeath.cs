using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// An NPC's schedule stops throwing once the NPC is gone.
    /// </summary>
    /// <remarks>
    /// Two methods on <c>NPCScheduleManager</c> walk the same <c>ActionList</c> and ask the same question.
    /// One checks whether the action is still there and the other does not:
    /// <code>
    /// // NPCScheduleManager.cs:234
    /// if (!(action == null) &amp;&amp; action.ShouldStart() &amp;&amp; ...)
    ///
    /// // NPCScheduleManager.cs:248   - GetActionsTotallyOccurringWithinRange
    /// if ((!checkShouldStart || action.ShouldStart()) &amp;&amp; ...)
    /// </code>
    /// <c>NPCAction.ShouldStart</c> reads <c>gameObject.activeInHierarchy</c> (NPCAction.cs:229), and on a
    /// destroyed object that throws. So the moment an NPC disappears while its actions are still listed,
    /// the second method throws on every scan - reported as
    /// <code>
    /// NullReferenceException
    ///   at ScheduleOne.NPCs.Schedules.NPCSignal.ShouldStart ()
    ///   at ScheduleOne.NPCs.NPCScheduleManager.GetActionsTotallyOccurringWithinRange (...)
    /// </code>
    /// THIS IS NOT ABOUT ONE MOD. It needs an NPC to be destroyed with its schedule still registered,
    /// which is what any mod spawning and despawning NPCs does, and it is why the report that led here
    /// arrived with a manager rather than anything to do with schedules. The repair is the check the
    /// neighbouring method already makes, and nothing else: dead entries are dropped before the original
    /// runs, and the original runs unchanged.
    ///
    /// A LIST, NOT A REWRITE. Pruning beforehand rather than transpiling the loop keeps the game's own
    /// ordering, its pooling and its second condition exactly as they are - the failure here is an
    /// entry that should not be in the list, not a loop that is wrong.
    /// </remarks>
    internal sealed class ScheduleActionsSurviveDeath : Fix
    {
        internal override string Id => "schedule-actions-survive-death";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "an NPC's schedule stops throwing once the NPC itself has been destroyed";

        internal override string StandsDownBecause
            => "a mod that despawns an NPC leaves its schedule actions behind, and the game throws on "
             + "every scan of them from then on.";

        private static MelonLogger.Instance _log;
        private static int _dropped;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var target = AccessTools.Method(typeof(NPCScheduleManager),
                                            "GetActionsTotallyOccurringWithinRange");
            if (target == null)
            {
                log.Warning("[fix] schedule-actions-survive-death: "
                          + "NPCScheduleManager.GetActionsTotallyOccurringWithinRange is not where it was.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, prefix: new HarmonyMethod(typeof(ScheduleActionsSurviveDeath), nameof(Prune)));
            return true;
        }

        /// <summary>Drop actions whose object is gone, then let the original walk what is left.</summary>
        private static void Prune(NPCScheduleManager __instance)
        {
            try
            {
                var actions = __instance?.ActionList;
                if (actions == null) return;

                for (int i = actions.Count - 1; i >= 0; i--)
                {
                    // The Unity comparison, which is what line 234 uses and what makes it safe: it is
                    // true for a destroyed object as well as for a null reference.
                    if (actions[i] != null) continue;

                    actions.RemoveAt(i);
                    if (++_dropped != 1) continue;

                    _log?.Msg("[fix] schedule-actions-survive-death: dropped a schedule action whose NPC "
                            + "was destroyed. The game asks one of these lists whether an action should "
                            + "start without checking it is still there, and throws for the rest of the "
                            + "session once it is not.");
                }
            }
            catch (Exception e) { _log?.Warning("[fix] schedule-actions-survive-death: " + e.Message); }
        }
    }
}
