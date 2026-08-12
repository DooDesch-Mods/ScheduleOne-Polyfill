using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Runs the mod fixes, and says what it ran.
    /// </summary>
    /// <remarks>
    /// On by default and never silent. A player is not asked to opt in, because a player does not know
    /// their mod is broken - the whole reason the mod is broken is that nothing told them. But nothing
    /// happens here that they cannot see in the log, list with `polyfillfixes`, and switch back off.
    /// </remarks>
    internal static class Fixes
    {
        internal sealed class Outcome
        {
            internal Fix Fix;
            internal string Mod;          // the version actually installed, or null when the mod is absent
            internal string State;        // applied | off | not installed | wrong version | did nothing
        }

        internal static readonly List<Outcome> Results = new();

        // The lookup goes first: it is the one that widens where a name is searched for, and the rename
        // pass below it should only ever have to deal with what is genuinely called something else now.
        private static readonly List<Fix> All = new()
        {
            new S1MapiPrefabLookup(),
            new S1MapiPrefabs(),
        };

        private static MelonPreferences_Entry<string> _disabled;

        internal static void Run(MelonLogger.Instance log)
        {
            ReadPreference();
            string game = GameVersion();

            foreach (var fix in All)
            {
                var outcome = new Outcome { Fix = fix, Mod = InstalledVersion(fix.Mod) };
                Results.Add(outcome);

                if (outcome.Mod == null) { outcome.State = "not installed"; continue; }
                if (IsOff(fix.Id)) { outcome.State = "off"; continue; }
                if (!fix.AppliesTo(outcome.Mod, game)) { outcome.State = "wrong version"; continue; }

                bool did;
                try { did = fix.Apply(log); }
                catch (Exception e)
                {
                    outcome.State = "failed: " + e.Message;
                    log.Warning($"[fix] {fix.Id} failed and changed nothing: {e.Message}");
                    continue;
                }

                outcome.State = did ? "applied" : "did nothing";
                if (did) log.Msg($"[fix] {fix.Id}: {fix.What}");
            }
        }

        /// <summary>
        /// What version of that mod is installed, or null when it is not.
        /// </summary>
        /// <remarks>
        /// Both shapes count, because both break the same way. A mod registers itself with MelonLoader and
        /// has a MelonInfo version. A library in UserLibs does not register at all - S1MAPI is one, and it
        /// is the thing carrying the stale prefab table - so it is found as a loaded assembly instead. The
        /// suffixed file name is checked too: S1MAPI ships as S1MAPI_Il2Cpp.dll.
        /// </remarks>
        private static string InstalledVersion(string name)
        {
            try
            {
                foreach (var melon in MelonBase.RegisteredMelons)
                    if (melon?.Info != null
                        && string.Equals(melon.Info.Name, name, StringComparison.OrdinalIgnoreCase))
                        return melon.Info.Version ?? "";
            }
            catch { }

            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string simple = assembly.GetName()?.Name;
                    if (simple == null) continue;
                    if (!string.Equals(simple, name, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(simple, name + "_Il2Cpp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    return assembly.GetName().Version?.ToString() ?? "";
                }
            }
            catch { }
            return null;
        }

        private static string GameVersion()
        {
            try { return UnityEngine.Application.version; } catch { return ""; }
        }

        internal static bool IsOff(string id)
        {
            string list = _disabled?.Value;
            if (string.IsNullOrEmpty(list)) return false;
            foreach (string one in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (string.Equals(one.Trim(), id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Switch one on or off. Takes effect on the next launch - a fix has already run.</summary>
        internal static void Set(string id, bool on)
        {
            ReadPreference();
            var kept = new List<string>();
            string list = _disabled.Value ?? "";
            foreach (string one in list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = one.Trim();
                if (trimmed.Length > 0 && !string.Equals(trimmed, id, StringComparison.OrdinalIgnoreCase))
                    kept.Add(trimmed);
            }
            if (!on) kept.Add(id);

            _disabled.Value = string.Join(",", kept);
            try { MelonPreferences.Save(); } catch { }
        }

        internal static bool Known(string id)
        {
            foreach (var fix in All)
                if (string.Equals(fix.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void ReadPreference()
        {
            if (_disabled != null) return;
            try
            {
                var category = MelonPreferences.GetCategory("Polyfill")
                               ?? MelonPreferences.CreateCategory("Polyfill");
                _disabled = category.GetEntry<string>("DisabledFixes")
                            ?? category.CreateEntry("DisabledFixes", "", "Mod fixes to leave alone",
                                "Comma separated ids of per-mod fixes that should not run. "
                                + "Type `polyfillfixes` in the console to see the ids.");
            }
            catch { }
        }
    }
}
