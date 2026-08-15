using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// Keep a mod's sweep over every loaded assembly out of the interop ones.
    /// </summary>
    /// <remarks>
    /// <c>Assembly.GetTypes()</c> on an IL2CPP interop assembly is a landmine, and this project has the
    /// minidump: it forces every type in the assembly to load, one of them will not, and the process dies
    /// inside coreclr. No exception, no log line, no crash message. <see cref="Dynamic.ReflectionFallback"/>
    /// defuses it for Harmony, which walks every assembly to resolve a type name.
    ///
    /// A mod doing the same walk by hand is not covered by that, and the failure is identical:
    /// <code>
    /// Fatal error. Internal CLR error. (0x80131506)
    ///    at System.Reflection.Assembly.GetTypes()
    ///    at CustomCommandsFramework.CustomCommandsHelper.GetSafeTypes(Assembly)
    ///    at CustomCommandsFramework.AutoRegisterCommands.RegisterCommands()
    /// </code>
    /// The mod is careful - it catches ReflectionTypeLoadException and everything else - and none of that
    /// helps, because a fatal runtime error is not an exception to catch.
    ///
    /// NOT A GENERAL PATCH ON <c>GetTypes</c>. Intercepting it for the whole process would cover every mod
    /// at once and is the wrong trade: MelonLoader and Il2CppInterop enumerate types themselves, and
    /// answering "none" to one of their calls would break a boot that works today, for everyone, to repair
    /// one that fails sometimes, for a few. Named methods only, one line each.
    ///
    /// AND NOT A MOD FIX, which is where this was nearly written. Those run on the first frame the game can
    /// answer them, and the call happens in <c>OnInitializeMelon</c> - long before. It has to be here, in
    /// the plugin, at the one point where the mod assemblies are loaded and none of them has been
    /// initialised yet.
    ///
    /// TWO SHAPES OF OFFENDER, AND THEY NEED OPPOSITE ANSWERS.
    ///
    /// One hands a single assembly to <c>GetTypes</c> and is looking for types from OTHER MODS carrying
    /// its own attribute. An interop assembly has none of those, so answering it with nothing loses
    /// nothing - the mod was looking straight past the assembly that kills it.
    ///
    /// The other walks every assembly itself, looking for a GAME type by the end of its name, and there
    /// nothing would be the wrong answer: the interop assembly is exactly where the answer is. That one is
    /// answered out of the file's metadata instead, which finds the same type without loading any.
    /// </remarks>
    internal static class InteropTypeSweep
    {
        private sealed class Sweep
        {
            internal string Type;
            internal string Method;
            internal string Mod;

            /// <summary>The signature to bind to, for the searches. Null for the <see cref="Known"/> shape,
            /// which is always <c>(Assembly)</c>.</summary>
            internal Type[] Takes;

            /// <summary>Which prefix answers it. Two searches can want the same metadata and hand it back
            /// through different signatures.</summary>
            internal string Answer;
        }

        /// <summary>
        /// Methods known to hand an interop assembly to <c>GetTypes</c>, by name.
        /// </summary>
        /// <remarks>
        /// One line per offender, added when a crash report names one. A list rather than a rule, for the
        /// same reason everything else here is: a rule would have to guess which sweeps are safe.
        /// </remarks>
        private static readonly Sweep[] Known =
        {
            new Sweep
            {
                Type = "CustomCommandsFramework.CustomCommandsHelper",
                Method = "GetSafeTypes",
                Mod = "Custom Commands Framework",
            },
        };

        /// <summary>
        /// Methods that search every assembly for a type by the END of its name, and die doing it.
        /// </summary>
        /// <remarks>
        /// A different shape from <see cref="Known"/> and it needs a different answer. Those hand one
        /// assembly to <c>GetTypes</c>, so skipping that assembly is enough. These do the walk themselves
        /// and there is no argument to intercept - the whole search has to be replaced.
        ///
        /// Answering "nothing" would be wrong here, which is what makes the two cases different. Ultimate
        /// Mod Menu reaches this looking for <c>ScheduleOne.Police...</c>, a GAME type - the very thing the
        /// interop assembly holds. It only gets this far because its earlier, targeted lookups missed the
        /// <c>Il2Cpp</c> prefix, so the fallback is doing real work and has to keep doing it.
        /// </remarks>
        /// <remarks>
        /// ONE MOD CAN HAVE MORE THAN ONE OF THESE, and finding the first is no reason to stop looking.
        /// Ultimate Mod Menu has two, in different namespaces, written differently and reached from
        /// different features - guarding only <c>Core.ReflectionUtil</c> moved the death from
        /// <c>InstallPolicePatches</c> to <c>InstallClipboardPatches</c> and looked, in a log, like a
        /// different bug. When a crash report names one, grep the mod for the other spellings before
        /// calling it fixed.
        /// </remarks>
        private static readonly Sweep[] KnownSearches =
        {
            new Sweep
            {
                Type = "UltimateModMenu.Core.ReflectionUtil",
                Method = "FindTypeBySuffix",
                Mod = "Ultimate Mod Menu",
                Takes = new[] { typeof(string) },
                Answer = nameof(Search),
            },
            new Sweep
            {
                Type = "UltimateModMenu.Workforce.GameTypeFinder",
                Method = "Find",
                Mod = "Ultimate Mod Menu",
                Takes = new[] { typeof(string), typeof(string[]) },
                Answer = nameof(FindByName),
            },
            new Sweep
            {
                Type = "UltimateModMenu.Core.ReflectionUtil",
                Method = "FindType",
                Mod = "Ultimate Mod Menu",
                Takes = new[] { typeof(string[]) },
                Answer = nameof(FindByNames),
            },
        };

        private static MelonLogger.Instance _log;
        private static string _interop;
        private static int _skipped;
        private static int _searched;

        internal static void Install(MelonLogger.Instance log, string interopDirectory)
        {
            _log = log;
            _interop = interopDirectory;
            if (string.IsNullOrEmpty(_interop)) return;

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.sweeps");

            foreach (var sweep in Known)
            {
                try
                {
                    var type = AccessTools.TypeByName(sweep.Type);
                    if (type == null) continue;                 // that mod is not installed

                    var target = AccessTools.Method(type, sweep.Method, new[] { typeof(Assembly) });
                    if (target == null)
                    {
                        log.Warning($"[sweep] {sweep.Mod} has no {sweep.Method}(Assembly) here, so its walk "
                                  + "over the interop assemblies is not guarded. If the game dies on "
                                  + "startup with no message, that is where to look.");
                        continue;
                    }

                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(InteropTypeSweep), nameof(Skip)));
                    log.Msg($"[sweep] {sweep.Mod} will not be handed an interop assembly to enumerate; "
                          + "doing that kills the process without a message.");
                }
                catch (Exception e)
                {
                    log.Warning($"[sweep] could not guard {sweep.Mod}: {e.Message}");
                }
            }

            foreach (var search in KnownSearches)
            {
                try
                {
                    var type = AccessTools.TypeByName(search.Type);
                    if (type == null) continue;

                    var target = AccessTools.Method(type, search.Method, search.Takes);
                    if (target == null)
                    {
                        log.Warning($"[sweep] {search.Mod} has no {search.Type}.{search.Method} of that "
                                  + "shape here, so its search over every loaded assembly is not guarded. "
                                  + "If the game dies on startup with no message, that is where to look.");
                        continue;
                    }

                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(InteropTypeSweep), search.Answer));
                    log.Msg($"[sweep] {search.Mod} ({search.Method}) searches for types by name without "
                          + "enumerating the game's generated assemblies; doing that kills the process "
                          + "without a message.");
                }
                catch (Exception e)
                {
                    log.Warning($"[sweep] could not guard {search.Mod}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Answer "a type whose name ends with this" without loading a single type to find out.
        /// </summary>
        /// <remarks>
        /// The name of every type in an interop assembly is in its metadata, and Cecil reads metadata
        /// without asking the runtime for anything. So the search runs on the file, and only the ONE type
        /// that matched is then asked for by full name - which loads that type and no other. That is the
        /// whole difference from <c>GetTypes()</c>, which loads all of them and dies on the first that
        /// will not.
        ///
        /// The interop assemblies are searched first, and deliberately: a caller that gets here is asking
        /// for a game type under a name the targeted lookups already failed to find, which in practice
        /// means it spelled it without the <c>Il2Cpp</c> prefix. Everything else is searched afterwards the
        /// way the original did it, because <c>GetTypes()</c> on a mod assembly is not the dangerous case.
        /// </remarks>
        private static bool Search(string requested, ref Type __result)
        {
            __result = null;
            try
            {
                string suffix = requested;
                if (requested.IndexOf('.') >= 0 && requested.StartsWith("Il2Cpp", StringComparison.Ordinal))
                    suffix = requested.Substring("Il2Cpp".Length);

                __result = InInterop(requested, suffix) ?? Elsewhere(requested, suffix);

                if (++_searched == 1)
                    _log?.Msg("[sweep] answered a mod's type search from the game's assembly metadata "
                            + "instead of letting it load every type there is. Reading them that way is "
                            + "what takes the process down.");
            }
            catch (Exception e) { _log?.Warning("[sweep] type search failed: " + e.Message); }

            return false;
        }

        /// <summary>
        /// The same answer for a search that asks by SIMPLE name, with a few full names to try first.
        /// </summary>
        /// <remarks>
        /// Simple name ONLY on the fallback, and that is not laziness: the caller compares
        /// <c>t.Name</c>, and matching on the end of the full name instead would answer "NPC" with
        /// "PoliceNPC" - a type it never asked for, handed back as if it had.
        /// </remarks>
        private static bool FindByName(string simpleName, string[] fullNames, ref Type __result)
        {
            __result = null;
            try
            {
                foreach (string full in fullNames ?? Array.Empty<string>())
                {
                    __result = Named(full);
                    if (__result != null) { Announce(); return false; }
                }

                __result = InInterop(simpleName, null) ?? Elsewhere(simpleName, null);
                Announce();
            }
            catch (Exception e) { _log?.Warning("[sweep] type search failed: " + e.Message); }

            return false;
        }

        /// <summary>
        /// A search that only ever asks for full names, one after another.
        /// </summary>
        /// <remarks>
        /// This one has no <c>GetTypes()</c> in it at all, and it still killed the process:
        /// <code>
        /// at System.Reflection.RuntimeAssembly.GetType(QCallAssembly, String, Boolean, Boolean, ...)
        /// at UltimateModMenu.Core.ReflectionUtil.FindType(System.String[])
        /// </code>
        /// The caller asks <c>assembly.GetType(name, throwOnError: false, ignoreCase: true)</c>, and the
        /// third argument is the whole problem: a case-insensitive lookup cannot go straight to a name, it
        /// has to walk the type table comparing - which on an interop assembly is the same landmine as
        /// enumerating on purpose.
        ///
        /// TWO EARLIER VERSIONS OF THIS FILE CALLED IT THAT WAY THEMSELVES, in the pass that was documented
        /// as "the safe part". The comparison belongs in managed code over names read from metadata, and
        /// only the exact name that came back may be handed to the runtime.
        /// </remarks>
        private static bool FindByNames(string[] names, ref Type __result)
        {
            __result = null;
            try
            {
                foreach (string name in names ?? Array.Empty<string>())
                {
                    __result = Named(name);
                    if (__result != null) break;
                }
                Announce();
            }
            catch (Exception e) { _log?.Warning("[sweep] type search failed: " + e.Message); }

            return false;
        }

        /// <summary>
        /// One full name, resolved without ever asking the runtime to match case-insensitively.
        /// </summary>
        private static Type Named(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (IsInterop(assembly))
                    {
                        // Case folded here, against names Cecil read out of the file, so the runtime only
                        // ever sees a spelling it can look up directly.
                        string exact = Exactly(assembly, fullName);
                        if (exact == null) continue;

                        var found = assembly.GetType(exact, false, false);
                        if (found != null) return found;
                        continue;
                    }

                    var elsewhere = assembly.GetType(fullName, false, true);
                    if (elsewhere != null) return elsewhere;
                }
                catch { }
            }
            return null;
        }

        /// <summary>The metadata's own spelling of a full name, or null if this assembly has no such type.</summary>
        private static string Exactly(Assembly assembly, string fullName)
        {
            foreach (string known in NamesOf(assembly))
                if (known.Equals(fullName, StringComparison.OrdinalIgnoreCase)) return known;
            return null;
        }

        private static void Announce()
        {
            if (++_searched != 1) return;
            _log?.Msg("[sweep] answered a mod's type search from the game's assembly metadata instead of "
                    + "letting it load every type there is. Reading them that way is what takes the "
                    + "process down.");
        }

        /// <summary>The matching type in one of the interop assemblies, found in metadata and then loaded
        /// on its own.</summary>
        private static Type InInterop(string requested, string suffix)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsInterop(assembly)) continue;

                string name = NameIn(assembly, requested, suffix);
                if (name == null) continue;

                try
                {
                    // Case-SENSITIVE, because the name came out of this assembly's own metadata and
                    // ignoreCase makes the runtime walk the type table - see FindByNames.
                    var found = assembly.GetType(name, false, false);
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
        }

        /// <summary>The full name of the first type in this assembly's metadata that answers to the
        /// request, or null.</summary>
        private static string NameIn(Assembly assembly, string requested, string suffix)
        {
            foreach (string full in NamesOf(assembly))
            {
                if (full.Equals(requested, StringComparison.OrdinalIgnoreCase)) return full;

                int dot = full.LastIndexOf('.');
                string simple = dot < 0 ? full : full.Substring(dot + 1);

                // A null suffix means the caller compares simple names only. Testing the end of the full
                // name there would answer "NPC" with "PoliceNPC".
                if (suffix != null && full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return full;
                if (simple.Equals(requested, StringComparison.OrdinalIgnoreCase)) return full;
            }
            return null;
        }

        /// <summary>Every public type name in an assembly, read once out of its metadata.</summary>
        private static List<string> NamesOf(Assembly assembly)
        {
            if (Names.TryGetValue(assembly, out var names)) return names;
            names = ReadNames(assembly);
            Names[assembly] = names;
            return names;
        }

        private static readonly Dictionary<Assembly, List<string>> Names = new();

        private static List<string> ReadNames(Assembly assembly)
        {
            var names = new List<string>();
            try
            {
                using var module = Mono.Cecil.ModuleDefinition.ReadModule(assembly.Location);
                foreach (var type in module.Types)
                {
                    if (!type.IsPublic) continue;
                    names.Add(type.FullName);
                }
            }
            catch (Exception e) { _log?.Warning("[sweep] could not read " + assembly.GetName().Name + ": " + e.Message); }
            return names;
        }

        /// <summary>The original search, over the assemblies where enumerating types is safe.</summary>
        private static Type Elsewhere(string requested, string suffix)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsInterop(assembly)) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException partial)
                {
                    var kept = new List<Type>();
                    foreach (var one in partial.Types) if (one != null) kept.Add(one);
                    types = kept.ToArray();
                }
                catch { continue; }

                foreach (var type in types)
                {
                    string full = type.FullName ?? type.Name;
                    if (full.Equals(requested, StringComparison.OrdinalIgnoreCase)) return type;
                    if (suffix != null && full.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return type;
                    if (type.Name.Equals(requested, StringComparison.OrdinalIgnoreCase)) return type;
                }
            }
            return null;
        }

        /// <summary>Answer an interop assembly with nothing, and let every other one through.</summary>
        private static bool Skip(Assembly assembly, ref IEnumerable<Type> __result)
        {
            if (!IsInterop(assembly)) return true;

            __result = Array.Empty<Type>();
            if (++_skipped == 1)
                _log?.Msg("[sweep] skipped the game's generated assemblies while a mod enumerated types. "
                        + "They carry no mod commands, and reading them that way is what takes the process "
                        + "down.");
            return false;
        }

        /// <summary>
        /// Is this one of MelonLoader's generated interop assemblies?
        /// </summary>
        /// <remarks>
        /// By folder rather than by name: the folder IS the definition, and a mod is free to be called
        /// anything. A dynamic assembly has no location and is never one of these.
        /// </remarks>
        private static bool IsInterop(Assembly assembly)
        {
            if (assembly == null) return false;
            try
            {
                if (assembly.IsDynamic) return false;
                string location = assembly.Location;
                if (string.IsNullOrEmpty(location)) return false;

                return string.Equals(Path.GetDirectoryName(location),
                                     _interop.TrimEnd(Path.DirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
