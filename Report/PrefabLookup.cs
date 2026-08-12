using Il2CppFishNet;
using Il2CppFishNet.Managing.Object;
using UnityEngine;

namespace Polyfill.Report
{
    /// <summary>
    /// What the game's spawnable prefab list is called TODAY.
    /// </summary>
    /// <remarks>
    /// A mod does not only ask for types and members. It also asks for prefabs, by name, out of FishNet's
    /// spawnable registry - and an update renames those too. When it happens the mod gets null and usually
    /// says nothing a player would see: OverTheCounter's two missing doors were only in the Unity log, which
    /// nobody reads, under a line that says a name was not found and not which names exist.
    ///
    /// This is deliberately a REPORT and not a repair, and the line is not squeamishness:
    ///
    /// For a type or a member, both sides are in metadata. Cecil reads what the mod asks for, the index reads
    /// what the game has, and a repair only happens when exactly one candidate matches - no inference at all.
    /// For a prefab there is no such link. The old name is a string constant inside the mod and the new one
    /// is a string on an asset; nothing connects them but their spelling. Choosing on spelling alone would
    /// mean spawning a guessed object into the player's world and their save, which is the same trade the
    /// project already refuses for reordered enum values: a wrong repair runs, a missing one does not.
    ///
    /// So this answers the question instead of pre-empting it. The registry only exists once FishNet is up,
    /// which is why it lives in the mod rather than the plugin.
    /// </remarks>
    internal static class PrefabLookup
    {
        /// <summary>Every spawnable prefab name, or null when the registry is not up yet.</summary>
        internal static List<string> Names()
        {
            try
            {
                var manager = InstanceFinder.NetworkManager;
                if (manager == null) return null;

                var objects = manager.GetPrefabObjects<PrefabObjects>(0, false);
                if (objects == null) return null;

                var names = new List<string>();
                int count = objects.GetObjectCount();
                for (int i = 0; i < count; i++)
                {
                    var networkObject = objects.GetObject(true, i);
                    var gameObject = networkObject == null ? null : networkObject.gameObject;
                    if (gameObject != null) names.Add(gameObject.name);
                }
                return names;
            }
            catch { return null; }
        }

        /// <summary>
        /// Report on one name: is it there, and if not, what is close.
        /// </summary>
        /// <remarks>
        /// The near matches are ordered by how little was changed, not scored into a verdict. Casing and
        /// spacing first, because those are the renames that happen, then anything sharing a word. The
        /// caller reads the list; nothing here decides.
        /// </remarks>
        internal static void Explain(string wanted)
        {
            var names = Names();
            if (names == null)
            {
                Core.Log.Warning("the prefab list is not up yet - load a save first.");
                return;
            }

            if (string.IsNullOrWhiteSpace(wanted))
            {
                Core.Log.Msg($"{names.Count} spawnable prefab(s) registered. "
                           + "Name one to check it: `polyfillprefab Basic Metal Glass Door`");
                return;
            }

            wanted = wanted.Trim();
            foreach (string name in names)
                if (name == wanted) { Core.Log.Msg($"'{wanted}' is registered - the game still has it."); return; }

            // Collection 0 and no more, because that is the list a mod's own lookup reads. Saying "the game
            // does not have it" off a wider search would be a different sentence than the one that matters.
            Core.Log.Warning($"'{wanted}' is not among the {names.Count} spawnable prefabs a mod can ask for.");

            var close = new List<string>();
            foreach (string name in names)
                if (Squashed(name) == Squashed(wanted)) close.Add(name + "   [only casing or spacing]");

            if (close.Count == 0)
                foreach (string name in names)
                    if (SharesAWord(name, wanted)) close.Add(name);

            if (close.Count == 0) { Core.Log.Msg("  nothing on this build shares a word with it."); return; }

            Core.Log.Msg("  the game has these, which is what the mod would have to ask for instead:");
            for (int i = 0; i < close.Count && i < 12; i++) Core.Log.Msg("    " + close[i]);
            if (close.Count > 12) Core.Log.Msg($"    ... and {close.Count - 12} more");
        }

        /// <summary>Lower case with every space and separator taken out, so only the letters are compared.</summary>
        private static string Squashed(string name)
        {
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
            return builder.ToString();
        }

        private static bool SharesAWord(string name, string wanted)
        {
            foreach (string word in wanted.Split(new[] { ' ', '_', '-', '.' },
                                                 StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length < 4) continue;              // "the", "of" and friends match everything
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }
}
