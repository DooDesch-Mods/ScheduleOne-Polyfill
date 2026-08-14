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
    /// ManagerClipboardPatch.UpdatePrefix   12   Interact  -> now VehicleToggleLights (H)
    /// </code>
    /// THE CLIPBOARD ONE WAS DELIBERATELY LEFT ALONE, AND THAT REASON EXPIRED. Past its button check the
    /// prefix asked for <c>ManagementInterface.NPCSelector</c>, a type 0.4.6 removed, and threw across the
    /// interop boundary - so pointing its button at Interact would only have moved the throw from H, which
    /// nobody presses, onto the key a player presses every few seconds.
    ///
    /// 0.7.4 gave that screen a stand-in and the throw stopped. What was left was a clipboard that answers
    /// to H, which is not a repair either - reported as "the prompt shows and nothing happens, and there is
    /// no error in the log", which is exactly what a mod listening on the wrong key looks like once it has
    /// stopped crashing.
    ///
    /// The note that Harmony refuses to transpile a patch method turned out not to hold here; it takes this
    /// one. Measured rather than assumed, which is why it is being written down twice.
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
        internal override string What
            => "the clipboard answers the interact key, and the route picker stops closing itself on it";

        internal override string StandsDownBecause
            => "The manager clipboard answers a key nobody presses, and the route picker closes without "
             + "choosing on the interact key - which is the key meant to choose with.";

        internal override bool Apply(MelonLogger.Instance log)
            => ButtonCodeShift.Apply(log, Id, "OverTheCounter.Il2Cpp",
                   ("OverTheCounter.UI.RouteEntitySelector", "Tick"),
                   ("OverTheCounter.Patches.ManagerClipboardPatch", "UpdatePrefix")) > 0;
    }
}
