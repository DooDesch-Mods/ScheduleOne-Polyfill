using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// One dead patch target costs one patch class, not the rest of the mod.
    /// </summary>
    /// <remarks>
    /// A mod registers its patches in one call, and the first class that will not bind ends that call.
    /// Everything after it in the assembly is never applied, and the log says only which class failed -
    /// so the visible symptom is a mod that loads, prints "Initialized", and does almost nothing.
    ///
    /// MEASURED ON DEAL OPTIMIZER, twice in one afternoon. Its first class patches
    /// <c>CounterofferInterface.ChangePrice</c>, which 0.4.6 deleted; with that repaired, the very next
    /// class patches <c>ChangeQuantity</c>, which 0.4.6 turned into two overloads, so Harmony cannot tell
    /// which one is meant and throws again. Both times the mod lost seven further patch classes that had
    /// nothing wrong with them - the shopping list, the product selector, the handover screen.
    ///
    /// So the exception is caught where it happens instead of where it lands. The class that failed is
    /// named at WARNING with the reason Harmony gave, which is strictly more than the player had before:
    /// the old behaviour printed one error and silently dropped the rest.
    ///
    /// This does not make a broken patch work. It stops a broken patch from taking working ones with it,
    /// which is the difference between a mod with one dead feature and a mod that does nothing.
    /// </remarks>
    internal static class PatchClassIsolation
    {
        private const string Id = "doodesch.polyfill.patchclass";

        private static MelonLogger.Instance _log;
        private static FieldInfo _container;

        internal static void Install(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var target = AccessTools.Method(typeof(PatchClassProcessor), nameof(PatchClassProcessor.Patch));
                if (target == null)
                {
                    log.Warning("[harmony] PatchClassProcessor.Patch is not where it was, so one bad patch "
                              + "target still costs a mod the rest of its patches.");
                    return;
                }

                // The type the class processor is working on, for the log line. Private, and worth reading
                // by hand rather than guessing from the exception text: the message names the PATCH method,
                // and what the player needs is the class that stopped.
                _container = AccessTools.Field(typeof(PatchClassProcessor), "containerType")
                             ?? AccessTools.Field(typeof(PatchClassProcessor), "container");

                new HarmonyLib.Harmony(Id).Patch(target,
                    finalizer: new HarmonyMethod(typeof(PatchClassIsolation), nameof(Isolate)));

                log.Msg("[harmony] a patch class that will not bind no longer stops the ones after it.");
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not isolate patch classes: " + e.Message);
            }
        }

        /// <summary>
        /// Swallow one class's failure and let the caller carry on with the next.
        /// </summary>
        /// <remarks>
        /// A finalizer returning null is Harmony's way of saying the exception is handled. The result is
        /// left null, which is what the caller does with it anyway - both MelonLoader's registration and
        /// Harmony's own PatchAll ignore the returned list.
        /// </remarks>
        private static Exception Isolate(Exception __exception, object __instance)
        {
            if (__exception == null) return null;

            string where = "a patch class";
            try
            {
                if (_container?.GetValue(__instance) is Type type) where = type.FullName;
            }
            catch { }

            var reason = __exception.InnerException ?? __exception;
            _log?.Warning($"[harmony] {where} did not bind and was skipped: {reason.Message} "
                        + "The mod's remaining patch classes were applied.");
            return null;
        }
    }
}
