using System.Text;

namespace Polyfill.Contract
{
    /// <summary>What Polyfill did about one thing a mod asked for.</summary>
    internal static class Outcome
    {
        /// <summary>Put back into the game.</summary>
        internal const string Applied = "applied";
        /// <summary>There was a candidate and Polyfill did not trust it. The reason follows a colon.</summary>
        internal const string Refused = "refused";
        /// <summary>A repair exists for it and was written for builds this is not one of.</summary>
        internal const string StoodDown = "stood-down";
        /// <summary>Nothing to point at.</summary>
        internal const string None = "none";
    }

    /// <summary>One thing a mod asks for that this installation does not have.</summary>
    internal sealed class Finding
    {
        internal string Kind;     // type | member | field | harmony-target
        internal string Scope;    // the game assembly it was expected in
        internal string Symbol;   // what the mod asks for
        internal string Reason;   // why it is not there
        internal string Hint;     // what it most likely became on this machine, or "" when nothing is certain
        internal string Site;     // where in the mod, when we know

        /// <summary>
        /// What happened to it, filled in after the repair rather than before.
        /// </summary>
        /// <remarks>
        /// The report used to be written BEFORE the injection ran, so it could only ever say what was found,
        /// never what was done. A repair the injector refused reached the log and nothing else, which made
        /// "Polyfill could have and chose not to" invisible in the one file a player is asked to send.
        /// </remarks>
        internal string Outcome = Contract.Outcome.None;

        /// <summary>Set with Outcome; the sentence a mod author needs and a log line does not carry.</summary>
        internal string OutcomeDetail;

        /// <summary>
        /// Which repair this finding turned into, when it turned into one. Not written to the file - it
        /// exists to carry the answer from the injector back to the finding it came from, since the two are
        /// deduplicated across every mod and cannot be matched by name afterwards.
        /// </summary>
        internal string RepairKey;

        internal bool Fixable => !string.IsNullOrEmpty(Hint);
    }

    internal sealed class ModReport
    {
        internal string Path, AssemblyName, Name, Version, Author;
        internal int TypeRefs, MemberRefs;

        /// <summary>How many [HarmonyPatch] targets were resolvable enough to check. Counted so that a
        /// report with no Harmony findings can be told apart from one where nothing was looked at.</summary>
        internal int HarmonyTargetsChecked;

        internal readonly List<Finding> Findings = new();

        internal string Display => !string.IsNullOrEmpty(Name) ? Name
                                 : !string.IsNullOrEmpty(AssemblyName) ? AssemblyName
                                 : System.IO.Path.GetFileName(Path ?? "");

        /// <summary>
        /// clean - nothing missing. adaptable - everything missing has exactly one candidate on this
        /// machine. blocked - at least one thing is gone with nothing to point at.
        /// </summary>
        internal string Verdict
        {
            get
            {
                if (Findings.Count == 0) return "clean";
                foreach (var finding in Findings)
                    if (string.IsNullOrEmpty(finding.Hint)) return "blocked";
                return "adaptable";
            }
        }
    }

    /// <summary>
    /// The run's findings, on disk, in a format a person can read without a tool.
    /// </summary>
    /// <remarks>
    /// Line-oriented and pipe-separated rather than JSON, for the same reason SideHustle writes its deferred
    /// lists this way: the plugin writes it before any mod exists and the companion mod reads it after
    /// everything does, and neither should need a serializer or share a type to do that. It is also the
    /// answer to "what did Polyfill decide about my mod" - a player can open it in Notepad.
    ///
    /// The two sides used to spell the format out separately, and the writer stamped a format version that
    /// the reader never looked at. So a change here was a silent mis-parse: the reader would take a line
    /// whose columns had moved and read the wrong field into the wrong place, forever, with no error. Now
    /// there is one implementation, compiled into both, and a file from a newer Polyfill is REFUSED by name
    /// rather than misread.
    /// </remarks>
    internal sealed class RunReport
    {
        internal const int Format = 2;
        internal const string HeaderPrefix = "# polyfill-report ";

        /// <summary>What AppendLine leaves at the end of every line on Windows.</summary>
        private const char CarriageReturn = (char)13;

        internal string Generated = "";
        internal string Game = "?";
        internal string Interop = "";
        internal int AssemblyCount;
        internal readonly List<ModReport> Mods = new();
        internal readonly List<string> Dropped = new();

        /// <summary>Set when the file could not be used, and why. Null means it was read.</summary>
        internal string Problem;

        internal string Text()
        {
            var text = new StringBuilder();
            text.AppendLine(HeaderPrefix + Format);
            text.AppendLine("# generated=" + Escape(Generated));
            text.AppendLine("# game=" + Escape(Game));
            text.AppendLine("# interop=" + Escape(Interop));
            text.AppendLine("# assemblies=" + AssemblyCount);
            text.AppendLine("# mods=" + Mods.Count);

            foreach (var mod in Mods)
            {
                text.AppendLine(string.Join("|", "M",
                    Escape(mod.Path), Escape(mod.AssemblyName), Escape(mod.Name),
                    Escape(mod.Version), Escape(mod.Author), mod.Verdict,
                    mod.TypeRefs.ToString(), mod.MemberRefs.ToString(),
                    mod.HarmonyTargetsChecked.ToString(), mod.Findings.Count.ToString()));

                foreach (var finding in mod.Findings)
                    text.AppendLine(string.Join("|", "F",
                        Escape(mod.Path), Escape(finding.Kind), Escape(finding.Scope),
                        Escape(finding.Symbol), Escape(finding.Reason), Escape(finding.Hint),
                        Escape(finding.Site), Escape(finding.Outcome), Escape(finding.OutcomeDetail)));
            }

            foreach (string line in Dropped) text.AppendLine("D|" + Escape(line));
            return text.ToString();
        }

        /// <summary>
        /// Read one back. Never throws; an unreadable file comes back with <see cref="Problem"/> set.
        /// </summary>
        /// <remarks>
        /// Format 1 is still read, because a player can end up with a new plugin and an old companion mod or
        /// the other way round for as long as it takes them to copy the second file. Format 1 has no outcome
        /// columns, so its findings come back as "none" - which is exactly what that build knew.
        ///
        /// A truncated file loses its last line and keeps the rest: the writer can be killed mid-write by a
        /// crash, and a half-written report is still better than no report.
        /// </remarks>
        internal static RunReport Read(IEnumerable<string> lines)
        {
            var report = new RunReport();
            var byPath = new Dictionary<string, ModReport>(StringComparer.OrdinalIgnoreCase);
            bool sawHeader = false;

            foreach (string raw in lines)
            {
                if (raw == null) continue;
                // Written with AppendLine, so every line ends CR LF on Windows. File.ReadAllLines hides
                // that; anything else - a stream, a split, a test - hands the CR straight through and it
                // ends up inside the last value on the line.
                string line = raw.TrimEnd(CarriageReturn);

                if (!sawHeader)
                {
                    if (!line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                    { report.Problem = "this is not a Polyfill report"; return report; }

                    string version = line.Substring(HeaderPrefix.Length).Trim();
                    if (!int.TryParse(version, out int format))
                    { report.Problem = $"the format is written as '{version}', which is not a number"; return report; }
                    if (format > Format)
                    {
                        report.Problem = $"it is format {format} and this build reads {Format}. "
                                       + "Polyfill.dll in Mods/ and Polyfill.Boot.dll in Plugins/ are from "
                                       + "different releases - update both.";
                        return report;
                    }
                    sawHeader = true;
                    continue;
                }

                if (line.StartsWith("# game=", StringComparison.Ordinal)) { report.Game = line.Substring(7); continue; }
                if (line.StartsWith("# interop=", StringComparison.Ordinal)) { report.Interop = line.Substring(10); continue; }
                if (line.StartsWith("# generated=", StringComparison.Ordinal)) { report.Generated = line.Substring(12); continue; }
                if (line.StartsWith("# assemblies=", StringComparison.Ordinal))
                { report.AssemblyCount = Int(line.Substring(13)); continue; }
                if (line.Length == 0 || line[0] == '#') continue;

                string[] p = line.Split('|');
                switch (p[0])
                {
                    case "M" when p.Length >= 10:
                        var mod = new ModReport
                        {
                            Path = p[1], AssemblyName = p[2], Name = p[3], Version = p[4], Author = p[5],
                            TypeRefs = Int(p[7]), MemberRefs = Int(p[8]), HarmonyTargetsChecked = Int(p[9]),
                        };
                        report.Mods.Add(mod);
                        byPath[mod.Path] = mod;
                        break;

                    case "F" when p.Length >= 8 && byPath.TryGetValue(p[1], out var owner):
                        owner.Findings.Add(new Finding
                        {
                            Kind = p[2], Scope = p[3], Symbol = p[4], Reason = p[5], Hint = p[6], Site = p[7],
                            Outcome = p.Length > 8 ? p[8] : Contract.Outcome.None,
                            OutcomeDetail = p.Length > 9 ? p[9] : "",
                        });
                        break;

                    case "D" when p.Length >= 2:
                        report.Dropped.Add(p[1]);
                        break;
                }
            }

            if (!sawHeader) report.Problem = "the file is empty";
            return report;
        }

        private static int Int(string s) => int.TryParse(s, out int value) ? value : 0;

        /// <summary>The separator and the line break are the format; anything carrying them is replaced
        /// rather than quoted, because a report is read by eye more often than by code.</summary>
        internal static string Escape(string value)
            => string.IsNullOrEmpty(value) ? "" : value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    }
}
