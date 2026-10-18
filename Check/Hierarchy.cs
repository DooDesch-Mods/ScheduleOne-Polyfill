using Mono.Cecil;

namespace Polyfill.Check
{
    /// <summary>
    /// Every type in the dumps by its interop name, and the base each one derives from.
    /// </summary>
    /// <remarks>
    /// The runtime resolves a member reference up the hierarchy, and so does the plugin (Triage's
    /// Inherits and Chain). A checker that looked only at the type the mod named reported every member
    /// the game had moved onto a base class as missing, while the player's game ran it fine.
    ///
    /// DumpIndex builds the Cecil types without their bases, so the `base=` field of each `T` line is read
    /// here on its own. It is spelled the game's way (`ScheduleOne.NPCs.NPC`) while `raw=` carries the
    /// interop name, so the two are joined through the game spelling.
    /// </remarks>
    internal sealed class Hierarchy
    {
        private readonly Dictionary<string, TypeDefinition> _byName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TypeDefinition>> _bySimpleName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _baseOf = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _accessorsOf = new(StringComparer.Ordinal);

        internal Hierarchy(IEnumerable<AssemblyDefinition> assemblies, IEnumerable<string> dumpFiles)
        {
            foreach (var assembly in assemblies)
                foreach (var type in assembly.MainModule.Types)
                    Add(type);

            var rawOf = new Dictionary<string, string>(StringComparer.Ordinal);
            var gameBase = new Dictionary<string, string>(StringComparer.Ordinal);
            var gameAccessors = new List<(string Owner, string Property, string Accessors)>();
            foreach (string file in dumpFiles)
                foreach (string line in File.ReadLines(file))
                {
                    if (line.StartsWith("P ", StringComparison.Ordinal))
                    {
                        if (ReadProperty(line) is { } property) gameAccessors.Add(property);
                        continue;
                    }
                    if (!line.StartsWith("T ", StringComparison.Ordinal)) continue;
                    string game = null, baseName = null, raw = null;
                    var parts = line.Split(' ');
                    for (int i = 2; i < parts.Length; i++)
                    {
                        string part = parts[i];
                        if (part.StartsWith("base=", StringComparison.Ordinal)) baseName = part.Substring(5);
                        else if (part.StartsWith("raw=", StringComparison.Ordinal)) raw = part.Substring(4);
                        else if (game == null && part.Length > 0 && part is not ("class" or "struct" or "interface"
                                 or "enum" or "abstract" or "sealed" or "static")) game = part;
                    }
                    // A Mono dump carries no `raw=`; DumpIndex then names the type by its interop rule.
                    if (game == null) continue;
                    raw ??= Polyfill.Core.DumpIndex.Interop(game);
                    rawOf[game] = raw;
                    if (baseName != null) gameBase[raw] = baseName;
                }

            foreach (var pair in gameBase)
                if (rawOf.TryGetValue(pair.Value, out string baseRaw)) _baseOf[pair.Key] = baseRaw;

            foreach (var (owner, property, accessors) in gameAccessors)
                if (rawOf.TryGetValue(owner, out string ownerRaw))
                    _accessorsOf[ownerRaw + "::" + property] = accessors;
        }

        /// <summary>`P public Foo/Bar::Price : System.Single accessors=get`</summary>
        private static (string, string, string)? ReadProperty(string line)
        {
            int marker = line.IndexOf("::", StringComparison.Ordinal);
            int tag = line.LastIndexOf(" accessors=", StringComparison.Ordinal);
            if (marker < 0 || tag < marker) return null;
            int ownerStart = line.LastIndexOf(' ', marker) + 1;
            int nameEnd = line.IndexOf(' ', marker);
            if (nameEnd < 0) return null;
            return (line.Substring(ownerStart, marker - ownerStart),
                    line.Substring(marker + 2, nameEnd - marker - 2),
                    line.Substring(tag + 11).Trim());
        }

        /// <summary>
        /// True when the name is an accessor of a game property that does not have that accessor.
        /// </summary>
        /// <remarks>
        /// DumpIndex records a property line as a member by name only, so `set_Price` on a property the
        /// game declares getter-only matched the name `Price` and read as present. The interop type has
        /// no `set_Price`, the plugin reports it missing, and AmmoCost &amp; Capacity is blocked over
        /// exactly that call. A game FIELD has both accessors on the interop side and is never refused.
        /// </remarks>
        internal bool LacksAccessor(TypeDefinition type, string name)
        {
            if (type == null || name == null || name.Length < 5) return false;
            string kind = name.StartsWith("get_", StringComparison.Ordinal) ? "get"
                        : name.StartsWith("set_", StringComparison.Ordinal) ? "set" : null;
            if (kind == null) return false;
            if (!_accessorsOf.TryGetValue(type.FullName + "::" + name.Substring(4), out string accessors)) return false;
            return !accessors.Split(',').Contains(kind);
        }

        private void Add(TypeDefinition type)
        {
            _byName[type.FullName] = type;
            if (!_bySimpleName.TryGetValue(type.Name, out var list))
                _bySimpleName[type.Name] = list = new List<TypeDefinition>();
            list.Add(type);
            foreach (var nested in type.NestedTypes) Add(nested);
        }

        internal TypeDefinition Find(string fullName)
            => fullName != null && _byName.TryGetValue(fullName, out var type) ? type : null;

        internal IReadOnlyList<TypeDefinition> BySimpleName(string name)
            => name != null && _bySimpleName.TryGetValue(name, out var list) ? list : Array.Empty<TypeDefinition>();

        /// <summary>The type and every base above it that the dumps know, nearest first.</summary>
        internal IEnumerable<TypeDefinition> Chain(TypeDefinition type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (type != null && seen.Add(type.FullName))
            {
                yield return type;
                type = _baseOf.TryGetValue(type.FullName, out string above) ? Find(above) : null;
            }
        }
    }
}
