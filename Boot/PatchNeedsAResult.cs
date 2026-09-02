using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// A postfix that wants a result from a method returning none is stopped before Harmony tries.
    /// </summary>
    /// <remarks>
    /// Harmony binds <c>__result</c> to the original's return value. Against a void method there is
    /// nothing to bind, so the wrapper will not compile - every time, on every build. Harmony reports that
    /// from inside PatchClassProcessor.Patch, which is why <see cref="PatchClassIsolation"/> cannot
    /// prevent the message: by the time a finalizer sees the exception the log already carries a stack
    /// trace naming no mod.
    ///
    /// AND POLYFILL REPAIRS IT ANYWAY. This is the shape of a return value that became an argument -
    /// <c>List&lt;EDay&gt; GetOrderDays(float, float)</c> became
    /// <c>void GetOrderDays(float, float, List&lt;EDay&gt;)</c> - and ModFixes/PatchesOnResultTurnedArgument
    /// puts the patch onto the real method with the filled list handed in as its result. That repair
    /// works; what it cannot do is un-log the attempt Harmony made first. So the attempt is not made.
    ///
    /// NOT HIDING A FAILURE. The failure is announced here instead, once, with the mod's own class named -
    /// which is more than Harmony's message carried. And it is announced whether or not a fix picks the
    /// patch up afterwards, because whether one does is not knowable from here.
    ///
    /// ONLY THIS SHAPE. A patch that names an argument the original does not have is a different case and
    /// belongs to <see cref="PatchArgumentByShape"/>, which makes those bind rather than skipping them.
    /// Anything this cannot read leaves Harmony to try, exactly as before.
    /// </remarks>
    internal static class PatchNeedsAResult
    {
        private const string Id = "doodesch.polyfill.needsaresult";

        private static MelonLogger.Instance _log;
        private static FieldInfo _container;
        private static FieldInfo _methods;
        private static MethodInfo _resolve;

        internal static void Install(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var target = AccessTools.Method(typeof(PatchClassProcessor),
                                                nameof(PatchClassProcessor.Patch));
                _container = AccessTools.Field(typeof(PatchClassProcessor), "containerType");
                _methods = AccessTools.Field(typeof(PatchClassProcessor), "patchMethods");
                _resolve = AccessTools.Method(AccessTools.TypeByName("HarmonyLib.PatchTools"),
                                              "GetOriginalMethod",
                                              new[] { typeof(HarmonyMethod) });

                if (target == null || _methods == null || _resolve == null)
                {
                    log.Warning("[harmony] Harmony's class processor is not the shape this reads, so a "
                              + "postfix wanting a result from a void method still reports itself as a "
                              + "stack trace naming no mod. Nothing else changes.");
                    return;
                }

                new HarmonyLib.Harmony(Id).Patch(
                    target, prefix: new HarmonyMethod(typeof(PatchNeedsAResult), nameof(Before)));

                log.Msg("[harmony] a postfix asking a void method for its result is named and skipped "
                      + "before Harmony tries it.");
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not install the result check: " + e.Message);
            }
        }

        private static bool Before(object __instance, ref List<MethodInfo> __result)
        {
            string why = Wrong(__instance, out string where);
            if (why == null) return true;

            _log?.Warning($"[harmony] {where} was skipped before Harmony tried it: {why} Polyfill repairs "
                        + "this shape separately where it can; the attempt itself could only fail.");

            // What Patch returns when it patched nothing. Harmony's own PatchAll ignores the list.
            __result = new List<MethodInfo>();
            return false;
        }

        private static string Wrong(object processor, out string where)
        {
            where = "a patch class";
            try
            {
                if (_container?.GetValue(processor) is Type type) where = type.FullName;

                var list = _methods.GetValue(processor) as System.Collections.IEnumerable;
                if (list == null) return null;

                FieldInfo info = null;
                foreach (var entry in list)
                {
                    if (entry == null) continue;
                    info ??= AccessTools.Field(entry.GetType(), "info");
                    if (info?.GetValue(entry) is not HarmonyMethod method || method.method == null) continue;

                    if (!Wants(method.method, "__result")) continue;

                    var original = _resolve.Invoke(null, new object[] { method }) as MethodBase;
                    if (original == null) continue;                    // let Harmony say what it thinks

                    bool nothingBack = original is ConstructorInfo
                                    || (original as MethodInfo)?.ReturnType == typeof(void);
                    if (!nothingBack) continue;

                    return $"{method.method.Name} takes __result and {original.DeclaringType?.Name}."
                         + $"{original.Name} returns nothing.";
                }
            }
            catch
            {
                // Unreadable means unjudged: Harmony tries, and the log is what it was before.
                return null;
            }
            return null;
        }

        private static bool Wants(MethodInfo patch, string name)
        {
            foreach (var parameter in patch.GetParameters())
                if (parameter.Name == name) return true;
            return false;
        }
    }
}
