using MelonLoader;
using Polyfill.Contract;

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

        /// <summary>Which versions of that mod this was written against. See VersionRange for the grammar.</summary>
        internal abstract string ModVersions { get; }

        /// <summary>
        /// Which game builds. "0.4.6*" is every f-build of 0.4.6, "&gt;=0.4.6f5" is that one and everything
        /// after it, "0.4.5f2..0.4.6f12" is a window.
        /// </summary>
        /// <remarks>
        /// A closed range is the honest default and it has a cost worth naming: the day the game ships
        /// 0.4.7, every fix written as "0.4.6*" stands down. That is correct - a module is written by
        /// reading one build of one mod against one build of the game - but it must not be SILENT, which is
        /// what <see cref="StandsDownBecause"/> and the warning in Fixes.Run are for.
        /// </remarks>
        internal abstract string GameVersions { get; }

        /// <summary>
        /// One line, printed when the version gate stops this fix on a game NEWER than it was written for.
        /// </summary>
        /// <remarks>
        /// A stand-down the player cannot see is the same as no fix at all. After a game update the fixes
        /// that no longer apply are exactly the work list, and it belongs in the log and in the export
        /// rather than behind a command nobody types.
        /// </remarks>
        internal virtual string StandsDownBecause => null;

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
            => ForMod.Allows(modVersion) && ForGame.Allows(gameVersion);

        /// <summary>Is the installed game NEWER than anything this fix was written for? The case that has to
        /// be said out loud rather than counted as "wrong version" and forgotten.</summary>
        internal bool GameIsNewerThanKnown(string gameVersion)
        {
            var installed = Contract.GameVersion.Parse(gameVersion);
            if (!installed.IsKnown) return false;

            bool sawBound = false;
            foreach (var bound in ForGame.Bounds())
            {
                sawBound = true;
                if (installed <= bound) return false;
            }
            return sawBound;
        }

        private VersionRange _forMod, _forGame;

        internal VersionRange ForMod => _forMod ??= Range(ModVersions, nameof(ModVersions));
        internal VersionRange ForGame => _forGame ??= Range(GameVersions, nameof(GameVersions));

        /// <summary>
        /// Set when one of the two ranges does not parse. A bug in Polyfill's own source, and named as one.
        /// </summary>
        /// <remarks>
        /// The old matcher answered an unparsable pattern with a silent "no term matched", which is exactly
        /// what "this fix is for another build" looks like - so a typo would have switched a fix off forever
        /// and told nobody. Refusing to apply is still the safe answer; saying why is the new part.
        /// </remarks>
        internal string RangeProblem { get; private set; }

        private VersionRange Range(string text, string which)
        {
            if (VersionRange.TryParse(text, out var range, out string problem)) return range;
            RangeProblem = $"{which} is '{text}', which is not a version range ({problem})";
            return VersionRange.None;
        }
    }
}
