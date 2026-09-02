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
    /// </remarks>
    internal static class Reconcile
    {
        /// <summary>Match what a fix repaired against what the report says, and correct the difference.</summary>
        internal static void After(MelonLogger.Instance log)
        {
            var repaired = ModFixes.PatchesOnGrownOverloads.Repaired;
            if (repaired.Count == 0) return;

            var corrected = new List<string>();
            var verdicts = new Dictionary<string, ModReport>(StringComparer.Ordinal);

            foreach (var mod in ReportReader.Mods)
            {
                string assembly = mod.AssemblyName;
                if (string.IsNullOrEmpty(assembly)) continue;

                foreach (var finding in mod.Findings)
                {
                    if (finding.Kind != "harmony-target") continue;
                    if (finding.Outcome == Outcome.Applied) continue;
                    if (!repaired.Contains(Key(assembly, finding.Symbol))) continue;

                    finding.Outcome = Outcome.Applied;
                    finding.OutcomeDetail = "moved onto the method the game calls";
                    corrected.Add(assembly + "|" + finding.Symbol);
                    verdicts[assembly] = mod;
                }
            }

            if (corrected.Count == 0) return;

            log.Msg($"[report] {corrected.Count} finding(s) were repaired after the report was written, and "
                  + "say so now: " + string.Join(", ", corrected));

            InFile(corrected, verdicts, log);
        }

        /// <summary>The one key both halves can agree on: whose patch, and what it now sits on.</summary>
        internal static string Key(string assembly, string symbol)
            => (assembly ?? "") + "|" + (symbol ?? "");

        /// <summary>
        /// Set the outcome column on the lines that changed, leaving the rest of the file alone.
        /// </summary>
        /// <remarks>
        /// Column nine is the outcome and ten is its sentence, matching what the plugin writes. Anything
        /// unexpected is left untouched and said out loud rather than guessed at: a report edited into a
        /// shape nothing can read is worse than one that is out of date.
        /// </remarks>
        private static void InFile(List<string> corrected,
                                   Dictionary<string, ModReport> verdicts,
                                   MelonLogger.Instance log)
        {
            string path = ReportReader.Path;
            try
            {
                if (!File.Exists(path)) return;

                var wanted = new HashSet<string>(corrected, StringComparer.Ordinal);
                var lines = File.ReadAllLines(path);
                int touched = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!line.StartsWith("F|", StringComparison.Ordinal)) continue;

                    var parts = line.Split('|');
                    if (parts.Length < 10 || parts[2] != "harmony-target") continue;

                    string assembly = AssemblyOf(lines, i);
                    if (assembly == null || !wanted.Contains(assembly + "|" + parts[4])) continue;

                    parts[8] = Outcome.Applied;
                    parts[9] = "moved onto the method the game calls";
                    lines[i] = string.Join("|", parts);
                    touched++;

                    // AND THE VERDICT ABOVE IT. Findings and verdict are two columns on two lines, and
                    // correcting only the finding left the file saying "blocked" over a row that now reads
                    // "applied" - two halves of one sentence disagreeing. The verdict is computed from the
                    // findings, so the in-memory mod already knows the new answer.
                    if (verdicts.TryGetValue(assembly, out var mod)) Verdict(lines, i, mod.Verdict);
                }

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


        /// <summary>Set the verdict on the <c>M|</c> line that owns the finding at <paramref name="index"/>.</summary>
        private static void Verdict(string[] lines, int index, string verdict)
        {
            if (string.IsNullOrEmpty(verdict)) return;
            for (int i = index - 1; i >= 0; i--)
            {
                if (!lines[i].StartsWith("M|", StringComparison.Ordinal)) continue;
                var parts = lines[i].Split('|');
                if (parts.Length > 6) { parts[6] = verdict; lines[i] = string.Join("|", parts); }
                return;
            }
        }

        /// <summary>Which mod a finding line belongs to: the nearest <c>M|</c> line above it.</summary>
        private static string AssemblyOf(string[] lines, int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (!lines[i].StartsWith("M|", StringComparison.Ordinal)) continue;
                var parts = lines[i].Split('|');
                return parts.Length > 2 ? parts[2] : null;
            }
            return null;
        }
    }
}
