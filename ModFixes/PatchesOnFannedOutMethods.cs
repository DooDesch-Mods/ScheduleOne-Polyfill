using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A patch on a method the game fanned out into one per case runs on each of them it fits.
    /// </summary>
    /// <remarks>
    /// The patch class names a method that is gone, so Harmony never bound it and nothing of it runs -
    /// Expanded Storage 1.0.4's postfix that sizes the customer slots when the handover screen opens is the
    /// case this was written for. See <see cref="FannedOutMethods"/> for why it is attached rather than called.
    ///
    /// ATTACHED, SO HARMONY DOES THE BINDING, and a patch is attached only where every parameter binds -
    /// Harmony refuses to compile one that does not, as "IL Compile Error (unknown location)". Checked here
    /// first, so a patch that fits nowhere is left alone with its reason in the log rather than failing there.
    /// </remarks>
    internal sealed class PatchesOnFannedOutMethods : Fix
    {
        internal override string Id => "patches-on-fanned-out-methods";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "a patch on a method the game split into one per case runs on each of them it fits";

        internal override string StandsDownBecause
            => "a mod patching HandoverScreen.Open never runs, because 0.4.7 opens the screen through one "
             + "Open_ method per mode.";

        private static readonly string[] Kinds = { "Prefix", "Postfix", "Finalizer" };

        internal override bool Apply(MelonLogger.Instance log)
        {
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fannedout");
            int attached = 0;

            foreach (var entry in FannedOutMethods.All)
            {
                var type = AccessTools.TypeByName(entry.Type);
                if (type == null) continue;
                // The old method is still there (or put back): the mod's patch bound on its own.
                if (type.GetMethods(AccessTools.all).Any(m => m.DeclaringType == type && m.Name == entry.OldName)) continue;

                var successors = type.GetMethods(AccessTools.all)
                    .Where(m => m.DeclaringType == type && entry.NowCalled.Contains(m.Name)).ToList();
                if (successors.Count == 0) continue;

                foreach (var (patchClass, kind, patch) in PatchesAimedAt(type, entry.OldName))
                {
                    int here = 0;
                    var refused = new List<string>();
                    foreach (var successor in successors)
                    {
                        string why = Unbound(patch, kind, successor, type);
                        if (why != null) { refused.Add($"{successor.Name} ({why})"); continue; }
                        try
                        {
                            var method = new HarmonyMethod(patch);
                            harmony.Patch(successor,
                                prefix: kind == "Prefix" ? method : null,
                                postfix: kind == "Postfix" ? method : null,
                                finalizer: kind == "Finalizer" ? method : null);
                            here++;
                        }
                        catch (Exception e) { refused.Add($"{successor.Name} ({e.Message})"); }
                    }
                    attached += here;
                    string name = $"{patchClass.FullName}.{patch.Name}";
                    if (here > 0)
                        log.Msg($"[fix] {Id}: {name} on {type.Name}.{entry.OldName} now runs on {here} of "
                              + $"{successors.Count} successor(s) ({entry.Because}).");
                    if (refused.Count > 0)
                        log.Warning($"[fix] {Id}: {name} does not fit " + string.Join(", ", refused) + ".");
                }
            }
            return attached > 0;
        }

        /// <summary>Why Harmony could not bind this patch on that method, or null when it can.</summary>
        private static string Unbound(MethodInfo patch, string kind, MethodInfo successor, Type type)
        {
            if (!patch.IsStatic) return "the patch is not static";
            var real = successor.GetParameters();
            foreach (var parameter in patch.GetParameters())
            {
                var wanted = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                switch (parameter.Name)
                {
                    case "__instance":
                        if (!wanted.IsAssignableFrom(type)) return "__instance is typed " + wanted.Name;
                        continue;
                    case "__originalMethod":
                        continue;
                    case "__runOriginal":
                        if (wanted != typeof(bool)) return "__runOriginal is not a bool";
                        continue;
                    case "__result":
                        if (successor.ReturnType == typeof(void)) return "it returns nothing to read as __result";
                        if (!wanted.IsAssignableFrom(successor.ReturnType)) return "__result is typed " + wanted.Name;
                        continue;
                    case "__exception":
                        if (kind == "Prefix") return "a prefix cannot take __exception";
                        continue;
                }
                if (parameter.Name.StartsWith("__", StringComparison.Ordinal))
                    return $"'{parameter.Name}' is positional or special, and the positions changed";
                var match = real.FirstOrDefault(p => p.Name == parameter.Name);
                if (match == null) return $"no '{parameter.Name}'";
                var have = match.ParameterType.IsByRef ? match.ParameterType.GetElementType() : match.ParameterType;
                if (!wanted.IsAssignableFrom(have)) return $"'{parameter.Name}' is {have.Name}, not {wanted.Name}";
            }
            return null;
        }

        /// <summary>
        /// The prefixes, postfixes and finalizers of every patch class aimed at this type and name.
        /// </summary>
        /// <remarks>
        /// Only mod assemblies are walked, and only their own types, for the reason
        /// PatchesOnNarrowedOverloads gives. A patch method is found by Harmony's naming convention or its
        /// attribute, the two ways Harmony itself finds one.
        /// </remarks>
        private static IEnumerable<(Type patchClass, string kind, MethodInfo patch)> PatchesAimedAt(Type target, string name)
        {
            foreach (var melon in MelonBase.RegisteredMelons)
            {
                var assembly = melon?.MelonAssembly?.Assembly;
                if (assembly == null || assembly == typeof(PatchesOnFannedOutMethods).Assembly) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException partial) { types = partial.Types; }
                catch { continue; }

                foreach (var candidate in types)
                {
                    if (candidate == null) continue;
                    Type declaring = null;
                    string method = null;
                    try
                    {
                        foreach (var attribute in candidate.GetCustomAttributes<HarmonyPatch>())
                        {
                            declaring ??= attribute.info?.declaringType;
                            method ??= attribute.info?.methodName;
                        }
                    }
                    catch { continue; }
                    if (declaring != target || method != name) continue;

                    foreach (var patch in candidate.GetMethods(AccessTools.all))
                    {
                        if (patch.DeclaringType != candidate) continue;
                        string kind = Kinds.FirstOrDefault(k => patch.Name == k);
                        if (patch.GetCustomAttribute<HarmonyPrefix>() != null) kind = "Prefix";
                        else if (patch.GetCustomAttribute<HarmonyPostfix>() != null) kind = "Postfix";
                        else if (patch.GetCustomAttribute<HarmonyFinalizer>() != null) kind = "Finalizer";
                        if (kind != null) yield return (candidate, kind, patch);
                    }
                }
            }
        }
    }
}
