using MelonLoader;
using Polyfill.Contract;
using System.Text;

namespace Polyfill.Report
{
    /// <summary>
    /// Watch the session for exceptions and remember whose they were.
    /// </summary>
    /// <remarks>
    /// THIS IS THE HALF THE LOAD-TIME CHECK CANNOT DO. Resolving every name a mod asks for answers whether
    /// it will start. It says nothing about whether the thing works, and "starts but throws on every
    /// interaction" is the failure players actually meet. So the session is watched, and what it saw goes
    /// out with the report at the end.
    ///
    /// ATTRIBUTION IS BY NAMESPACE, which is honest about being approximate. Unity hands a stack trace as
    /// text with no assembly on it, so the owner is guessed from the type names in the frames against the
    /// roots the plugin wrote down while reading each assembly. A mod that throws from inside a game
    /// callback with none of its own frames on the stack is missed; one that shares a root namespace with
    /// another could be blamed for it. Both are why the site says how many players saw an error rather than
    /// asserting the mod is broken.
    ///
    /// WHAT IS NOT KEPT: the message. An exception message can carry a file path, a save name, a player
    /// name - anything the throwing code chose to put in it. Only the exception's type and the top frame
    /// leave the machine, both of which are code identifiers and neither of which is about the person.
    /// </remarks>
    internal static class Watch
    {
        /// <summary>One mod's trouble during one session.</summary>
        internal sealed class Trouble
        {
            internal string Mod;
            internal string Kind;        // the exception type, e.g. NullReferenceException
            internal string Frame;       // the topmost frame belonging to the mod
            internal int Count;
        }

        /// <summary>Enough to characterise a session; past this a loop is repeating itself.</summary>
        private const int DistinctCap = 40;
        private const int PerModCap = 8;
        private const int FrameLength = 120;

        private static readonly Dictionary<string, Trouble> Seen =
            new Dictionary<string, Trouble>(StringComparer.Ordinal);

        /// <summary>Root namespace to mod display name. Built once from the plugin's report.</summary>
        private static readonly List<KeyValuePair<string, string>> Owners =
            new List<KeyValuePair<string, string>>();

        private static bool _watching;
        private static DateTime _started;

        /// <summary>
        /// The converted delegate, held for as long as it is subscribed.
        /// </summary>
        /// <remarks>
        /// Two reasons it is a field. The managed side of an Il2Cpp delegate is collectable the moment
        /// nothing references it, and the native side keeps calling into what used to be there; and
        /// remove_logMessageReceived only unsubscribes an instance equal to the one that was added, so a
        /// second conversion would leave the first attached forever.
        /// </remarks>
        private static UnityEngine.Application.LogCallback _handler;

        /// <summary>How long this session has been running. Zero until the watch starts.</summary>
        internal static int Minutes =>
            _watching ? (int)Math.Max(0, (DateTime.UtcNow - _started).TotalMinutes) : 0;

        internal static IEnumerable<Trouble> Troubles => Seen.Values;

        /// <summary>
        /// Start watching, once, using the namespace map from the plugin's report.
        /// </summary>
        /// <remarks>
        /// A mod with no namespaces in the report simply cannot be blamed for anything, which is the right
        /// failure: silence is better than attributing somebody else's crash to them.
        /// </remarks>
        internal static void Begin(RunReport report, MelonLogger.Instance log)
        {
            if (_watching || report == null) return;

            foreach (var mod in report.Mods)
                foreach (string space in mod.Namespaces)
                    Owners.Add(new KeyValuePair<string, string>(space + ".", mod.Display));

            // Longest root first, so "BreedToSeed.Genetics" wins over a shorter root that is its prefix.
            Owners.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));

            if (Owners.Count == 0)
                log.Msg("[watch] no mod namespaces in the report, so only the errors MelonLoader "
                      + "attributes itself will be counted.");

            try
            {
                // THE SOURCE THAT ACTUALLY FIRES. MelonLoader wraps every melon callback in try/catch and
                // logs what it caught itself, so Unity never sees an unhandled exception from a mod and
                // logMessageReceived never fires for one. Measured: a mod throwing from OnUpdate produced
                // "[ERROR] [WatchProbe] System.NullReferenceException" in the log and nothing at all in
                // the watcher. This callback also carries the melon's own name, which beats guessing it
                // from a namespace.
                MelonLogger.ErrorCallbackHandler += OnMelonError;

                // Kept as well, for what MelonLoader does not catch: a throw from a coroutine or from an
                // event the game itself invokes never passes through a melon callback.
                _handler = new Action<string, string, UnityEngine.LogType>(OnLog);
                UnityEngine.Application.add_logMessageReceived(_handler);

                _watching = true;
                _started = DateTime.UtcNow;
                log.Msg($"[watch] watching this session for errors in {report.Mods.Count} mod(s).");
            }
            catch (Exception e)
            {
                // Not fatal: the load-time half of the report still goes out. Say so rather than leaving
                // an empty error list looking like a clean session.
                log.Warning("[watch] could not attach to the log, so this session reports no errors "
                          + "either way: " + e.Message);
            }
        }

        internal static void End()
        {
            if (!_watching) return;
            try { MelonLogger.ErrorCallbackHandler -= OnMelonError; } catch { }
            try
            {
                if (_handler != null) UnityEngine.Application.remove_logMessageReceived(_handler);
            }
            catch { }
            _handler = null;
            _watching = false;
        }

        /// <summary>
        /// One error MelonLoader caught and logged, with the melon it belongs to already named.
        /// </summary>
        /// <remarks>
        /// Only exceptions count. A mod that deliberately logs "could not reach the server" has not
        /// crashed, and counting that as a failed session would mark the mods that handle their own
        /// errors properly as the broken ones.
        /// </remarks>
        private static void OnMelonError(string melon, string text)
        {
            if (Seen.Count >= DistinctCap) return;
            if (string.IsNullOrEmpty(melon) || string.IsNullOrEmpty(text)) return;
            if (text.IndexOf("Exception", StringComparison.Ordinal) < 0) return;

            try { Remember(melon.Trim(), Kind(text), TopFrame(text)); }
            catch { }
        }

        /// <summary>The first stack line of a logged exception, which is where it was thrown.</summary>
        private static string TopFrame(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                string candidate = line.Trim();
                if (candidate.StartsWith("at ", StringComparison.Ordinal))
                    return Cut(candidate.Substring(3));
            }
            return "";
        }

        private static void OnLog(string message, string stackTrace, UnityEngine.LogType type)
        {
            if (type != UnityEngine.LogType.Exception) return;
            if (Seen.Count >= DistinctCap) return;

            try { Record(message, stackTrace); }
            catch { }                                    // a log handler that throws would spiral
        }

        private static void Record(string message, string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace)) return;

            string owner = null, frame = null;
            foreach (string line in stackTrace.Split('\n'))
            {
                string candidate = line.Trim();
                if (candidate.Length == 0) continue;

                foreach (var pair in Owners)
                {
                    if (!candidate.StartsWith(pair.Key, StringComparison.Ordinal)) continue;
                    owner = pair.Value;
                    frame = candidate;
                    break;
                }
                if (owner != null) break;               // topmost frame that belongs to somebody wins
            }

            if (owner == null) return;                  // the game's own, or a mod we cannot name

            Remember(owner, Kind(message), Cut(frame));
        }

        /// <summary>Book one error against one mod, deduplicated and capped.</summary>
        private static void Remember(string owner, string kind, string frame)
        {
            // A separator that cannot appear in a namespace, a type name or a frame.
            const char Unit = (char)31;
            string key = owner + Unit + kind + Unit + frame;

            if (Seen.TryGetValue(key, out var already)) { already.Count++; return; }

            int forThisMod = 0;
            foreach (var trouble in Seen.Values) if (trouble.Mod == owner) forThisMod++;
            if (forThisMod >= PerModCap) return;

            Seen[key] = new Trouble { Mod = owner, Kind = kind, Frame = frame, Count = 1 };
        }

        /// <summary>The exception's type name, and nothing else from the message.</summary>
        private static string Kind(string message)
        {
            if (string.IsNullOrEmpty(message)) return "Exception";

            int colon = message.IndexOf(':');
            string head = colon > 0 ? message.Substring(0, colon) : message;
            head = head.Trim();

            // A type name and nothing that looks like prose, so a message without a colon cannot leak.
            foreach (char c in head)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '+') return "Exception";

            return head.Length == 0 || head.Length > 80 ? "Exception" : head;
        }

        /// <summary>The frame, without the source location Unity appends and without arguments.</summary>
        private static string Cut(string frame)
        {
            if (string.IsNullOrEmpty(frame)) return "";

            int at = frame.IndexOf(" (at ", StringComparison.Ordinal);
            if (at > 0) frame = frame.Substring(0, at);

            int paren = frame.IndexOf(" (", StringComparison.Ordinal);
            if (paren > 0) frame = frame.Substring(0, paren);

            frame = frame.Trim();
            return frame.Length > FrameLength ? frame.Substring(0, FrameLength) : frame;
        }

        /// <summary>What to print for a player who types the console command.</summary>
        internal static string Describe()
        {
            if (!_watching && Seen.Count == 0) return "Not watching this session.";
            if (Seen.Count == 0) return $"No errors in {Minutes} minute(s) of play.";

            var text = new StringBuilder();
            text.Append(Seen.Count).Append(" distinct error(s) in ").Append(Minutes)
                .Append(" minute(s) of play:");
            foreach (var trouble in Seen.Values)
                text.Append('\n').Append("  ").Append(trouble.Mod).Append("  ").Append(trouble.Kind)
                    .Append("  ").Append(trouble.Frame).Append("  x").Append(trouble.Count);
            return text.ToString();
        }
    }
}
