using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Boot
{
    /// <summary>
    /// A patch aimed at a renamed type reaches the method the game actually has.
    /// </summary>
    /// <remarks>
    /// PUTTING THE TYPE BACK IS ONLY HALF THE REPAIR, and the missing half was invisible because the
    /// project's own probe used the wrong lookup to check it. When 0.4.6 renamed
    /// <c>MixingStationCanvas</c> to <c>MixingStationInterface</c>, Polyfill puts the old name back as a
    /// class deriving from the new one - so the type loads, a field of that type resolves, and reflection
    /// finds <c>Open</c> on it through the base. Harmony does not:
    ///
    /// <code>
    /// AccessTools.DeclaredMethod: Could not find method for type
    ///     Il2CppScheduleOne.UI.Stations.MixingStationCanvas and name Open and parameters
    /// HarmonyException: Undefined target method for patch method
    ///     static void BackPack.MixingStationCanvasPatch::Open(MixingStationCanvas __instance)
    /// </code>
    ///
    /// <c>DeclaredMethod</c> means declared: it does not walk the base chain, by design, because a patch
    /// aimed at a base method through a derived name is usually a mistake. Here it is the opposite - the
    /// derived name IS the base type, under the spelling the mod was compiled against. So the fallback is
    /// allowed for exactly the stand-ins Polyfill wrote and for nothing else, and only when Harmony has
    /// already come back empty.
    ///
    /// The win is that the patch lands on the REAL method. <c>MixingStationInterface.Open(MixingStation)</c>
    /// is what the game calls, so a prefix on it runs - which is not true of anything Polyfill could write
    /// onto the stand-in itself.
    ///
    /// Installed in <c>OnPreModsLoaded</c> because that is the last moment before the first mod's
    /// <c>PatchAll</c>, and a patch that failed to register cannot be repaired afterwards - Harmony throws
    /// the whole patch class away.
    /// </remarks>
    internal static class DeclaredMethodFallback
    {
        private const string Id = "doodesch.polyfill.declaredmethod";

        internal static void Install(MelonLogger.Instance log)
        {
            try
            {
                var target = AccessTools.Method(typeof(AccessTools), nameof(AccessTools.DeclaredMethod),
                    new[] { typeof(Type), typeof(string), typeof(Type[]), typeof(Type[]) });
                if (target == null)
                {
                    log.Warning("[harmony] AccessTools.DeclaredMethod is not where it was, so patches aimed "
                              + "at a renamed type will not find it. Repairs are unaffected.");
                    return;
                }

                new HarmonyLib.Harmony(Id).Patch(target,
                    postfix: new HarmonyMethod(typeof(DeclaredMethodFallback), nameof(Fallback)));

                log.Msg("[harmony] a patch aimed at a type this build renamed will be pointed at the "
                      + "method the game has.");
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not install the lookup fallback: " + e.Message);
            }
        }

        /// <summary>
        /// Only when Harmony found nothing, only on a stand-in, and only up its own base chain.
        /// </summary>
        /// <remarks>
        /// <c>AccessTools.Method</c> is the one that walks bases, so the answer is Harmony's own and not a
        /// second implementation of its rules - including how it reads the parameter list and the generics.
        /// </remarks>
        private static void Fallback(Type type, string name, Type[] parameters, Type[] generics,
                                     ref MethodInfo __result)
        {
            if (__result != null || type == null || name == null) return;
            if (!RenamedTypes.IsStandIn(type.FullName)) return;

            try
            {
                var found = AccessTools.Method(type, name, parameters, generics);
                if (found == null || found.DeclaringType == type) return;
                __result = found;
            }
            catch { }
        }
    }
}
