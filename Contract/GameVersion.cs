namespace Polyfill.Contract
{
    /// <summary>
    /// A Schedule I build number, comparable.
    /// </summary>
    /// <remarks>
    /// The game numbers itself <c>0.4.6f12</c>: three parts, an f, and a build. String comparison gets that
    /// wrong in the obvious place (f9 sorts after f11), so both halves of Polyfill grew their own arithmetic
    /// and both were wrong in different ways.
    ///
    /// The one that mattered packed each run of digits into a single number, base 1000, four parts at most:
    /// <code>
    /// Rank("0.4.6f5") = 4006005      Rank("0.5.0") = 5000      5000 &gt;= 4006005 is false
    /// </code>
    /// Every row of the version database is gated on "is the installed game the same or newer", and every
    /// one of them carries a four-part version. The day the game ships <c>0.5.0</c> or <c>1.0</c> with no f
    /// suffix, that comparison says no to all of them and the whole database goes dark without a word. The
    /// same packing also clamps a part at 999 and stops after four, so <c>f100</c> and <c>f999</c> compare
    /// equal.
    ///
    /// So: no packing. The parts are kept as parts and compared as parts.
    /// </remarks>
    internal readonly struct GameVersion : IComparable<GameVersion>, IEquatable<GameVersion>
    {
        /// <summary>Every run of digits in the string, in order. Non-digits are separators, whatever they are.</summary>
        internal readonly int[] Parts;

        /// <summary>What was parsed, kept for messages: a version nobody can order is still a version
        /// somebody has to be told about.</summary>
        internal readonly string Raw;

        internal bool IsKnown => Parts != null && Parts.Length > 0;

        private GameVersion(int[] parts, string raw) { Parts = parts; Raw = raw; }

        internal static readonly GameVersion Unknown = new(null, "");

        /// <summary>
        /// Read a version out of whatever the game or a mod calls itself.
        /// </summary>
        /// <remarks>
        /// Deliberately the same scan the old code did - runs of digits, everything else is a separator -
        /// because that is what makes <c>0.4.6f12</c>, <c>0.4.6</c>, <c>1.0</c> and <c>2.0.10</c> all work
        /// without a table of formats. What is new is that nothing is packed, capped or truncated.
        /// </remarks>
        internal static bool TryParse(string text, out GameVersion version)
        {
            version = Unknown;
            if (string.IsNullOrEmpty(text)) return false;

            var parts = new List<int>(4);
            long number = 0;
            bool inNumber = false;
            foreach (char c in text)
            {
                if (c >= '0' && c <= '9')
                {
                    // Absurd input stops growing rather than overflowing into a negative version.
                    if (number < int.MaxValue / 10) number = number * 10 + (c - '0');
                    inNumber = true;
                    continue;
                }
                if (inNumber) { parts.Add((int)number); number = 0; inNumber = false; }
            }
            if (inNumber) parts.Add((int)number);

            if (parts.Count == 0) return false;
            version = new GameVersion(parts.ToArray(), text);
            return true;
        }

        /// <summary>Unknown when it cannot be read. The caller decides what that means; see VersionRange.</summary>
        internal static GameVersion Parse(string text)
            => TryParse(text, out var version) ? version : Unknown;

        /// <summary>
        /// Older is less. A shorter version is older than a longer one that starts the same way.
        /// </summary>
        /// <remarks>
        /// <c>0.4.6</c> before <c>0.4.6f1</c> is the deliberate call: an f-build is a build ON that version,
        /// so a hypothetical suffix-less one came first. Comparing an unknown version is meaningless and the
        /// callers must not do it - VersionRange is where the two possible policies live.
        /// </remarks>
        public int CompareTo(GameVersion other)
        {
            var mine = Parts ?? Array.Empty<int>();
            var theirs = other.Parts ?? Array.Empty<int>();

            int shared = Math.Min(mine.Length, theirs.Length);
            for (int i = 0; i < shared; i++)
                if (mine[i] != theirs[i]) return mine[i] < theirs[i] ? -1 : 1;

            return mine.Length.CompareTo(theirs.Length);
        }

        /// <summary>Do the first <paramref name="count"/> parts match? What a trailing <c>*</c> means.</summary>
        internal bool StartsWith(GameVersion prefix, int count)
        {
            if (Parts == null || prefix.Parts == null) return false;
            if (count > prefix.Parts.Length || count > Parts.Length) return false;
            for (int i = 0; i < count; i++)
                if (Parts[i] != prefix.Parts[i]) return false;
            return true;
        }

        public bool Equals(GameVersion other) => CompareTo(other) == 0 && IsKnown == other.IsKnown;

        public override bool Equals(object obj) => obj is GameVersion other && Equals(other);

        public override int GetHashCode()
        {
            int hash = 17;
            if (Parts != null) foreach (int part in Parts) hash = hash * 31 + part;
            return hash;
        }

        public override string ToString() => IsKnown ? Raw : "unknown";

        public static bool operator <(GameVersion a, GameVersion b) => a.CompareTo(b) < 0;
        public static bool operator >(GameVersion a, GameVersion b) => a.CompareTo(b) > 0;
        public static bool operator <=(GameVersion a, GameVersion b) => a.CompareTo(b) <= 0;
        public static bool operator >=(GameVersion a, GameVersion b) => a.CompareTo(b) >= 0;
        public static bool operator ==(GameVersion a, GameVersion b) => a.Equals(b);
        public static bool operator !=(GameVersion a, GameVersion b) => !a.Equals(b);
    }
}
