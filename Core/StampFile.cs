using System.Text;

namespace Polyfill.Core
{
    /// <summary>
    /// What Polyfill did to the interop assemblies last launch.
    /// </summary>
    /// <remarks>
    /// A cache and an audit trail, never the sole authority. Every decision in <see cref="InteropOriginals"/>
    /// can be made without this file, because the note inside each written assembly carries the same facts;
    /// what the stamp adds is the list of assemblies Polyfill touched, so a launch after a player deleted a
    /// kept copy still knows to look at that assembly rather than treating it as untouched.
    ///
    /// It lives in UserData and not next to the assemblies on purpose: the generator keeps a list of the
    /// files it owns in that folder and removes what is not on it, so a stamp there is either deleted or
    /// orphaned. Same line-oriented dialect as last-run.txt, and readable in Notepad for the same reason.
    /// </remarks>
    internal static class StampFile
    {
        internal const string FormatHeader = "# polyfill-stamp 1";

        internal static string Path => System.IO.Path.Combine(Report.Directory, "interop.stamp");

        internal sealed class Entry
        {
            internal string Assembly;
            internal string OriginalSha;
            internal int Repairs;
        }

        internal sealed class Stamp
        {
            internal string Generator = "";
            internal string Game = "";
            internal string Polyfill = "";
            internal readonly List<Entry> Assemblies = new();

            internal bool Knows(string assembly)
            {
                foreach (var one in Assemblies)
                    if (string.Equals(one.Assembly, assembly, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        /// <summary>The last stamp, or an empty one. Never throws and never reports a parse problem as a
        /// failure: a stamp nobody can read simply says nothing, and the notes inside the assemblies do
        /// the deciding.</summary>
        internal static Stamp Read()
        {
            var stamp = new Stamp();
            try
            {
                if (!File.Exists(Path)) return stamp;
                string[] lines = File.ReadAllLines(Path);
                if (lines.Length == 0 || !lines[0].StartsWith("# polyfill-stamp ", StringComparison.Ordinal))
                    return stamp;
                // A newer format is not read at all rather than read wrongly. A stamp says what happened
                // last time; getting that wrong is worse than not knowing.
                if (lines[0] != FormatHeader) return stamp;

                foreach (string line in lines)
                {
                    string[] parts = line.Split('|');
                    switch (parts[0])
                    {
                        case "G" when parts.Length >= 2:
                            stamp.Generator = parts[1];
                            break;
                        case "V" when parts.Length >= 3:
                            stamp.Game = parts[1];
                            stamp.Polyfill = parts[2];
                            break;
                        case "A" when parts.Length >= 4:
                            stamp.Assemblies.Add(new Entry
                            {
                                Assembly = parts[1],
                                OriginalSha = parts[2],
                                Repairs = int.TryParse(parts[3], out int n) ? n : 0,
                            });
                            break;
                    }
                }
            }
            catch { }
            return stamp;
        }

        internal static void Write(string generatorDigest, IEnumerable<Entry> assemblies)
        {
            var text = new StringBuilder();
            text.AppendLine(FormatHeader);
            text.AppendLine("# generated=" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            text.AppendLine("G|" + Escape(generatorDigest));
            text.AppendLine(string.Join("|", "V", Escape(Report.GameVersion()), Escape(DooDesch.ModVersion.Current)));

            foreach (var one in assemblies)
                text.AppendLine(string.Join("|", "A", Escape(one.Assembly), Escape(one.OriginalSha),
                                            one.Repairs.ToString()));

            try
            {
                Directory.CreateDirectory(Report.Directory);
                File.WriteAllText(Path, text.ToString());
            }
            catch (Exception e)
            {
                Boot.Plugin.Log?.Warning("[stamp] could not be written: " + e.Message);
            }
        }

        /// <summary>Forget everything. Used when a regeneration has made every recorded fact untrue.</summary>
        internal static void Clear()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { }
        }

        private static string Escape(string value)
            => string.IsNullOrEmpty(value) ? "" : value.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
    }
}
