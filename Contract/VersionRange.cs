using System.Text;

namespace Polyfill.Contract
{
    /// <summary>
    /// Which builds something applies to.
    /// </summary>
    /// <remarks>
    /// Grammar, comma separated, any term matching is a match:
    /// <code>
    /// *                        everything, an unknown version included
    /// 0.4.6*                   any build whose parts start 0, 4, 6
    /// &gt;=0.4.6   &gt;0.4.6         and the two other way round
    /// 0.4.5f2..0.4.6f12        inclusive at both ends
    /// 0.4.6f12                 exactly that, part for part
    /// </code>
    ///
    /// A prefix is a PARTS prefix, not a string prefix. The old spelling matched
    /// <c>"0.4.6*"</c> against the text, which let <c>0.4.65f1</c> through and is the kind of thing nobody
    /// finds until the game ships a two-digit patch number.
    ///
    /// The two ways to answer for a version nobody could parse are both correct, in different places, so
    /// they are two named methods rather than a flag:
    ///
    /// <see cref="Allows"/> says no. That is for a per-mod fix, which has no second check: it simply runs,
    /// and running one that was never tried on this build gets the MOD's author a bug report.
    ///
    /// <see cref="AllowsOrUnknown"/> says yes. That is for the version database, where the real gate is a
    /// different one - a rename is only ever used if the new name is actually on the live type - so a row
    /// let through by mistake cannot fire.
    /// </remarks>
    internal sealed class VersionRange
    {
        private enum Kind { Any, Prefix, AtLeast, Above, AtMost, Below, Between, Exact }

        private readonly struct Term
        {
            internal readonly Kind Kind;
            internal readonly GameVersion Low, High;
            internal readonly int PrefixParts;

            internal Term(Kind kind, GameVersion low, GameVersion high = default, int prefixParts = 0)
            { Kind = kind; Low = low; High = high; PrefixParts = prefixParts; }
        }

        private readonly Term[] _terms;

        /// <summary>The text it was built from, for a log line that has to name what it refused.</summary>
        internal string Text { get; }

        internal static readonly VersionRange Any = new(new[] { new Term(Kind.Any, GameVersion.Unknown) }, "*");

        /// <summary>Matches nothing. What a range that could not be read falls back to, so a typo costs one
        /// module rather than letting it run on builds nobody checked.</summary>
        internal static readonly VersionRange None = new(Array.Empty<Term>(), "nothing");

        private VersionRange(Term[] terms, string text) { _terms = terms; Text = text; }

        /// <summary>
        /// Build one, or throw.
        /// </summary>
        /// <remarks>
        /// A term nobody can parse is a mistake in Polyfill's own source, and the old matcher answered it
        /// with a silent "no term matched" - which reads exactly like "this fix is for another build" and
        /// hides the typo forever. Loud is the only useful answer.
        /// </remarks>
        internal static VersionRange Parse(string text)
        {
            if (!TryParse(text, out var range, out string problem))
                throw new FormatException($"'{text}' is not a version range: {problem}");
            return range;
        }

        internal static bool TryParse(string text, out VersionRange range, out string problem)
        {
            range = null;
            problem = null;

            if (string.IsNullOrWhiteSpace(text)) { range = Any; return true; }

            var terms = new List<Term>();
            foreach (string piece in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string term = piece.Trim();
                if (term.Length == 0) continue;

                if (term == "*") { terms.Add(new Term(Kind.Any, GameVersion.Unknown)); continue; }

                if (term.EndsWith("*", StringComparison.Ordinal))
                {
                    string head = term.Substring(0, term.Length - 1).TrimEnd('.', 'f', ' ');
                    if (!GameVersion.TryParse(head, out var prefix))
                    { problem = $"'{term}' has no version in front of the star"; return false; }
                    terms.Add(new Term(Kind.Prefix, prefix, default, prefix.Parts.Length));
                    continue;
                }

                int dots = term.IndexOf("..", StringComparison.Ordinal);
                if (dots > 0)
                {
                    if (!GameVersion.TryParse(term.Substring(0, dots), out var low)
                        || !GameVersion.TryParse(term.Substring(dots + 2), out var high))
                    { problem = $"'{term}' is not two versions with .. between them"; return false; }
                    if (low > high)
                    { problem = $"'{term}' starts after it ends"; return false; }
                    terms.Add(new Term(Kind.Between, low, high));
                    continue;
                }

                Kind kind = Kind.Exact;
                string rest = term;
                if (rest.StartsWith(">=", StringComparison.Ordinal)) { kind = Kind.AtLeast; rest = rest.Substring(2); }
                else if (rest.StartsWith("<=", StringComparison.Ordinal)) { kind = Kind.AtMost; rest = rest.Substring(2); }
                else if (rest.StartsWith(">", StringComparison.Ordinal)) { kind = Kind.Above; rest = rest.Substring(1); }
                else if (rest.StartsWith("<", StringComparison.Ordinal)) { kind = Kind.Below; rest = rest.Substring(1); }

                if (!GameVersion.TryParse(rest.Trim(), out var bound))
                { problem = $"'{term}' has no version in it"; return false; }
                terms.Add(new Term(kind, bound));
            }

            if (terms.Count == 0) { problem = "it is empty"; return false; }
            range = new VersionRange(terms.ToArray(), text);
            return true;
        }

        /// <summary>Does this build fall in the range? An unknown build does not - see the class remarks.</summary>
        internal bool Allows(GameVersion version) => Matches(version, unknownAnswer: false);

        /// <summary>The same question where being unable to answer must not block. Only for gates that have
        /// a second, harder check behind them.</summary>
        internal bool AllowsOrUnknown(GameVersion version) => Matches(version, unknownAnswer: true);

        internal bool Allows(string version) => Allows(GameVersion.Parse(version));

        internal bool AllowsOrUnknown(string version) => AllowsOrUnknown(GameVersion.Parse(version));

        private bool Matches(GameVersion version, bool unknownAnswer)
        {
            foreach (var term in _terms)
            {
                // "Everything" means everything. A range written as * is somebody saying they do not care
                // which build this is, and an unparsable one is still not a build they care about.
                if (term.Kind == Kind.Any) return true;
                if (!version.IsKnown) return unknownAnswer;

                bool hit = term.Kind switch
                {
                    Kind.Prefix => version.StartsWith(term.Low, term.PrefixParts),
                    Kind.AtLeast => version >= term.Low,
                    Kind.Above => version > term.Low,
                    Kind.AtMost => version <= term.Low,
                    Kind.Below => version < term.Low,
                    Kind.Between => version >= term.Low && version <= term.High,
                    _ => version == term.Low,
                };
                if (hit) return true;
            }
            return false;
        }

        public override string ToString() => Text;

        /// <summary>Every version named anywhere in the range, so a test can check that they exist.</summary>
        internal IEnumerable<GameVersion> Bounds()
        {
            foreach (var term in _terms)
            {
                if (term.Kind == Kind.Any) continue;
                if (term.Low.IsKnown) yield return term.Low;
                if (term.Kind == Kind.Between && term.High.IsKnown) yield return term.High;
            }
        }

        /// <summary>One line for the report: what a player or a mod author reads next to a stood-down repair.</summary>
        internal string Describe()
        {
            var text = new StringBuilder();
            foreach (var term in _terms)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(term.Kind switch
                {
                    Kind.Any => "any build",
                    Kind.Prefix => term.Low + " and its builds",
                    Kind.AtLeast => term.Low + " and newer",
                    Kind.Above => "newer than " + term.Low,
                    Kind.AtMost => term.Low + " and older",
                    Kind.Below => "older than " + term.Low,
                    Kind.Between => term.Low + " to " + term.High,
                    _ => term.Low.ToString(),
                });
            }
            return text.ToString();
        }
    }
}
