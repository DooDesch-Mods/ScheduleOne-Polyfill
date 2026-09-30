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
    /// SKIPPED, NOT MOVED. The bridge forwards to an overload the same enumeration already returns, so the
    /// mod's patch is on the real method either way; moving it there as well would run it twice. A patch that
    /// names a bridge on purpose - the handover stand-ins, whose patches PatchesOnDroppedArguments relays - is
    /// a single target, not a bulk one, and is not touched here.
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
            if (__result == null || __result.Count == 0) return;
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                var method = __result[i];
                if (!IsBridge(method)) continue;
                __result.RemoveAt(i);
                var container = _container?.GetValue(__instance) as Type;
                _log?.Msg($"[harmony] {container?.FullName ?? "a patch class"}: not patching "
                        + $"{method.DeclaringType?.Name}.{method.Name}({method.GetParameters().Length} args) - a Polyfill "
                        + "bridge with no native method; the overload it forwards to is in the same list.");
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
            catch { return false; }
        }

        /// <summary>The generated assemblies all sit in MelonLoader's Il2CppAssemblies folder.</summary>
        private static bool InteropAssembly(Assembly assembly)
        {
            try
            {
                var location = assembly.Location;
                return !string.IsNullOrEmpty(location)
                    && location.Replace('\\', '/').Contains("/Il2CppAssemblies/", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
