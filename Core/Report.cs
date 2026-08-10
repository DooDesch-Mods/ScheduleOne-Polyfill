using System.Reflection;
using System.Text;
using MelonLoader.Utils;

namespace Polyfill.Core
{
    /// <summary>One thing a mod asks for that this installation does not have.</summary>
    internal sealed class Finding
    {
        internal string Kind;    // type | member | field | harmony-target
        internal string Scope;   // the game assembly it was expected in
        internal string Symbol;  // what the mod asks for
        internal string Reason;  // why it is not there
        internal string Hint;    // what it most likely became on this machine, or "" when nothing is certain
        internal string Site;    // where in the mod, when we know
    }

    internal sealed class ModReport
    {
        internal string Path, AssemblyName, Name, Version, Author;
        internal int TypeRefs, MemberRefs;

        /// <summary>How many [HarmonyPatch] targets were resolvable enough to check. Counted so that a
        /// report with no Harmony findings can be told apart from one where nothing was looked at.</summary>
        internal int HarmonyTargetsChecked;

        internal readonly List<Finding> Findings = new();

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
    /// Line-oriented and pipe-separated rather than JSON, for the same reason SideHustle writes its
    /// deferred lists this way: the plugin writes it before any mod exists and the companion mod reads it
    /// after everything does, and neither should need a serializer or share a type to do that. It is also
    /// the answer to "what did Polyfill decide about my mod" - a player can open it in Notepad.
    /// </remarks>
    internal static class Report
    {
        internal const string FormatHeader = "# polyfill-report 1";

        internal static string Directory
            => Path.Combine(MelonEnvironment.UserDataDirectory ?? ".", "Polyfill");

        internal static string LastRunPath => Path.Combine(Directory, "last-run.txt");

        internal static void Write(List<ModReport> reports, string interopDirectory, int assemblyCount,
                                   List<string> dropped)
        {
            var text = new StringBuilder();
            text.AppendLine(FormatHeader);
            text.AppendLine("# generated=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            text.AppendLine("# game=" + Escape(GameVersion()));
            text.AppendLine("# interop=" + Escape(interopDirectory ?? "?"));
            text.AppendLine("# assemblies=" + assemblyCount);
            text.AppendLine("# mods=" + reports.Count);

            foreach (var report in reports)
            {
                text.AppendLine(string.Join("|", "M",
                    Escape(report.Path), Escape(report.AssemblyName), Escape(report.Name),
                    Escape(report.Version), Escape(report.Author), report.Verdict,
                    report.TypeRefs.ToString(), report.MemberRefs.ToString(),
                    report.HarmonyTargetsChecked.ToString(),
                    report.Findings.Count.ToString()));

                foreach (var finding in report.Findings)
                    text.AppendLine(string.Join("|", "F",
                        Escape(report.Path), Escape(finding.Kind), Escape(finding.Scope),
                        Escape(finding.Symbol), Escape(finding.Reason), Escape(finding.Hint),
                        Escape(finding.Site)));
            }

            foreach (string line in dropped) text.AppendLine("D|" + Escape(line));

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(LastRunPath, text.ToString());
            }
            catch (Exception e)
            {
                Boot.Plugin.Log?.Warning("[report] could not be written: " + e.Message);
            }
        }

        /// <summary>
        /// The game version, without touching Unity.
        /// </summary>
        /// <remarks>
        /// UnityEngine.Application.version is the obvious source and it is not available here: this runs
        /// before the support module is set up, and reaching into Unity at this point would load interop
        /// metadata that must stay untouched. MelonLoader has already read the version out of the build
        /// (it prints it at startup), so it is asked instead - by reflection, because the property has moved
        /// between MelonLoader versions and an unknown version string is a caption, not a failure.
        /// </remarks>
        internal static string GameVersion()
        {
            string[] candidates =
            {
                "MelonLoader.InternalUtils.UnityInformationHandler, MelonLoader",
                "MelonLoader.MelonUtils, MelonLoader",
            };
            foreach (string typeName in candidates)
            {
                try
                {
                    var type = Type.GetType(typeName, false);
                    var property = type?.GetProperty("GameVersion",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (property?.GetValue(null) is string version && version.Length > 0) return version;
                }
                catch { }
            }
            return "unknown";
        }

        private static string Escape(string value)
            => string.IsNullOrEmpty(value) ? "" : value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    }
}
