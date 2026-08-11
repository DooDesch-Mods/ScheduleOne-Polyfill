using Mono.Cecil;

namespace Polyfill.Core
{
    /// <summary>
    /// What the game actually offers on THIS machine, right now.
    /// </summary>
    /// <remarks>
    /// Every question Polyfill answers is settled here rather than against a table, because the interop
    /// assemblies are generated on the player's own machine from their own GameAssembly.dll. A database can
    /// propose that a member was renamed; only this can confirm the new name exists.
    ///
    /// Read with Cecil, not reflection, for two reasons that both matter at this point in startup. Almost
    /// none of the interop set is loaded yet, so reflection would have nothing to look at - and loading it
    /// to find out would pull tens of megabytes of interop metadata into the process before
    /// RegisterTypeInIl2Cpp.SetReady() and the support module have run, for every player, whether or not
    /// anything needed fixing. Cecil also sees what reflection cannot: parameter names, and which assembly
    /// a type lives in.
    /// </remarks>
    internal sealed class InteropIndex : IDisposable
    {
        private readonly Dictionary<string, string> _pathByAssembly = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModuleDefinition> _modules = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// A resolver whose answers can be fixed in advance.
        /// </summary>
        /// <remarks>
        /// Cecil's own RegisterAssembly is protected, and this needs it: without pinning, resolving a
        /// reference goes back to the folder and finds whatever we wrote there on an earlier launch.
        /// </remarks>
        private sealed class PinnedResolver : DefaultAssemblyResolver
        {
            internal void Pin(AssemblyDefinition assembly) => RegisterAssembly(assembly);
        }

        private readonly PinnedResolver _resolver = new();

        /// <summary>The game's own assemblies, as opposed to libraries a player installed.</summary>
        private readonly HashSet<string> _game = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Simple type name to every full name carrying it, across all game assemblies. Built on
        /// first use: it is the answer to "where did this type go", and most runs never ask.</summary>
        private Dictionary<string, List<TypeDefinition>> _bySimpleName;

        internal string Directory { get; }

        /// <param name="libraryDirectories">
        /// Where installed libraries live - Mods/ and UserLibs/. Indexed as well as searched, because a mod
        /// built against S1API 3.0.0 running with 3.1.11 breaks in exactly the same way as one built against
        /// an older game, and it breaks harder: a removed library method is a direct compiled call, so it
        /// throws at JIT rather than returning null. Between S1API 3.0.0 and 3.1.8 alone, 224 public
        /// declarations were removed.
        /// </param>
        internal InteropIndex(string interopDirectory, IEnumerable<string> libraryDirectories,
                              IEnumerable<string> searchOnlyDirectories)
        {
            Directory = interopDirectory;

            if (!string.IsNullOrEmpty(interopDirectory) && System.IO.Directory.Exists(interopDirectory))
            {
                _resolver.AddSearchDirectory(interopDirectory);
                foreach (string file in System.IO.Directory.GetFiles(interopDirectory, "*.dll"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    // Analysis always reads what MELONLOADER generated, never what we wrote over it. The
                    // findings must be the same on every launch: read the augmented file instead and a
                    // repair applied last time looks resolved, so it is not collected, so the next write
                    // rebuilds from the pristine original WITHOUT it - and silently undoes itself.
                    string pristine = file + InteropAugmentor.BackupSuffix;
                    _pathByAssembly[name] = File.Exists(pristine) ? pristine : file;
                    _game.Add(name);
                }
            }

            foreach (string dir in libraryDirectories)
            {
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir)) continue;
                _resolver.AddSearchDirectory(dir);
                foreach (string file in System.IO.Directory.GetFiles(dir, "*.dll"))
                {
                    // By ASSEMBLY name, not by file name. For interop assemblies the two are the same, so
                    // the distinction is invisible until it matters: S1API ships as
                    // S1API.Il2Cpp.MelonLoader.dll and calls itself "S1API", which is the name every mod
                    // references it by. Keyed by file name it is simply never found, and a mod built
                    // against an older S1API reads as clean when it is not.
                    string name;
                    try { name = System.Reflection.AssemblyName.GetAssemblyName(file)?.Name; }
                    catch { continue; }                       // native or unreadable
                    if (string.IsNullOrEmpty(name)) continue;

                    // The game always wins the name: an interop assembly and a mod DLL sharing one is a
                    // coincidence, and the game's is the one every reference means.
                    if (!_pathByAssembly.ContainsKey(name)) _pathByAssembly[name] = file;
                }
            }

            foreach (string dir in searchOnlyDirectories)
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                    _resolver.AddSearchDirectory(dir);

            // The running framework, so signature comparison can see System.* types when it needs to.
            try
            {
                string core = Path.GetDirectoryName(typeof(object).Assembly.Location);
                if (!string.IsNullOrEmpty(core)) _resolver.AddSearchDirectory(core);
            }
            catch { }
        }

        /// <summary>
        /// Where MelonLoader put the generated interop assemblies. Asked through MelonEnvironment where that
        /// property exists, because the folder has moved between MelonLoader versions.
        /// </summary>
        internal static string LocateDirectory()
        {
            try
            {
                var property = typeof(MelonLoader.Utils.MelonEnvironment).GetProperty(
                    "Il2CppAssembliesDirectory",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (property?.GetValue(null) is string fromEnvironment
                    && fromEnvironment.Length > 0 && System.IO.Directory.Exists(fromEnvironment))
                    return fromEnvironment;
            }
            catch { }

            try
            {
                string root = MelonLoader.Utils.MelonEnvironment.MelonLoaderDirectory;
                if (!string.IsNullOrEmpty(root))
                {
                    string guess = Path.Combine(root, "Il2CppAssemblies");
                    if (System.IO.Directory.Exists(guess)) return guess;
                }
            }
            catch { }
            return null;
        }

        internal IAssemblyResolver Resolver => _resolver;

        internal int AssemblyCount => _pathByAssembly.Count;

        /// <summary>
        /// Is this an assembly we can check a reference against? Answered by what is actually installed,
        /// not by a hardcoded name list that would go stale on the next update that adds an assembly.
        /// </summary>
        internal bool IsTracked(string assemblyName)
            => assemblyName != null && _pathByAssembly.ContainsKey(assemblyName);

        /// <summary>"game" for an interop assembly, "library" for something installed alongside the mods.</summary>
        internal string Kind(string assemblyName)
            => assemblyName != null && _game.Contains(assemblyName) ? "game" : "library";

        internal ModuleDefinition Module(string assemblyName)
        {
            if (assemblyName == null) return null;
            if (_modules.TryGetValue(assemblyName, out var cached)) return cached;
            if (!_pathByAssembly.TryGetValue(assemblyName, out string path)) return null;

            ModuleDefinition module = null;
            try
            {
                module = ModuleDefinition.ReadModule(path, new ReaderParameters
                {
                    InMemory = true,              // never hold a lock on a file MelonLoader may rewrite
                    ReadingMode = ReadingMode.Deferred,
                    AssemblyResolver = _resolver,
                });
                // Hand it to the resolver as the answer for this name. Without this, anything Cecil
                // resolves by reference goes back to the directory and picks up the file we have already
                // written - so a repair applied last launch reads as "nothing was missing" and is never
                // collected again.
                if (module.Assembly != null) _resolver.Pin(module.Assembly);
            }
            catch { }

            _modules[assemblyName] = module;
            return module;
        }

        /// <summary>The type under this exact full name in this exact assembly, or null.</summary>
        internal TypeDefinition FindType(string assemblyName, string fullName)
        {
            var module = Module(assemblyName);
            if (module == null || fullName == null) return null;
            // Cecil writes nested types as Outer/Inner, which is also what TypeReference.FullName produces.
            return module.GetType(fullName) ?? module.GetType(fullName.Replace('+', '/'));
        }

        /// <summary>
        /// Every type in the game carrying this simple name, wherever it lives now.
        /// </summary>
        /// <remarks>
        /// This is the lookup that answers a moved type without any version history: a mod's metadata names
        /// the type it wants, and if exactly one type on the machine carries that simple name, there is
        /// nothing to infer. More than one, and there is - so the caller gets the list, not a guess.
        /// </remarks>
        internal IReadOnlyList<TypeDefinition> BySimpleName(string simpleName)
        {
            _bySimpleName ??= BuildSimpleNameIndex();
            return _bySimpleName.TryGetValue(simpleName, out var hits) ? hits : Array.Empty<TypeDefinition>();
        }

        /// <summary>Every simple type name the game defines, so a caller can turn the index inside out
        /// without loading a type to find out what is in it.</summary>
        internal IEnumerable<string> SimpleNames()
        {
            _bySimpleName ??= BuildSimpleNameIndex();
            return _bySimpleName.Keys;
        }

        private Dictionary<string, List<TypeDefinition>> BuildSimpleNameIndex()
        {
            var index = new Dictionary<string, List<TypeDefinition>>(StringComparer.Ordinal);
            // Game assemblies only. A game type never "moves into" a mod, and indexing every installed mod
            // would invent candidates across authors who have never heard of each other.
            foreach (string assembly in _game)
            {
                var module = Module(assembly);
                if (module == null) continue;
                try
                {
                    foreach (var type in module.Types) Add(index, type);
                }
                catch { }
            }
            return index;
        }

        private static void Add(Dictionary<string, List<TypeDefinition>> index, TypeDefinition type)
        {
            if (!index.TryGetValue(type.Name, out var list)) index[type.Name] = list = new List<TypeDefinition>();
            list.Add(type);
            if (!type.HasNestedTypes) return;
            foreach (var nested in type.NestedTypes) Add(index, nested);
        }

        public void Dispose()
        {
            foreach (var module in _modules.Values) { try { module?.Dispose(); } catch { } }
            _modules.Clear();
            try { _resolver.Dispose(); } catch { }
        }
    }
}
