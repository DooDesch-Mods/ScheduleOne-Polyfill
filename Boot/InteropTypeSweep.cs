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

            /// <summary>For a self-sweep: the assembly whose base type the walk is looking for.</summary>
            /// <remarks>
            /// A type can only derive from something in this assembly if its own assembly references it,
            /// directly or through another one. That is the whole filter, and it is read from metadata
            /// rather than by loading anything - which is the point, because loading is what kills.
            /// </remarks>
            internal string Anchor;
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
        /// Methods that fetch the assembly list themselves and sweep it, so there is no argument to
        /// intercept.
        /// </summary>
        /// <remarks>
        /// The <see cref="Known"/> shape needs a method that TAKES an assembly - stand in front of it,
        /// answer nothing for an interop one, done. These take nothing. They call
        /// <c>AppDomain.CurrentDomain.GetAssemblies()</c> inside their own body and walk the result, so
        /// the only place to stand is inside the method.
        ///
        /// A prefix is the wrong tool here and would be worse than the crash: skipping
        /// <c>PreRegisterAllNpcPrefabsInternal</c> means S1API never registers a single custom NPC, and
        /// every mod that ships one breaks quietly instead of loudly.
        /// </remarks>
        private static readonly Sweep[] KnownSelfSweeps =
        {
            new Sweep
            {
                Type = "S1API.Entities.NPC",
                Method = "PreRegisterAllNpcPrefabsInternal",
                Mod = "S1API",
                Anchor = "S1API",
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
            foreach (var sweep in KnownSelfSweeps)
            {
                try
                {
                    var type = AccessTools.TypeByName(sweep.Type);
                    if (type == null) continue;                 // that mod is not installed

                    // Exact shape, not the first method of that name: an overload taking arguments
                    // is a different method with different behaviour, and rewriting it blind would be a
                    // guess dressed as a guard.
                    var target = AccessTools.DeclaredMethod(type, sweep.Method, Type.EmptyTypes);
                    if (target == null)
                    {
                        log.Warning($"[sweep] {sweep.Mod} has no {sweep.Method}() here, so its walk over "
                                  + "every loaded assembly is not guarded. If the game dies on startup "
                                  + "with no message, that is where to look.");
                        continue;
                    }

                    if (_anchor != null && _anchor != sweep.Anchor)
                    {
                        // One rewritten call site, one filter. A second self-sweep hunting a different
                        // base type would need its own, and quietly handing it this one would filter
                        // its walk by the wrong assembly.
                        log.Warning($"[sweep] {sweep.Mod}.{sweep.Method}() looks for {sweep.Anchor} but "
                                  + $"the filter is already set to {_anchor}, so it was not guarded.");
                        continue;
                    }
                    _anchor = sweep.Anchor;

                    _replaced = 0;
                    harmony.Patch(target,
                                  transpiler: new HarmonyMethod(typeof(InteropTypeSweep), nameof(WithoutInterop)));

                    // ONE, not "at least one". Binding and rewriting are two different successes,
                    // and a transpiler that matched nothing hands the method back unchanged with no
                    // error - which reads exactly like a guard that is in place. A second call site is
                    // not better news than none: it means the method was rewritten upstream, and what
                    // this rewrote is no longer the thing it was read against.
                    if (_replaced != 1)
                    {
                        log.Warning($"[sweep] {sweep.Mod}.{sweep.Method}() calls AppDomain.GetAssemblies "
                                  + $"{_replaced} time(s) where this expected exactly one, so it was not "
                                  + "guarded. The crash it protects against is still possible.");
                        continue;
                    }

                    log.Msg($"[sweep] {sweep.Mod} will only walk the loaded assemblies that name "
                          + $"{sweep.Anchor}; enumerating one of the others can kill the process "
                          + "without a message.");
                }
                catch (Exception e)
                {
                    log.Warning($"[sweep] could not guard {sweep.Mod}: {e.Message}");
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

        private static int _replaced;
        private static string _anchor;

        /// <summary>
        /// Swap the call to <c>AppDomain.GetAssemblies()</c> for one that leaves the interop assemblies
        /// out. Everything else in the method is untouched.
        /// </summary>
        /// <remarks>
        /// NAMED APART FROM <see cref="Mortals"/> ON PURPOSE. Harmony resolves a transpiler by name off
        /// a type, so two methods sharing one name is an ambiguity that only shows at patch time.
        ///
        /// ONE INSTRUCTION, because <c>GetAssemblies</c> is an instance method and the replacement is a
        /// static one taking the same <c>AppDomain</c>: it consumes the same stack slot and returns the
        /// same type, so nothing around it has to move. Labels and exception blocks are carried across,
        /// or a branch or a try that pointed at this instruction would point at nothing.
        /// </remarks>
        private static IEnumerable<CodeInstruction> WithoutInterop(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.DeclaredMethod(typeof(AppDomain),
                                                      nameof(AppDomain.GetAssemblies), Type.EmptyTypes);
            var ours = AccessTools.DeclaredMethod(typeof(InteropTypeSweep), nameof(Mortals),
                                                  new[] { typeof(AppDomain) });

            foreach (var instruction in instructions)
            {
                if (original != null && ours != null && instruction.Calls(original))
                {
                    _replaced++;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, ours)
                        .WithLabels(instruction.labels)
                        .WithBlocks(instruction.blocks);
                    continue;
                }
                yield return instruction;
            }
        }

        /// <summary>The loaded assemblies whose types could possibly derive from the anchor's.</summary>
        /// <remarks>
        /// NOT "everything except the interop assemblies", which is what this filtered at first and what
        /// turned out to be the wrong cut. GetTypes() on a MOD assembly loads that assembly's types, and
        /// loading a type resolves its fields - so a mod holding a static UnityEngine.GameObject reaches
        /// straight into the interop assembly the filter had just removed, and dies there anyway. That is
        /// a real report: a server with 55 mods, narrowed by its operator to one mod whose runtime class
        /// holds a GameObject, a LineRenderer and a dictionary of an interop type.
        ///
        /// So the cut is made where it holds: a type can only derive from the anchor assembly's base
        /// class if its own assembly references that assembly, directly or through another one. Every
        /// assembly that does not is dropped, its types are never loaded, and nothing it holds is
        /// reached. Nothing is lost, because none of them could have answered.
        ///
        /// Read from metadata with Cecil, which opens the file rather than loading it - the same trick
        /// the type searches above use, and for the same reason.
        ///
        /// Private, although the call to it now sits in somebody else's assembly: Harmony emits the
        /// patched body as a dynamic method that skips visibility checks, which is the same reason every
        /// transpiler may call its own private helpers. Making it public would not have helped anyway,
        /// since the type around it is internal.
        /// </remarks>
        private static Assembly[] Mortals(AppDomain domain)
        {
            var all = domain?.GetAssemblies() ?? Array.Empty<Assembly>();
            if (_anchor == null) return all;              // no filter set; leave the walk alone

            // Who names the anchor, one hop at a time. A mod subclassing S1API's NPC references S1API;
            // a mod subclassing THAT mod's class references the mod and not S1API, so the set has to
            // grow until it stops growing.
            /*
             * IF THE ANCHOR IS NOT LOADED, FILTER NOTHING. The anchor is a name in a table, and the
             * assembly it names could be renamed upstream tomorrow. Then no assembly matches, the walk
             * sees an empty list, and every custom NPC in the game stops registering - with the log
             * saying only that the guard installed fine. Handing the walk back untouched puts the old
             * risk back, which is the lesser of the two and the one that says so out loud.
             */
            bool anchorLoaded = false;
            foreach (var assembly in all)
            {
                if (!string.Equals(Simple(assembly), _anchor, StringComparison.OrdinalIgnoreCase)) continue;
                anchorLoaded = true;
                break;
            }

            if (!anchorLoaded)
            {
                Complain($"nothing loaded is called {_anchor}, so its walk was left alone rather than "
                       + "filtered down to nothing");
                return all;
            }

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _anchor };
            var references = new Dictionary<Assembly, string[]>();

            foreach (var assembly in all)
            {
                string[] named = NamesReferenced(assembly);
                if (named != null) references[assembly] = named;
            }

            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var pair in references)
                {
                    string self = Simple(pair.Key);
                    if (self == null || wanted.Contains(self)) continue;
                    foreach (string name in pair.Value)
                    {
                        if (!wanted.Contains(name)) continue;
                        wanted.Add(self);
                        grew = true;
                        break;
                    }
                }
            }

            var kept = new List<Assembly>();
            foreach (var assembly in all)
            {
                if (!references.TryGetValue(assembly, out var named))
                {
                    /*
                     * COULD NOT BE READ, so it is kept and named. Dropping it silently would mean a mod
                     * whose NPCs never register and no line saying why; keeping it is what happened
                     * before this filter existed. Rare enough that a warning is affordable, and the
                     * name is the only thing that makes it actionable.
                     */
                    if (assembly != null && !assembly.IsDynamic && !string.IsNullOrEmpty(SafeLocation(assembly)))
                    {
                        Complain($"could not read {Simple(assembly) ?? "an assembly"} to see whether it "
                               + $"references {_anchor}, so it is walked as before");
                        kept.Add(assembly);
                    }
                    continue;
                }

                string self = Simple(assembly);
                if (self != null && wanted.Contains(self)) kept.Add(assembly);
            }

            // Once, and in Release too. "The guard is installed" and "the guard narrowed anything" are
            // different claims, and only the second one is worth trusting - the numbers say which
            // happened, and how much of the walk never had to load a thing.
            if (!_said)
            {
                _said = true;
                _log?.Msg($"[sweep] {_anchor}'s walk sees {kept.Count} of {all.Length} loaded "
                        + $"assemblies - the rest cannot name {_anchor}, so none of their types are "
                        + "loaded to find out.");
            }

            return kept.ToArray();
        }

        private static bool _said;

        private static readonly HashSet<string> Complained = new(StringComparer.Ordinal);

        private static void Complain(string what)
        {
            if (!Complained.Add(what)) return;
            _log?.Warning("[sweep] " + what + ".");
        }

        private static string Simple(Assembly assembly)
        {
            try { return assembly?.GetName()?.Name; }
            catch { return null; }
        }

        private static string SafeLocation(Assembly assembly)
        {
            try { return assembly.Location; }
            catch { return null; }
        }

        /// <summary>The assemblies this one names, out of its metadata - nothing is loaded.</summary>
        /// <remarks>
        /// Null when the question cannot be answered: a dynamic assembly, one with no file, or one Cecil
        /// refuses. The caller decides what that means; it is not the same as "references nothing".
        /// </remarks>
        private static string[] NamesReferenced(Assembly assembly)
        {
            try
            {
                if (assembly == null || assembly.IsDynamic) return null;
                string location = SafeLocation(assembly);
                if (string.IsNullOrEmpty(location) || !File.Exists(location)) return null;

                using var module = Mono.Cecil.ModuleDefinition.ReadModule(location);
                var names = new List<string>(module.AssemblyReferences.Count);
                foreach (var reference in module.AssemblyReferences) names.Add(reference.Name);
                return names.ToArray();
            }
            catch { return null; }
        }
    }
}
