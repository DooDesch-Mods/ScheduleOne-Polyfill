using HarmonyLib;
using Il2CppScheduleOne.Building.Doors;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A door a mod puts up after you already own the place can be opened.
    /// </summary>
    /// <remarks>
    /// Every property door locks itself on the way up and waits for one event to let it go:
    /// <code>
    /// // ScheduleOne.Building.Doors/PropertyDoorController.cs:106-115
    /// base.Awake();
    /// PlayerAccess = EDoorAccess.ExitOnly;
    /// if (Property != null)
    ///     Property.onThisPropertyAcquired.AddListener(Unlock);
    /// </code>
    /// That event fires exactly once and only from inside <c>if (!IsOwned)</c>
    /// (`ScheduleOne.Property/Property.cs:291-311`), so for a property you already own it is over. A door
    /// that comes into existence afterwards subscribes to something that will never happen again, and stays
    /// on <c>ExitOnly</c> for good.
    ///
    /// What that looks like from the outside is not a locked door. <c>CanPlayerAccess</c> returns false for
    /// the exterior side, and the hover handler then sets every interactable on the door to
    /// <c>Disabled</c> (`ScheduleOne.Doors/DoorController.cs:185-208`) - no prompt, no message, nothing to
    /// press. It reads as "this thing is not interactable", which is exactly how it gets reported.
    ///
    /// Polyfill is part of how these doors get created: `s1mapi-prefab-lookup` hands over a copy standing in
    /// the map when there is no prefab template, and that copy carries its PropertyDoorController and the
    /// property it was copied from. So this repairs the far end of a fix rather than someone else's bug.
    ///
    /// IT ONLY EVER UNLOCKS A DOOR WHOSE PROPERTY THE PLAYER ALREADY OWNS. That is the same condition the
    /// event carries, on a door that simply missed it - not a new permission. A door bound to a property
    /// nobody bought stays shut and is reported instead, because "the player may walk into a building they
    /// did not pay for" is not a trade a repair gets to make on a guess.
    /// </remarks>
    internal sealed class S1MapiClonedDoors : Fix
    {
        internal override string Id => "s1mapi-cloned-doors";
        internal override string Mod => "S1MAPI";
        internal override string ModVersions => "*";
        /// <summary>The same window as `s1mapi-prefab-lookup`, deliberately. That fix is what puts these
        /// doors in the world; a repair for its far end has no business outliving it.</summary>
        internal override string GameVersions => "0.4.6*";
        internal override string What => "doors a mod puts up in a property you own can be opened";

        internal override string StandsDownBecause
            => "A door a mod spawns will have no prompt at all from the outside, which reads as the whole "
             + "building being dead rather than as a locked door.";

        private static MelonLogger.Instance _log;
        private static int _unlocked;
        private static int _leftShut;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var awake = AccessTools.Method(typeof(PropertyDoorController), "Awake");
            if (awake == null)
            {
                log.Warning("[fix] s1mapi-cloned-doors: PropertyDoorController.Awake is not where it was.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                awake, postfix: new HarmonyMethod(typeof(S1MapiClonedDoors), nameof(AwakePostfix)));
            return true;
        }

        /// <summary>
        /// After the door has locked itself, give it the acquisition it was waiting for.
        /// </summary>
        /// <remarks>
        /// A postfix and not a prefix: the lock is set in the body, so anything decided before it is
        /// overwritten a line later.
        ///
        /// Every door in the map passes through here once at load, when no property is owned yet - the save
        /// is applied afterwards - so this costs one null check per door and changes nothing for them.
        /// </remarks>
        private static void AwakePostfix(PropertyDoorController __instance)
        {
            try
            {
                var property = __instance?.Property;
                if (property == null) return;

                if (!property.IsOwned)
                {
                    // Worth saying once. A door on an unowned property is normal at load and suspicious
                    // later, and this is the shape a report needs to tell those apart.
                    //
                    // The unlock count is asked FIRST because ++ on the left of && always runs: every door
                    // in the map arrives here before the save has granted anything, and counting those
                    // spends the budget before the case worth reporting can happen.
                    if (_unlocked > 0 && ++_leftShut <= 3)
                        _log?.Msg($"[fix] s1mapi-cloned-doors: a door on '{Name(property)}' was left shut - "
                                + "that property is not owned.");
                    return;
                }

                __instance.Unlock();
                _unlocked++;
                if (_unlocked <= 8)
                    _log?.Msg($"[fix] s1mapi-cloned-doors: unlocked a door on '{Name(property)}', which is "
                            + "yours - it was put up after you bought it, so it never heard.");
                else if (_unlocked == 9)
                    _log?.Msg("[fix] s1mapi-cloned-doors: further doors are handled the same way silently.");
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] s1mapi-cloned-doors: " + e.Message);
            }
        }

        private static string Name(Il2CppScheduleOne.Property.Property property)
        {
            try { return property.PropertyName ?? property.name; } catch { return "?"; }
        }
    }
}
