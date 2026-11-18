using System.Text;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Report
{
    /// <summary>
    /// Findings a runtime fix repaired, written back into the report that was made before it ran.
    /// </summary>
    /// <remarks>
    /// THE REPORT IS OLDER THAN HALF THE REPAIRS. The plugin triages and writes the file while the game is
    /// still loading; the per-mod fixes run on the first frame the world exists, which was measured
    /// twenty-two seconds later in a different assembly. So a finding a fix repaired was still recorded as
    /// unrepaired, the mod's verdict stayed "blocked", and the index ranked the symbol as open work across
    /// every installation that reported it - OG Backpack's StorageMenu.Open patch is moved onto the method
    /// the game calls, provably, and sat at the top of the queue anyway.
    ///
    /// NOT THE AGGREGATE. A fix reports how many patches it moved and nothing about which, and feeding that
    /// number back would mark one mod's finding repaired because another mod's was. What is reconciled here
    /// is an exact key per patch - the mod's own assembly, and the method the patch ended up on - recorded
    /// by the fix at the moment it succeeded.
    ///
    /// BOTH COPIES. The in-memory report is what gets sent at quit, and the file is what a player is asked
    /// to read and send by hand; correcting one and not the other would leave the two disagreeing about the
    /// same session. The file is edited in place, one column on the lines that changed, because rewriting it
    /// would need the plugin's writer and a second copy of the format to keep in step.
    ///
    /// TWO KINDS OF REPAIR, TWO KEYS. A patch a fix moved is found by the method it sits on, the
    /// finding's symbol. A cloned piece of the game's interface a fix repaired is found by where the mod
    /// makes it, the finding's site.
    ///
    /// MORE THAN ONCE. The first pass follows the fixes on the first frame of the world. A fix that acts
    /// later - borrowed-app-layout waits for the mod to build its phone app - asks again when it has
    /// acted. A finding that already reads applied is skipped, so asking again costs a loop.
    /// </remarks>
    internal static class Reconcile
    {
        /// <summary>The sentence a finding gets when a fix moved its patch.</summary>
        private const string Moved = "moved onto the method the game calls";

        /// <summary>Match what a fix repaired against what the report says, and correct the difference.</summary>
        internal static void After(MelonLogger.Instance log)
        {
            RepairedMods(log);

            var repaired = ModFixes.Fixes.Repaired;
            var repairedAt = ModFixes.Fixes.RepairedAt;
            if (repaired.Count == 0 && repairedAt.Count == 0) return;

            var corrected = new List<(ModReport Mod, Finding Finding)>();

            foreach (var mod in ReportReader.Mods)
            {
                string assembly = mod.AssemblyName;
                if (string.IsNullOrEmpty(assembly)) continue;

                foreach (var finding in mod.Findings)
                {
                    if (finding.Outcome == Outcome.Applied) continue;

                    string detail = null;
                    if (finding.Kind == "harmony-target")
                    {
                        if (repaired.Contains(Key(assembly, finding.Symbol))) detail = Moved;
                    }
                    else if (finding.Kind == "shape-coupled")
                    {
                        repairedAt.TryGetValue(Key(assembly, finding.Site), out detail);
                    }
                    if (detail == null) continue;

                    finding.Outcome = Outcome.Applied;
                    finding.OutcomeDetail = detail;
                    corrected.Add((mod, finding));
                }
            }

            if (corrected.Count == 0) return;

            var names = new List<string>();
            foreach (var (mod, finding) in corrected) names.Add(mod.AssemblyName + "|" + finding.Symbol);
            log.Msg($"[report] {corrected.Count} finding(s) were repaired after the report was written, and "
                  + "say so now: " + string.Join(", ", names));

            InFile(corrected, log);
        }

        /// <summary>
        /// Every mod a per-mod fix applied to in this run reports adaptable, findings or not.
        /// </summary>
        /// <remarks>
        /// A ModFix leaves no finding, so without this a mod that runs only because Polyfill patched it
        /// went out as "clean" and the index showed "Works" over it. The fixes that name no mod ("*") are
        /// left out: they repair patches other mods aim at the game, and those already arrive as
        /// harmony-target findings that the loop in After marks applied.
        ///
        /// Only ever adds. This runs again when a deferred fix acts, and a fix that applied earlier in the
        /// session stays the reason the mod needed Polyfill even if its later work found nothing to do.
        ///
        /// Two kinds of fix are not counted: an armed one until it has acted ("armed" does not start with
        /// "applied"), and one the mod does not need without Polyfill (Fix.NeededWithoutPolyfill).
        /// </remarks>
        private static void RepairedMods(MelonLogger.Instance log)
        {
            var changed = new List<ModReport>();
            foreach (var outcome in ModFixes.Fixes.Results)
            {
                if (!Applied(outcome.State)) continue;
                string target = outcome.Fix.Mod;
                if (target == "*") continue;
                if (!outcome.Fix.NeededWithoutPolyfill)
                {
                    if (NotCounted.Add(outcome.Fix.Id))
                        log.Msg($"[report] fix {outcome.Fix.Id} applied to {target} and does not make it "
                              + "adaptable: the mod runs without it when Polyfill is not installed.");
                    continue;
                }

                bool listed = false;
                foreach (var mod in ReportReader.Mods)
                {
                    if (!Names(mod, target)) continue;
                    listed = true;
                    if (mod.RepairedBy.Contains(outcome.Fix.Id)) continue;

                    string was = mod.Verdict;
                    mod.RepairedBy.Add(outcome.Fix.Id);
                    if (mod.Verdict != was && !changed.Contains(mod)) changed.Add(mod);
                }

                // A library loaded from UserLibs is repaired like any mod and is not in the startup report
                // when the plugin did not triage it. Nothing is wrong, but nothing can carry the verdict.
                if (!listed)
                    log.Msg($"[report] fix {outcome.Fix.Id} applied to {target}, which the startup report "
                          + "does not list, so no verdict says it.");
            }

            if (changed.Count == 0) return;

            foreach (var mod in changed)
                log.Msg($"[report] {mod.Display} {mod.Version} is repaired by a Polyfill fix "
                      + $"({string.Join(", ", mod.RepairedBy)}), so it reports {mod.Verdict} instead of clean.");

            VerdictsInFile(changed, log);
        }

        /// <summary>Fixes already said to be left out of the verdict, so a second pass does not repeat it.</summary>
        private static readonly HashSet<string> NotCounted = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>"applied", or a fix's later note that starts with it.</summary>
        private static bool Applied(string state)
            => state != null && state.StartsWith("applied", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Is this report row the mod a fix names? By the name it registers with MelonLoader, which is what
        /// Fix.Mod holds, or by its assembly for a library that has no MelonInfo.
        /// </summary>
        private static bool Names(ModReport mod, string target)
            => string.Equals(mod.Name, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.AssemblyName, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(mod.AssemblyName, target + "_Il2Cpp", StringComparison.OrdinalIgnoreCase);

        /// <summary>The verdict column of the mods a fix repaired, in last-run.txt.</summary>
        private static void VerdictsInFile(List<ModReport> changed, MelonLogger.Instance log)
        {
            string path = ReportReader.Path;
            try
            {
                if (!File.Exists(path))
                {
                    log.Warning("[report] the in-memory verdicts were corrected and there is no "
                              + "UserData/Polyfill/last-run.txt to correct. What gets sent is right.");
                    return;
                }

                var lines = File.ReadAllLines(path);
                int touched = 0;
                foreach (var mod in changed) if (RunReport.CorrectVerdict(lines, mod)) touched++;

                if (touched == 0)
                {
                    log.Warning("[report] the in-memory verdicts were corrected and the file was not - no "
                              + "matching mod line was found. UserData/Polyfill/last-run.txt is out of date "
                              + "for those mods; what gets sent is right.");
                    return;
                }

                File.WriteAllLines(path, lines);
            }
            catch (Exception e)
            {
                log.Warning("[report] could not write the corrected verdicts back to last-run.txt ("
                          + e.GetType().Name + ": " + e.Message + "), so the file still shows those mods as "
                          + "clean. What gets sent is right.");
            }
        }

        /// <summary>
        /// The one key both halves can agree on: whose finding, and the symbol or the site that names it.
        /// </summary>
        internal static string Key(string assembly, string name)
            => (assembly ?? "") + "|" + (name ?? "");

        /// <summary>
        /// Set the outcome columns on the lines that changed, leaving the rest of the file alone.
        /// </summary>
        /// <remarks>
        /// The columns and how a line is matched are the format's business, so that part is
        /// RunReport.Correct. Anything unexpected is left untouched and said out loud rather than guessed
        /// at: a report edited into a shape nothing can read is worse than one that is out of date.
        /// </remarks>
        private static void InFile(List<(ModReport Mod, Finding Finding)> corrected, MelonLogger.Instance log)
        {
            string path = ReportReader.Path;
            try
            {
                if (!File.Exists(path))
                {
                    log.Warning("[report] the in-memory report was corrected and there is no "
                              + "UserData/Polyfill/last-run.txt to correct. What gets sent is right.");
                    return;
                }

                var lines = File.ReadAllLines(path);
                int touched = 0;
                foreach (var (mod, finding) in corrected) touched += RunReport.Correct(lines, mod, finding);

                if (touched == 0)
                {
                    log.Warning("[report] the in-memory report was corrected and the file was not - no "
                              + "matching line was found. UserData/Polyfill/last-run.txt is out of date "
                              + "for those findings; what gets sent is right.");
                    return;
                }

                File.WriteAllLines(path, lines);
            }
            catch (Exception e)
            {
                // A report that cannot be rewritten is stale, which is a smaller problem than a half
                // written one - so this says what happened and leaves the file as it was.
                log.Warning("[report] could not write the corrected findings back to last-run.txt ("
                          + e.GetType().Name + ": " + e.Message + "), so the file still shows them as "
                          + "unrepaired. What gets sent is right.");
            }
        }
    }
}
