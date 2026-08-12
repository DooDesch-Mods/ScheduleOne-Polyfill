using System.Reflection;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Point S1MAPI's prefab names at what the game calls those prefabs now.
    /// </summary>
    /// <remarks>
    /// S1MAPI hands every mod built on it a table of prefabs by name (<c>S1MAPI.S1.Prefabs</c>, 69 of them),
    /// and each one is looked up by an EXACT string against FishNet's spawnable list. An update renames
    /// entries in that list - 0.4.6 puts a "_Built" suffix on the placeable furniture - and every name that
    /// moved silently returns null. The mod then spawns nothing and says so in the Unity log, where nobody
    /// is looking.
    ///
    /// This is a mod fix rather than a rule because the two sides are strings on opposite sides of a runtime
    /// registry, with no metadata linking them. What keeps it honest is the same test the rest of Polyfill
    /// uses: a name is only rewritten when EXACTLY ONE prefab on this machine matches once case, spaces and
    /// the suffix are taken out. Two candidates is an ambiguity, and an ambiguity is reported, never
    /// resolved - spawning the wrong object puts it in the player's save.
    ///
    /// What it will not do is substitute something that merely looks close. A name with no unique match is
    /// reported and left alone - and most of those turn out not to be renames at all: see
    /// <see cref="S1MapiPrefabLookup"/>, which runs first and finds them under their own names outside the
    /// spawnable list. By the time this pass runs, what is left is genuinely called something else now.
    /// </remarks>
    internal sealed class S1MapiPrefabs : Fix
    {
        internal override string Id => "s1mapi-prefabs";
        internal override string Mod => "S1MAPI";
        internal override string ModVersions => "*";
        internal override string GameVersions => "0.4.6*";
        internal override string What => "prefab names that the game renamed now point at the new name";

        internal override string StandsDownBecause
            => "Prefabs the game renamed are no longer followed, so a mod placing furniture may find nothing.";

        internal override bool Apply(MelonLogger.Instance log)
        {
            var live = Report.PrefabLookup.Names();
            if (live == null || live.Count == 0)
            {
                log.Msg("[fix] s1mapi-prefabs: the spawnable list is not up yet; nothing checked.");
                return false;
            }

            var table = FindTable();
            if (table == null) { log.Warning("[fix] s1mapi-prefabs: S1MAPI.S1.Prefabs is not where it was."); return false; }

            var byShape = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string name in live)
            {
                string shape = Shape(name);
                if (!byShape.TryGetValue(shape, out var list)) byShape[shape] = list = new List<string>();
                list.Add(name);
            }

            int repaired = 0;
            var gone = new List<string>();
            var ambiguous = new List<string>();

            foreach (var field in table.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType.Name != "PrefabRef") continue;

                object reference = field.GetValue(null);
                string wanted = NameOf(reference);
                if (reference == null || string.IsNullOrEmpty(wanted)) continue;
                if (live.Contains(wanted)) continue;                     // still there under its own name

                if (!byShape.TryGetValue(Shape(wanted), out var candidates)) { gone.Add(wanted); continue; }
                if (candidates.Count != 1) { ambiguous.Add($"{wanted} ({candidates.Count} candidates)"); continue; }

                if (!Rename(reference, candidates[0])) { gone.Add(wanted); continue; }
                log.Msg($"[fix]   {wanted} -> {candidates[0]}");
                repaired++;
            }

            if (gone.Count > 0)
            {
                log.Warning($"[fix] s1mapi-prefabs: {gone.Count} prefab(s) this game build does not have at "
                          + "all, so anything spawning them gets nothing:");
                foreach (string one in gone) log.Warning("[fix]   " + one);
            }
            foreach (string one in ambiguous)
                log.Warning($"[fix] s1mapi-prefabs: {one} - more than one could be meant, so none was chosen.");

            return repaired > 0;
        }

        /// <summary>The name with everything a rename touches taken out: case, spaces, and the suffix
        /// 0.4.6 put on the placeable furniture.</summary>
        private static string Shape(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));

            string flat = builder.ToString();
            if (flat.EndsWith("built", StringComparison.Ordinal)) flat = flat.Substring(0, flat.Length - 5);
            return flat;
        }

        private static Type FindTable()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = null;
                try { found = assembly.GetType("S1MAPI.S1.Prefabs", false); } catch { }
                if (found != null) return found;
            }
            return null;
        }

        private static string NameOf(object reference)
        {
            try { return reference?.GetType().GetProperty("Name")?.GetValue(reference) as string; }
            catch { return null; }
        }

        /// <summary>
        /// Write the new name into the PrefabRef the table already handed out.
        /// </summary>
        /// <remarks>
        /// The field is <c>static readonly</c> and every mod has already read it, so replacing the object
        /// would leave each of them holding the old one. The name goes into the instance instead, through
        /// the property's backing field - which is why this looks for that field rather than a setter.
        /// </remarks>
        private static bool Rename(object reference, string name)
        {
            var type = reference.GetType();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public
                                               | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(string)) continue;
                if (field.Name != "<Name>k__BackingField" && field.Name != "Name") continue;
                try { field.SetValue(reference, name); return true; } catch { return false; }
            }
            return false;
        }
    }
}
