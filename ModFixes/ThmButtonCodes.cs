using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// The syringe and the garrote answer to the interact key again.
    /// </summary>
    /// <remarks>
    /// All three of T.H.M's interaction paths read button number 12, which was <c>Interact</c> when the mod
    /// was built and is <c>VehicleToggleLights</c> since 0.4.6 - the H key. Everything else about the mod
    /// works: the items are given, the models are drawn, the contract is tracked. Pressing E does nothing,
    /// and pressing H does the whole thing, including the strangling mini-game, which reads the same number
    /// for every press.
    ///
    /// <see cref="ButtonCodeShift"/> carries the reasoning and the mapping.
    /// </remarks>
    internal sealed class ThmButtonCodes : Fix
    {
        internal override string Id => "thm-button-codes";
        internal override string Mod => "T.H.M - The Hitman Mod";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";
        internal override string What => "the syringe and the garrote answer to the interact key again";

        internal override string StandsDownBecause
            => "The syringe and the garrote will be in your hand and do nothing on the interact key.";

        internal override bool Apply(MelonLogger.Instance log)
            => ButtonCodeShift.Apply(log, Id, "Kowyx_THM",
                   ("HitmanMod.PoisonHandler", "Update"),
                   ("HitmanMod.StrangleHandler", "Update"),
                   ("HitmanMod.StrangleMiniGame", "Update")) > 0;
    }
}
