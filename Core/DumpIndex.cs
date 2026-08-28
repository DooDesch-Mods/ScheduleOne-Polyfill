using Mono.Cecil;

namespace Polyfill.Core
{
    /// <summary>
    /// A game's surface, built from a text dump instead of the player's own assemblies.
    /// </summary>
    /// <remarks>
    /// The analysis reads the game with Cecil - types, methods, parameter names, fields - and never runs
    /// any of it. That is the whole reason it can happen at all before mods load, and it is also why the
    /// same question can be asked with no game installed: give it those names from somewhere else and
    /// nothing downstream can tell the difference.
    ///
    /// WHY A DUMP AND NOT THE ASSEMBLIES. The interop assemblies are 50 MB of generated code built from
    /// the player's own GameAssembly.dll, and shipping them to a mod author means shipping the game's
    /// compiled surface. The dumps under `Workspace/gamesnapshots/&lt;version&gt;/api/` are names and
    /// signatures and nothing else - no bodies, no constants, no assets - which is the least that can
    /// answer "does this member still exist" and considerably less than a decompiler produces.
    ///
    /// THE NAMES NEED A PREFIX. A dump says `ScheduleOne.Economy.Supplier` because that is what the game
    /// calls it; a mod compiled against the interop assemblies says `Il2CppScheduleOne.Economy.Supplier`.
    /// The mapping is mechanical and Il2CppInterop applies it the same way to everything in the
    /// assembly, so it is applied here rather than asked of every caller.
    ///
    /// WHAT THIS CANNOT ANSWER, and the checker says so rather than guessing: anything that depends on a
    /// method BODY. Whether a call is inlined, what a field is initialised to, whether a Harmony target
    /// still does what its name suggests. The dump is a contract, not a program.
    /// </remarks>
    internal static class DumpIndex
    {
        private const string Prefix = "Il2Cpp";

        /// <summary>The format this reads, refusing anything newer rather than misreading it.</summary>
        private const int KnownFormat = 1;

        internal sealed class Result
        {
            internal AssemblyDefinition Assembly;
            internal string Label;          // the game version the dump was taken from
            internal string Refusal;        // why there is no assembly, when there is none
            internal int Types;
            internal int Members;
        }

        /// <summary>
        /// Turn one `.api.txt` into an assembly the analysis can read.
        /// </summary>
        /// <remarks>
        /// Built in one pass, with members attached as they arrive. A member whose declaring type was
        /// never declared is kept anyway, under a type created on the spot: a dump can be trimmed, and
        /// losing a member because its type line was filtered out would make the checker quieter than
        /// the truth.
        /// </remarks>
        internal static Result Read(string path)
        {
            var result = new Result();
            if (!File.Exists(path))
            {
                result.Refusal = "there is no dump at " + path;
                return result;
            }

            string assemblyName = Path.GetFileName(path);
            int cut = assemblyName.IndexOf(".api.txt", StringComparison.OrdinalIgnoreCase);
            if (cut > 0) assemblyName = assemblyName.Substring(0, cut);

            var assembly = AssemblyDefinition.CreateAssembly(
                new AssemblyNameDefinition(assemblyName, new Version(1, 0, 0, 0)), assemblyName,
                ModuleKind.Dll);
            var module = assembly.MainModule;
            var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);

            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length == 0) continue;

                if (raw[0] == '#')
                {
                    if (raw.StartsWith("# apidump ", StringComparison.Ordinal))
                    {
                        if (!int.TryParse(raw.Substring(10).Trim(), out int format) || format > KnownFormat)
                        {
                            result.Refusal = $"the dump says it is format {raw.Substring(10).Trim()} and this "
                                           + $"reads {KnownFormat}. Update the checker rather than trusting it.";
                            return result;
                        }
                    }
                    else if (raw.StartsWith("# label=", StringComparison.Ordinal))
                        result.Label = raw.Substring(8).Trim();
                    continue;
                }

                switch (raw[0])
                {
                    case 'T': Declare(module, types, raw); break;
                    case 'M': Member(module, types, raw, method: true); break;
                    case 'F':
                    case 'P': Member(module, types, raw, method: false); break;
                    // V is an enum value. The analysis never asks for one by name - a mod compiles an
                    // enum to its number - so carrying them would add rows nothing reads.
                }
            }

            foreach (var type in types.Values) module.Types.Add(type);

            result.Assembly = assembly;
            result.Types = types.Count;
            foreach (var type in types.Values) result.Members += type.Methods.Count + type.Fields.Count;
            return result;
        }

        /// <summary>`T public class Foo base=Bar raw=...`</summary>
        private static void Declare(ModuleDefinition module, Dictionary<string, TypeDefinition> types,
                                    string line)
        {
            var parts = line.Split(' ');
            if (parts.Length < 3) return;

            // `raw=` IS THE INTEROP NAME, straight from the generator, so it is taken over any rule of
            // ours. It settles the case the rule gets wrong: a type in no namespace becomes
            // `Il2Cpp.GUIDManager`, not `Il2CppGUIDManager`, and a mod asks for the former.
            foreach (string part in parts)
                if (part.StartsWith("raw=", StringComparison.Ordinal))
                {
                    string given = part.Substring(4);
                    if (given.Length > 0) { EnsureExact(module, types, given); return; }
                }

            string name = null;
            for (int i = 2; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                if (parts[i] is "class" or "struct" or "interface" or "enum" or "abstract" or "sealed"
                    or "static") continue;
                if (parts[i].StartsWith("base=", StringComparison.Ordinal)) break;
                name = parts[i];
                break;
            }
            if (name == null) return;

            Ensure(module, types, name);
        }

        /// <summary>`M public Foo::Bar(System.Int32) : System.Void pnames=count`</summary>
        private static void Member(ModuleDefinition module, Dictionary<string, TypeDefinition> types,
                                   string line, bool method)
        {
            int marker = line.IndexOf("::", StringComparison.Ordinal);
            if (marker < 0) return;

            int nameStart = line.LastIndexOf(' ', marker) + 1;
            string owner = line.Substring(nameStart, marker - nameStart);
            var type = Ensure(module, types, owner);
            if (type == null) return;

            string rest = line.Substring(marker + 2);
            int colon = rest.LastIndexOf(" : ", StringComparison.Ordinal);
            string returns = colon < 0 ? "System.Void" : Cut(rest.Substring(colon + 3));
            string head = colon < 0 ? rest : rest.Substring(0, colon);

            if (!method)
            {
                string fieldName = Cut(head);
                if (fieldName.Length == 0) return;
                type.Fields.Add(new FieldDefinition(fieldName, FieldAttributes.Public,
                                                    Reference(module, returns)));
                return;
            }

            int open = head.IndexOf('(');
            if (open < 0) return;
            int close = head.LastIndexOf(')');
            if (close < open) return;

            string methodName = head.Substring(0, open);
            var definition = new MethodDefinition(methodName, MethodAttributes.Public,
                                                  Reference(module, returns));

            string inside = head.Substring(open + 1, close - open - 1).Trim();
            var names = ParameterNames(line);
            if (inside.Length > 0)
            {
                var parameterTypes = Split(inside);
                for (int i = 0; i < parameterTypes.Count; i++)
                {
                    string given = i < names.Count ? names[i] : "arg" + i;
                    definition.Parameters.Add(new ParameterDefinition(given, ParameterAttributes.None,
                                                                      Reference(module, parameterTypes[i])));
                }
            }
            type.Methods.Add(definition);
        }

        /// <summary>
        /// The parameter names, which are the reason a dump beats a decompiler for this job.
        /// </summary>
        /// <remarks>
        /// Harmony binds a patch argument BY NAME. A checker that knows every signature and no parameter
        /// name would pass a patch that cannot bind at runtime, which is the failure this whole thing
        /// exists to catch early.
        /// </remarks>
        private static List<string> ParameterNames(string line)
        {
            var names = new List<string>();
            int at = line.IndexOf(" pnames=", StringComparison.Ordinal);
            if (at < 0) return names;

            string list = line.Substring(at + 8);
            int space = list.IndexOf(' ');
            if (space > 0) list = list.Substring(0, space);

            foreach (string name in list.Split(','))
                if (name.Length > 0) names.Add(name);
            return names;
        }

        /// <summary>Split a parameter list without cutting a generic argument in half.</summary>
        private static List<string> Split(string list)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < list.Length; i++)
            {
                char c = list[i];
                if (c == '<' || c == '[') depth++;
                else if (c == '>' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(list.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            if (start < list.Length) parts.Add(list.Substring(start).Trim());
            return parts;
        }

        private static string Cut(string value)
        {
            int space = value.IndexOf(' ');
            return (space < 0 ? value : value.Substring(0, space)).Trim();
        }

        /// <summary>The type under the name a mod would use for it, created once.</summary>
        private static TypeDefinition Ensure(ModuleDefinition module,
                                             Dictionary<string, TypeDefinition> types, string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return null;

            string full = Interop(gameName);
            if (types.TryGetValue(full, out var existing)) return existing;

            int dot = full.LastIndexOf('.');
            string ns = dot < 0 ? "" : full.Substring(0, dot);
            string name = dot < 0 ? full : full.Substring(dot + 1);

            // A nested type arrives as Outer/Inner. Cecil wants it nested, and the analysis asks for it
            // by the same slashed name a mod's metadata carries - so it is kept flat under that name
            // rather than reparented. Nothing here resolves a base type, so nesting buys nothing.
            var type = new TypeDefinition(ns, name, TypeAttributes.Public | TypeAttributes.Class,
                                          module.TypeSystem.Object);
            types[full] = type;
            return type;
        }

        /// <summary>
        /// `ScheduleOne.X` as the interop assemblies spell it, which is what a mod asks for.
        /// </summary>
        /// <remarks>
        /// Il2CppInterop puts its prefix on the NAMESPACE, so a type in no namespace becomes
        /// <c>Il2CppFoo</c> and one in a namespace becomes <c>Il2CppScheduleOne.Foo</c>. Types already
        /// carrying the prefix in the dump - the generator emits some - are left alone, and so is
        /// anything from the BCL, which interop does not rename.
        /// </remarks>
        private static string Interop(string gameName)
        {
            if (gameName.StartsWith(Prefix, StringComparison.Ordinal)) return gameName;
            if (gameName.StartsWith("System.", StringComparison.Ordinal)) return gameName;
            if (gameName.StartsWith("UnityEngine.", StringComparison.Ordinal)) return gameName;

            // No namespace means the prefix becomes one: `GUIDManager` is `Il2Cpp.GUIDManager` on the
            // managed side, not `Il2CppGUIDManager`. The dump's own `raw=` says so wherever it is
            // present, and this is the rule for the lines that carry none.
            return gameName.Contains('.') ? Prefix + gameName : Prefix + "." + gameName;
        }

        /// <summary>The type under a name the dump already spelled the way a mod would.</summary>
        private static TypeDefinition EnsureExact(ModuleDefinition module,
                                                  Dictionary<string, TypeDefinition> types, string full)
        {
            if (types.TryGetValue(full, out var existing)) return existing;

            int dot = full.LastIndexOf('.');
            var type = new TypeDefinition(dot < 0 ? "" : full.Substring(0, dot),
                                          dot < 0 ? full : full.Substring(dot + 1),
                                          TypeAttributes.Public | TypeAttributes.Class,
                                          module.TypeSystem.Object);
            types[full] = type;
            return type;
        }

        /// <summary>A type reference by name, invented if the dump never declared it.</summary>
        private static TypeReference Reference(ModuleDefinition module, string gameName)
        {
            if (string.IsNullOrEmpty(gameName)) return module.TypeSystem.Object;

            string full = Interop(gameName.TrimEnd('&', '*'));
            int dot = full.LastIndexOf('.');
            string ns = dot < 0 ? "" : full.Substring(0, dot);
            string name = dot < 0 ? full : full.Substring(dot + 1);

            return new TypeReference(ns, name, module, module.TypeSystem.CoreLibrary);
        }
    }
}
