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
    /// MEASURED ON DEAL OPTIMIZER, and twice over: its first class patches
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
    ///
    /// AND IT HAS TO TAKE THE FAILED ENTRY BACK OUT, which the first version did not. Harmony registers a
    /// patch into the target's shared PatchInfo BEFORE it builds the wrapper, so a class that throws
    /// during the build leaves its entry behind. Every later patch of that same method - by any mod, or
    /// by Polyfill itself - rebuilds the wrapper, meets the same bad entry, and fails the same way.
    /// Measured: Tweakables' postfix asks for <c>__result</c> on a method 0.4.6 made void, and afterwards
    /// a relay of ours could not attach to that method under any signature at all.
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
                // rather than taking from the exception text: that message names the PATCH method, and what
                // the player needs is the class that stopped. Null on a Harmony that renamed it, which the
                // log line handles.
                _container = AccessTools.Field(typeof(PatchClassProcessor), "containerType");

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
            int removed = 0;
            try { removed = TakeBackOut(_container?.GetValue(__instance) as Type); } catch { }

            _log?.Warning($"[harmony] {where} did not bind and was skipped: {reason.Message} "
                        + "The mod's remaining patch classes were applied."
                        + (removed > 0 ? $" {removed} half-registered patch(es) were taken back out." : ""));
            return null;
        }

        /// <summary>
        /// Remove what the failed class managed to register, so it cannot poison the next patch.
        /// </summary>
        /// <remarks>
        /// Found by walking the patched methods rather than by resolving the class's target: resolving is
        /// exactly what failed a moment ago - an ambiguous name, a missing method - so asking the same
        /// question again answers nothing. Harmony already knows which methods carry a patch, and every
        /// entry names the method that supplied it, so the failed class's entries are the ones whose patch
        /// method it declares.
        ///
        /// Removed one at a time by patch method, never by owner id: a mod may hold a working patch on the
        /// same method from another class, and taking that away to tidy up would break something that was
        /// never broken.
        /// </remarks>
        private static int TakeBackOut(Type container)
        {
            if (container == null) return 0;

            int removed = 0;
            var harmony = new HarmonyLib.Harmony(Id);

            foreach (var original in HarmonyLib.Harmony.GetAllPatchedMethods())
            {
                Patches info;
                try { info = HarmonyLib.Harmony.GetPatchInfo(original); }
                catch { continue; }
                if (info == null) continue;

                foreach (var patch in Everything(info))
                {
                    if (patch.PatchMethod?.DeclaringType != container) continue;
                    try { harmony.Unpatch(original, patch.PatchMethod); removed++; }
                    catch (Exception e)
                    {
                        _log?.Warning($"[harmony] {container.FullName}: its half-registered patch on "
                                    + $"{original.Name} could not be taken back out: {e.Message}");
                    }
                }
            }

            return removed;
        }

        private static IEnumerable<Patch> Everything(Patches info)
        {
            foreach (var patch in info.Prefixes) yield return patch;
            foreach (var patch in info.Postfixes) yield return patch;
            foreach (var patch in info.Transpilers) yield return patch;
            foreach (var patch in info.Finalizers) yield return patch;
        }
    }
}
