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

                var harmony = new HarmonyLib.Harmony(Id);
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(DeclaredMethodFallback), nameof(Unambiguous)),
                    postfix: new HarmonyMethod(typeof(DeclaredMethodFallback), nameof(Fallback)));

                var property = AccessTools.Method(typeof(AccessTools), nameof(AccessTools.DeclaredProperty),
                    new[] { typeof(Type), typeof(string) });
                if (property != null)
                    harmony.Patch(property,
                        postfix: new HarmonyMethod(typeof(DeclaredMethodFallback), nameof(ForProperty)));

                log.Msg("[harmony] a patch aimed at a type this build renamed will be pointed at the "
                      + "member the game has.");
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

            // A METHOD THE BUILD RENAMED, answered with what took over. Nothing is written back for
            // these: a hook Polyfill writes is a name the game never calls, so a patch on it binds and
            // never fires, and relaying from the replacement afterwards cannot honour a prefix that
            // returns false. Tweakables suppresses the vanilla packaging animation exactly that way.
            // Redirecting the lookup puts the patch on the method the game really calls, where a prefix
            // is still a prefix.
            string renamed = RenamedMethods.Successor(type.FullName, name);
            if (renamed != null)
            {
                try
                {
                    var moved = AccessTools.Method(type, renamed, parameters, generics);
                    if (moved != null) { __result = Canonical(moved); return; }
                }
                catch { }
            }

            if (!RenamedTypes.IsStandIn(type.FullName)) return;

            try
            {
                var found = AccessTools.Method(type, name, parameters, generics);
                if (found == null || found.DeclaringType == type) return;
                __result = Canonical(found);
            }
            catch { }
        }


        /// <summary>
        /// The same method, looked up through the type that DECLARES it.
        /// </summary>
        /// <remarks>
        /// THIS ONE COST A CRASH, so it is worth stating exactly. Both redirects above answer a lookup on a
        /// stand-in with a method that lives on its base, and .NET hands back a MethodInfo whose
        /// ReflectedType is the stand-in it was asked through. Two MethodInfos differing only in
        /// ReflectedType are not equal and do not hash alike, so Harmony files them as two separate
        /// patch targets - while they are one native method.
        ///
        /// The second patcher then detours an address that is already detoured and captures the FIRST
        /// detour as its original. Every call re-enters, and the process dies of a stack overflow with no
        /// exception and nothing in the log: 0xc00000fd, roughly 1,561 identical reverse-P/Invoke frames.
        /// Measured on 0.4.6f13 - Tweakables patches HandoverScreenPriceSelector.SetPrice, this redirect
        /// sends it to AmountSelector.SetAmount, Polyfill's own amount-changed-after-override patches the
        /// same method through the base type, and opening a counteroffer reaches SetAmount through
        /// SetProduct -> ApplyFairPrice before any of it can be noticed.
        ///
        /// Answering with the declaring type's own MethodInfo gives both of them one key, and Harmony
        /// merges the two patches the way it does for any other pair.
        /// </remarks>
        private static MethodInfo Canonical(MethodInfo method)
        {
            if (method?.DeclaringType == null) return method;
            if (method.ReflectedType == method.DeclaringType) return method;

            try
            {
                var declared = AccessTools.DeclaredMethod(method.DeclaringType, method.Name,
                                                          Types(method.GetParameters()));
                return declared ?? method;
            }
            catch
            {
                // A lookup that fails leaves the method as it was. That is the behaviour before this
                // existed, and a redirect that answers nothing would be worse than one that answers a
                // method with the wrong ReflectedType.
                return method;
            }
        }

        private static Type[] Types(System.Reflection.ParameterInfo[] parameters)
        {
            var types = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) types[i] = parameters[i].ParameterType;
            return types;
        }

        /// <summary>
        /// A patch that names no parameters gets the GAME's method, not the one Polyfill added.
        /// </summary>
        /// <remarks>
        /// PUTTING A SIGNATURE BACK MAKES A NAME AMBIGUOUS, and Harmony's lookup answers that by throwing.
        /// <c>[HarmonyPatch(typeof(CustomerData), "GetOrderDays")]</c> resolved fine until Polyfill added
        /// the pre-0.4.6 form beside the current one; after that the same attribute produces
        /// <c>AmbiguousMatchException</c>, Harmony discards the patch class, and a mod that worked before
        /// the repair stops working because of it.
        ///
        /// Naming no parameters means "the method", and the method is the game's - the stand-in exists only
        /// so an old CALL resolves. So this answers with the game's own, and only ever for a name Polyfill
        /// is on record as having doubled.
        ///
        /// A prefix rather than a postfix because there is no result to correct: the original throws.
        /// </remarks>
        private static bool Unambiguous(Type type, string name, Type[] parameters, ref MethodInfo __result)
        {
            if (type == null || name == null || parameters != null) return true;
            if (!GrownOverloads.Doubled(type.FullName, name)) return true;

            try
            {
                MethodInfo game = null;
                foreach (var candidate in type.GetMethods(AccessTools.all))
                {
                    if (candidate.Name != name || candidate.DeclaringType != type) continue;
                    if (GrownOverloads.IsStandIn(type.FullName, name, candidate.GetParameters().Length))
                        continue;
                    if (game != null) return true;              // the game has two of its own; not ours to pick
                    game = candidate;
                }
                if (game == null) return true;

                __result = game;
                return false;
            }
            catch { return true; }
        }

        /// <summary>The same fallback for a property, which is how Harmony resolves a getter patch.</summary>
        /// <remarks>
        /// Lithium aims a postfix at <c>ATMInterface.remainingAllowedDeposit</c>, a getter. That route goes
        /// through <c>DeclaredProperty</c>, so repairing only <c>DeclaredMethod</c> left it dead while
        /// every method patch on the same renamed type came back to life.
        /// </remarks>
        private static void ForProperty(Type type, string name, ref PropertyInfo __result)
        {
            if (__result != null || type == null || name == null) return;
            if (!RenamedTypes.IsStandIn(type.FullName)) return;

            try
            {
                var found = AccessTools.Property(type, name);
                if (found == null || found.DeclaringType == type) return;
                __result = found;
            }
            catch { }
        }
    }
}
