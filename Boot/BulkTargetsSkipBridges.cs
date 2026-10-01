using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Boot
{
    /// <summary>
    /// A patch class that names its targets itself is not handed a Polyfill bridge.
    /// </summary>
    /// <remarks>
    /// A bridge is a managed method the plugin wrote into the interop: it forwards to the game's own method and
    /// has no native function behind it. Harmony takes its managed route for one, which compiles the bridge
    /// while the patch is being installed (ManagedMethodPatcher.DetourTo, ILHook, PrepareMethod), and that
    /// compile can end the process with an access violation in the runtime and no exception to catch.
    ///
    /// A mod never asks for a bridge on purpose - it was written before the bridge existed. It gets one from
    /// <c>TargetMethods()</c> when it enumerates overloads: Expanded Storage 1.0.4's
    /// <c>StorageMenuOpenStoragePatch</c> returns every <c>StorageMenu.Open</c>, stand-ins included. And from
    /// <c>TargetMethod()</c> when it names the old signature: Over The Counter's <c>StorageMenuOpenPatch</c>
    /// asks for <c>Open(StorageEntity)</c>.
    ///
    /// DROPPED WHEN THE REAL METHOD IS IN THE SAME LIST. The mod's patch is on the real method either way,
    /// and a second copy on the bridge would run twice for whoever calls the old signature.
    ///
    /// SWAPPED FOR THE REAL METHOD WHEN IT IS NOT. Only for a stand-in <see cref="GrownOverloads"/> lists, and
    /// only when the return type and the parameter names are the same: the old parameter list is a prefix of
    /// the new one and Harmony binds by name, so the patch binds unchanged. The game calls the real method and
    /// a mod calling the old signature reaches it through the bridge, so the patch runs once on both roads.
    /// The swap is noted in <see cref="ReaimedPatches"/> for the mod's row in the report.
    ///
    /// EVERY OTHER BRIDGE STAYS. A stand-in whose patches are relayed (PatchesOnDroppedArguments), the empty
    /// <c>SetIsOpen</c> of a split screen, a grown overload whose return type changed: a patch that names one
    /// means it. Taking it out of a list of one would end as "Undefined target method" and the class would
    /// not bind.
    ///
    /// ONLY THIS ROUTE. <c>GetBulkMethods</c> serves <c>TargetMethods()</c> and a single
    /// <c>TargetMethod()</c>. A target declared in a <c>[HarmonyPatch(type, name, argumentTypes)]</c>
    /// attribute does not pass through here; it lands on the stand-in and PatchesOnGrownOverloads moves it.
    ///
    /// A bridge is told apart by its body rather than a list: every generated interop method loads its
    /// <c>NativeMethodInfoPtr_</c> field, and a bridge loads none. That is the same test Il2CppInterop's own
    /// helper makes, done here without referencing the runtime the plugin loads before.
    /// </remarks>
    internal static class BulkTargetsSkipBridges
    {
        private const string Id = "doodesch.polyfill.bulktargets";

        private static MelonLogger.Instance _log;
        private static FieldInfo _container;
        private static bool _warnedBody, _warnedAssembly;

        internal static void Install(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var target = AccessTools.Method(typeof(PatchClassProcessor), "GetBulkMethods");
                if (target == null)
                {
                    log.Warning("[harmony] PatchClassProcessor.GetBulkMethods is not where it was, so a mod that "
                              + "names its own patch targets may still be handed a bridge.");
                    return;
                }
                _container = AccessTools.Field(typeof(PatchClassProcessor), "containerType");
                new HarmonyLib.Harmony(Id).Patch(target,
                    postfix: new HarmonyMethod(typeof(BulkTargetsSkipBridges), nameof(WithoutBridges)));
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not keep bridges out of patch targets: " + e.Message);
            }
        }

        private static void WithoutBridges(List<MethodBase> __result, object __instance)
        {
            if (__result == null || __result.Count == 0) return;

            // Judged against the list as the mod returned it, so a bridge that forwards to another bridge is
            // dropped with it and the order of the list does not matter.
            var asked = __result.ToArray();
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                var method = __result[i];
                if (!IsBridge(method)) continue;

                var container = _container?.GetValue(__instance) as Type;
                string who = container?.FullName ?? "a patch class";

                var listed = ForwardTargetIn(method, asked);
                if (listed != null)
                {
                    __result.RemoveAt(i);
                    _log?.Msg($"[harmony] {who}: not patching {Describe(method)} - a Polyfill bridge with no native "
                            + $"method; {Describe(listed)}, which it forwards to, is in the same list.");
                    continue;
                }

                var real = GameMethodBehind(method, who);
                if (real == null) continue;

                if (Has(__result, real)) __result.RemoveAt(i);
                else __result[i] = real;

                string owner = container?.Assembly?.GetName()?.Name;
                if (!string.IsNullOrEmpty(owner))
                    ReaimedPatches.Add(owner + "|" + real.DeclaringType?.FullName + "::" + real.Name);

                _log?.Msg($"[harmony] {who}: patching {Describe(real)} in place of {Describe(method)} - the old "
                        + "signature is a Polyfill bridge, and the game calls the method it forwards to.");
            }
        }

        /// <summary>The game's method a listed stand-in forwards to, when a patch written for one binds on the other.</summary>
        private static MethodInfo GameMethodBehind(MethodBase bridge, string who)
        {
            MethodInfo real;
            try { real = GrownOverloads.RealFor(bridge); }
            catch (Exception e)
            {
                _log?.Warning($"[harmony] {who}: could not look up what {Describe(bridge)} stands in for, so it "
                            + "stays the patch target: " + e.Message);
                return null;
            }
            if (real == null) return null;

            if (bridge is not MethodInfo standIn || standIn.ReturnType != real.ReturnType)
            {
                _log?.Msg($"[harmony] {who}: {Describe(bridge)} stays the patch target - {Describe(real)} returns "
                        + "something else, so a patch on the result would not bind.");
                return null;
            }

            var old = standIn.GetParameters();
            var now = real.GetParameters();
            for (int i = 0; i < old.Length; i++)
            {
                if (old[i].Name == now[i].Name) continue;
                _log?.Msg($"[harmony] {who}: {Describe(bridge)} stays the patch target - its parameter "
                        + $"'{old[i].Name}' is '{now[i].Name}' on {Describe(real)}, and Harmony binds by name.");
                return null;
            }
            return real;
        }

        private static bool Has(List<MethodBase> list, MethodBase method)
        {
            foreach (var other in list)
                if (Same(other, method)) return true;
            return false;
        }

        private static string Describe(MethodBase method)
            => $"{method.DeclaringType?.Name}.{method.Name}({method.GetParameters().Length} args)";

        /// <summary>An interop method with a body that never loads a native method pointer.</summary>
        private static bool IsBridge(MethodBase method)
        {
            try
            {
                var type = method?.DeclaringType;
                if (type == null || !InteropAssembly(type.Assembly)) return false;
                var il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return false;
                var module = method.Module;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x7E) continue;                     // ldsfld
                    int token = BitConverter.ToInt32(il, i + 1);
                    FieldInfo field;
                    try { field = module.ResolveField(token); }
                    catch { continue; }                               // a byte that only looked like ldsfld
                    if (field != null && field.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal))
                        return false;
                }
                return true;
            }
            catch (Exception e)
            {
                if (!_warnedBody)
                {
                    _warnedBody = true;
                    _log?.Warning($"[harmony] could not read the body of {method?.DeclaringType?.Name}.{method?.Name}, "
                                + "so it is treated as the game's own method and stays a patch target: " + e.Message);
                }
                return false;
            }
        }

        /// <summary>The method in <paramref name="asked"/> that <paramref name="bridge"/> calls, or null.</summary>
        private static MethodBase ForwardTargetIn(MethodBase bridge, MethodBase[] asked)
        {
            try
            {
                var il = bridge.GetMethodBody()?.GetILAsByteArray();
                if (il == null) return null;
                var module = bridge.Module;
                var typeArguments = bridge.DeclaringType?.IsGenericType == true ? bridge.DeclaringType.GetGenericArguments() : null;
                var methodArguments = bridge.IsGenericMethod ? bridge.GetGenericArguments() : null;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x28 && il[i] != 0x6F) continue;    // call, callvirt
                    int token = BitConverter.ToInt32(il, i + 1);
                    MethodBase called;
                    try { called = module.ResolveMethod(token, typeArguments, methodArguments); }
                    catch { continue; }                               // a byte that only looked like a call
                    if (called == null) continue;
                    foreach (var other in asked)
                        if (!Same(other, bridge) && Same(other, called)) return other;
                }
                return null;
            }
            catch (Exception e)
            {
                if (!_warnedBody)
                {
                    _warnedBody = true;
                    _log?.Warning($"[harmony] could not read what {bridge.DeclaringType?.Name}.{bridge.Name} forwards to, "
                                + "so it stays a patch target: " + e.Message);
                }
                return null;
            }
        }

        /// <summary>One method, whichever type it was reflected through.</summary>
        private static bool Same(MethodBase a, MethodBase b)
            => a != null && b != null && a.Module == b.Module && a.MetadataToken == b.MetadataToken;

        /// <summary>The generated assemblies all sit in MelonLoader's Il2CppAssemblies folder.</summary>
        private static bool InteropAssembly(Assembly assembly)
        {
            try
            {
                var location = assembly.Location;
                return !string.IsNullOrEmpty(location)
                    && location.Replace('\\', '/').Contains("/Il2CppAssemblies/", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                if (!_warnedAssembly)
                {
                    _warnedAssembly = true;
                    _log?.Warning($"[harmony] could not tell where {assembly?.GetName().Name} was loaded from, so its "
                                + "methods are treated as the game's own: " + e.Message);
                }
                return false;
            }
        }
    }
}
