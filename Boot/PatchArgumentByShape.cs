using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// A patch argument Harmony cannot place by name is placed by the shape it can only be.
    /// </summary>
    /// <remarks>
    /// Harmony binds a patch method's parameters to the original's BY NAME. A patch whose author spelled
    /// an argument differently from the game does not bind, and the whole patch class goes with it - an
    /// IL Compile Error with a stack trace that names no mod. Four of those in one session from one mod:
    /// its postfixes take <c>growContainer</c> where the game says <c>_growContainer</c>, and <c>bed</c>
    /// where it says <c>mushroomBed</c>.
    ///
    /// NOT A VERSION GAP, which is why no bridge reaches it: both spellings are the same in 0.4.5f2 and in
    /// 0.4.6f13. The patch could never have bound. Harmony's own answer is
    /// <c>[HarmonyArgument("_growContainer")]</c> or <c>[HarmonyArgument(0)]</c>, and that belongs in the
    /// mod - but a mod nobody is updating leaves a working feature switched off for a spelling.
    ///
    /// ONLY AFTER HARMONY HAS FAILED. This runs as a postfix on the resolver and does nothing unless the
    /// answer was -1, so every correctly spelled patch is bound by Harmony exactly as before and never
    /// reaches this code. It cannot change where a working patch lands.
    ///
    /// THREE RULES, EACH REFUSING WHEN IT IS NOT SURE. Leading underscores ignored; then the same
    /// ignoring case; then the type, when the original has exactly one parameter of it. More than one
    /// candidate at any step means -1 stands - a wrong argument silently bound is worse than a patch that
    /// does not load, because the mod would then run against the wrong object.
    /// </remarks>
    internal static class PatchArgumentByShape
    {
        private const string Id = "doodesch.polyfill.argshape";

        private static MelonLogger.Instance _log;
        private static readonly HashSet<string> Said = new(StringComparer.Ordinal);
        private static MethodInfo _resolve;                    // HarmonyMethod.GetOriginalMethod, internal

        internal static void Install(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                _resolve = AccessTools.Method(AccessTools.TypeByName("HarmonyLib.PatchTools"),
                                              "GetOriginalMethod",
                                              new[] { typeof(HarmonyMethod) });
                var extensions = AccessTools.TypeByName("HarmonyLib.PatchArgumentExtensions");
                var target = extensions == null
                    ? null
                    : AccessTools.Method(extensions, "GetArgumentIndex");

                if (target == null)
                {
                    log.Warning("[harmony] HarmonyLib.PatchArgumentExtensions.GetArgumentIndex is not where "
                              + "this expects it, so a patch whose argument is spelled differently from the "
                              + "game still fails to bind. Nothing else changes.");
                    return;
                }

                new HarmonyLib.Harmony(Id).Patch(
                    target, postfix: new HarmonyMethod(typeof(PatchArgumentByShape), nameof(After)));

                log.Msg("[harmony] an argument Harmony cannot place by name is placed by shape, when there "
                      + "is exactly one it can be.");
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not install the argument fallback: " + e.Message);
            }
        }

        /// <summary>Answer only where Harmony had none.</summary>
        private static void After(MethodInfo patch, string[] originalParameterNames,
                                  ParameterInfo patchParam, ref int __result)
        {
            if (__result != -1 || patchParam?.Name == null || originalParameterNames == null) return;

            int found = ByName(patchParam.Name, originalParameterNames);
            string how = "the name, allowing for an underscore, case or a dropped qualifier";

            if (found < 0) { found = ByType(patch, patchParam, originalParameterNames); how = "its type"; }
            if (found < 0) return;

            __result = found;

            string key = patch?.DeclaringType?.FullName + "." + patch?.Name + "(" + patchParam.Name + ")";
            if (!Said.Add(key)) return;

            _log?.Msg($"[harmony] {key} was bound to argument {found} "
                    + $"('{originalParameterNames[found]}') by {how}; the names do not match and the patch "
                    + "would otherwise have been thrown out with its whole class.");
        }

        /// <summary>The one argument whose name this is, underscores and then case ignored.</summary>
        private static int ByName(string wanted, string[] names)
        {
            int match = Only(names, wanted, StringComparison.Ordinal);
            if (match != -2) return match;

            match = Only(names, wanted, StringComparison.OrdinalIgnoreCase);
            if (match != -2) return match;

            return Tail(names, wanted);
        }


        /// <summary>
        /// The one argument whose name ENDS with this one, at a word boundary.
        /// </summary>
        /// <remarks>
        /// A patch calling the argument <c>bed</c> where the game calls it <c>mushroomBed</c> is naming the
        /// same thing with the qualifier dropped, and that is the last shape worth recognising. The
        /// boundary is what keeps it honest: <c>bed</c> matches <c>mushroomBed</c> because a capital starts
        /// the tail, and does not match <c>bedding</c> or <c>embed</c>, which are different words.
        ///
        /// Uniqueness is required as everywhere else here. Two arguments ending the same way mean the name
        /// cannot decide, and -1 stands.
        /// </remarks>
        private static int Tail(string[] names, string wanted)
        {
            string want = wanted.TrimStart('_');
            if (want.Length == 0) return -1;

            int found = -1;
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i]?.TrimStart('_');
                if (name == null || name.Length <= want.Length) continue;
                if (!name.EndsWith(want, StringComparison.OrdinalIgnoreCase)) continue;

                // The tail has to START a word: the character it begins with is upper case, and the one
                // before it is not. Otherwise "id" would match "valid".
                int at = name.Length - want.Length;
                if (!char.IsUpper(name[at]) || char.IsUpper(name[at - 1])) continue;

                if (found >= 0) return -1;
                found = i;
            }
            return found;
        }

        /// <summary>Index of the only match, -1 for none, -2 to say "try a looser rule".</summary>
        private static int Only(string[] names, string wanted, StringComparison how)
        {
            int found = -1;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == null) continue;
                if (!string.Equals(names[i].TrimStart('_'), wanted.TrimStart('_'), how)) continue;
                if (found >= 0) return -1;                 // two of them; the name cannot decide
                found = i;
            }
            return found >= 0 ? found : -2;
        }

        /// <summary>
        /// The one argument of that type, when the original has exactly one.
        /// </summary>
        /// <remarks>
        /// The last resort and the strictest: it needs the original's parameter list, which the names array
        /// alone does not give, so it is only attempted when the patch's own declaring method can be read
        /// back. A patch taking <c>MushroomBed bed</c> against a constructor with one MushroomBed is not a
        /// guess; two of them would be, and then this refuses.
        /// </remarks>
        private static int ByType(MethodInfo patch, ParameterInfo patchParam, string[] names)
        {
            var original = Original(patch, names);
            if (original == null) return -1;

            int found = -1;
            for (int i = 0; i < original.Length && i < names.Length; i++)
            {
                if (original[i].ParameterType != patchParam.ParameterType) continue;
                if (found >= 0) return -1;                 // two of that type; the type cannot decide
                found = i;
            }
            return found;
        }

        /// <summary>
        /// The method the names came from, found through the patch's own Harmony attributes.
        /// </summary>
        /// <remarks>
        /// Harmony hands the resolver names and not the method, so this asks the patch what it targets -
        /// the same question its attributes already answer. Null whenever that cannot be read, which
        /// leaves the type rule unused rather than guessed.
        /// </remarks>
        private static ParameterInfo[] Original(MethodInfo patch, string[] names)
        {
            try
            {
                if (_resolve == null) return null;

                var info = HarmonyMethodExtensions.GetFromMethod(patch);
                if (info == null) return null;

                var merged = HarmonyMethod.Merge(new List<HarmonyMethod>(info));
                if (merged == null) return null;

                var resolved = _resolve.Invoke(null, new object[] { merged }) as MethodBase;
                var parameters = resolved?.GetParameters();

                // The names Harmony passed are the ones it read off the target. If the method this found
                // has a different arity it is not that target, and using it would place arguments by a
                // list that does not belong to them.
                return parameters != null && parameters.Length == names.Length ? parameters : null;
            }
            catch { return null; }
        }
    }
}
