using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Polyfill.Contract
{
    /// <summary>
    /// The body of a log check on polyfill.doomods.com/check, built from this launch instead of a log.
    /// </summary>
    /// <remarks>
    /// The page reads a dropped Latest.log down to this same shape in the browser. Asked from the console,
    /// the report and the error watch already hold every piece, so nothing is parsed: one mod per report
    /// entry, its verdict as the triage, its errors as the watch counted them.
    ///
    /// THE SERVER REFUSES THE WHOLE BODY over one bad field. So every field is held to the server's own
    /// patterns here, and a value that fails one is dropped or emptied rather than sent: an exception
    /// type the pattern does not allow is still counted on its mod but not listed, a frame that does not
    /// fit is sent empty, and a mod without a usable name or version is left out and counted.
    /// </remarks>
    internal static class CheckExcerpt
    {
        internal const int NameMax = 120;
        internal const int ModsMax = 300;
        internal const int ModErrorsMax = 5;
        internal const int CountMax = 9999;

        // The patterns of the server's validation, PolyfillIndex src/lib/player-report.ts.
        internal static readonly Regex Version = new(@"^[0-9A-Za-z][0-9A-Za-z._+-]{0,31}$");
        internal static readonly Regex Kind = new(@"^[A-Za-z_][A-Za-z0-9_.+`]{0,79}$");
        internal static readonly Regex Frame = new(@"^[A-Za-z0-9_<][A-Za-z0-9_.,`<>\[\]()+&*$|: -]{0,119}$");

        private static readonly Regex Unprintable = new(@"[\p{Cc}\\/]+");
        private static readonly Regex Spaces = new(@"\s+");

        /// <summary>One distinct error as the watch counted it.</summary>
        internal sealed class Error
        {
            internal string Mod;
            internal string Kind;
            internal string Frame;
            internal string Site;
            internal int Count;
        }

        internal sealed class Built
        {
            internal string Json;
            internal int Mods;
            /// <summary>Report entries left out for a name or version the server would refuse.</summary>
            internal int Skipped;
        }

        /// <summary>A name as the server takes it: no control character, no slash, no backslash.</summary>
        internal static string NameOf(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string clean = Spaces.Replace(Unprintable.Replace(raw, " "), " ").Trim();
            return clean.Length > NameMax ? clean.Substring(0, NameMax).TrimEnd() : clean;
        }

        internal static string FrameOf(string raw)
            => !string.IsNullOrEmpty(raw) && Frame.IsMatch(raw) ? raw : "";

        /// <summary>
        /// Null when the game version itself would be refused, because then nothing can be checked.
        /// </summary>
        internal static Built Build(RunReport report, IEnumerable<Error> errors, string gameVersion,
                                    string melonLoader, string polyfillVersion)
        {
            if (report == null || string.IsNullOrEmpty(gameVersion) || !Version.IsMatch(gameVersion)) return null;

            var byMod = new Dictionary<string, List<Error>>(System.StringComparer.Ordinal);
            int other = 0, polyfill = 0;
            var known = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var mod in report.Mods) known.Add(mod.Display ?? "");

            foreach (var error in errors ?? new List<Error>())
            {
                if (error == null || error.Count <= 0) continue;
                if (error.Mod == "Polyfill") { polyfill += error.Count; continue; }
                if (string.IsNullOrEmpty(error.Mod) || !known.Contains(error.Mod)) { other += error.Count; continue; }
                if (!byMod.TryGetValue(error.Mod, out var list)) byMod[error.Mod] = list = new List<Error>();
                list.Add(error);
            }

            var json = new StringBuilder();
            json.Append("{\"gameVersion\":").Append(Quote(gameVersion));
            json.Append(",\"melonLoader\":").Append(Quote(Version.IsMatch(melonLoader ?? "") ? melonLoader : ""));
            json.Append(",\"polyfillVersion\":").Append(Quote(Version.IsMatch(polyfillVersion ?? "") ? polyfillVersion : ""));
            json.Append(",\"mods\":[");

            int sent = 0, skipped = 0;
            foreach (var mod in report.Mods)
            {
                string name = NameOf(mod.Display);
                // Polyfill is stated by its version above, never as one of the mods.
                if (name == "Polyfill") continue;
                if (name.Length == 0 || mod.Version == null || !Version.IsMatch(mod.Version) || sent == ModsMax)
                { skipped++; continue; }

                byMod.TryGetValue(mod.Display ?? "", out var own);
                own ??= new List<Error>();
                own.Sort((a, b) => b.Count.CompareTo(a.Count));

                int total = 0;
                var listed = new List<Error>();
                foreach (var error in own)
                {
                    total += error.Count;
                    if (listed.Count < ModErrorsMax && !string.IsNullOrEmpty(error.Kind) && Kind.IsMatch(error.Kind))
                        listed.Add(error);
                }

                if (sent > 0) json.Append(',');
                json.Append("{\"name\":").Append(Quote(name));
                json.Append(",\"version\":").Append(Quote(mod.Version));
                json.Append(",\"author\":").Append(Quote(NameOf(mod.Author)));
                json.Append(",\"triage\":").Append(Quote(mod.Verdict));
                json.Append(",\"errorCount\":").Append(Cap(total, CountMax * ModErrorsMax));
                json.Append(",\"errors\":[");
                for (int i = 0; i < listed.Count; i++)
                {
                    if (i > 0) json.Append(',');
                    json.Append("{\"kind\":").Append(Quote(listed[i].Kind));
                    json.Append(",\"frame\":").Append(Quote(FrameOf(listed[i].Frame)));
                    json.Append(",\"site\":").Append(Quote(FrameOf(listed[i].Site)));
                    json.Append(",\"count\":").Append(Cap(listed[i].Count, CountMax));
                    json.Append('}');
                }
                json.Append("]}");
                sent++;
            }

            json.Append("],\"otherErrors\":").Append(Cap(other, CountMax * ModErrorsMax));
            json.Append(",\"polyfillErrors\":").Append(Cap(polyfill, CountMax * ModErrorsMax));
            json.Append(",\"stop\":null}");

            return new Built { Json = json.ToString(), Mods = sent, Skipped = skipped };
        }

        /// <summary>The request: the excerpt, kept and shared so the answer has a link.</summary>
        internal static string Request(string excerpt) => "{\"excerpt\":" + excerpt + ",\"share\":true}";

        /// <summary>The id the server gave the shared check, or null when it gave none.</summary>
        internal static string IdOf(string answer)
        {
            if (string.IsNullOrEmpty(answer)) return null;
            var match = Regex.Match(answer, "^\\{\"id\":\"([A-Za-z0-9]{12})\"");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>The server's own reason for a refusal, `{"error":"..."}`, or the raw text cut short.</summary>
        internal static string ReasonOf(string answer)
        {
            if (string.IsNullOrEmpty(answer)) return "no answer text";
            var match = Regex.Match(answer, "\"error\":\"([^\"]{1,200})\"");
            if (match.Success) return match.Groups[1].Value;
            return answer.Length > 200 ? answer.Substring(0, 200) : answer;
        }

        private static string Cap(int value, int max)
            => (value < 0 ? 0 : value > max ? max : value).ToString(CultureInfo.InvariantCulture);

        private static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            }
            return text.Append('"').ToString();
        }
    }
}
