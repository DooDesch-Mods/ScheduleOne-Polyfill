namespace Polyfill.Contract
{
    /// <summary>
    /// What the game called things on every build since 0.4.4, one file per release step.
    /// </summary>
    /// <remarks>
    /// The old shape of this was a single composed table: for every renamed member, the name it has TODAY.
    /// That works and it has one structural problem - the target build is baked into every row, so adding
    /// one game version rewrites the whole file. Every release was a whole-file diff nobody could review, a
    /// curated row could not live in it because the next run would blow it away, and where a row came from
    /// was unanswerable.
    ///
    /// So the file is the step, and the chain is walked at load. A new game version adds ONE file and
    /// changes none, which is the property the whole thing exists for. It also means the chain is walked to
    /// the version actually installed: a player on 0.4.6f9 is never handed a name that arrived in f12.
    ///
    /// Line-oriented and pipe-separated, like everything else here. Ten columns, positional, append-only:
    /// a later format may add an eleventh and this reader will ignore it rather than break.
    ///
    /// <code>
    /// op | kind | type | from | to | arity | origin | rule | confidence | note
    /// R|F|ScheduleOne.Economy.Supplier|debt|_debt|0|derived|name-underscore|high|
    /// -|F|ScheduleOne.NPCs.NPC|ID||0|derived|removed|high|
    /// ?|F|ScheduleOne.NPCs.NPCMovement|MovementSpeedScale||0|derived|ambiguous|high|1 lost, 1 gained
    /// </code>
    ///
    /// Removals and ambiguities are shipped rather than thrown away. They cost what they always cost to
    /// generate, and they turn "nothing on this type to point at" into "removed in 0.4.6f5" or "there was a
    /// candidate and it was refused because three members of that shape moved at once".
    /// </remarks>
    internal sealed class VersionDb
    {
        internal const string HeaderPrefix = "# polyfill-versiondb ";
        internal const int Format = 1;

        internal static class Op
        {
            internal const string Rename = "R";
            internal const string Removed = "-";
            internal const string Ambiguous = "?";
            /// <summary>Two old names became one new one. Never resolved, only reported.</summary>
            internal const string Merge = "M";
        }

        internal static class Origin
        {
            /// <summary>Produced by the differ from the game's own history. Beats nothing.</summary>
            internal const string Derived = "derived";
            /// <summary>A person read both builds and wrote the row. Beats derived, and beats a refusal.</summary>
            internal const string Curated = "curated";
            /// <summary>A person deliberately contradicts the derived data. Beats everything, and is
            /// refused without a note - the cheapest enforceable version of signing your name to it.</summary>
            internal const string Override = "override";
        }

        internal sealed class Row
        {
            internal string Op, Kind, Type, From, To, RuleName, Confidence, Note, Origin;
            internal int Arity;
            internal string Key => Kind + "|" + Type + "|" + From + "|" + Arity;
            /// <summary>The same identity without the arity, for the ambiguity and merge sets: a name that
            /// could not be resolved is unusable at every arity.</summary>
            internal string NameKey => Kind + "|" + Type + "|" + From;
        }

        internal sealed class Step
        {
            internal GameVersion From, To;
            internal string Source = "";
            internal readonly Dictionary<string, Row> Renames = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, Row> Removed = new(StringComparer.Ordinal);
            internal readonly HashSet<string> Refused = new(StringComparer.Ordinal);
        }

        private readonly List<Step> _steps = new();
        private readonly Dictionary<string, Row> _overrides = new(StringComparer.Ordinal);
        private readonly List<VersionRange> _overrideRanges = new();

        internal readonly List<string> Notes = new();
        internal int RenameCount { get; private set; }
        internal int StepCount => _steps.Count;

        /// <summary>The newest build any step ends on: what this database was built to know about.</summary>
        internal GameVersion Newest => _steps.Count == 0 ? GameVersion.Unknown : _steps[^1].To;

        /// <summary>
        /// Read every file handed in. A file that cannot be used is named and skipped; the rest still load.
        /// </summary>
        /// <remarks>
        /// Later files with the same step replace earlier ones WHOLE, never line by line. That is what makes
        /// an overlay dropped into UserData a reviewable thing: a half-merged step cannot be told apart from
        /// an incomplete one.
        /// </remarks>
        internal static VersionDb Load(IEnumerable<(string Name, IEnumerable<string> Lines)> files)
        {
            var db = new VersionDb();
            var steps = new Dictionary<string, Step>(StringComparer.Ordinal);

            foreach (var file in files)
            {
                try { db.ReadOne(file.Name, file.Lines, steps); }
                catch (Exception e) { db.Notes.Add($"{file.Name} could not be read ({e.Message})"); }
            }

            db._steps.AddRange(steps.Values);
            db._steps.Sort((a, b) => a.From.CompareTo(b.From));
            foreach (var step in db._steps) db.RenameCount += step.Renames.Count;
            return db;
        }

        private void ReadOne(string name, IEnumerable<string> lines, Dictionary<string, Step> steps)
        {
            Step step = null;
            VersionRange applies = null;
            bool header = false;
            var renames = new List<Row>();

            foreach (string raw in lines)
            {
                if (raw == null) continue;
                string line = raw.TrimEnd((char)13);

                if (!header)
                {
                    if (!line.StartsWith(HeaderPrefix, StringComparison.Ordinal))
                    { Notes.Add($"{name} is not a version database"); return; }
                    if (!int.TryParse(line.Substring(HeaderPrefix.Length).Trim(), out int format)
                        || format > Format)
                    { Notes.Add($"{name} is a newer format than this build reads; skipped"); return; }
                    header = true;
                    continue;
                }

                if (line.StartsWith("# from=", StringComparison.Ordinal))
                { (step ??= new Step()).From = GameVersion.Parse(line.Substring(7)); continue; }
                if (line.StartsWith("# to=", StringComparison.Ordinal))
                { (step ??= new Step()).To = GameVersion.Parse(line.Substring(5)); continue; }
                if (line.StartsWith("# source=", StringComparison.Ordinal))
                { (step ??= new Step()).Source = line.Substring(9); continue; }
                if (line.StartsWith("# applies=", StringComparison.Ordinal))
                {
                    if (!VersionRange.TryParse(line.Substring(10), out applies, out string problem))
                    { Notes.Add($"{name} applies to '{line.Substring(10)}', which is not a range ({problem})"); return; }
                    continue;
                }
                if (line.Length == 0 || line[0] == '#') continue;

                var row = Parse(line);
                if (row == null) continue;

                if (applies != null) { TakeOverride(name, row, applies); continue; }
                if (step == null) { Notes.Add($"{name} has rows but no step"); return; }

                switch (row.Op)
                {
                    case Op.Rename: renames.Add(row); break;
                    case Op.Removed: step.Removed[row.Key] = row; break;
                    case Op.Ambiguous:
                    case Op.Merge: step.Refused.Add(row.NameKey); break;
                }
            }

            if (applies != null) return;                      // an override file, already taken
            if (step == null || !step.From.IsKnown || !step.To.IsKnown)
            { Notes.Add($"{name} does not say which two builds it is between"); return; }

            // Two old names becoming one new one, or one old name with two answers. Neither is resolvable
            // and neither is guessed at: both sides are refused and the report says why. Checked here and
            // not only in the generator, because an overlay file was not written by us.
            var byTarget = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (var row in renames)
            {
                if (step.Renames.TryGetValue(row.Key, out var clash))
                {
                    step.Refused.Add(row.NameKey);
                    step.Renames.Remove(row.Key);
                    Notes.Add($"{name}: {row.Type}.{row.From} is renamed twice in one step "
                            + $"({clash.To} and {row.To}); neither is used");
                    continue;
                }
                string target = row.Kind + "|" + row.Type + "|" + row.To + "|" + row.Arity;
                if (byTarget.TryGetValue(target, out var merged))
                {
                    step.Refused.Add(row.NameKey);
                    step.Refused.Add(merged.NameKey);
                    step.Renames.Remove(merged.Key);
                    Notes.Add($"{name}: {merged.From} and {row.From} both became {row.To} on {row.Type}; "
                            + "neither is used");
                    continue;
                }
                byTarget[target] = row;
                step.Renames[row.Key] = row;
            }

            // Same pair of builds twice: the later file wins outright.
            string id = step.From + "->" + step.To;
            if (steps.ContainsKey(id)) Notes.Add($"{name} replaces the step {id} that was loaded before it");
            steps[id] = step;
        }

        private void TakeOverride(string name, Row row, VersionRange applies)
        {
            if (row.Origin == Origin.Override && string.IsNullOrEmpty(row.Note))
            {
                Notes.Add($"{name}: {row.Type}.{row.From} overrides the history with no reason given; skipped");
                return;
            }
            if (_overrides.ContainsKey(row.Key))
            {
                Notes.Add($"{name}: {row.Type}.{row.From} is decided twice by hand; neither is used");
                _overrides.Remove(row.Key);
                return;
            }
            _overrides[row.Key] = row;
            _overrideRanges.Add(applies);
        }

        private static Row Parse(string line)
        {
            string[] p = line.Split('|');
            if (p.Length < 10) return null;
            return new Row
            {
                Op = p[0], Kind = p[1], Type = p[2], From = p[3], To = p[4],
                Arity = int.TryParse(p[5], out int arity) ? arity : 0,
                Origin = p[6], RuleName = p[7], Confidence = p[8], Note = p[9],
            };
        }

        /// <summary>
        /// What this build calls <paramref name="name"/>, or null when nothing here can say.
        /// </summary>
        /// <remarks>
        /// Walked oldest step first and stopped at the installed build, so the answer is a name that exists
        /// HERE rather than on whatever version this database was built for. A name that any step could not
        /// resolve poisons everything hanging off it - the middle name would be a guess and every later step
        /// inherits the guess.
        /// </remarks>
        internal string Successor(string kind, string type, string name, int arity, GameVersion game)
        {
            string key = kind + "|" + type + "|" + name + "|" + arity;
            if (_overrides.TryGetValue(key, out var hand)) return hand.To;

            string current = name;

            foreach (var step in _steps)
            {
                // A step that lands on a build newer than the installed one has not happened here.
                if (game.IsKnown && step.To > game) break;

                string here = kind + "|" + type + "|" + current;
                if (step.Refused.Contains(here)) return null;

                if (step.Renames.TryGetValue(here + "|" + arity, out var row)) current = row.To;
            }

            // Renamed and renamed back is not a rename. Comparing the ends rather than counting the steps
            // also means a detour through a name that no longer exists never reaches a caller.
            return current == name ? null : current;
        }

        /// <summary>The build a member was last seen on, when the history says it was removed rather than
        /// renamed. For the report: "removed in 0.4.6f5" beats "nothing on this type to point at".</summary>
        internal string RemovedIn(string kind, string type, string name, int arity)
        {
            string key = kind + "|" + type + "|" + name + "|" + arity;
            foreach (var step in _steps)
                if (step.Removed.ContainsKey(key)) return step.To.ToString();
            return null;
        }

        /// <summary>Was this name one the history looked at and refused to resolve?</summary>
        internal bool WasRefused(string kind, string type, string name)
        {
            string key = kind + "|" + type + "|" + name;
            foreach (var step in _steps) if (step.Refused.Contains(key)) return true;
            return false;
        }

        /// <summary>Every build the chain names, oldest first. The gaps in it are the thing worth checking.</summary>
        internal IEnumerable<string> Versions()
        {
            foreach (var step in _steps) yield return step.From.ToString();
            if (_steps.Count > 0) yield return _steps[^1].To.ToString();
        }

        /// <summary>Null when the chain is unbroken, otherwise the first gap in it. A missing step means
        /// every rename before it silently stops being followed.</summary>
        internal string Gap()
        {
            for (int i = 1; i < _steps.Count; i++)
                if (_steps[i - 1].To != _steps[i].From)
                    return $"{_steps[i - 1].To} is followed by a step that starts at {_steps[i].From}";
            return null;
        }
    }
}
