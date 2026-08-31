using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Media Player's app draws where it was told to, instead of crushed into a band at the top.
    /// </summary>
    /// <remarks>
    /// NOT A MISSING NAME, which is why nothing else here catches it and why the load check calls this
    /// mod clean while a player looks at a broken screen.
    ///
    /// The mod does not build a phone app. It clones the game's <c>ProductManagerApp</c> out of
    /// <c>AppsCanvas</c>, destroys the children of that clone's <c>Container</c>, and builds its own
    /// panels in there. Its panels are stretch-anchored, which was right when it was written and is not
    /// now: 0.4.6's Container carries a <c>VerticalLayoutGroup</c> with ChildControlWidth and
    /// ChildControlHeight set, so the layout group decides the size and position of every child and the
    /// anchors are ignored. A panel with no preferred height gets a minimal one, stacked from the top -
    /// the buttons in a band, nothing below them.
    ///
    /// Proven, not inferred. The Container's component list is exactly a RectTransform and that
    /// VerticalLayoutGroup, under a parent named ProductManagerApp
    /// (0.4.6f12 export, Player.prefab:8294 and :133508).
    ///
    /// THE MOMENT MATTERS. The group is switched off in the window between the mod emptying the
    /// container and filling it - a postfix on its own <c>ClearContainer</c>. Do it later and the
    /// children have already been laid out and collapsed, and putting them back means guessing what
    /// the mod wanted; the anchors it is about to set would have to be reconstructed rather than simply
    /// left alone.
    ///
    /// ONLY THE CLONE. The check walks up to AppsCanvas and reads the component list before touching
    /// anything, so the game's own product manager keeps its layout. A mod that WANTS the inherited
    /// vertical layout would be broken by this, which is exactly why it names one mod instead of being
    /// a rule about cloned apps.
    /// </remarks>
    internal sealed class MediaPlayerAppLayout : Fix
    {
        internal override string Id => "mediaplayer-app-layout";
        internal override string Mod => "MediaPlayer";
        internal override string ModVersions => "*";

        /// <summary>
        /// Closed on purpose: this is a statement about one build's prefab, not about the game.
        /// </summary>
        /// <remarks>
        /// The shape was read off the 0.4.6f12 export and seen on 0.4.6f13. On 0.4.7 the component may
        /// be gone, may be configured differently, or the mod may have been rebuilt - and the check
        /// below would refuse anyway. Standing down loudly is still the better default: a layout fix
        /// that silently does nothing is indistinguishable from the bug it was written for.
        /// </remarks>
        internal override string GameVersions => "0.4.6*";

        internal override string What
            => "Media Player's app fills the phone screen again instead of being squeezed into a strip "
             + "at the top";

        internal override string StandsDownBecause
            => "0.4.6 put a vertical layout group on the app container Media Player clones, which "
             + "overrules where the mod puts its own panels.";

        private const string Owner = "MediaPlayer.PhoneIntegration";

        private static MelonLogger.Instance _log;
        private static bool _done;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var type = AccessTools.TypeByName(Owner);
            if (type == null)
            {
                log.Warning("[fix] mediaplayer-app-layout: " + Owner + " is not in this build of the "
                          + "mod, so the moment to act cannot be found.");
                return false;
            }

            var target = AccessTools.Method(type, "ClearContainer", new[] { typeof(GameObject) });
            if (target == null)
            {
                log.Warning("[fix] mediaplayer-app-layout: " + Owner + ".ClearContainer(GameObject) is "
                          + "not there. This version of the mod builds its app some other way and this "
                          + "fix cannot help it.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, postfix: new HarmonyMethod(typeof(MediaPlayerAppLayout), nameof(Loosen)));
            return true;
        }

        /// <summary>Switch off the inherited layout, once, and only on a container that is that clone's.</summary>
        private static void Loosen(GameObject container)
        {
            if (_done || container == null) return;

            try
            {
                if (!UnderAppsCanvas(container.transform))
                {
                    Complain("the container is not under AppsCanvas, so it is not the cloned phone app "
                           + "this was written for");
                    return;
                }

                var layout = container.GetComponent<VerticalLayoutGroup>();
                if (layout == null)
                {
                    // The reason to repair is gone. Say so rather than reporting a repair that had
                    // nothing to do - the next reader needs to know the prefab changed again.
                    Complain("the app container has no vertical layout group any more, so there is "
                           + "nothing here to loosen and the mod's own anchors already decide");
                    return;
                }

                if (!layout.enabled)
                {
                    Complain("the layout group is already switched off - something else got here first");
                    return;
                }

                layout.enabled = false;
                _done = true;
                _log?.Msg("[fix] mediaplayer-app-layout: switched off the vertical layout group the app "
                        + "container inherited from the game's product manager, so Media Player's own "
                        + "panels decide where they go again.");
            }
            catch (Exception e)
            {
                Complain("could not read the app container: " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>Is this the phone's app container, or something else that happens to be passed in?</summary>
        private static bool UnderAppsCanvas(Transform from)
        {
            for (var at = from; at != null; at = at.parent)
                if (at.name == "AppsCanvas") return true;
            return false;
        }

        private static void Complain(string why)
        {
            if (_done) return;
            _done = true;                                  // once, whatever happened
            _log?.Warning("[fix] mediaplayer-app-layout: " + why + ". The app is left exactly as the mod "
                        + "built it.");
            Fixes.Record("mediaplayer-app-layout", "did nothing: " + why);
        }
    }
}
