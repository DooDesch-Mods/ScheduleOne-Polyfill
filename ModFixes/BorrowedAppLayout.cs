using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A phone app built inside a borrowed vanilla one fills the screen again.
    /// </summary>
    /// <remarks>
    /// NOT A MISSING NAME, which is why nothing else here catches it and why these mods read clean
    /// while a player looks at a broken screen.
    ///
    /// Three mods do the same thing: rather than build a phone app, they clone the game's
    /// <c>ProductManagerApp</c> out of <c>AppsCanvas</c>, empty its <c>Container</c> and put their own
    /// panels in there. Their panels are stretch-anchored, which was right when they were written and is
    /// not now: 0.4.6's Container carries a <c>VerticalLayoutGroup</c> with ChildControlWidth and
    /// ChildControlHeight set, so the layout group decides the size and position of every child and the
    /// anchors are ignored. A panel with no preferred height gets a minimal one, stacked from the top -
    /// buttons in a band with nothing below them.
    ///
    /// Proven, not inferred: the Container's component list is exactly a RectTransform and that
    /// VerticalLayoutGroup, under a parent named ProductManagerApp
    /// (0.4.6f12 export, Player.prefab:8294 and :133508).
    ///
    /// ONE OBJECT PER MOD, NEVER A SWEEP, and that is not caution for its own sake. The first version of
    /// this looked at every child of AppsCanvas that had a Container and skipped only the game's own
    /// ProductManagerApp by name - and switched the layout group off on the vanilla DeliveryApp and on a
    /// fourth mod's ModSettingsApp as well, both measured in game. A repair that breaks the game's own
    /// delivery screen is worse than the bug it was written for.
    ///
    /// So each entry says exactly where its mod keeps the clone: Media Player hands the container to
    /// ClearContainer directly, and the other two store theirs in a static field this reads by name.
    /// Nothing else is looked at.
    ///
    /// A mod that WANTED the inherited vertical layout would be broken by this. None of the three does -
    /// every one of them destroys the vanilla children and rebuilds - and that is why entries name mods
    /// instead of being a rule about cloned apps.
    ///
    /// AFTER THE BUILD, BEFORE THE LAYOUT. Unity lays out at the end of the frame, so a postfix on a
    /// method that built its panels synchronously still gets there first. Media Player is hooked one step
    /// earlier, on its own ClearContainer, because it hands the container over directly.
    /// </remarks>
    internal sealed class BorrowedAppLayout : Fix
    {
        internal override string Id => "borrowed-app-layout";
        internal override string Mod => "*";
        internal override string ModVersions => "*";

        /// <summary>
        /// Closed on purpose: this is a statement about one build's prefab, not about the game.
        /// </summary>
        /// <remarks>
        /// The shape was read off the 0.4.6f12 export and seen on 0.4.6f13. On 0.4.7 the component may be
        /// gone or configured differently - and the check below would refuse anyway. Standing down loudly
        /// is still the better default: a layout fix that silently does nothing is indistinguishable from
        /// the bug it was written for.
        /// </remarks>
        internal override string GameVersions => "0.4.6*";

        internal override string What
            => "phone apps built inside a borrowed vanilla one fill the screen again instead of being "
             + "squeezed into a strip at the top";

        internal override string StandsDownBecause
            => "0.4.6 put a vertical layout group on the app container these mods clone, which overrules "
             + "where they put their own panels.";

        /// <summary>
        /// Where each mod builds, and where it keeps what it built.
        /// </summary>
        /// <remarks>
        /// <c>Field</c> null means the hooked method is handed the container itself, as Media Player's
        /// ClearContainer is. Otherwise it is a static field on the same type holding the cloned app, and
        /// the container is its child of that name.
        /// </remarks>
        private static readonly (string Type, string Method, string Field)[] Hooks =
        {
            ("MediaPlayer.PhoneIntegration", "ClearContainer", null),
            ("Tweakables.TweakablesApp", "ClonePanel", "_appPanel"),
            ("ElDiablo59WagesManager.WagesApp", "BuildAppRoot", "_appRoot"),
        };

        private static MelonLogger.Instance _log;
        private static readonly HashSet<int> Done = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            int wired = 0;
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");

            foreach (var (typeName, methodName, fieldName) in Hooks)
            {
                var type = AccessTools.TypeByName(typeName);
                if (type == null) continue;               // that mod is not installed; not our business

                var target = AccessTools.Method(type, methodName);
                if (target == null)
                {
                    log.Warning($"[fix] borrowed-app-layout: {typeName} is here but {methodName} is not, "
                              + "so this version of the mod builds its app some other way and the moment "
                              + "to act cannot be found.");
                    continue;
                }

                var field = fieldName == null ? null : AccessTools.Field(type, fieldName);
                if (fieldName != null && field == null)
                {
                    log.Warning($"[fix] borrowed-app-layout: {typeName} has no {fieldName}, so the app it "
                              + "clones cannot be identified - and guessing which one is its would risk "
                              + "the game's own screens.");
                    continue;
                }

                try
                {
                    Owners[target] = field;
                    harmony.Patch(target,
                        postfix: new HarmonyMethod(typeof(BorrowedAppLayout), nameof(Loosen)));
                    wired++;
                }
                catch (Exception e)
                {
                    log.Warning($"[fix] borrowed-app-layout: could not hook {typeName}.{methodName}: "
                              + e.Message);
                }
            }

            return wired > 0;
        }

        /// <summary>The field each hooked method's clone lives in, or null when it is handed one.</summary>
        private static readonly Dictionary<System.Reflection.MethodBase, System.Reflection.FieldInfo> Owners
            = new();

        /// <summary>Switch off the inherited layout on THIS mod's clone, once.</summary>
        private static void Loosen(System.Reflection.MethodBase __originalMethod, object[] __args)
        {
            try
            {
                Owners.TryGetValue(__originalMethod, out var field);

                GameObject app = null;
                if (field != null) app = field.GetValue(null) as GameObject;
                else if (__args != null && __args.Length > 0) app = __args[0] as GameObject;

                if (app == null)
                {
                    Complain("the mod's own app object was not there to read after it built, so the "
                           + "container cannot be identified");
                    return;
                }

                // Handed the container itself, or handed the app that holds one.
                var container = app.transform.name == "Container" ? app.transform : app.transform.Find("Container");
                if (container == null)
                {
                    Complain("the cloned app has no Container child, so the shape it was written against "
                           + "is not the shape it got");
                    return;
                }

                if (!Done.Add(container.GetInstanceID())) return;

                var layout = container.GetComponent<VerticalLayoutGroup>();
                if (layout == null)
                {
                    // The reason to repair is gone. Say so rather than reporting a repair that had
                    // nothing to do - the next reader needs to know the prefab changed again.
                    Complain("the app container has no vertical layout group any more, so there is nothing "
                           + "to loosen and the mod's own anchors already decide");
                    return;
                }

                if (!layout.enabled) return;              // the mod dealt with it itself; nothing to say

                layout.enabled = false;
                _log?.Msg($"[fix] borrowed-app-layout: switched off the vertical layout group "
                        + $"{app.name} inherited from the game's product manager, so its own panels "
                        + "decide where they go again.");
            }
            catch (Exception e)
            {
                Complain("could not read the cloned app: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static readonly HashSet<string> Complained = new();

        private static void Complain(string why)
        {
            if (!Complained.Add(why)) return;
            _log?.Warning("[fix] borrowed-app-layout: " + why + ". The app is left exactly as the mod "
                        + "built it.");
            Fixes.Record("borrowed-app-layout", "did nothing: " + why);
        }
    }
}
