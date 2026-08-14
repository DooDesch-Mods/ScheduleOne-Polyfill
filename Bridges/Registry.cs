using Polyfill.Contract;

namespace Polyfill.Bridges
{
    /// <summary>
    /// Every set of bridges there is.
    /// </summary>
    /// <remarks>
    /// NEWEST STEP FIRST, and that order is the tie-break: when two steps both wrote a bridge for the same
    /// member and both still fit this build, the newer one wins and the older is named in the log. It is
    /// the only place order matters, and it is the only line this file ever grows.
    ///
    /// A hard-coded list rather than a scan, deliberately. The plugin's second-largest feature exists to
    /// stop things from enumerating types at startup - the minidump that produced it is quoted in
    /// Dynamic/ReflectionFallback.cs - and adding a type scan to that same assembly would be tone-deaf even
    /// though its own types are safe to walk. A source generator would buy the same thing at the price of a
    /// build-time dependency in a project that references three DLLs.
    ///
    /// So the honest claim is not "a new game version changes nothing". It is: a new game version is ONE new
    /// folder, plus ONE line in a file that contains nothing but this list, and a test that fails if you
    /// forget the line. That is cheaper to get right than reflection and far easier to debug when it is
    /// wrong.
    /// </remarks>
    internal static class Registry
    {
        private static readonly BridgeSet[] Sets =
        {
            new Steps.S0_4_5f2_To_0_4_6f5.Set(),
        };

        internal static IEnumerable<BridgeSet> All => Sets;

        internal static IEnumerable<Bridge> Bridges()
        {
            foreach (var set in Sets)
                foreach (var bridge in set.Bridges)
                    yield return bridge;
        }

        /// <summary>
        /// The bridge for this exact member, or null.
        /// </summary>
        /// <remarks>
        /// Matched on all four parts and nothing fuzzy: a bridge is a person's decision about one member,
        /// and a decision that spreads to a member nobody looked at is not the same decision.
        /// </remarks>
        internal static Bridge Find(string assembly, string declaringType, string oldName, int parameterCount)
        {
            foreach (var bridge in Bridges())
                if (bridge.OldName == oldName && bridge.DeclaringType == declaringType
                    && bridge.ParameterCount == parameterCount
                    && string.Equals(bridge.Assembly, assembly, StringComparison.OrdinalIgnoreCase))
                    return bridge;
            return null;
        }

        /// <summary>
        /// The rename written for this exact type, or null.
        /// </summary>
        /// <remarks>
        /// Asked BEFORE the name search, for the reason the member pipeline puts a bridge first: a person
        /// read both builds to write this, and a coincidence of spelling is not allowed to outvote that.
        /// Whether the target is actually on the installed game is decided afterwards, by resolving it -
        /// naming a pair is a claim about one update, not about the player's files.
        /// </remarks>
        internal static TypeRename FindType(string assembly, string oldFullName)
        {
            foreach (var set in Sets)
                foreach (var rename in set.Renames)
                    if (rename.OldFullName == oldFullName
                        && string.Equals(rename.Assembly, assembly, StringComparison.OrdinalIgnoreCase))
                        return rename;
            return null;
        }

        /// <summary>
        /// What to say once, at startup, on a build newer than anything here was read against.
        /// </summary>
        /// <remarks>
        /// Not a warning: nothing is wrong. Everything that can be checked against the player's own game
        /// still runs, and what could not be checked is named so it becomes work rather than a silence.
        /// </remarks>
        internal static string PastTheHorizon(GameVersion game)
        {
            if (!game.IsKnown) return null;

            var newest = GameVersion.Unknown;
            foreach (var set in Sets)
            {
                var verified = GameVersion.Parse(set.VerifiedTo);
                if (!newest.IsKnown || verified > newest) newest = verified;
            }
            if (!newest.IsKnown || game <= newest) return null;

            return $"Schedule I {game} is newer than anything these repairs were read against ({newest}). "
                 + "They still run: each one checks the game you have before it does anything. What no "
                 + "longer fits is named in the log and in `polyfillexport`, which is what turns it into "
                 + "an update.";
        }
    }
}
