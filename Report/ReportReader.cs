namespace Polyfill.Report
{
    internal sealed class ModLine
    {
        internal string Path, AssemblyName, Name, Version, Author, Verdict;
        internal int TypeRefs, MemberRefs, HarmonyChecked, FindingCount;
        internal readonly List<FindingLine> Findings = new();

        internal string Display => !string.IsNullOrEmpty(Name) ? Name
                                 : !string.IsNullOrEmpty(AssemblyName) ? AssemblyName
                                 : System.IO.Path.GetFileName(Path);
    }

    internal sealed class FindingLine
    {
        internal string Kind, Scope, Symbol, Reason, Hint, Site;
        internal bool Fixable => !string.IsNullOrEmpty(Hint);
    }

    /// <summary>
    /// Read what the plugin wrote at startup.
    /// </summary>
    /// <remarks>
    /// The plugin and this mod share no type and no library - the file between them is the whole contract.
    /// That is deliberate: the plugin runs before Il2Cpp interop exists and this mod cannot exist without
    /// it, so anything they both touched would have to be built for the stricter of the two.
    /// </remarks>
    internal static class ReportReader
    {
        internal static string Path => System.IO.Path.Combine(
            MelonLoader.Utils.MelonEnvironment.UserDataDirectory ?? ".", "Polyfill", "last-run.txt");

        internal static string GameVersion { get; private set; } = "?";
        internal static string InteropDirectory { get; private set; } = "";
        internal static bool Loaded { get; private set; }

        private static List<ModLine> _mods = new();

        internal static IReadOnlyList<ModLine> Mods
        {
            get { if (!Loaded) Load(); return _mods; }
        }

        internal static void Load()
        {
            Loaded = true;
            _mods = new List<ModLine>();
            string path = Path;
            if (!File.Exists(path)) return;

            var byPath = new Dictionary<string, ModLine>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.StartsWith("# game=")) { GameVersion = line.Substring(7); continue; }
                if (line.StartsWith("# interop=")) { InteropDirectory = line.Substring(10); continue; }
                if (line.Length == 0 || line[0] == '#') continue;

                string[] p = line.Split('|');
                if (p[0] == "M" && p.Length >= 10)
                {
                    var mod = new ModLine
                    {
                        Path = p[1], AssemblyName = p[2], Name = p[3], Version = p[4], Author = p[5],
                        Verdict = p[6],
                        TypeRefs = Int(p[7]), MemberRefs = Int(p[8]),
                        HarmonyChecked = Int(p[9]), FindingCount = p.Length > 10 ? Int(p[10]) : 0,
                    };
                    _mods.Add(mod);
                    byPath[mod.Path] = mod;
                }
                else if (p[0] == "F" && p.Length >= 8 && byPath.TryGetValue(p[1], out var owner))
                {
                    owner.Findings.Add(new FindingLine
                    {
                        Kind = p[2], Scope = p[3], Symbol = p[4], Reason = p[5], Hint = p[6], Site = p[7],
                    });
                }
            }
        }

        private static int Int(string s) => int.TryParse(s, out int value) ? value : 0;

        /// <summary>Mods whose name or file contains <paramref name="term"/>, case-insensitively.</summary>
        internal static List<ModLine> Find(string term)
        {
            var hits = new List<ModLine>();
            foreach (var mod in Mods)
            {
                if (Contains(mod.Name, term) || Contains(mod.AssemblyName, term)
                    || Contains(System.IO.Path.GetFileName(mod.Path), term))
                    hits.Add(mod);
            }
            return hits;
        }

        private static bool Contains(string value, string term)
            => value != null && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
