using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.UI.Management;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// MoreEmployeeAssignments opens the Packager's route list without an error when another employee mod
    /// already made that list scroll.
    /// </summary>
    /// <remarks>
    /// MoreEmployeeAssignments adds route rows to the Packager panel and, once there are more than fit,
    /// builds its own scroll view around them. It assumes the rows' container, the prefab's
    /// <c>RouteListFieldUI/Contents</c>, is a direct child of the route list: it puts its scroll root next
    /// to that container, and afterwards looks the root up with
    ///
    /// <code>
    /// ui.transform.Find("MoreEmployeeAssignments_PackagerRoutesScroll")
    /// </code>
    ///
    /// to move the Add button below it. Harder Working Employees moves <c>Contents</c> into its own
    /// <c>ScrollArea</c> (UIUtilities.MakeScrollable), and Actually Working Employees into an
    /// <c>AWE Route Viewport</c>. With either one loaded, the scroll root lands one level deeper, the
    /// <c>Find</c> returns null, <c>ScrollableListHelper.AsRectTransform(null)</c> throws, and the player
    /// gets "Could not create the Packager scroll views" each time a Packager's panel opens. The rows have
    /// by then been pulled out of the other mod's list into a second, nested scroll view, and the Add
    /// button stays behind in the emptied list.
    ///
    /// Two scroll views around one list cannot both be right, and the other mod's is already working
    /// when this runs. So when the rows' container sits inside a scroll or mask that is not
    /// MoreEmployeeAssignments' own, its scroll view is not built. The extra route rows still come from
    /// MoreEmployeeAssignments (its capacity patches are not touched) and scroll in the other mod's list.
    /// A plain panel, where the container is a direct child, runs MoreEmployeeAssignments unchanged.
    /// </remarks>
    internal sealed class MoreEmployeeAssignmentsWrappedRoutes : Fix
    {
        internal override string Id => "mea-wrapped-routes";
        internal override string Mod => "MoreEmployeeAssignments";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";
        internal override string What => "the Packager's route list opens without an error next to another employee mod's scrolling";
        internal override bool NeedsAScreen => true;

        private const string OwnRoot = "MoreEmployeeAssignments_PackagerRoutesScroll";

        private static MelonLogger.Instance _log;
        private static readonly HashSet<string> Reported = new HashSet<string>();

        internal override bool Apply(MelonLogger.Instance log)
        {
            var target = AccessTools.Method("MoreEmployeeAssignments.PackagerConfigPanelPatch:CreateRouteScrollView");
            if (target == null)
            {
                log.Warning("[fix] mea-wrapped-routes: PackagerConfigPanelPatch.CreateRouteScrollView is not in this MoreEmployeeAssignments; nothing was changed.");
                return false;
            }

            _log = log;
            new HarmonyLib.Harmony("doodesch.polyfill.fixes")
                .Patch(target, prefix: new HarmonyMethod(typeof(MoreEmployeeAssignmentsWrappedRoutes), nameof(SkipWhenWrapped)));
            return true;
        }

        private static bool SkipWhenWrapped(RouteListFieldUI ui)
        {
            if (ui == null || ui.RouteEntries == null || ui.RouteEntries.Length == 0)
                return true;

            Transform contents = null;
            for (int i = 0; i < ui.RouteEntries.Length && contents == null; i++)
            {
                var entry = ui.RouteEntries[i];
                if (entry != null)
                    contents = entry.transform.parent;
            }
            if (contents == null || contents.parent == ui.transform)
                return true;

            // Walk from the rows' container up to the route list. MoreEmployeeAssignments' own root on the way
            // means this is a second bind of a panel it already wrapped, which it handles itself.
            Transform wrapper = null;
            for (var t = contents.parent; t != null && t != ui.transform; t = t.parent)
            {
                if (t.name == OwnRoot)
                    return true;
                if (wrapper == null && (t.GetComponent<ScrollRect>() != null || t.GetComponent<RectMask2D>() != null || t.GetComponent<Mask>() != null))
                    wrapper = t;
            }

            if (wrapper == null)
            {
                if (Reported.Add("unknown:" + contents.parent.name))
                    _log.Warning("[fix] mea-wrapped-routes: the Packager's route rows sit in '" + contents.parent.name
                               + "', which neither scrolls nor masks; MoreEmployeeAssignments builds its scroll view as usual.");
                return true;
            }

            if (Reported.Add(wrapper.name))
                _log.Msg("[fix] mea-wrapped-routes: the Packager's routes already scroll in '" + wrapper.name
                       + "' from another mod; MoreEmployeeAssignments keeps its " + ui.RouteEntries.Length
                       + " route rows there and does not build a second scroll view.");
            Fixes.Record("mea-wrapped-routes", "left the route scrolling to '" + wrapper.name + "'");
            return false;
        }
    }
}
