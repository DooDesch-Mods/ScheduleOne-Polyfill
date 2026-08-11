using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Dynamic
{
    /// <summary>
    /// Answer <c>AccessTools.TypeByName</c> from metadata instead of by loading every type in the process.
    /// </summary>
    /// <remarks>
    /// Harmony resolves a type name it cannot find by walking <c>AllTypes()</c>, which is
    /// <c>AppDomain.CurrentDomain.GetAssemblies().SelectMany(GetTypesFromAssembly)</c> - one
    /// <c>Assembly.GetTypes()</c> per loaded assembly. On an IL2CPP interop assembly that call is not safe:
    /// it forces every type in the assembly to load, and one bad type takes the process down with an access
    /// violation inside coreclr. There is no exception to catch, no log line and no crash message.
    ///
    /// Measured, from the minidump of a dead boot:
    /// <code>
    /// System.Reflection.RuntimeModule.GetTypes            &lt;- 0xc0000005 in coreclr.dll
    /// System.Reflection.Assembly.GetTypes
    /// OverTheCounter.Patches.SafeTypeLoadPatch.SafeGetTypes
    /// HarmonyLib.AccessTools+&lt;&gt;c.&lt;AllTypes&gt;b__5_0
    /// HarmonyLib.AccessTools.TypeByName
    /// OverTheCounter.Patches.DealCompletionPopupPatch.TargetMethod
    /// </code>
    /// It is not deterministic, because which assemblies are loaded when a mod asks differs from launch to
    /// launch - which is exactly what makes it read as "the boot randomly dies" rather than as a bug.
    ///
    /// The replacement asks the same question a different way. A full name is a dictionary lookup
    /// (<c>Assembly.GetType</c>) that loads one type, never all of them. A simple name - Harmony's last
    /// resort - enumerates only assemblies that are NOT interop assemblies, and looks the game up in Cecil
    /// metadata, where a name costs nothing to read.
    ///
    /// This is a strict improvement on the original for every caller: same answers, without the landmine.
    /// </remarks>
    internal static class ReflectionFallback
    {
        private static MelonLogger.Instance _log;
        private static string _interop;

        /// <summary>Names already answered. Positives only - an assembly that loads later must still be
        /// found, and a cached null would hide it forever.</summary>
        private static readonly Dictionary<string, Type> _answers = new(StringComparer.Ordinal);

        /// <summary>Simple name to "assembly|full name" for the game's own types. Built from metadata the
        /// first time anyone asks for a simple name, which most launches never do.</summary>
        private static Dictionary<string, List<string>> _gameBySimpleName;
        private static bool _gameIndexBuilt;

        internal static void Install(MelonLogger.Instance log, string interopDirectory)
        {
            _log = log;
            _interop = interopDirectory;

            try
            {
                var target = AccessTools.Method(typeof(AccessTools), nameof(AccessTools.TypeByName),
                                                new[] { typeof(string) });
                if (target == null)
                {
                    log.Warning("[reflect] this Harmony has no AccessTools.TypeByName(string); left alone.");
                    return;
                }

                new HarmonyLib.Harmony("doodesch.polyfill.reflection").Patch(
                    target,
                    prefix: new HarmonyMethod(typeof(ReflectionFallback), nameof(TypeByNamePrefix)));

                log.Msg("[reflect] type lookups answer from metadata; nothing enumerates interop types.");

                var lookup = AccessTools.Method(typeof(AccessTools), nameof(AccessTools.Method),
                                                new[] { typeof(Type), typeof(string), typeof(Type[]), typeof(Type[]) });
                if (lookup != null)
                    new HarmonyLib.Harmony("doodesch.polyfill.reflection").Patch(
                        lookup,
                        postfix: new HarmonyMethod(typeof(ReflectionFallback), nameof(MethodPostfix)));
            }
            catch (Exception e)
            {
                // Harmony not patchable here is not fatal - it costs the crash guard, not the repairs.
                log.Warning("[reflect] could not take over type lookups: " + e.Message);
            }
        }

        private static bool TypeByNamePrefix(string name, ref Type __result)
        {
            __result = Resolve(name);
            return false;
        }

        /// <summary>
        /// The method a mod is looking for, after the update gave it another argument.
        /// </summary>
        /// <remarks>
        /// <c>StorageMenu.Open(StorageEntity)</c> is <c>Open(StorageEntity, Action)</c> now, and a mod asking
        /// for the old parameter list gets null - which for a Harmony <c>TargetMethod</c> means the patch does
        /// not merely miss, it throws and takes the mod's whole patch class with it.
        ///
        /// This has to be layer 2 rather than an added overload in the assembly, and the difference is the
        /// whole reason the two layers exist. A patch must land on the method THE GAME CALLS. An overload
        /// added next to it would satisfy the lookup and then sit there never being called, which is worse
        /// than the error: the mod would report itself as working.
        ///
        /// Only a unique prefix match counts. Two methods of that name both starting with the requested
        /// types means choosing one is a guess, and a Harmony patch on the wrong overload is silent.
        /// </remarks>
        private static void MethodPostfix(Type type, string name, Type[] parameters, Type[] generics,
                                          ref MethodInfo __result)
        {
            if (__result != null || type == null || string.IsNullOrEmpty(name)) return;
            if (parameters == null || generics != null) return;

            var widened = WidenedMatch(type, name, parameters);
            if (widened == null) return;

            __result = widened;
            _log?.Msg($"[reflect] {type.Name}.{name} takes {widened.GetParameters().Length} argument(s) now; "
                    + $"answered the {parameters.Length}-argument lookup with it.");
        }

        private static MethodInfo WidenedMatch(Type type, string name, Type[] parameters)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                   | BindingFlags.Static | BindingFlags.DeclaredOnly;

            for (var current = type; current != null; current = current.BaseType)
            {
                MethodInfo[] candidates;
                try { candidates = current.GetMethods(all); } catch { continue; }

                MethodInfo only = null;
                foreach (var candidate in candidates)
                {
                    if (candidate.Name != name) continue;
                    var have = candidate.GetParameters();
                    if (have.Length <= parameters.Length) continue;

                    bool startsTheSame = true;
                    for (int i = 0; i < parameters.Length; i++)
                        if (have[i].ParameterType != parameters[i]) { startsTheSame = false; break; }
                    if (!startsTheSame) continue;

                    if (only != null) return null;
                    only = candidate;
                }
                if (only != null) return only;
            }
            return null;
        }

        private static Type Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            lock (_answers)
                if (_answers.TryGetValue(name, out var cached)) return cached;

            Type found = null;
            try { found = Type.GetType(name, false); } catch { }

            if (found == null)
                foreach (var assembly in Loaded())
                {
                    try { found = assembly.GetType(name, false); } catch { }
                    if (found != null) break;
                }

            if (found == null) found = BySimpleName(name);

            if (found != null) lock (_answers) _answers[name] = found;
            return found;
        }

        /// <summary>Harmony's own assembly list, filtered the same way.</summary>
        private static IEnumerable<Assembly> Loaded()
        {
            Assembly[] all;
            try { all = AppDomain.CurrentDomain.GetAssemblies(); }
            catch { yield break; }

            foreach (var assembly in all)
            {
                string full;
                try { full = assembly.FullName; } catch { continue; }
                if (full != null && full.StartsWith("Microsoft.VisualStudio", StringComparison.Ordinal)) continue;
                yield return assembly;
            }
        }

        /// <summary>
        /// Harmony's second pass: a type whose SIMPLE name matches. Dotted names cannot match one, so they
        /// stop here rather than paying for a search that can only fail.
        /// </summary>
        private static Type BySimpleName(string name)
        {
            if (name.IndexOf('.') >= 0 || name.IndexOf('+') >= 0 || name.IndexOf('/') >= 0) return null;

            foreach (var assembly in Loaded())
            {
                if (IsInterop(assembly)) continue;              // the one thing we must not enumerate
                foreach (var type in TypesOf(assembly))
                    if (type != null && type.Name == name) return type;
            }

            foreach (string entry in GameBySimpleName(name))
            {
                int bar = entry.IndexOf('|');
                if (bar <= 0) continue;
                try
                {
                    var assembly = Assembly.Load(new AssemblyName(entry.Substring(0, bar)));
                    var type = assembly?.GetType(entry.Substring(bar + 1), false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static IEnumerable<Type> TypesOf(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
            catch { return Array.Empty<Type>(); }
        }

        private static bool IsInterop(Assembly assembly)
        {
            if (string.IsNullOrEmpty(_interop)) return false;
            string location;
            try { location = assembly.IsDynamic ? null : assembly.Location; } catch { return true; }
            if (string.IsNullOrEmpty(location)) return false;
            try
            {
                return string.Equals(Path.GetDirectoryName(location), _interop.TrimEnd(Path.DirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Where the game keeps a type of this simple name, read once from metadata and remembered.
        /// </summary>
        private static IReadOnlyList<string> GameBySimpleName(string name)
        {
            if (!_gameIndexBuilt)
            {
                _gameIndexBuilt = true;
                _gameBySimpleName = BuildGameIndex();
            }
            return _gameBySimpleName != null && _gameBySimpleName.TryGetValue(name, out var hits)
                ? hits
                : Array.Empty<string>();
        }

        private static Dictionary<string, List<string>> BuildGameIndex()
        {
            if (string.IsNullOrEmpty(_interop)) return null;
            try
            {
                using var index = new Core.InteropIndex(_interop, Array.Empty<string>(), Array.Empty<string>());
                var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (string simple in index.SimpleNames())
                    foreach (var type in index.BySimpleName(simple))
                    {
                        string assembly = type.Module?.Assembly?.Name?.Name;
                        if (string.IsNullOrEmpty(assembly)) continue;
                        if (!map.TryGetValue(simple, out var list)) map[simple] = list = new List<string>();
                        list.Add(assembly + "|" + type.FullName.Replace('/', '+'));
                    }
                _log?.Msg($"[reflect] read {map.Count} game type name(s) from metadata for a name-only lookup.");
                return map;
            }
            catch (Exception e)
            {
                _log?.Warning("[reflect] could not read the game's type names: " + e.Message);
                return null;
            }
        }
    }
}
