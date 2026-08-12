using MelonLoader;

namespace Polyfill.Core
{
    /// <summary>
    /// Which file is the untouched original, asked once and answered for everybody.
    /// </summary>
    /// <remarks>
    /// The interop assemblies are MelonLoader's cache, rebuilt from the player's GameAssembly.dll whenever
    /// the game, Unity, the dumper or MelonLoader itself changes. Polyfill keeps the untouched copy of
    /// anything it writes as <c>.polyfill-orig</c> and rebuilds from that copy, so repairs never stack.
    ///
    /// That kept copy is only an original for as long as the generation it came from. After a game update
    /// MelonLoader writes fresh assemblies over ours, and a kept copy from yesterday is not a backup any
    /// more - it is the previous version of the game. Reading it and writing the result over the new file
    /// hands every installed mod the metadata of a build that is no longer installed, including the mods
    /// that were working. That is the failure this exists to make impossible.
    ///
    /// The discriminator is not a hash of a hundred megabytes and not a version string: it is whether
    /// MelonLoader's output still carries the note Polyfill leaves inside everything it writes (see
    /// <see cref="Provenance"/>). An unmarked live file next to a kept copy means the generator has been
    /// here since, and it works the same way after a game update, a MelonLoader upgrade, a Steam file
    /// verification and a folder copied from another machine.
    ///
    /// Both halves of Polyfill ask this one object. The analysis has to read the original or a repair
    /// applied last launch reads as "nothing was missing" and is silently dropped; the injector has to
    /// write from the same original or the two disagree about what they are looking at.
    /// </remarks>
    internal sealed class InteropOriginals
    {
        internal enum Origin
        {
            /// <summary>The live file is untouched. Nothing kept aside yet.</summary>
            Seed,
            /// <summary>The kept copy is provably the original of this generation.</summary>
            Reuse,
            /// <summary>The kept copy is from an earlier generation and has been removed.</summary>
            Reseed,
            /// <summary>No original can be established. Nothing is written to this assembly.</summary>
            StandDown,
            /// <summary>Our own output survived a generation it does not belong to. Put back, write nothing.</summary>
            Regenerated,
        }

        private readonly Dictionary<string, Origin> _origin = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _source = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _directory;

        internal GeneratorIdentity Generator { get; private set; }

        /// <summary>Set when something makes writing wrong for the whole launch rather than for one
        /// assembly: the window is shut, or a generation did not complete.</summary>
        internal string StoodDownBecause { get; private set; }

        private InteropOriginals(string directory) => _directory = directory;

        /// <summary>
        /// Work out the state of every assembly Polyfill has ever written to, and put right what can be put
        /// right. Runs before the index is built, because the index reads whatever this decides.
        /// </summary>
        /// <param name="mayAct">
        /// False under DryRun and when the injection window is already shut. The decisions are still made -
        /// the analysis needs them to read the right file - but nothing on disk is touched.
        /// </param>
        internal static InteropOriginals Take(string directory, bool mayAct, MelonLogger.Instance log)
        {
            var originals = new InteropOriginals(directory) { Generator = GeneratorIdentity.Read() };
            var stamp = StampFile.Read();

            if (!originals.Generator.IsKnown)
                log.Msg("[stamp] MelonLoader's generator config could not be read, so a generation that did "
                      + "not finish cannot be told apart from one that did. Everything else still holds.");

            foreach (string assembly in originals.Examine(stamp))
                originals.DecideOne(assembly, stamp, mayAct, log);

            originals.Summarise(stamp, mayAct, log);
            return originals;
        }

        /// <summary>
        /// The untouched original of this assembly - whichever file that currently is.
        /// </summary>
        /// <remarks>
        /// An assembly Polyfill has never written to answers with the live file, which IS the original.
        /// Nothing has to be copied to find that out, so a player with no repairs pays nothing.
        /// </remarks>
        internal string SourceFor(string assembly)
        {
            if (assembly != null && _source.TryGetValue(assembly, out string path)) return path;
            return Live(assembly);
        }

        internal Origin OriginOf(string assembly)
            => assembly != null && _origin.TryGetValue(assembly, out var origin) ? origin : Origin.Seed;

        /// <summary>May a repair be written into this assembly at all?</summary>
        internal bool MayWrite(string assembly)
        {
            if (StoodDownBecause != null) return false;
            var origin = OriginOf(assembly);
            return origin != Origin.StandDown && origin != Origin.Regenerated;
        }

        /// <summary>Why not, in one sentence, for the report.</summary>
        internal string RefusalFor(string assembly)
        {
            if (StoodDownBecause != null) return StoodDownBecause;
            return OriginOf(assembly) switch
            {
                Origin.StandDown => "no untouched copy of this assembly could be established",
                Origin.Regenerated => "the generated assemblies were put back this launch",
                _ => null,
            };
        }

        /// <summary>Every assembly worth looking at: one we kept a copy of, or one the stamp says we
        /// wrote to. Anything else has never been written to, so the live file is its own original and
        /// there is nothing to check.</summary>
        private IEnumerable<string> Examine(StampFile.Stamp stamp)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string backup in Directory.GetFiles(_directory, "*" + InteropAugmentor.BackupSuffix))
                {
                    string file = backup.Substring(0, backup.Length - InteropAugmentor.BackupSuffix.Length);
                    names.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch { }

            foreach (var entry in stamp.Assemblies)
                if (!string.IsNullOrEmpty(entry.Assembly)) names.Add(entry.Assembly);

            return names;
        }

        private void DecideOne(string assembly, StampFile.Stamp stamp, bool mayAct, MelonLogger.Instance log)
        {
            string live = Live(assembly);
            string backup = live + InteropAugmentor.BackupSuffix;
            bool liveExists = File.Exists(live);
            bool backupExists = File.Exists(backup);

            if (!liveExists)
            {
                // The game dropped an assembly we once repaired. Nothing to do and nothing to delete: an
                // orphaned copy costs a few megabytes and removing somebody's file to save them is not a
                // trade this makes.
                Set(assembly, Origin.StandDown, live);
                if (backupExists)
                    log.Msg($"[stamp] {assembly} is no longer installed; the copy kept of it was left alone.");
                return;
            }

            var mark = Provenance.ReadFrom(live);

            if (mark == null)
            {
                if (!backupExists) { Set(assembly, Origin.Seed, live); return; }

                // No note and a kept copy beside it: either MelonLoader wrote over our file, or a Polyfill
                // from before notes existed wrote it. The two need opposite answers - throw the copy away,
                // or keep it as the only original there is - so guessing is not on offer.
                //
                // The generator writes every interop assembly in one pass, within about a second of itself.
                // Anything written measurably later than its untouched neighbours was not written by that
                // pass. See GenerationTime.
                if (WrittenAfterGeneration(live))
                {
                    Set(assembly, Origin.Reuse, backup);
                    _adopted++;
                    log.Msg($"[stamp] {assembly} was repaired by an older Polyfill, from before it left a note "
                          + "in what it writes. The copy kept beside it is still the original and is used as "
                          + "one; from now on the note settles this without a timestamp.");
                    return;
                }

                // MelonLoader has written over our file since the copy was kept. The copy is the PREVIOUS
                // generation of this assembly, and reading it now would build this launch's repairs out of
                // the last game's metadata.
                Set(assembly, Origin.Reseed, live);
                if (mayAct)
                {
                    try { File.Delete(backup); }
                    catch (Exception e)
                    { log.Warning($"[stamp] the stale copy of {assembly} could not be removed: {e.Message}"); }
                }
                return;
            }

            if (!backupExists)
            {
                // Our own output with nothing to rebuild from. Injecting again would stack repairs on top of
                // repairs, and there is no way back to the original from here.
                Set(assembly, Origin.StandDown, live);
                log.Warning($"[stamp] {assembly} was repaired by Polyfill and the untouched copy beside it is "
                          + "gone, so nothing can be rebuilt from it. Left exactly as it is. "
                          + "`polyfillregen` makes MelonLoader generate it again.");
                return;
            }

            if (Generator.IsKnown && mark.Generator != Generator.Digest())
            {
                // The generator's inputs changed but our file survived, which means the regeneration did not
                // run to the end. Putting the original back is the only honest move: what is there now was
                // built for a game this no longer is.
                Set(assembly, Origin.Regenerated, backup);
                StoodDownBecause = "the interop assemblies were put back because a regeneration did not finish";
                if (mayAct)
                {
                    try { File.Copy(backup, live, true); File.Delete(backup); }
                    catch (Exception e)
                    { log.Error($"[stamp] {assembly} could not be put back: {e.Message}"); }
                }
                log.Warning($"[stamp] {assembly} is Polyfill's output, but MelonLoader's generator has changed "
                          + "its inputs since - so the rebuild did not finish. The untouched copy was put back "
                          + "and nothing was repaired this launch. If this repeats, delete "
                          + "MelonLoader/Dependencies/Il2CppAssemblyGenerator/Cpp2IL/cpp2il_out and start again.");
                return;
            }

            string sha = Provenance.Sha256(backup);
            if (sha == null || !string.Equals(sha, mark.Source, StringComparison.OrdinalIgnoreCase))
            {
                Set(assembly, Origin.StandDown, live);
                log.Warning($"[stamp] the copy kept of {assembly} is not the one it was built from, so which "
                          + "of the two is the original cannot be settled here. Both were left alone.");
                return;
            }

            Set(assembly, Origin.Reuse, backup);
        }

        private void Summarise(StampFile.Stamp stamp, bool mayAct, MelonLogger.Instance log)
        {
            int reseeded = 0, reused = 0, refused = 0;
            foreach (var origin in _origin.Values)
                switch (origin)
                {
                    case Origin.Reseed: reseeded++; break;
                    case Origin.Reuse: reused++; break;
                    case Origin.StandDown: refused++; break;
                }

            if (reseeded > 0)
            {
                // A game update is the usual reason and the one worth naming. It is not the only one: a
                // MelonLoader upgrade and a Steam file verification both regenerate too, and then the game
                // version is the same on both sides and an arrow between two identical numbers reads as a bug.
                string now = Report.GameVersion();
                string moved = string.IsNullOrEmpty(stamp.Game) || stamp.Game == now
                    ? ""
                    : $"Schedule I {stamp.Game} -> Schedule I {now}. ";

                log.Msg($"[stamp] {moved}MelonLoader generated the interop assemblies again, so the {reseeded} "
                      + "copy/copies Polyfill kept of the old ones are "
                      + (mayAct ? "stale and were removed. " : "stale and were left in place (DryRun). ")
                      + "Starting from the new originals.");
                StampFile.Clear();
            }
            else if (reused - _adopted > 0)
            {
                // Only the ones whose note and kept copy were checked against each other. The copies taken
                // over from an older Polyfill said so a moment ago and have nothing to match against yet.
                log.Msg($"[stamp] {reused - _adopted} assembly/assemblies still match what Polyfill built "
                      + "them from.");
            }

            if (refused > 0)
                log.Warning($"[stamp] {refused} assembly/assemblies were left untouched because no original "
                          + "could be established for them.");
        }

        /// <summary>
        /// Was this file written after the pass that generated all the others?
        /// </summary>
        /// <remarks>
        /// The generator writes the whole folder in one go: on this machine 136 assemblies carry write times
        /// inside the same second. Anything Polyfill writes lands days later. So an assembly that is much
        /// newer than its untouched neighbours was written by something that is not the generator - which,
        /// in this folder, means Polyfill.
        ///
        /// This is the only thing that can tell a Polyfill from before notes existed apart from a fresh
        /// generation, and getting it wrong the other way would throw away the last untouched copy on every
        /// machine that upgrades. It is a fallback, not the mechanism: once a note is in the file, the note
        /// answers and this is never consulted.
        /// </remarks>
        private bool WrittenAfterGeneration(string live)
        {
            var generation = GenerationTime();
            if (generation == null) return false;          // cannot tell: treat it as the generator's

            try
            {
                // A minute of slack. The pass takes about a second here; the point is to separate seconds
                // from days, not to be precise.
                return File.GetLastWriteTimeUtc(live) > generation.Value.AddSeconds(60);
            }
            catch { return false; }
        }

        /// <summary>When the interop assemblies were last generated, read off the files Polyfill has never
        /// touched - which is all but one or two of them.</summary>
        private DateTime? GenerationTime()
        {
            if (_generationTime.HasValue) return _generationTime;
            if (_generationTimeAsked) return null;
            _generationTimeAsked = true;

            try
            {
                DateTime newest = DateTime.MinValue;
                foreach (string file in Directory.GetFiles(_directory, "*.dll"))
                {
                    if (File.Exists(file + InteropAugmentor.BackupSuffix)) continue;   // one we have written
                    var written = File.GetLastWriteTimeUtc(file);
                    if (written > newest) newest = written;
                }
                if (newest != DateTime.MinValue) _generationTime = newest;
            }
            catch { }
            return _generationTime;
        }

        private DateTime? _generationTime;
        private bool _generationTimeAsked;

        /// <summary>Kept copies taken over from a Polyfill that left no note. Counted apart because they
        /// were believed on a timestamp, not on a match.</summary>
        private int _adopted;

        private void Set(string assembly, Origin origin, string source)
        {
            _origin[assembly] = origin;
            _source[assembly] = source;
        }

        private string Live(string assembly)
            => assembly == null ? null : Path.Combine(_directory, assembly + ".dll");
    }
}
