using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A postfix that edits the returned list still gets one, after the return became an argument.
    /// </summary>
    /// <remarks>
    /// The fourth shape in the family, and the one where the value the patch works on stopped being a
    /// return value at all:
    /// <code>
    /// 0.4.5f2   List&lt;EDay&gt; CustomerData.GetOrderDays(float, float)
    /// 0.4.6     void          CustomerData.GetOrderDays(float, float, List&lt;EDay&gt; days)
    /// </code>
    /// A bridge already puts the old two-argument form back, so a mod's CALL keeps working. A mod that
    /// PATCHES the name meets what no bridge reaches: Harmony binds <c>__result</c> to the return value,
    /// and the method the game calls has none, so the patch will not compile - and takes its class with it.
    ///
    /// THE LIST IS THE RESULT, and that is what makes this repairable rather than merely detectable. The
    /// game clears and fills the list it was handed (CustomerData.cs:92) and every caller reads that same
    /// list straight afterwards (Customer.cs:742 is one of four), so a postfix that edits <c>__result</c>
    /// in place edits exactly what the game goes on to use. Tweakables adds days until the customer orders
    /// often enough, and never reassigns.
    ///
    /// THE ARGUMENT IS TAKEN BY POSITION. <c>__2</c> rather than the parameter's name: the name is
    /// metadata that a build can change, the position is the signature this rule already checked.
    ///
    /// A postfix that REPLACES the list instead of editing it cannot be served - the caller keeps the one
    /// it handed in - and is warned about rather than silently half-applied.
    /// </remarks>
    internal sealed class PatchesOnResultTurnedArgument : Fix
    {
        internal override string Id => "patches-on-result-turned-argument";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a postfix that edits the order-day list runs, now that the list is an argument";

        internal override string StandsDownBecause
            => "a mod that patches CustomerData.GetOrderDays loses that patch class - Tweakables stops "
             + "raising how often a customer orders.";

        private const string Target = "GetOrderDays";

        private static MelonLogger.Instance _log;
        private static readonly List<MethodInfo> Relayed = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            Relayed.Clear();

            var real = AccessTools.Method(typeof(CustomerData), Target,
                new[] { typeof(float), typeof(float), typeof(Il2CppSystem.Collections.Generic.List<EDay>) });
            if (real == null)
            {
                log.Warning($"[fix] {Id}: CustomerData.{Target} does not take the list as an argument on "
                          + "this build, so a patch written for the returned one stays where it is.");
                return false;
            }

            foreach (var patch in PostfixesAimedAt(typeof(CustomerData), Target, log))
            {
                Relayed.Add(patch);
                log.Msg($"[fix] {Id}: {patch.DeclaringType?.FullName} -> CustomerData.{Target}(.., days)");
            }

            if (Relayed.Count == 0) return false;

            new HarmonyLib.Harmony("doodesch.polyfill.resultargument").Patch(
                real, postfix: new HarmonyMethod(typeof(PatchesOnResultTurnedArgument), nameof(After)));

            log.Msg($"[fix] {Id}: {Relayed.Count} patch(es) now edit the list the game reads back.");
            return true;
        }

        /// <summary>Patch classes aimed at this name that want a result, which is the shape that cannot bind.</summary>
        private static IEnumerable<MethodInfo> PostfixesAimedAt(Type target, string name,
                                                                MelonLogger.Instance log)
        {
            foreach (var melon in MelonBase.RegisteredMelons)
            {
                var assembly = melon?.MelonAssembly?.Assembly;
                if (assembly == null) continue;
                if (assembly == typeof(PatchesOnResultTurnedArgument).Assembly) continue;

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
                    if (attribute.info.declaringType != target || attribute.info.methodName != name) continue;

                    var postfix = AccessTools.Method(candidate, "Postfix");
                    if (postfix == null || !postfix.IsStatic) continue;

                    var result = Result(postfix);
                    if (result == null) continue;                  // not a patch about the return value

                    if (result.ParameterType.IsByRef)
                        log.Warning($"[fix] patches-on-result-turned-argument: "
                                  + $"{postfix.DeclaringType?.Name}.{postfix.Name} takes the list by "
                                  + "reference. What it adds is kept; a list it replaces outright is not.");

                    yield return postfix;
                }
            }
        }

        private static ParameterInfo Result(MethodInfo patch)
        {
            foreach (var parameter in patch.GetParameters())
                if (parameter.Name == "__result") return parameter;
            return null;
        }

        /// <summary>Hand the list the game just filled to every patch that wanted it as a result.</summary>
        private static void After(object __instance, object __2)
        {
            foreach (var patch in Relayed)
            {
                try
                {
                    var wanted = patch.GetParameters();
                    var arguments = new object[wanted.Length];
                    for (int i = 0; i < wanted.Length; i++)
                        arguments[i] = wanted[i].Name switch
                        {
                            "__instance" => __instance,
                            "__result" => __2,
                            _ => wanted[i].ParameterType.IsValueType
                                ? Activator.CreateInstance(wanted[i].ParameterType)
                                : null,
                        };

                    patch.Invoke(null, arguments);
                }
                catch (Exception e)
                {
                    _log?.Warning($"[fix] patches-on-result-turned-argument: {patch.DeclaringType?.Name}."
                                + $"{patch.Name} threw: " + (e.InnerException ?? e).Message);
                }
            }
        }
    }
}
