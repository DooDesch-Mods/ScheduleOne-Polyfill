using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A patch on a method 0.4.6 replaced with two differently-named ones fires again.
    /// </summary>
    /// <remarks>
    /// THE HALF THAT MAKES THE STAND-IN WORTH ANYTHING, and the same argument as
    /// <see cref="SplitScreenPatches"/> makes for the five station canvases. The plugin writes the old
    /// signature back so a mod's patch resolves and its class registers; the game, compiled against the
    /// new names, never calls it. So this postfixes the methods the game DOES call and has them run the
    /// stand-in, with the values that path would have passed.
    ///
    /// Where it differs from the station version is what it can express. That one forwards an argument -
    /// the station instance - and is written in terms of one signature. This one passes constants read
    /// off the replacement bodies, which is what <c>ObjectSelector.CloseAndSubmit</c> and
    /// <c>CloseAndCancel</c> need: they take nothing, and the two flags the old <c>Close</c> took are
    /// decided by which of them ran.
    ///
    /// NOTHING IS WIRED FOR NOBODY. A stand-in nothing patched gets no postfixes, because two reflected
    /// calls on every close is a cost with no reader.
    ///
    /// AND NOTHING FAILS QUIETLY. Every refusal below says which entry and why, because the whole point
    /// of this fix is that a mod stops failing silently - doing that silently ourselves would be the
    /// same bug one level down.
    /// </remarks>
    internal sealed class PatchesOnReplacedMethods : Fix
    {
        internal override string Id => "patches-on-replaced-methods";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a patch on a method 0.4.6 renamed into two runs again, from both of the methods that "
             + "replaced it";

        internal override string StandsDownBecause
            => "a mod patching one of these will register and never fire, because the game calls the two "
             + "methods that replaced it and neither carries the old name.";

        /// <summary>Which stand-in a replacement should call, and with what. Keyed by the real method.</summary>
        private sealed class Relay
        {
            internal MethodInfo StandIn;
            internal object[] Arguments;
        }

        private static readonly Dictionary<MethodBase, Relay> Relays = new();
        private static MelonLogger.Instance _log;
        private static readonly HashSet<string> Complained = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            int wired = 0;
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.replacedmethods");

            foreach (var entry in ReplacedMethods.All)
            {
                var type = AccessTools.TypeByName(entry.Type);
                if (type == null) continue;               // the type is not on this build; not our business

                var wanted = Types(entry.Parameters);
                if (wanted == null)
                {
                    Complain(entry.Type, "one of the parameter types " + entry.OldName + " takes is not "
                                       + "on this build, so the stand-in cannot be identified without "
                                       + "guessing which overload was meant.");
                    continue;
                }

                var standIn = AccessTools.Method(type, entry.OldName, wanted);
                if (standIn == null)
                {
                    // The plugin refused, or a later build changed the shape. Either way the mod is broken
                    // and this cannot help - but nobody should have to guess that from silence.
                    Complain(entry.Type, $"{entry.OldName} is not on the type, so the plugin did not put "
                                       + "it back. A mod patching it will not load.");
                    continue;
                }

                if (!IsPatchedByAnybodyElse(standIn)) continue;

                foreach (var replacement in entry.Replacements)
                {
                    var takes = Types(replacement.Parameters);
                    if (takes == null)
                    {
                        Complain(entry.Type, "one of the parameter types " + replacement.Name + " takes "
                                           + "is not on this build.");
                        continue;
                    }

                    var real = AccessTools.Method(type, replacement.Name, takes);
                    if (real == null)
                    {
                        Complain(entry.Type, $"{replacement.Name} is not on this build, so a patch on "
                                           + $"{entry.OldName} cannot be carried to it.");
                        continue;
                    }

                    try
                    {
                        Relays[real] = new Relay { StandIn = standIn, Arguments = replacement.Arguments };
                        harmony.Patch(real, postfix: new HarmonyMethod(
                            typeof(PatchesOnReplacedMethods), nameof(After)));
                        wired++;
                    }
                    catch (Exception e)
                    {
                        Complain(entry.Type, $"{replacement.Name} could not be patched: {e.Message}");
                    }
                }
            }

            if (wired == 0) return false;
            log.Msg($"[fix] patches-on-replaced-methods: {wired} method(s) call the old name again, so a "
                  + "patch aimed at it runs at the moment it used to.");
            return true;
        }

        /// <summary>
        /// The named types, or null when one of them is not on this build.
        /// </summary>
        /// <remarks>
        /// Null has to be handled by the caller and never passed on. AccessTools.Method reads a null
        /// parameter list as "any signature", so handing this straight through would quietly match an
        /// overload nobody named - the exact class of mistake this whole file exists to stop.
        /// </remarks>
        private static Type[] Types(string[] names)
        {
            var found = new Type[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                found[i] = AccessTools.TypeByName(names[i]);
                if (found[i] == null) return null;
            }
            return found;
        }

        /// <summary>Did anything other than Polyfill patch it? Wiring for nobody costs two calls a close.</summary>
        private static bool IsPatchedByAnybodyElse(MethodBase method)
        {
            try
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(method);
                if (info == null) return false;
                foreach (var owner in info.Owners)
                    if (!owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) return true;
                return false;
            }
            catch (Exception e)
            {
                // Reading Harmony's own bookkeeping should not throw. If it does, wire it: a needless
                // relay costs two calls, a missing one costs the repair.
                _log?.Warning("[fix] patches-on-replaced-methods: could not read who patched "
                            + method.Name + " (" + e.Message + "), so it was wired anyway.");
                return true;
            }
        }

        /// <summary>Run the old method now, with what this path would have handed it.</summary>
        private static void After(object __instance, MethodBase __originalMethod)
        {
            if (__instance == null || __originalMethod == null) return;
            if (!Relays.TryGetValue(__originalMethod, out var relay)) return;

            try { relay.StandIn.Invoke(__instance, relay.Arguments); }
            catch (Exception e)
            {
                // Once per method, and loud. A mod's patch not firing is invisible from the outside -
                // the mod is loaded, nothing throws, and its feature is simply absent.
                Complain(relay.StandIn.DeclaringType?.FullName ?? "?",
                         $"calling {relay.StandIn.Name} from {__originalMethod.Name} threw "
                       + $"{e.GetType().Name}: {e.Message}. A patch on it is not running.");
            }
        }

        private static void Complain(string type, string what)
        {
            string key = type + "/" + what;
            if (!Complained.Add(key)) return;
            _log?.Warning($"[fix] patches-on-replaced-methods: {type}: {what}");
        }
    }
}
