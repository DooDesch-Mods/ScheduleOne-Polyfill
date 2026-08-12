using Polyfill.Contract;

namespace Polyfill.Report
{
    /// <summary>
    /// Read what the plugin wrote at startup.
    /// </summary>
    /// <remarks>
    /// The plugin and this mod share no assembly - the file between them is the whole contract. That is
    /// deliberate: the plugin runs before Il2Cpp interop exists and this mod cannot exist without it, so
    /// anything they both linked would have to be built for the stricter of the two.
    ///
    /// The FORMAT is now shared, by compiling one source file into both, which is a different thing from
    /// sharing an assembly. It used to be transcribed twice, and the version stamped at the top was written
    /// and never read - so a change to the columns would have been a silent mis-parse rather than an error.
    /// </remarks>
    internal static class ReportReader
    {
        internal static string Path => PolyfillPaths.LastRun(MelonLoader.Utils.MelonEnvironment.UserDataDirectory);

        internal static string GameVersion => _report.Game;
        internal static string InteropDirectory => _report.Interop;
        internal static bool Loaded { get; private set; }

        /// <summary>Why the file could not be used, or null. Printed instead of an empty report, because
        /// "no mods have problems" and "I could not read the file" look identical otherwise.</summary>
        internal static string Problem => _report.Problem;

        private static RunReport _report = new();

        internal static IReadOnlyList<ModReport> Mods
        {
            get { if (!Loaded) Load(); return _report.Mods; }
        }

        internal static void Load()
        {
            Loaded = true;
            try
            {
                string path = Path;
                _report = File.Exists(path)
                    ? RunReport.Read(File.ReadAllLines(path))
                    : new RunReport { Problem = "no report has been written yet" };
            }
            catch (Exception e)
            {
                _report = new RunReport { Problem = "the report could not be read: " + e.Message };
            }
        }

        /// <summary>Mods whose name or file contains <paramref name="term"/>, case-insensitively.</summary>
        internal static List<ModReport> Find(string term)
        {
            var hits = new List<ModReport>();
            foreach (var mod in Mods)
            {
                if (Contains(mod.Name, term) || Contains(mod.AssemblyName, term)
                    || Contains(System.IO.Path.GetFileName(mod.Path ?? ""), term))
                    hits.Add(mod);
            }
            return hits;
        }

        private static bool Contains(string value, string term)
            => value != null && term != null && value.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
