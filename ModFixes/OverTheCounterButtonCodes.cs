using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// The manager clipboard and the route picker answer to the right keys again.
    /// </summary>
    /// <remarks>
    /// The same shifted enum as <see cref="ThmButtonCodes"/>, in a mod that reads four buttons:
    /// <code>
    /// RouteEntitySelector.Tick              0   PrimaryClick, unmoved
    /// RouteEntitySelector.Tick             10   Escape    -> 0.4.6 has no Escape button
    /// RouteEntitySelector.Tick             11   Back      -> 0.4.6 has no Back button
    /// ManagerClipboardPatch.UpdatePrefix   12   Interact  -> now VehicleToggleLights (H), left alone
    /// </code>
    /// THE CLIPBOARD ONE IS DELIBERATELY LEFT ALONE, and that is the interesting decision here. Past its
    /// button check the prefix asks for <c>ManagementInterface.NPCSelector</c>, a type 0.4.6 removed, and
    /// throws a MissingMethodException across the interop boundary. Pointing its button at Interact would
    /// not fix that - it would move the throw from H, which nobody presses, onto the key a player presses
    /// every few seconds. A repair that turns a rare error into a constant one is not a repair.
    ///
    /// Harmony also declines to transpile it, since it is itself a patch method; the reason above is why
    /// that is left as it is rather than worked around.
    ///
    /// Escape and Back are gone from the game with no successor, so the two ways of closing the route
    /// picker without choosing anything become checks that never fire, rather than checks that fire on
    /// Interact and Submit - which is what those numbers mean today, and which would close the picker on
    /// the very key meant to choose with.
    /// </remarks>
    internal sealed class OverTheCounterButtonCodes : Fix
    {
        internal override string Id => "otc-button-codes";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";
        internal override string What => "the route picker stops closing itself on the key that chooses";

        internal override string StandsDownBecause
            => "The route picker closes without choosing on the interact key, which is the key meant to "
             + "choose with.";

        internal override bool Apply(MelonLogger.Instance log)
            => ButtonCodeShift.Apply(log, Id, "OverTheCounter.Il2Cpp",
                   ("OverTheCounter.UI.RouteEntitySelector", "Tick")) > 0;
    }
}
