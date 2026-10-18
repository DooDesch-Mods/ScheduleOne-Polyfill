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

            string mod = null, dumps = null, version = null, backend = null;
            var refs = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--game" when i + 1 < args.Length: version = args[++i]; break;
                    case "--dumps" when i + 1 < args.Length: dumps = args[++i]; break;
                    case "--backend" when i + 1 < args.Length: backend = args[++i]; break;
                    case "--refs" when i + 1 < args.Length: refs.Add(args[++i]); break;
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

            backend ??= Backend(mod, refs);
            if (backend is not ("il2cpp" or "mono"))
            {
                Console.Error.WriteLine($"--backend is il2cpp or mono, not {backend}");
                return 2;
            }

            // The dumps sit under <version>/api/<backend>/, because a snapshot also carries bin/ and
            // the mono branch. Accepting the version folder itself as well keeps a hand-made folder
            // working - the checker should not care how the author arranged them.
            string folder = Path.Combine(dumps, version, "api", backend);
            if (!Directory.Exists(folder)) folder = Path.Combine(dumps, version);
            if (!Directory.Exists(folder))
            {
                Console.Error.WriteLine($"no dump for {version}. Present: {string.Join(", ", Versions(dumps))}");
                return 2;
            }

            return Report(mod, folder, version, backend, refs);
        }

        /// <summary>
        /// Which build of the game a mod was written for, read off the names it uses.
        /// </summary>
        /// <remarks>
        /// An IL2CPP mod names the game through interop, so its types start with `Il2Cpp`; a Mono mod names
        /// `ScheduleOne.*` directly. A library that names the game not at all is checked against IL2CPP,
        /// where the repairs live.
        /// </remarks>
        private static string Backend(string modPath, List<string> refs)
        {
            try
            {
                using var module = ModuleDefinition.ReadModule(modPath, Reading(modPath, refs));
                bool game = false;
                foreach (var reference in module.GetTypeReferences())
                {
                    if (AssemblyOf(reference) != "Assembly-CSharp") continue;
                    if (reference.FullName.StartsWith("Il2Cpp", StringComparison.Ordinal)) return "il2cpp";
                    game = true;
                }
                return game ? "mono" : "il2cpp";
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"could not read {Path.GetFileName(modPath)} to pick a backend ({e.Message}); "
                                      + "checking against il2cpp");
                return "il2cpp";
            }
        }

        /// <summary>Read the mod, read the game, and say what the mod names that the game does not have.</summary>
        private static int Report(string modPath, string dumpFolder, string version, string backend,
                                  List<string> refs)
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
            try { module = ModuleDefinition.ReadModule(modPath, Reading(modPath, refs)); }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{Path.GetFileName(modPath)} could not be read as a .NET assembly: "
                                      + e.Message);
                return 2;
            }

            var types = new Hierarchy(game.Values.Select(r => r.Assembly), Directory.GetFiles(dumpFolder, "*.api.txt"));

            // Polyfill writes its repairs into the IL2CPP interop assemblies. A Mono game has none, so on
            // Mono the list is what the mod needs and nothing answers for it.
            var repairs = backend == "il2cpp" ? new Repairs(types, version) : null;
            _mono = backend == "mono";

            int typeCount = 0, memberCount = 0, patchCount = 0, unprovable = 0, unprovablePatches = 0, monoBranch = 0;
            var missing = new List<(string Line, string Repair)>();
            // A lookup in code that finds nothing hands back null instead of throwing, and what follows is
            // the mod's own decision: Polyfill's ModFixes and SideHustle both look up names that exist on
            // one game version only and skip the patch when they are absent. These are listed apart and do
            // not move the verdict; the plugin does not read them at all.
            var lookups = new List<(string Line, string Repair)>();
            string melon = null;

            using (module)
            {
                melon = MelonInfo(module);

                // The plugin's own rule: a reference made only from methods the mod calls on Mono is never
                // compiled on IL2CPP. See Core/MonoOnlyCode.
                var monoOnly = MonoOnlyCode.References(module);

                foreach (var reference in module.GetTypeReferences())
                {
                    string assembly = AssemblyOf(reference);
                    if (assembly == null || !game.TryGetValue(assembly, out var dump)) continue;

                    typeCount++;
                    if (Lookup(types, dump, reference.FullName) != null) continue;

                    // A generic type reaches us as `Singleton`1` and appears in the dump only where
                    // something derives from it, spelled `Singleton<T>`. The dump lists what exists, not
                    // every name mentioned, so an open generic no line declares is unprovable here -
                    // and reporting it as missing would be a claim the author cannot check.
                    if (reference.FullName.Contains('`')) { unprovable++; continue; }

                    missing.Add(($"type    {reference.FullName}", repairs?.ForType(assembly, reference)));
                }

                foreach (var reference in module.GetMemberReferences())
                {
                    var declaring = reference.DeclaringType;
                    string assembly = AssemblyOf(declaring);
                    if (assembly == null || !game.TryGetValue(assembly, out var dump)) continue;

                    if (monoOnly.Contains(reference.FullName)) { monoBranch++; continue; }

                    memberCount++;
                    var type = Lookup(types, dump, declaring?.FullName);
                    string movedFrom = "";

                    if (type == null && repairs?.StandIn(assembly, declaring) is { } standIn)
                    {
                        if (Repairs.Answers(standIn, reference)) continue;
                        missing.Add(($"member  {declaring.FullName}::{reference.Name} (the stand-in around "
                                   + $"{standIn.NewFullName} does not carry it)", null));
                        continue;
                    }

                    // A type the plugin forwards takes its members along, and each one has to be on the
                    // target. SaveGameModChecker calls MainMenuScreen.Close(bool); the forward to MenuScreen
                    // binds, and the call then fails because MenuScreen.Close changed its parameters.
                    if (type == null && repairs?.ForwardTarget(assembly, declaring) is { } target)
                    {
                        type = target;
                        movedFrom = $" (on {target.FullName}, where the type was forwarded)";
                    }
                    if (type == null) continue;        // already reported as a missing type

                    // A generic method is the same case as an open generic type: the dump records the
                    // declaration, and a call site names an instantiation of it. Measured on NACops,
                    // whose three "missing" members were all one generic helper the game still has.
                    if (reference is GenericInstanceMethod
                        || (reference is MethodReference generic && generic.HasGenericParameters))
                    { unprovable++; memberCount--; continue; }

                    // Up the hierarchy, as the runtime resolves it: a member the game moved onto a base
                    // class was never missing.
                    if (types.Chain(type).Any(t => Has(t, reference) && !types.LacksAccessor(t, reference.Name)))
                        continue;

                    string repair = reference switch
                    {
                        MethodReference method => repairs?.ForMethod(assembly, type, method),
                        FieldReference field => repairs?.ForField(type, field),
                        _ => null,
                    };
                    missing.Add(($"member  {declaring.FullName}::{reference.Name}{movedFrom}", repair));
                }

                foreach (var target in PatchTargets.Read(module, out unprovablePatches))
                {
                    string assembly = AssemblyOf(target.Type);
                    if (assembly == null || !game.TryGetValue(assembly, out var dump)) continue;

                    // The dump leaves out the constructors the generator always emits; see Has.
                    if (target.Name is ".ctor" or ".cctor") continue;

                    patchCount++;
                    var type = Lookup(types, dump, target.Type.FullName);
                    if (type == null) continue;        // already reported as a missing type

                    var overloads = Overloads(types, type, target.Name);
                    int fitting = target.ParameterCount < 0
                        ? overloads.Count
                        : overloads.Count(m => m.Parameters.Count == target.ParameterCount);
                    // Arity is enough to tell "gone" from "there". It cannot choose between overloads of the
                    // same arity - Console.SubmitCommand(string) and (List<string>) - so a patch that names
                    // its types is taken to mean one of them, and only a patch that names none is ambiguous.
                    if (fitting == 1 || (fitting > 1 && target.ParameterCount >= 0)) continue;

                    bool ambiguous = fitting > 1;
                    string what = ambiguous ? $"{overloads.Count} overloads, the patch names no parameters" : "not found";
                    (target.InCode ? lookups : missing).Add(
                        ($"patch   {type.FullName}::{target.Name} ({what}; {target.From})",
                         repairs?.ForPatch(assembly, type, target.Name, target.ParameterCount, ambiguous)));
                }
            }

            string name = Path.GetFileName(modPath);
            Console.WriteLine($"{name} against Schedule I {version} ({backend})");
            if (melon != null) Console.WriteLine($"  mod: {melon}");
            Console.WriteLine($"  {typeCount} type reference(s), {memberCount} member reference(s) and {patchCount} "
                            + "patch target(s) checked against the game's own surface");
            if (unprovable > 0)
                Console.WriteLine($"  {unprovable} generic reference(s) skipped - a dump lists what exists, and an "
                                + "open generic only appears where something derives from it.");
            if (unprovablePatches > 0)
                Console.WriteLine($"  {unprovablePatches} patch class(es) skipped - their target is computed in code "
                                + "(TargetMethod) or incomplete in the attributes.");
            if (monoBranch > 0)
                Console.WriteLine($"  {monoBranch} member reference(s) skipped - only methods your mod calls when "
                                + "MelonUtils.IsGameIl2Cpp() is false use them.");
            if (repairs == null)
                Console.WriteLine("  Polyfill repairs only the IL2CPP build, so nothing below has a repair.");

            if (lookups.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"LOOKUPS - {lookups.Count} name(s) the mod looks up in code that come back null; "
                                + "read the call site to see whether the mod skips or fails");
                foreach (var (line, repair) in lookups)
                    Console.WriteLine($"  {line}\n      -> {repair ?? "nothing in Polyfill answers for this"}");
            }

            if (missing.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine("VERDICT clean: everything it names is still there.");
                Console.WriteLine("This does not promise the mod WORKS: a method that kept its name and changed "
                                + "what it does looks identical here. It promises nothing it names is gone.");
                return 0;
            }

            int open = missing.Count(m => m.Repair == null);
            Console.WriteLine();
            Console.WriteLine($"MISSING - {missing.Count} name(s), {missing.Count - open} with a repair in Polyfill, {open} open");
            foreach (var (line, repair) in missing)
                Console.WriteLine($"  {line}\n      -> {repair ?? "OPEN: nothing in Polyfill answers for this"}");
            Console.WriteLine();

            if (open == 0)
            {
                Console.WriteLine("VERDICT polyfill: every missing name has a repair in this Polyfill build. The plugin "
                                + "still confirms each one against the player's files at startup.");
                return 3;
            }

            Console.WriteLine($"VERDICT open: {open} name(s) throw when they are reached, and nothing in this Polyfill "
                            + "build repairs them.");
            return 1;
        }

        /// <summary>
        /// Where Cecil looks for the assemblies a mod references.
        /// </summary>
        /// <remarks>
        /// Reading an attribute argument of an enum type - <c>MethodType.Getter</c> - needs the enum's
        /// definition, which lives in 0Harmony. Without a resolver that finds it the read throws, and a
        /// checker that dies on the first Harmony mod is no checker. The mod's own folder is searched first,
        /// then each <c>--refs</c> folder (MelonLoader/net6 holds 0Harmony and MelonLoader).
        /// </remarks>
        private static ReaderParameters Reading(string modPath, List<string> refs)
        {
            var resolver = new DefaultAssemblyResolver();
            string folder = Path.GetDirectoryName(Path.GetFullPath(modPath));
            if (folder != null) resolver.AddSearchDirectory(folder);
            foreach (string dir in refs) resolver.AddSearchDirectory(dir);
            return new ReaderParameters { AssemblyResolver = resolver };
        }

        /// <summary>True while a Mono mod is checked; its names carry no interop prefix.</summary>
        private static bool _mono;

        /// <summary>The type by its interop name, through the hierarchy first and the dump's own list second.</summary>
        /// <remarks>
        /// DumpIndex spells every type the way interop does, because the plugin only ever asks for those.
        /// A Mono mod says `ScheduleOne.NPCs.NPC` where the index holds `Il2CppScheduleOne.NPCs.NPC`, and
        /// without the same rule applied to its names every game type it touches read as missing.
        /// </remarks>
        private static TypeDefinition Lookup(Hierarchy types, DumpIndex.Result dump, string fullName)
        {
            if (_mono && fullName != null) fullName = DumpIndex.Interop(fullName);
            return types.Find(fullName) ?? Find(dump.Assembly.MainModule, fullName);
        }

        /// <summary>
        /// The methods of this name on the nearest type in the chain that declares any.
        /// </summary>
        /// <remarks>
        /// Harmony's lookups walk up to the first type that declares the name and choose among its
        /// overloads there, so two methods on that one type are what makes a name-only target ambiguous.
        /// </remarks>
        private static List<MethodDefinition> Overloads(Hierarchy types, TypeDefinition type, string name)
        {
            foreach (var level in types.Chain(type))
            {
                // The dump writes an RPC's hash as `#`; see WithoutRpcHash.
                string sought = WithoutRpcHash(name);
                var here = level.Methods.Where(m => m.Name == name || WithoutRpcHash(m.Name) == sought).ToList();
                if (here.Count > 0) return here;
            }
            return new List<MethodDefinition>();
        }

        /// <summary>Name, version and author from the mod's MelonInfo, or null for a library.</summary>
        private static string MelonInfo(ModuleDefinition module)
        {
            foreach (var attribute in module.Assembly.CustomAttributes)
            {
                if (attribute.AttributeType.Name != "MelonInfoAttribute") continue;
                var values = attribute.ConstructorArguments.Skip(1).Select(a => a.Value as string).ToList();
                if (values.Count < 2) return null;
                string author = values.Count > 2 && !string.IsNullOrEmpty(values[2]) ? " by " + values[2] : "";
                return $"{values[0]} {values[1]}{author}";
            }
            return null;
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
            Console.WriteLine("polyfill-check <mod.dll> [--game <version>] [--dumps <folder>] [--backend il2cpp|mono] [--refs <folder>]");
            Console.WriteLine();
            Console.WriteLine("  Says which names your mod asks for that the game no longer has, and whether");
            Console.WriteLine("  Polyfill repairs each one, without installing anything. Reads metadata only -");
            Console.WriteLine("  your mod is never loaded or run.");
            Console.WriteLine();
            Console.WriteLine("  --game     which build to check against. Default: the newest one shipped.");
            Console.WriteLine("  --dumps    where the game surfaces live. Default: the `api` folder beside this.");
            Console.WriteLine("  --backend  which build of the game. Default: read off the names the mod uses.");
            Console.WriteLine("  --refs     a folder with the assemblies the mod references (0Harmony, MelonLoader).");
            Console.WriteLine("             Repeatable. The mod's own folder is always searched.");
            Console.WriteLine();
            Console.WriteLine("  Exit 0 nothing missing, 1 something missing that Polyfill does not repair,");
            Console.WriteLine("  3 something missing and Polyfill repairs all of it, 2 could not check.");
        }
    }
}
