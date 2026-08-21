using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A patch aimed at a method whose argument type changed runs anyway.
    /// </summary>
    /// <remarks>
    /// See <see cref="NarrowedOverloads"/> for the shape and why the other two repairs in that family do
    /// not reach it. What is left after Harmony has refused the patch class is a postfix nobody calls, and
    /// this calls it.
    ///
    /// The patch class is found rather than repaired: Harmony threw it away at registration, so there is
    /// no patch info to read and no owner to ask. The mod's own assembly still carries the class and its
    /// attribute, so the attribute is what is read - the same declaration Harmony read, minus the lookup
    /// that could not choose.
    ///
    /// Two things are checked before anything is invoked, and both have cost a repair before:
    ///
    /// THE ATTRIBUTE MUST NAME NO ARGUMENT TYPES. One that does resolved fine on its own and was never
    /// broken; running its postfix a second time would double it.
    ///
    /// THE VALUE IS MATCHED BY NAME, NOT BY POSITION. Harmony binds that way, so a postfix written as
    /// <c>(int change)</c> is asking for the parameter called <c>change</c> and nothing else. A parameter
    /// this cannot fill honestly gets its default, and a reference type it cannot fill means the patch is
    /// left alone entirely rather than handed a null it never expected.
    /// </remarks>
    internal sealed class PatchesOnNarrowedOverloads : Fix
    {
        internal override string Id => "patches-on-narrowed-overloads";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a mod's patch on a method whose argument changed type runs when the game calls it";

        internal override string StandsDownBecause
            => "a mod that patches CounterofferInterface.ChangeQuantity loses that patch class outright - "
             + "Deal Optimizer stops re-evaluating a counteroffer after the quantity changes.";

        private static MelonLogger.Instance _log;

        /// <summary>The mod postfixes to run, and the parameter each one wants the game's value under.</summary>
        private static readonly List<(MethodInfo Patch, string ValueName)> Relayed = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            Relayed.Clear();

            foreach (var entry in NarrowedOverloads.All)
            {
                var type = AccessTools.TypeByName(entry.Type);
                if (type == null) continue;

                var real = Exactly(type, entry.Name, entry.RealParameters);
                if (real == null)
                {
                    log.Warning($"[fix] {Id}: {entry.Type}.{entry.Name} does not take "
                              + $"{string.Join(", ", entry.RealParameters)} on this build, so a patch aimed "
                              + "at the old argument stays where it is.");
                    continue;
                }

                int here = 0;
                foreach (var patch in PostfixesAimedAt(type, entry.Name, log))
                {
                    Relayed.Add((patch, entry.ParameterName));
                    here++;
                    log.Msg($"[fix] {Id}: {patch.DeclaringType?.FullName} -> {type.Name}.{entry.Name}"
                          + $"({string.Join(", ", entry.RealParameters)})");
                }

                if (here > 0)
                    new HarmonyLib.Harmony("doodesch.polyfill.narrowed").Patch(
                        real, postfix: new HarmonyMethod(typeof(PatchesOnNarrowedOverloads), nameof(After)));
            }

            if (Relayed.Count == 0) return false;
            log.Msg($"[fix] {Id}: {Relayed.Count} patch(es) now run on the method the game calls; Harmony "
                  + "could not bind them because the argument changed type.");
            return true;
        }

        /// <summary>
        /// The postfix methods of every patch class aimed at this type and name without naming arguments.
        /// </summary>
        /// <remarks>
        /// Only mod assemblies are walked, and only their own types. An interop assembly is never touched:
        /// enumerating one costs the process, which is the whole reason the plugin has a reflection layer
        /// of its own.
        /// </remarks>
        private static IEnumerable<MethodInfo> PostfixesAimedAt(Type target, string name,
                                                                MelonLogger.Instance log)
        {
            foreach (var melon in MelonBase.RegisteredMelons)
            {
                var assembly = melon?.MelonAssembly?.Assembly;
                if (assembly == null) continue;
                if (assembly == typeof(PatchesOnNarrowedOverloads).Assembly) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException partial) { types = partial.Types; }
                catch { continue; }

                foreach (var candidate in types)
                {
                    if (candidate == null) continue;

                    HarmonyPatch attribute;
                    try { attribute = candidate.GetCustomAttribute<HarmonyPatch>(); }
                    catch { continue; }
                    if (attribute?.info == null) continue;
                    if (attribute.info.declaringType != target) continue;
                    if (attribute.info.methodName != name) continue;

                    // An attribute that names its arguments resolved on its own and was never broken.
                    if (attribute.info.argumentTypes != null) continue;

                    var postfix = AccessTools.Method(candidate, "Postfix");
                    if (postfix == null || !postfix.IsStatic) continue;
                    if (!Callable(postfix, log)) continue;

                    yield return postfix;
                }
            }
        }

        /// <summary>Can every parameter of this patch be filled honestly?</summary>
        private static bool Callable(MethodInfo patch, MelonLogger.Instance log)
        {
            foreach (var parameter in patch.GetParameters())
            {
                if (parameter.Name == "__instance") continue;
                if (parameter.ParameterType.IsValueType) continue;

                log.Warning($"[fix] patches-on-narrowed-overloads: {patch.DeclaringType?.Name}.{patch.Name} "
                          + $"takes '{parameter.Name}', which the game's own call cannot fill. Left alone.");
                return false;
            }
            return true;
        }

        /// <summary>Run the relayed postfixes with the value the game was given.</summary>
        private static void After(object __instance, float change)
        {
            foreach (var (patch, valueName) in Relayed)
            {
                try
                {
                    var wanted = patch.GetParameters();
                    var arguments = new object[wanted.Length];
                    for (int i = 0; i < wanted.Length; i++)
                    {
                        if (wanted[i].Name == "__instance") { arguments[i] = __instance; continue; }

                        arguments[i] = wanted[i].Name == valueName
                            ? Convert.ChangeType(change, wanted[i].ParameterType)
                            : Activator.CreateInstance(wanted[i].ParameterType);
                    }

                    patch.Invoke(null, arguments);
                }
                catch (Exception e)
                {
                    _log?.Warning($"[fix] patches-on-narrowed-overloads: {patch.DeclaringType?.Name}."
                                + $"{patch.Name} threw: " + (e.InnerException ?? e).Message);
                }
            }
        }

        /// <summary>The one method of that name with exactly these parameters, or null on a tie.</summary>
        private static MethodInfo Exactly(Type type, string name, string[] parameters)
        {
            MethodInfo found = null;

            foreach (var method in type.GetMethods(AccessTools.all))
            {
                if (method.Name != name || method.DeclaringType != type) continue;

                var actual = method.GetParameters();
                if (actual.Length != parameters.Length) continue;

                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                    if (actual[i].ParameterType.FullName != parameters[i]) { matches = false; break; }
                if (!matches) continue;

                if (found != null) return null;
                found = method;
            }
            return found;
        }
    }
}
