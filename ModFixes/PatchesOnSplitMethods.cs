using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod's patch on a method the game split reaches the half the game actually calls.
    /// </summary>
    /// <remarks>
    /// The sibling of <see cref="PatchesOnGrownOverloads"/>, and the same repair: a bridge puts an old
    /// signature back so a mod's CALL works, a mod PATCHES that signature, and the patch lands on a method
    /// only the mod itself ever calls. It registers, it logs nothing, and it never runs.
    ///
    /// The difference is where the real method is. A grown overload has MORE parameters than the stand-in;
    /// a split one has FEWER, because the flag that used to choose became the choice of which method to
    /// call. See <see cref="SplitMethods"/> for why the flag then comes out right without a mapping.
    ///
    /// WHAT MADE THIS VISIBLE IS WORTH KEEPING. Over The Counter's manager panel stayed on screen and the
    /// next employee's panel opened on top of it, and every obvious explanation was wrong: the panel is
    /// the mod's own rather than the game's, the flag does not control teardown, and the clipboard repair
    /// that shipped the same day looked like it should have covered it. It took reading which method the
    /// game's exit path calls (ManagementClipboard.cs:72-78) to see that the mod's postfix had simply
    /// stopped being reachable.
    /// </remarks>
    internal sealed class PatchesOnSplitMethods : Fix
    {
        internal override string Id => "patches-on-split-methods";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a mod's patch on a method the game split in two reaches the half the game calls";

        internal override string StandsDownBecause
            => "a mod that patches ManagementClipboard.Close will only see the closes it performs itself, "
             + "so panels it opened stay on screen after the player puts the clipboard away.";

        private static MelonLogger.Instance _log;
        private static readonly List<MethodInfo> Relayed = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            Relayed.Clear();

            foreach (var entry in SplitMethods.All)
            {
                var type = AccessTools.TypeByName(entry.Type);
                if (type == null) continue;

                var standIn = Exactly(type, entry.Name, entry.StandInParameters);
                var real = Exactly(type, entry.Name, entry.RealParameters);
                if (standIn == null || real == null || standIn == real) continue;

                HarmonyLib.Patches info;
                try { info = HarmonyLib.Harmony.GetPatchInfo(standIn); }
                catch (Exception e) { log.Warning($"[fix] {Id}: " + e.Message); continue; }
                if (info == null) continue;

                int here = 0;
                foreach (var patch in info.Postfixes)
                {
                    if (patch.owner != null
                        && patch.owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) continue;
                    if (!Callable(patch.PatchMethod, log)) continue;

                    Relayed.Add(patch.PatchMethod);
                    here++;
                    log.Msg($"[fix] {Id}: {patch.owner} -> {type.Name}.{entry.Name}() "
                          + "(the half the player's own exit calls)");
                }

                if (here > 0)
                    new HarmonyLib.Harmony("doodesch.polyfill.repoint").Patch(
                        real, postfix: new HarmonyMethod(typeof(PatchesOnSplitMethods), nameof(After)));
            }

            if (Relayed.Count == 0) return false;
            log.Msg($"[fix] patches-on-split-methods: {Relayed.Count} patch(es) now also run on the half "
                  + "the game calls; they were on a stand-in Polyfill added for the old signature.");
            return true;
        }

        /// <summary>
        /// Can this patch be called with an instance and the flag the successor no longer takes?
        /// </summary>
        /// <remarks>
        /// MOVING THE PATCH DOES NOT WORK HERE, and that was the first attempt. Harmony does not hand a
        /// declared parameter a default when it cannot bind it - it refuses to compile the patch at all,
        /// which arrives as "IL Compile Error (unknown location)" and nothing else. The successor has no
        /// <c>preserveState</c>, so Over The Counter's postfix cannot be re-aimed at it.
        ///
        /// So the patch is CALLED rather than moved, the way SplitScreenPatches calls its empty stand-in.
        /// Only the two shapes that can be filled honestly are accepted: the instance, and a value type
        /// that gets its default - which for the flag this exists for means "an ordinary close", the truth
        /// on this path. Anything else is refused by name rather than guessed at.
        /// </remarks>
        private static bool Callable(MethodInfo patch, MelonLogger.Instance log)
        {
            if (patch == null || !patch.IsStatic) return false;

            foreach (var parameter in patch.GetParameters())
            {
                if (parameter.Name == "__instance") continue;
                if (parameter.ParameterType.IsValueType) continue;

                log.Warning($"[fix] {Id_}: {patch.DeclaringType?.Name}.{patch.Name} takes "
                          + $"'{parameter.Name}', which nothing on the other half can fill. Left alone.");
                return false;
            }
            return true;
        }

        private const string Id_ = "patches-on-split-methods";

        /// <summary>Run the relayed patches at the moment the game closes for real.</summary>
        private static void After(object __instance)
        {
            foreach (var patch in Relayed)
            {
                try
                {
                    var wanted = patch.GetParameters();
                    var arguments = new object[wanted.Length];
                    for (int i = 0; i < wanted.Length; i++)
                        arguments[i] = wanted[i].Name == "__instance"
                            ? __instance
                            : Activator.CreateInstance(wanted[i].ParameterType);

                    patch.Invoke(null, arguments);
                }
                catch (Exception e)
                {
                    _log?.Warning($"[fix] {Id_}: {patch.DeclaringType?.Name}.{patch.Name} threw: "
                                + (e.InnerException ?? e).Message);
                }
            }
        }

        /// <summary>The one method of that name with exactly these parameters, or null on a tie.</summary>
        private static MethodInfo Exactly(Type type, string name, string[] parameters)
        {
            MethodInfo found = null;

            foreach (var method in type.GetMethods(AccessTools.all))
            {
                if (method.Name != name || method.DeclaringType != type) continue;

                var actual = method.GetParameters();
                if (actual.Length != parameters.Length) continue;

                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                    if (actual[i].ParameterType.FullName != parameters[i]) { matches = false; break; }
                if (!matches) continue;

                if (found != null) return null;
                found = method;
            }
            return found;
        }
    }
}
