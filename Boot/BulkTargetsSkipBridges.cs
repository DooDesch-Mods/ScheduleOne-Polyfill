using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// A mod that patches every overload of a method through <c>TargetMethods()</c> does not get Polyfill's
    /// bridges in the list.
    /// </summary>
    /// <remarks>
    /// A bridge is a managed method the plugin wrote into the interop: it forwards to the game's own method and
    /// has no native function behind it, so there is nothing for the IL2CPP detour to hook and patching it
    /// fails. A mod never asks for one by name - it was written before the bridge existed. It gets one when it
    /// enumerates overloads in bulk: Expanded Storage 1.0.4's <c>StorageMenuOpenStoragePatch.TargetMethods</c>
    /// returns every <c>StorageMenu.Open</c>, which on 0.4.7 includes the <c>Open(StorageEntity)</c> bridge,
    /// and the failure took the mod's start-up down with it.
    ///
    /// SKIPPED, NOT MOVED, AND ONLY WHEN THE REAL METHOD IS THERE. A bridge is dropped when the method it
    /// forwards to is in the same list: the mod's patch is on the real method either way, and moving it there
    /// as well would run it twice. A bridge whose forward target is not in the list stays, because dropping it
    /// would leave the mod with no patch at all.
    ///
    /// ONE TARGET IS NEVER TOUCHED. <c>GetBulkMethods</c> also serves a single <c>TargetMethod()</c>, wrapped
    /// into a list of one. A patch that names a bridge that way means it: Over The Counter's
    /// <c>StorageMenuOpenPatch</c> names the old <c>StorageMenu.Open</c> and PatchesOnGrownOverloads moves it
    /// onto the method the game calls, and the handover stand-ins are relayed by PatchesOnDroppedArguments.
    /// An emptied list would end as "Undefined target method" and the class would not bind.
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
                              + "patches overloads in bulk may still be handed a bridge.");
                    return;
                }
                _container = AccessTools.Field(typeof(PatchClassProcessor), "containerType");
                new HarmonyLib.Harmony(Id).Patch(target,
                    postfix: new HarmonyMethod(typeof(BulkTargetsSkipBridges), nameof(WithoutBridges)));
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not keep bridges out of bulk patch targets: " + e.Message);
            }
        }

        private static void WithoutBridges(List<MethodBase> __result, object __instance)
        {
            if (__result == null || __result.Count < 2) return;

            // Judged against the list as the mod returned it, so a bridge that forwards to another bridge is
            // dropped with it and the order of the list does not matter.
            var asked = __result.ToArray();
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                var method = __result[i];
                if (!IsBridge(method)) continue;
                var real = ForwardTargetIn(method, asked);
                if (real == null) continue;
                __result.RemoveAt(i);
                var container = _container?.GetValue(__instance) as Type;
                _log?.Msg($"[harmony] {container?.FullName ?? "a patch class"}: not patching "
                        + $"{method.DeclaringType?.Name}.{method.Name}({method.GetParameters().Length} args) - a Polyfill "
                        + $"bridge with no native method; {real.Name}({real.GetParameters().Length} args), which it "
                        + "forwards to, is in the same list.");
            }
        }

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
