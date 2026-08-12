using System.Reflection;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Core
{
    /// <summary>
    /// The game's own rename history, asked in the game's own spelling.
    /// </summary>
    /// <remarks>
    /// Two things sit between the history and a mod's metadata, and both are translations rather than
    /// decisions:
    ///
    /// The history is written in the game's names - <c>ScheduleOne.NPCs.NPC</c> - and a mod's metadata is
    /// written in the interop spelling - <c>Il2CppScheduleOne.NPCs.NPC</c>, nested types with a slash.
    ///
    /// And a field on the game side is a PROPERTY on the interop side, so a mod asking for
    /// <c>get_Database</c> has to be answered out of a row about a field called <c>Database</c>. That is
    /// where 59 of the 79 renames live; without it the field half of the history would be unreachable.
    ///
    /// Nothing here decides anything. Whatever comes back is still checked against the live type by the
    /// caller before it is used - the history says what a build CALLED something, only the installed game
    /// says whether it is there.
    /// </remarks>
    internal static class AliasDb
    {
        private static VersionDb _db;
        private static GameVersion _game;

        internal static int Count => _db?.RenameCount ?? 0;

        /// <summary>
        /// Read every step file: the ones shipped inside this build, then any the player has put beside
        /// them.
        /// </summary>
        /// <remarks>
        /// The overlay is how a new game version can be bridged on the day it ships rather than on the day
        /// somebody cuts a release: the same file this build embeds, published on its own and dropped into
        /// UserData. It is not a back door - every row still has to survive the same checks, and every file
        /// loaded is named in the log with where it came from.
        ///
        /// Files are identified by their first line, not by their name. MSBuild mangles a resource path in
        /// ways that depend on where the file sits, and a database that silently fails to load is the exact
        /// failure this whole layer exists to prevent.
        /// </remarks>
        internal static void Load(MelonLogger.Instance log)
        {
            _game = GameVersionSource.Current;
            var files = new List<(string, IEnumerable<string>)>();

            files.AddRange(Embedded(log));
            files.AddRange(Loose(log));

            if (files.Count == 0)
            {
                log?.Msg("[alias] no version history is shipped with this build.");
                _db = VersionDb.Load(files);
                return;
            }

            _db = VersionDb.Load(files);
            foreach (string note in _db.Notes) log?.Warning("[alias] " + note);

            string gap = _db.Gap();
            if (gap != null)
                log?.Warning($"[alias] the history has a hole in it: {gap}. Renames across it are not followed.");

            string caption = _game.IsKnown && _db.Newest.IsKnown && _game < _db.Newest
                ? $"; this game is {_game}, so anything newer is skipped."
                : ".";
            log?.Msg($"[alias] {_db.RenameCount} rename(s) over {_db.StepCount} build(s) of the game, "
                   + $"{_db.Versions().First()} to {_db.Newest}{caption}");
        }

        private static IEnumerable<(string, IEnumerable<string>)> Embedded(MelonLogger.Instance log)
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                string[] lines = null;
                try
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    string text = reader.ReadToEnd();
                    if (!text.StartsWith(VersionDb.HeaderPrefix, StringComparison.Ordinal)) continue;
                    lines = text.Split('\n');
                }
                catch { }
                if (lines != null) yield return (Short(name), lines);
            }
        }

        /// <summary>Step files a player or a release asset has put in UserData. Same format, same checks.</summary>
        private static IEnumerable<(string, IEnumerable<string>)> Loose(MelonLogger.Instance log)
        {
            string folder;
            try
            {
                folder = Path.Combine(PolyfillPaths.Folder(MelonLoader.Utils.MelonEnvironment.UserDataDirectory),
                                      "versiondb");
            }
            catch { yield break; }
            if (!Directory.Exists(folder)) yield break;

            foreach (string file in Directory.GetFiles(folder, "*.txt", SearchOption.AllDirectories))
            {
                string[] lines = null;
                try { lines = File.ReadAllLines(file); } catch { }
                if (lines == null || lines.Length == 0) continue;

                log?.Msg($"[alias] reading {Path.GetFileName(file)} from UserData as well.");
                yield return (Path.GetFileName(file), lines);
            }
        }

        private static string Short(string resourceName)
        {
            int dot = resourceName.LastIndexOf(".txt", StringComparison.OrdinalIgnoreCase);
            if (dot < 0) return resourceName;
            int slash = resourceName.LastIndexOf('.', dot - 1);
            return slash < 0 ? resourceName : resourceName.Substring(slash + 1);
        }

        /// <summary>
        /// What this build calls <paramref name="wanted"/>, or null.
        /// </summary>
        /// <param name="interopType">Cecil's full name, interop spelling, nested types with '/'.</param>
        internal static string Successor(string interopType, string wanted, int parameters, string gameVersion)
        {
            if (_db == null || interopType == null || wanted == null) return null;

            var game = gameVersion == null ? _game : GameVersion.Parse(gameVersion);
            string type = Game(interopType);

            string stem = wanted, prefix = "";
            if (wanted.StartsWith("get_", StringComparison.Ordinal)
                || wanted.StartsWith("set_", StringComparison.Ordinal))
            { prefix = wanted.Substring(0, 4); stem = wanted.Substring(4); }

            if (prefix.Length == 0) return _db.Successor("M", type, stem, parameters, game);

            // An accessor can only come from something that HAS accessors, and the game side spells that as
            // either a field or a property. Two answers is no answer.
            string asProperty = _db.Successor("P", type, stem, 0, game);
            string asField = _db.Successor("F", type, stem, 0, game);
            if (asProperty != null && asField != null && asProperty != asField) return null;

            string answer = asProperty ?? asField;
            return answer == null ? null : prefix + answer;
        }

        /// <summary>Was this member removed rather than renamed, and when? For the report.</summary>
        internal static string RemovedIn(string interopType, string wanted, int parameters)
        {
            if (_db == null || interopType == null || wanted == null) return null;
            string type = Game(interopType);

            if (wanted.StartsWith("get_", StringComparison.Ordinal)
                || wanted.StartsWith("set_", StringComparison.Ordinal))
            {
                string stem = wanted.Substring(4);
                return _db.RemovedIn("P", type, stem, 0) ?? _db.RemovedIn("F", type, stem, 0);
            }
            return _db.RemovedIn("M", type, wanted, parameters);
        }

        /// <summary>
        /// The game's own name for a type the mod spells the interop way.
        /// </summary>
        /// <remarks>
        /// Two differences and nothing else: the generator prefixes every namespace with Il2Cpp, and Cecil
        /// writes a nested type as Outer/Inner where the source writes Outer.Inner.
        ///
        /// The leading dot matters. A type in the global namespace becomes <c>Il2Cpp.ActionList</c>, and
        /// taking six characters off the front leaves <c>.ActionList</c>, which matches nothing, forever.
        /// </remarks>
        internal static string Game(string interopType)
        {
            string name = interopType.Replace('/', '.');
            if (name.StartsWith("Il2Cpp", StringComparison.Ordinal)) name = name.Substring(6);
            return name.TrimStart('.');
        }
    }
}
