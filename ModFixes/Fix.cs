using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A repair that knows which mod it is for.
    /// </summary>
    /// <remarks>
    /// Everything else in Polyfill is keyed on GAME SYMBOLS and has never heard of any particular mod: a
    /// type that moved, a member that was renamed, a signature that grew an argument. Both sides of those
    /// sit in metadata, a repair happens only on a unique match, and nothing is inferred.
    ///
    /// Some breakage has no symbol at all. A prefab spawned by name, a scene path, a method that kept its
    /// signature and changed its meaning - there is nothing to match, so an automatic rule must refuse.
    /// A person who has read both the mod and the game can still decide, and this is where that decision
    /// lives: visible, versioned, and never inside somebody else's DLL.
    ///
    /// The asymmetry is the point. A FIX MAY JUDGE; A RULE MAY NOT. That is why this is a folder with
    /// names on it rather than one more heuristic.
    /// </remarks>
    internal abstract class Fix
    {
        /// <summary>Stable, lowercase, and what the player types to switch it off.</summary>
        internal abstract string Id { get; }

        /// <summary>The mod's name as it registers itself with MelonLoader.</summary>
        internal abstract string Mod { get; }

        /// <summary>Which versions of that mod this was written against. "*" for any.</summary>
        internal abstract string ModVersions { get; }

        /// <summary>Which game versions. A trailing "*" matches a prefix, so "0.4.6*" covers the f-builds.</summary>
        internal abstract string GameVersions { get; }

        /// <summary>One line, for the log and the console list. What the player gets, not how.</summary>
        internal abstract string What { get; }

        /// <summary>Do it. False means the conditions were not there after all and nothing happened.</summary>
        internal abstract bool Apply(MelonLogger.Instance log);

        /// <summary>
        /// Is this fix for the mod and game actually installed?
        /// </summary>
        /// <remarks>
        /// The version gate is not politeness, it is the difference between a fix and a liability. A module
        /// is written by reading one build of one mod against one build of the game. Let it run outside
        /// that and it either does nothing or does something nobody checked - and the mod's author gets
        /// the bug report.
        /// </remarks>
        internal bool AppliesTo(string modVersion, string gameVersion)
            => Matches(ModVersions, modVersion) && Matches(GameVersions, gameVersion);

        private static bool Matches(string pattern, string actual)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*") return true;
            if (string.IsNullOrEmpty(actual)) return false;

            foreach (string one in pattern.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string wanted = one.Trim();
                if (wanted.EndsWith("*", StringComparison.Ordinal))
                {
                    if (actual.StartsWith(wanted.Substring(0, wanted.Length - 1),
                                          StringComparison.OrdinalIgnoreCase)) return true;
                }
                else if (string.Equals(wanted, actual, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
