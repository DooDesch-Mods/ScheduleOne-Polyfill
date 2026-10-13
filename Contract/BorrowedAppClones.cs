namespace Polyfill.Contract
{
    /// <summary>
    /// The cloned phone apps the fix <c>borrowed-app-layout</c> repairs, mod by mod.
    /// </summary>
    /// <remarks>
    /// TWO READERS, ONE LIST. The plugin finds these clones while it reads a mod's code, before the game
    /// exists, and writes a shape-coupled finding for each. The fix repairs them much later, in the other
    /// assembly, at the moment the mod builds its app. As long as only the fix knew which clones it
    /// answers for, the finding said "Polyfill cannot repair it" about a screen Polyfill had just
    /// repaired, and the mod stayed blocked over it.
    ///
    /// A HINT AT TRIAGE, AN OUTCOME AFTER THE REPAIR, the same split as <see cref="CoveredElsewhere"/>.
    /// What is known before the game exists is that a fix for this clone exists on this game version. It
    /// is not known whether the fix is switched off, or whether the container still has the layout group
    /// it was written against. So the plugin writes a hint, and the finding reads repaired only once the
    /// fix has switched the layout group off and said so - see Report/Reconcile.cs.
    ///
    /// BY NAME, AND NARROW. An entry answers for one method in one mod. A clone anywhere else keeps the
    /// plain note, because the fix does not look at it.
    /// </remarks>
    internal static class BorrowedAppClones
    {
        internal const string FixId = "borrowed-app-layout";

        /// <summary>
        /// The game builds the fix runs on. The fix reads its own range from here, so the hint cannot
        /// promise a repair on a build the fix stands down on.
        /// </summary>
        internal const string GameVersions = "0.4.6*,0.4.7*";

        /// <summary>What the hint says the fix does about the clone.</summary>
        internal const string Because
            = "it switches off the vertical layout group the clone inherits from the game's product "
            + "manager, at the moment the mod builds its app";

        /// <summary>The outcome sentence, written when the fix has done it.</summary>
        internal const string Repaired
            = "the fix " + FixId + " switched off the vertical layout group the clone inherited, so the "
            + "app's own panels decide where they go";

        internal sealed class Entry
        {
            /// <summary>The mod's type that builds the app.</summary>
            internal string Type;

            /// <summary>The method that clones the game's app. The finding sits here.</summary>
            internal string CloneMethod;

            /// <summary>The method the fix runs after. The mod's panels exist when it returns.</summary>
            internal string HookMethod;

            /// <summary>
            /// The static field on <see cref="Type"/> that holds the cloned app, or null when the hooked
            /// method is handed the container itself.
            /// </summary>
            internal string Field;

            /// <summary>Where the plugin reports the clone, in the spelling a finding's site uses.</summary>
            internal string Site => Type + "/" + CloneMethod;
        }

        internal static readonly Entry[] All =
        {
            // Media Player clones in SpawnApp and hands the container to ClearContainer before it
            // builds, so the finding and the hook are two different methods.
            new Entry
            {
                Type = "MediaPlayer.PhoneIntegration",
                CloneMethod = "SpawnApp", HookMethod = "ClearContainer", Field = null,
            },
            new Entry
            {
                Type = "Tweakables.TweakablesApp",
                CloneMethod = "ClonePanel", HookMethod = "ClonePanel", Field = "_appPanel",
            },
            new Entry
            {
                Type = "ElDiablo59WagesManager.WagesApp",
                CloneMethod = "BuildAppRoot", HookMethod = "BuildAppRoot", Field = "_appRoot",
            },
        };

        private static readonly VersionRange Range = VersionRange.Parse(GameVersions);

        /// <summary>The entry for a clone reported at this site, or null when the fix does not answer for it.</summary>
        internal static Entry At(string site)
        {
            if (string.IsNullOrEmpty(site)) return null;
            foreach (var entry in All)
                if (string.Equals(entry.Site, site, StringComparison.Ordinal)) return entry;
            return null;
        }

        /// <summary>
        /// Does the fix run on this game build? An unknown build answers no: a hint that might be false
        /// is worse than the plain note.
        /// </summary>
        internal static bool RunsOn(GameVersion game) => Range.Allows(game);
    }
}
