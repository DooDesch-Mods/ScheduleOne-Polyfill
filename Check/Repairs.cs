using System.Reflection;
using Mono.Cecil;
using Polyfill.Contract;
using Polyfill.Core;

namespace Polyfill.Check
{
    /// <summary>
    /// What the plugin would do about one missing name, asked in the plugin's own order.
    /// </summary>
    /// <remarks>
    /// Mirrors Core/Triage, which cannot be linked here because it runs inside MelonLoader. The order is
    /// the plugin's: a bridge first, because a person read both builds to write it; then the game's own
    /// version history; then a single candidate by spelling; then a ModFix that restores the purpose of a
    /// name nothing can put back. Each answer comes back as one short line, and null means nothing in
    /// Polyfill answers for the name.
    ///
    /// "Would" is the honest word. The plugin confirms every repair against the player's own files at
    /// startup and can still refuse one, and a repair that binds says nothing about whether the mod then
    /// behaves. What this adds over a bare list of missing names is the split between "Polyfill already
    /// handles this" and "somebody has to write something".
    /// </remarks>
    internal sealed class Repairs
    {
        private readonly Hierarchy _types;
        private readonly VersionDb _history;
        private readonly GameVersion _game;

        internal Repairs(Hierarchy types, string gameVersion)
        {
            _types = types;
            _game = GameVersion.Parse(gameVersion);
            _history = VersionDb.Load(EmbeddedHistory());
        }

        internal int HistoryRenames => _history.RenameCount;

        /// <summary>The step files the plugin embeds, embedded here from the same folder.</summary>
        private static IEnumerable<(string, IEnumerable<string>)> EmbeddedHistory()
        {
            var assembly = Assembly.GetExecutingAssembly();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                string text = reader.ReadToEnd();
                if (!text.StartsWith(VersionDb.HeaderPrefix, StringComparison.Ordinal)) continue;
                yield return (name, text.Split('\n'));
            }
        }

        /// <summary>The rename that puts the type back as a class around its successor's native class.</summary>
        internal Bridges.TypeRename StandIn(string assembly, TypeReference reference)
        {
            if (reference == null || reference.IsGenericInstance) return null;
            var renamed = Bridges.Registry.FindType(assembly, reference.FullName);
            return renamed is { ByNativeClass: true } ? renamed : null;
        }

        /// <summary>
        /// Does the stand-in answer this call? Its declared answers are its whole surface.
        /// </summary>
        /// <remarks>
        /// Triage's CheckStandIn: the stand-in derives from a generic closed over itself, not from the
        /// successor, so a member the rule does not list is missing even where the successor has it.
        /// MoreRealisticSleeping's SleepCanvas.SleepStart is one: SetIsOpen and get_onSleepEndFade are
        /// answered, the sleep sequence was not put back.
        /// </remarks>
        internal static bool Answers(Bridges.TypeRename standIn, MemberReference wanted)
        {
            if (wanted is not MethodReference call || standIn.Answers == null) return false;
            foreach (var answer in standIn.Answers)
            {
                if (answer.Name != call.Name) continue;
                var takes = answer.Takes ?? Array.Empty<string>();
                if (call.Parameters.Count != takes.Length) continue;
                bool same = true;
                for (int i = 0; i < takes.Length && same; i++)
                    same = call.Parameters[i].ParameterType.FullName == takes[i];
                if (same) return true;
            }
            return false;
        }

        /// <summary>The type the plugin forwards a missing type to, or null when it does not forward it.</summary>
        internal TypeDefinition ForwardTarget(string assembly, TypeReference reference)
        {
            // A generic instance is never a type the dump lists by that name; see the type loop.
            if (reference == null || reference.IsGenericInstance || reference.FullName.Contains('`')) return null;
            var renamed = Bridges.Registry.FindType(assembly, reference.FullName);
            if (renamed != null) return _types.Find(renamed.NewFullName);
            var elsewhere = _types.BySimpleName(reference.Name);
            return elsewhere.Count == 1 ? elsewhere[0] : null;
        }

        internal string ForType(string assembly, TypeReference reference)
        {
            var creator = Bridges.Registry.Creator(assembly, reference.FullName);
            if (creator != null)
                return $"bridge: an empty stand-in, made by the rule for {creator.DeclaringType}::{creator.OldName}";

            var renamed = Bridges.Registry.FindType(assembly, reference.FullName);
            if (renamed != null && _types.Find(renamed.NewFullName) != null)
                return "bridge: forwarded to " + renamed.NewFullName;

            if (renamed == null)
            {
                var elsewhere = _types.BySimpleName(reference.Name);
                if (elsewhere.Count == 1) return "moved: forwarded to " + elsewhere[0].FullName;
            }

            var covered = CoveredElsewhere.ForType(reference.FullName);
            if (covered != null) return "covered by the fix " + covered.FixId;
            return null;
        }

        internal string ForMethod(string assembly, TypeDefinition declaring, MethodReference wanted)
        {
            int parameters = wanted.Parameters.Count;
            var parameterTypes = wanted.Parameters.Select(p => p.ParameterType?.FullName ?? "?").ToArray();

            var authored = Bridges.Registry.Find(assembly, declaring.FullName, wanted.Name, parameters, parameterTypes);
            if (authored != null) return "bridge: " + authored.Because;

            string historical = Successor(declaring.FullName, wanted.Name, parameters);
            if (historical != null && Declares(declaring, historical))
                return "version history: forwarded to " + historical;

            foreach (var type in _types.Chain(declaring))
            {
                var hits = NameHeuristics.ForMethod(type, wanted.Name, wanted)
                    .Select(h => (h.NewName, h.Rule)).Concat(AccessorsOfFields(type, wanted.Name))
                    .Where(h => !_types.LacksAccessor(type, h.NewName)).ToList();
                if (hits.Count == 1) return $"spelling: forwarded to {hits[0].NewName} [{hits[0].Rule}]";
                if (hits.Count > 1) break;
            }

            var covered = CoveredElsewhere.For(declaring.FullName, wanted.Name);
            if (covered != null) return "covered by the fix " + covered.FixId;
            return null;
        }

        /// <summary>
        /// A missing field has no repair in the plugin.
        /// </summary>
        /// <remarks>
        /// Il2CppInterop projects a game field as a property over native memory, so there is no managed
        /// storage to write back, and HarmonyX JITs the original before a transpiler could swap the
        /// instruction. Triage's CheckField says so in the report and repairs nothing.
        /// </remarks>
        internal string ForField(TypeDefinition declaring, FieldReference wanted)
        {
            var covered = CoveredElsewhere.For(declaring.FullName, wanted.Name);
            return covered != null ? "covered by the fix " + covered.FixId : null;
        }

        /// <summary>A Harmony target the build does not have, or has twice.</summary>
        internal string ForPatch(string assembly, TypeDefinition declaring, string name, int parameterCount,
                                 bool ambiguous)
        {
            if (ambiguous)
            {
                var entry = NameOnlyTargets.For(declaring.FullName, name);
                return entry != null ? "name-only target: the plugin picks the overload the patch means" : null;
            }

            string renamed = RenamedMethods.Successor(declaring.FullName, name);
            if (renamed != null) return "redirect: the patch binds to " + renamed;

            var bridge = Bridges.Registry.FindByName(assembly, declaring.FullName, name, parameterCount);
            if (bridge != null)
                return "bridge: the patch binds, and fires only where something still calls the old name";

            var covered = CoveredElsewhere.For(declaring.FullName, name);
            return covered != null ? "covered by the fix " + covered.FixId : null;
        }

        /// <summary>
        /// The accessors interop writes over the game's fields, as spelling candidates for an accessor.
        /// </summary>
        /// <remarks>
        /// Il2CppInterop projects the game field `_equippable` as a property, so the plugin sees a method
        /// `get__equippable` and its underscore+casing rule takes `get_Equippable` there. The dump records
        /// the field and not the accessor, and without this the checker called that call open while the
        /// player's game forwards it.
        /// </remarks>
        private static IEnumerable<(string NewName, string Rule)> AccessorsOfFields(TypeDefinition type, string wanted)
        {
            if (!wanted.StartsWith("get_", StringComparison.Ordinal) && !wanted.StartsWith("set_", StringComparison.Ordinal))
                yield break;
            string prefix = wanted.Substring(0, 4);
            foreach (var field in type.Fields)
            {
                string accessor = prefix + field.Name;
                if (accessor != wanted && NameHeuristics.IsNameVariant(wanted, accessor, out string rule))
                    yield return (accessor, rule);
            }
        }

        /// <summary>The version history's answer, spelled the way the plugin's AliasDb spells it.</summary>
        private string Successor(string interopType, string wanted, int parameters)
        {
            string type = interopType.Replace('/', '.');
            if (type.StartsWith("Il2Cpp", StringComparison.Ordinal)) type = type.Substring(6);
            type = type.TrimStart('.');

            string stem = wanted, prefix = "";
            if (wanted.StartsWith("get_", StringComparison.Ordinal) || wanted.StartsWith("set_", StringComparison.Ordinal))
            { prefix = wanted.Substring(0, 4); stem = wanted.Substring(4); }

            if (prefix.Length == 0) return _history.Successor("M", type, stem, parameters, _game);

            string asProperty = _history.Successor("P", type, stem, 0, _game);
            string asField = _history.Successor("F", type, stem, 0, _game);
            if (asProperty != null && asField != null && asProperty != asField) return null;
            string answer = asProperty ?? asField;
            return answer == null ? null : prefix + answer;
        }

        private bool Declares(TypeDefinition declaring, string name)
        {
            string bare = name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal)
                ? name.Substring(4) : null;
            foreach (var type in _types.Chain(declaring))
            {
                if (type.Methods.Any(m => m.Name == name)) return true;
                if (bare != null && !_types.LacksAccessor(type, name)
                    && (type.Fields.Any(f => f.Name == bare) || type.Properties.Any(p => p.Name == bare)))
                    return true;
            }
            return false;
        }
    }
}
