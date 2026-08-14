using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod's patch follows the method it was aimed at, when Polyfill is what it landed on.
    /// </summary>
    /// <remarks>
    /// THE SECOND HALF OF A REPAIR NOBODY SEES. When 0.4.6 gave <c>StorageMenu.Open</c> a closing callback,
    /// Polyfill put the old three-argument signature back so a mod's CALL keeps working. A mod that also
    /// PATCHES <c>Open</c> then resolves that same signature - and patches the method Polyfill added, which
    /// the game never calls. It registers cleanly, logs nothing, and does nothing. Backpack's sort and
    /// filter row is exactly that: the backpack opens, and the row it adds to the window does not appear.
    ///
    /// So every patch on one of those stand-ins is applied a second time to the method the game really
    /// calls. Harmony binds a patch's parameters BY NAME, and the old parameter list is a prefix of the new
    /// one, so a prefix written as <c>(StorageMenu __instance, string title, IItemSlotOwner owner)</c>
    /// binds on the four-argument form unchanged - it simply never asks for the argument it did not know
    /// about. That is what makes this a re-aim rather than a rewrite.
    ///
    /// Three limits, each deliberate:
    ///
    /// TRANSPILERS ARE LEFT WHERE THEY ARE. A transpiler is a rewrite of a specific method body, and the
    /// body it was written against is not the body it would meet. Moving one is not a re-aim, it is running
    /// somebody's edit against a text they have not read.
    ///
    /// THE STAND-IN KEEPS ITS PATCHES. Taking them off would be tidier and is not free: a mod may call its
    /// own patched method, and the patch coming off underneath it changes what that mod does.
    ///
    /// ONLY THE NAMED LIST. See <see cref="GrownOverloads"/> for why "anything with a longer sibling" is
    /// not the rule.
    /// </remarks>
    internal sealed class PatchesOnGrownOverloads : Fix
    {
        internal override string Id => "patches-on-grown-overloads";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a mod's patch on a method the game gave an extra argument reaches the real method";

        internal override string StandsDownBecause
            => "a mod that patches StorageMenu.Open will patch Polyfill's stand-in, which nothing calls, "
             + "so its addition to the storage window will not appear.";

        internal override bool Apply(MelonLogger.Instance log)
        {
            int moved = 0;

            foreach (var entry in GrownOverloads.All)
            {
                var type = AccessTools.TypeByName(entry.Type);
                if (type == null) continue;

                var standIn = Find(type, entry.Name, entry.OldParameters, exact: true);
                var real = Find(type, entry.Name, entry.OldParameters, exact: false);
                if (standIn == null || real == null || standIn == real) continue;

                moved += Move(standIn, real, log);
            }

            if (moved == 0) return false;
            log.Msg($"[fix] patches-on-grown-overloads: moved {moved} patch(es) onto the method the game "
                  + "calls; they were on a stand-in Polyfill added for the old signature.");
            return true;
        }

        /// <summary>
        /// The stand-in (exactly these parameters) or the method that replaced it (these and more).
        /// </summary>
        /// <remarks>
        /// The real one is required to have MORE parameters and to start with the same ones, which is the
        /// same test the bridge used to build the stand-in. Refuses on a tie rather than picking: two
        /// candidates means the shape here is not what this was written for.
        /// </remarks>
        private static MethodInfo Find(Type type, string name, string[] parameters, bool exact)
        {
            MethodInfo found = null;

            foreach (var method in type.GetMethods(AccessTools.all))
            {
                if (method.Name != name || method.DeclaringType != type) continue;

                var actual = method.GetParameters();
                if (exact ? actual.Length != parameters.Length : actual.Length <= parameters.Length) continue;

                bool matches = true;
                for (int i = 0; i < parameters.Length; i++)
                    if (actual[i].ParameterType.FullName != parameters[i]) { matches = false; break; }
                if (!matches) continue;

                if (found != null) return null;
                found = method;
            }
            return found;
        }

        /// <summary>Copy every prefix, postfix and finalizer from one method onto another.</summary>
        private static int Move(MethodInfo standIn, MethodInfo real, MelonLogger.Instance log)
        {
            HarmonyLib.Patches info;
            try { info = HarmonyLib.Harmony.GetPatchInfo(standIn); }
            catch (Exception e) { log.Warning("[fix] patches-on-grown-overloads: " + e.Message); return 0; }
            if (info == null) return 0;

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.repoint");
            int moved = 0;

            foreach (var patch in info.Prefixes) moved += One(harmony, real, patch, prefix: true, log);
            foreach (var patch in info.Postfixes) moved += One(harmony, real, patch, prefix: false, log);

            return moved;
        }

        private static int One(HarmonyLib.Harmony harmony, MethodInfo real, HarmonyLib.Patch patch,
                               bool prefix, MelonLogger.Instance log)
        {
            // Polyfill's own patches are not moved. Nothing here patches a stand-in, so this can only
            // fire if a later version does - and a repair re-applying itself is the kind of loop that is
            // much easier to prevent than to notice.
            if (patch.owner != null && patch.owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal))
                return 0;

            try
            {
                var method = new HarmonyMethod(patch.PatchMethod)
                {
                    priority = patch.priority,
                    before = patch.before,
                    after = patch.after,
                };

                harmony.Patch(real,
                              prefix: prefix ? method : null,
                              postfix: prefix ? null : method);

                log.Msg($"[fix] patches-on-grown-overloads: {patch.owner} -> "
                      + $"{real.DeclaringType?.Name}.{real.Name}({real.GetParameters().Length} args)");
                return 1;
            }
            catch (Exception e)
            {
                log.Warning($"[fix] patches-on-grown-overloads: could not move {patch.owner}'s patch: "
                          + e.Message);
                return 0;
            }
        }
    }
}
