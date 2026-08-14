using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.UI;
using MelonLoader;
using UnityEngine.Events;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Fire the storage menu's closing event, which the game stopped having.
    /// </summary>
    /// <remarks>
    /// THE HALF THAT MAKES THE MEMBER WORTH PUTTING BACK. The plugin gives <c>StorageMenu</c> its
    /// <c>onClosed</c> event again, because a mod that subscribes to it throws without one - but nothing in
    /// 0.4.6 would ever raise it. <c>public UnityEvent onClosed</c> became <c>private Action
    /// _onClosedCallback</c>, handed in by whoever calls Open, and a subscriber from outside has no way in.
    ///
    /// So this raises it where the game used to: at the end of <c>CloseMenu</c>, which is what
    /// <c>StorageMenu.cs:148-151</c> did in 0.4.5f2, after the screen is taken down.
    ///
    /// The symptom without it is not an error, which is what makes it worth a fix rather than a report.
    /// OverTheCounter subscribes, opens a manager's inventory, and does its cleanup in the listener -
    /// clearing <c>IsPlayerInteracting</c> and putting the NPC's behaviour back. No event, no cleanup, and
    /// the manager stands there for good. Reported as "they became unresponsive".
    ///
    /// Costs nothing when nobody is listening: the event is only created the first time somebody asks for
    /// it, so an unused game has no event and this raises nothing.
    /// </remarks>
    internal sealed class StorageMenuClosedEvent : Fix
    {
        internal override string Id => "storage-menu-closed-event";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What => "the storage window tells a mod when it closed, as it used to";

        internal override string StandsDownBecause
            => "a mod that subscribes to StorageMenu.onClosed will never hear it, so whatever it does when "
             + "the window closes - putting an NPC back the way it was, for one - does not happen.";

        private static MelonLogger.Instance _log;
        private static MethodInfo _event;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            // The getter is one Polyfill emits, and only when a mod asked for it. Absent means nobody
            // subscribes on this machine, so there is nothing to raise and nothing to say.
            _event = AccessTools.Method(typeof(StorageMenu), "get_onClosed");
            if (_event == null) return false;

            var close = AccessTools.Method(typeof(StorageMenu), "CloseMenu");
            if (close == null)
            {
                log.Warning("[fix] storage-menu-closed-event: StorageMenu.CloseMenu is not where it was, so "
                          + "the closing event cannot be raised.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                close, postfix: new HarmonyMethod(typeof(StorageMenuClosedEvent), nameof(AfterClose)));
            return true;
        }

        /// <summary>
        /// Raise it on the instance that just closed.
        /// </summary>
        /// <remarks>
        /// Invoked through the getter rather than a field read, so the event is created on demand exactly
        /// as a subscriber would create it - and so this holds no reference to something the game owns.
        /// </remarks>
        private static void AfterClose(StorageMenu __instance)
        {
            try
            {
                if (__instance == null) return;

                var raised = _event.Invoke(__instance, null) as UnityEvent;
                if (raised == null) return;

                raised.Invoke();

                if (_said) return;
                _said = true;
                _log?.Msg("[fix] storage-menu-closed-event: raised the storage window's closing event, "
                        + "which 0.4.6 stopped having and a mod is still listening for.");
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] storage-menu-closed-event: " + e.Message);
            }
        }
    }
}
