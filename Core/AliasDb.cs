using System.Reflection;

namespace Polyfill.Core
{
    /// <summary>
    /// What every member used to be called, chained across the game's own version history.
    /// </summary>
    /// <remarks>
    /// The index answers "is this name on the machine" and the name rules answer "did it keep its
    /// spelling". Neither can answer a rename that shares nothing: <c>Database</c> became
    /// <c>_currentDatabase</c>, and no amount of looking at the installed game connects those two.
    ///
    /// This can, because it was built from the game's own history. Between two ADJACENT builds, a member
    /// that disappeared and a member of the identical shape that appeared on the same type are the same
    /// member - the step is small enough for that to hold, which it would not be across a year. Those
    /// steps are chained from 0.4.4 forward, so a name from any of those builds resolves to what this one
    /// calls it. Built by Workspace/tools/gamediff/versiondb.ps1 out of the decompiled source archive; a
    /// pairing that was ambiguous at any step is dropped rather than guessed, and the chain with it.
    ///
    /// Two translations happen here and nowhere else. The database is written in the game's own names
    /// (<c>ScheduleOne.NPCs.NPC</c>) while everything Polyfill touches is the interop mirror of them
    /// (<c>Il2CppScheduleOne.NPCs.NPC</c>). And a plain field on the game is a PROPERTY on the interop
    /// side, so a mod asking for <c>get_Database</c> has to be answered out of a row that says
    /// <c>field Database</c>.
    /// </remarks>
    internal static class AliasDb
    {
        private sealed class Alias
        {
            internal string Kind;        // M, P, F, E
            internal string To;
            internal int Parameters;
            internal string At;          // the version the new name arrived in
        }

        /// <summary>type + "|" + old name, lower-cased kind bucket folded in by the lookup.</summary>
        private static Dictionary<string, List<Alias>> _byName;
        private static string _builtFor;
        private static int _count;

        internal static int Count => _count;
        internal static string BuiltFor => _builtFor;

        /// <summary>
        /// Read the shipped database. Missing or unreadable is not an error - it costs the aliases, not
        /// the rest of Polyfill.
        /// </summary>
        internal static void Load(MelonLoader.MelonLogger.Instance log)
        {
            if (_byName != null) return;
            _byName = new Dictionary<string, List<Alias>>(StringComparer.Ordinal);

            string json = null;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (!name.EndsWith("polyfill-aliases.json", StringComparison.OrdinalIgnoreCase)) continue;
                    using var stream = assembly.GetManifestResourceStream(name);
                    using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                    break;
                }
            }
            catch { }

            if (string.IsNullOrEmpty(json)) { log?.Msg("[alias] no version database is shipped with this build."); return; }

            try { Parse(json); }
            catch (Exception e) { log?.Warning("[alias] the version database could not be read: " + e.Message); return; }

            string game = Report.GameVersion();
            log?.Msg($"[alias] {_count} rename(s) from the game's own history, built for {_builtFor}"
                   + (SameOrNewer(game, _builtFor) ? "." : $"; this game is {game}, so anything newer is skipped."));
        }

        /// <summary>
        /// What this build calls <paramref name="wanted"/>, or null.
        /// </summary>
        /// <param name="interopType">Cecil's full name, interop spelling, nested types with '/'.</param>
        internal static string Successor(string interopType, string wanted, int parameters, string gameVersion)
        {
            if (_byName == null || _byName.Count == 0 || interopType == null || wanted == null) return null;

            string type = Game(interopType);
            string stem = wanted, prefix = "";
            if (wanted.StartsWith("get_", StringComparison.Ordinal)
                || wanted.StartsWith("set_", StringComparison.Ordinal))
            { prefix = wanted.Substring(0, 4); stem = wanted.Substring(4); }

            if (!_byName.TryGetValue(type + "|" + stem, out var candidates)) return null;

            string answer = null;
            foreach (var alias in candidates)
            {
                // A rename that has not happened on this build yet must not be handed out - the name it
                // points at does not exist here.
                if (!SameOrNewer(gameVersion, alias.At)) continue;

                // An accessor can only come from something that HAS accessors; a bare method name can only
                // come from a method, and then the parameter list has to line up.
                bool accessor = prefix.Length > 0;
                if (accessor && alias.Kind != "P" && alias.Kind != "F") continue;
                if (!accessor && alias.Kind != "M") continue;
                if (!accessor && alias.Parameters != parameters) continue;

                if (answer != null && answer != alias.To) return null;    // two answers is no answer
                answer = alias.To;
            }
            return answer == null ? null : prefix + answer;
        }

        /// <summary>The game's own name for an interop type: no Il2Cpp prefix, nested types with a dot.</summary>
        private static string Game(string interopType)
        {
            string name = interopType.Replace('/', '.');
            return name.StartsWith("Il2Cpp", StringComparison.Ordinal) ? name.Substring(6) : name;
        }

        private static void Parse(string json)
        {
            _builtFor = Between(json, "\"game\":", ",") ?? "";

            int at = 0;
            while (true)
            {
                int start = json.IndexOf("\"kind\":", at, StringComparison.Ordinal);
                if (start < 0) break;
                int end = json.IndexOf('}', start);
                if (end < 0) break;
                string row = json.Substring(start, end - start);
                at = end;

                string kind = Field(row, "kind"), type = Field(row, "type"), from = Field(row, "from");
                string to = Field(row, "to"), parameters = Field(row, "parameters"), landed = Field(row, "at");
                if (kind == null || type == null || from == null || to == null) continue;

                string key = type + "|" + from;
                if (!_byName.TryGetValue(key, out var list)) _byName[key] = list = new List<Alias>();
                list.Add(new Alias
                {
                    Kind = kind,
                    To = to,
                    At = landed ?? "",
                    Parameters = string.IsNullOrEmpty(parameters)
                        ? 0
                        : parameters.Split(',').Length,
                });
                _count++;
            }
        }

        private static string Field(string row, string name)
        {
            string tag = "\"" + name + "\":";
            int at = row.IndexOf(tag, StringComparison.Ordinal);
            if (at < 0) return null;
            int open = row.IndexOf('"', at + tag.Length);
            if (open < 0) return null;
            int close = row.IndexOf('"', open + 1);
            return close < 0 ? null : row.Substring(open + 1, close - open - 1);
        }

        private static string Between(string text, string tag, string end)
        {
            int at = text.IndexOf(tag, StringComparison.Ordinal);
            if (at < 0) return null;
            int open = text.IndexOf('"', at + tag.Length);
            if (open < 0) return null;
            int close = text.IndexOf('"', open + 1);
            return close < 0 ? null : text.Substring(open + 1, close - open - 1);
        }

        /// <summary>
        /// Is <paramref name="have"/> at least <paramref name="wanted"/>? Compared as numbers, because
        /// 0.4.6f11 is newer than 0.4.6f9 and a string comparison says the opposite.
        /// </summary>
        internal static bool SameOrNewer(string have, string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) return true;
            long a = Rank(have), b = Rank(wanted);
            return a == 0 || b == 0 || a >= b;          // unparsable either side: do not block on it
        }

        private static long Rank(string version)
        {
            if (string.IsNullOrEmpty(version)) return 0;
            long value = 0;
            int number = 0;
            bool any = false;
            int parts = 0;

            foreach (char c in version)
            {
                if (c >= '0' && c <= '9') { number = number * 100 + (c - '0'); any = true; continue; }
                if (!any) continue;
                value = value * 1000 + Math.Min(number, 999);
                number = 0; any = false; parts++;
                if (parts > 3) return value;
            }
            if (any) value = value * 1000 + Math.Min(number, 999);
            return value;
        }
    }
}
