using Mono.Cecil;
using Polyfill.Core;

namespace Polyfill.Check
{
    /// <summary>
    /// What a mod asks of the game, checked against a game that is not installed.
    /// </summary>
    /// <remarks>
    /// The author's half of Polyfill. The plugin answers "will this player's mods work" while the game
    /// starts; this answers "will mine work" while the author still has the source open, which is the
    /// only moment where fixing it costs nothing.
    ///
    /// It reads the mod's metadata with Cecil and compares it to a dump of the game's surface - names
    /// and signatures, no bodies. So it needs no game, no MelonLoader, no Unity and no install, and it
    /// can check against a version the author does not own.
    /// </remarks>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Usage();
                return args.Length == 0 ? 2 : 0;
            }

            string mod = null, dumps = null, version = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--game" when i + 1 < args.Length: version = args[++i]; break;
                    case "--dumps" when i + 1 < args.Length: dumps = args[++i]; break;
                    default:
                        if (mod == null && !args[i].StartsWith("-", StringComparison.Ordinal)) mod = args[i];
                        break;
                }
            }

            if (mod == null) { Usage(); return 2; }
            if (!File.Exists(mod))
            {
                Console.Error.WriteLine($"no such file: {mod}");
                return 2;
            }

            dumps ??= Beside();
            if (dumps == null || !Directory.Exists(dumps))
            {
                Console.Error.WriteLine("no game dumps found. Pass --dumps <folder>, or put an `api` folder "
                                      + "next to this program.");
                return 2;
            }

            version ??= Newest(dumps);
            if (version == null)
            {
                Console.Error.WriteLine($"no game version in {dumps}. Each one is a folder holding "
                                      + "`<name>.api.txt` files.");
                return 2;
            }

            // The dumps sit under <version>/api/<backend>/, because a snapshot also carries bin/ and
            // the mono branch. Accepting the version folder itself as well keeps a hand-made folder
            // working - the checker should not care how the author arranged them.
            string folder = Path.Combine(dumps, version, "api", "il2cpp");
            if (!Directory.Exists(folder)) folder = Path.Combine(dumps, version);
            if (!Directory.Exists(folder))
            {
                Console.Error.WriteLine($"no dump for {version}. Present: {string.Join(", ", Versions(dumps))}");
                return 2;
            }

            return Report(mod, folder, version);
        }

        /// <summary>Read the mod, read the game, and say what the mod names that the game does not have.</summary>
        private static int Report(string modPath, string dumpFolder, string version)
        {
            var game = new Dictionary<string, DumpIndex.Result>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(dumpFolder, "*.api.txt"))
            {
                var read = DumpIndex.Read(file);
                if (read.Refusal != null)
                {
                    Console.Error.WriteLine($"{Path.GetFileName(file)}: {read.Refusal}");
                    return 2;
                }
                game[read.Assembly.Name.Name] = read;
            }

            if (game.Count == 0)
            {
                Console.Error.WriteLine($"{dumpFolder} holds no `.api.txt` files.");
                return 2;
            }

            ModuleDefinition module;
            try { module = ModuleDefinition.ReadModule(modPath); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{Path.GetFileName(modPath)} could not be read as a .NET assembly: "
                                      + e.Message);
                return 2;
            }

            int typeCount = 0, missingTypes = 0, memberCount = 0, missingMembers = 0, unprovable = 0;
            var missing = new List<string>();

            using (module)
            {
                foreach (var reference in module.GetTypeReferences())
                {
                    string assembly = AssemblyOf(reference);
                    if (assembly == null || !game.TryGetValue(assembly, out var dump)) continue;

                    typeCount++;
                    if (Find(dump.Assembly.MainModule, reference.FullName) != null) continue;

                    // A generic type reaches us as `Singleton`1` and appears in the dump only where
                    // something derives from it, spelled `Singleton<T>`. The dump lists what exists, not
                    // every name mentioned, so an open generic no line declares is unprovable here -
                    // and reporting it as missing would be a claim the author cannot check.
                    if (reference.FullName.Contains('`')) { unprovable++; continue; }

                    missingTypes++;
                    missing.Add($"  type    {reference.FullName}");
                }

                foreach (var reference in module.GetMemberReferences())
                {
                    var declaring = reference.DeclaringType;
                    string assembly = AssemblyOf(declaring);
                    if (assembly == null || !game.TryGetValue(assembly, out var dump)) continue;

                    memberCount++;
                    var type = Find(dump.Assembly.MainModule, declaring?.FullName);
                    if (type == null) continue;        // already reported as a missing type

                    // A generic method is the same case as an open generic type: the dump records the
                    // declaration, and a call site names an instantiation of it. Measured on NACops,
                    // whose three "missing" members were all one generic helper the game still has.
                    if (reference is GenericInstanceMethod
                        || (reference is MethodReference generic && generic.HasGenericParameters))
                    { unprovable++; memberCount--; continue; }

                    if (Has(type, reference)) continue;
                    missingMembers++;
                    missing.Add($"  member  {declaring.FullName}::{reference.Name}");
                }
            }

            string name = Path.GetFileName(modPath);
            Console.WriteLine($"{name} against Schedule I {version}");
            Console.WriteLine($"  {typeCount} type reference(s), {memberCount} member reference(s) checked "
                            + "against the game's own surface");
            if (unprovable > 0)
                Console.WriteLine($"  {unprovable} generic type(s) skipped - a dump lists what exists, and an "
                                + "open generic only appears where something derives from it.");

            if (missing.Count == 0)
            {
                Console.WriteLine("  everything it names is still there.");
                Console.WriteLine();
                Console.WriteLine("This does not promise the mod WORKS: a method that kept its name and changed "
                                + "what it does looks identical here. It promises nothing it names is gone.");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine($"MISSING - {missingTypes} type(s), {missingMembers} member(s)");
            foreach (string line in missing) Console.WriteLine(line);
            Console.WriteLine();
            Console.WriteLine("Each of these compiles today and throws when it is reached. Polyfill may bridge "
                            + "some of them at runtime; this list is what your mod asks for, not what a player "
                            + "will see.");
            return 1;
        }

        /// <summary>Does the type carry this member, by name and shape?</summary>
        private static bool Has(TypeDefinition type, MemberReference reference)
        {
            if (reference is FieldReference)
            {
                foreach (var field in type.Fields)
                    if (field.Name == reference.Name) return true;

                // A game FIELD is a property on the interop side, so a mod's field reference and the
                // dump's property line are the same thing under two spellings.
                foreach (var method in type.Methods)
                    if (method.Name == "get_" + reference.Name || method.Name == "set_" + reference.Name)
                        return true;
                return false;
            }

            if (reference is not MethodReference wanted) return false;

            string sought = WithoutRpcHash(wanted.Name);
            foreach (var method in type.Methods)
            {
                if (method.Name != wanted.Name && WithoutRpcHash(method.Name) != sought) continue;
                if (method.Parameters.Count == wanted.Parameters.Count) return true;
            }

            // THE OTHER HALF OF THE SAME RULE. A game field is an interop PROPERTY, so a mod calls
            // `get_Uses()` where the dump says `F ...::Uses`. Comparing those literally reported every
            // property in the game as missing - the first run of this checker did exactly that, against
            // a mod the plugin calls clean, which is how the rule was found rather than assumed.
            string bare = Accessed(wanted.Name);
            if (bare != null)
            {
                foreach (var field in type.Fields)
                    if (field.Name == bare || Managed(field.Name) == bare) return true;
                foreach (var method in type.Methods)
                    if (method.Name == bare) return true;
            }

            // FishNet writes its own accessors around a SyncVar field, and interop turns those into
            // methods with names no dump line carries: `sync___get_value_onlineBalance` sits on top of
            // the field `onlineBalance`. Reflash's single finding was one of these.
            string synced = Synced(wanted.Name);
            if (synced != null)
                foreach (var field in type.Fields)
                    if (field.Name == synced) return true;

            // A constructor the dump never lists is not evidence of absence: the generator emits the
            // ones it sees, and an interop type always has the pointer constructor a mod uses.
            return wanted.Name == ".ctor";
        }

        /// <summary>
        /// A FishNet RPC carries a hash in its name, and the dump writes that hash as `#`.
        /// </summary>
        /// <remarks>
        /// `RpcLogic___ActivateRagdoll_2690242654` in a mod is `RpcLogic___ActivateRagdoll_#` in the
        /// dump, because the number is derived and repeating it would make every dump differ from the
        /// next for no reason. Comparing the two literally reports every RPC a mod patches as gone -
        /// measured on Yoink, whose one finding was exactly that.
        ///
        /// The hash is NOT noise, though: it changes when the signature changes, which is a real break.
        /// The dump's `#` means "this comparison cannot see it", not "it does not matter" - so a
        /// changed RPC signature has to be caught by the parameter count beside this, and is.
        /// </remarks>
        private static string WithoutRpcHash(string name)
        {
            int last = name.LastIndexOf('_');
            if (last <= 0 || last == name.Length - 1) return name;

            for (int i = last + 1; i < name.Length; i++)
                if (!char.IsDigit(name[i])) return name;

            return name.Substring(0, last + 1) + "#";
        }

        /// <summary>
        /// A backing field as the managed side spells it: `&lt;Crouched&gt;k__BackingField` becomes
        /// `_Crouched_k__BackingField`, because angle brackets are not a name in C#.
        /// </summary>
        private static string Managed(string dumpName)
            => dumpName.IndexOf('<') < 0 ? dumpName : dumpName.Replace('<', '_').Replace('>', '_');

        /// <summary>The field under a FishNet sync accessor, or null when the name is not one.</summary>
        private static string Synced(string name)
        {
            const string get = "sync___get_value_";
            const string set = "sync___set_value_";
            if (name.StartsWith(get, StringComparison.Ordinal)) return name.Substring(get.Length);
            if (name.StartsWith(set, StringComparison.Ordinal)) return name.Substring(set.Length);
            return null;
        }

        /// <summary>`get_Foo` and `set_Foo` are both about `Foo`.</summary>
        private static string Accessed(string name)
            => name.StartsWith("get_", StringComparison.Ordinal)
            || name.StartsWith("set_", StringComparison.Ordinal) ? name.Substring(4) : null;

        private static TypeDefinition Find(ModuleDefinition module, string fullName)
        {
            if (fullName == null) return null;
            foreach (var type in module.Types)
                if (type.FullName == fullName) return type;
            return null;
        }

        /// <summary>Which assembly a reference points into, as the dump would name it.</summary>
        private static string AssemblyOf(TypeReference reference)
        {
            var scope = reference?.Scope;
            return scope switch
            {
                AssemblyNameReference assembly => assembly.Name,
                ModuleDefinition module => module.Assembly?.Name?.Name,
                _ => null,
            };
        }

        /// <summary>The `api` folder shipped beside the program, when there is one.</summary>
        private static string Beside()
        {
            string here = AppContext.BaseDirectory;
            string api = Path.Combine(here, "api");
            return Directory.Exists(api) ? api : null;
        }

        private static IEnumerable<string> Versions(string dumps)
        {
            foreach (string folder in Directory.GetDirectories(dumps))
            {
                if (Directory.GetFiles(folder, "*.api.txt").Length > 0
                    || Directory.Exists(Path.Combine(folder, "api", "il2cpp")))
                    yield return Path.GetFileName(folder);
            }
        }

        /// <summary>
        /// The newest version present, ordered the way a game version orders - not the way text does.
        /// </summary>
        /// <remarks>
        /// Alphabetically `0.4.6f9` sorts after `0.4.6f11`, which would silently check against the wrong
        /// build and report members that are perfectly present.
        /// </remarks>
        private static string Newest(string dumps)
        {
            string best = null;
            foreach (string candidate in Versions(dumps))
                if (best == null || Compare(candidate, best) > 0) best = candidate;
            return best;
        }

        private static int Compare(string left, string right)
        {
            var a = Numbers(left);
            var b = Numbers(right);
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                long one = i < a.Count ? a[i] : 0;
                long two = i < b.Count ? b[i] : 0;
                if (one != two) return one < two ? -1 : 1;
            }
            return 0;
        }

        private static List<long> Numbers(string value)
        {
            var parts = new List<long>();
            int i = 0;
            while (i < value.Length)
            {
                if (!char.IsDigit(value[i])) { i++; continue; }
                long number = 0;
                while (i < value.Length && char.IsDigit(value[i])) number = number * 10 + (value[i++] - '0');
                parts.Add(number);
            }
            return parts;
        }

        private static void Usage()
        {
            Console.WriteLine("polyfill-check <mod.dll> [--game <version>] [--dumps <folder>]");
            Console.WriteLine();
            Console.WriteLine("  Says which names your mod asks for that the game no longer has, without");
            Console.WriteLine("  installing anything. Reads metadata only - your mod is never loaded or run.");
            Console.WriteLine();
            Console.WriteLine("  --game    which build to check against. Default: the newest one shipped.");
            Console.WriteLine("  --dumps   where the game surfaces live. Default: the `api` folder beside this.");
            Console.WriteLine();
            Console.WriteLine("  Exit 0 nothing missing, 1 something missing, 2 could not check.");
        }
    }
}
