using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Tools;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Over The Counter's manager panel goes away when the clipboard is opened on something else.
    /// </summary>
    /// <remarks>
    /// Reported three times running - 0.9.11, 0.9.13, 0.9.15: use the clipboard on a manager, then on
    /// another employee, and both panels are on screen at once. The last report adds the useful part,
    /// that there is no error or warning in the log at all.
    ///
    /// THE FIRST REPAIR HUNG ON THE WRONG SIDE. Over The Counter tears its panel down from a postfix on
    /// <c>ManagementClipboard.Close(bool)</c>, and 0.9.13 made that postfix reach the close the player's
    /// own exit performs (<see cref="PatchesOnSplitMethods"/>). That is correct and it does not help
    /// here, because walking from a manager to another employee never closes the clipboard:
    /// <code>
    /// ManagerConfigPanel.Close()   is called from exactly one place, ManagerClipboardPatch.cs:250
    /// ManagementClipboard.Open()   pushes state and opens; it closes nothing (ManagementClipboard.cs:81-99)
    /// </code>
    /// So the second employee opens on top of a panel nothing was ever asked to remove.
    ///
    /// ON OPEN, AND ONLY WHEN SOMETHING REAL IS SELECTED. Closing the panel on every open would break the
    /// route picker, which deliberately closes the clipboard while it runs and REOPENS it afterwards
    /// (RouteEntitySelector.cs) - the manager panel has to survive that. The three paths separate cleanly
    /// on the selection:
    /// <code>
    /// Over The Counter opening for a manager   new List&lt;IConfigurable&gt;()   empty   keep
    /// the route picker handing control back    ManagementInterface.Configurables   empty   keep
    /// the clipboard on any other employee      the real selection            not empty   close
    /// </code>
    /// Over The Counter opens the clipboard and creates its panel immediately afterwards
    /// (ManagerClipboardPatch.cs:150-151), so a postfix here runs BEFORE the panel it is meant to keep
    /// exists, and the manager case is untouched.
    /// </remarks>
    internal sealed class OverTheCounterStalePanel : Fix
    {
        internal override string Id => "otc-stale-manager-panel";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "the manager panel goes away when the clipboard opens on another employee";

        internal override string StandsDownBecause
            => "Over The Counter's manager panel stays on screen when you use the clipboard on a "
             + "different employee, and the two draw over each other.";

        private static MelonLogger.Instance _log;
        private static MethodInfo _close;
        private static PropertyInfo _isOpen;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var panel = AccessTools.TypeByName("OverTheCounter.UI.ManagerConfigPanel");
            if (panel == null) return false;                    // not this mod's build

            _close = AccessTools.Method(panel, "Close", Type.EmptyTypes);
            _isOpen = AccessTools.Property(panel, "IsOpen");
            if (_close == null || _isOpen == null)
            {
                log.Warning("[fix] otc-stale-manager-panel: ManagerConfigPanel has no Close()/IsOpen "
                          + "here, so a stale manager panel is not cleaned up.");
                return false;
            }

            var open = AccessTools.Method(typeof(ManagementClipboard), "Open");
            if (open == null)
            {
                log.Warning("[fix] otc-stale-manager-panel: ManagementClipboard.Open is not where it was.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                open, postfix: new HarmonyMethod(typeof(OverTheCounterStalePanel), nameof(After)));
            return true;
        }

        /// <summary>Take the old panel down once the clipboard is opened on a real selection.</summary>
        private static void After(Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Management.IConfigurable> selection)
        {
            try
            {
                if (selection == null || selection.Count == 0) return;   // the manager and picker paths
                if (_isOpen?.GetValue(null) is not true) return;

                _close.Invoke(null, null);

                if (_said) return;
                _said = true;
                _log?.Msg("[fix] otc-stale-manager-panel: closed OverTheCounter's manager panel because "
                        + "the clipboard opened on something else; nothing else ever takes it down.");
            }
            catch (Exception e) { _log?.Warning("[fix] otc-stale-manager-panel: " + e.Message); }
        }
    }
}
