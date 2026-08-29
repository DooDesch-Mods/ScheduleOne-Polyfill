using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A patch on a station screen's old open-or-close method fires again.
    /// </summary>
    /// <remarks>
    /// THE HALF THAT MAKES THE OTHER HALF WORTH ANYTHING. 0.4.6 replaced
    /// <c>SetIsOpen(station, open)</c> with <c>Open(station)</c> and <c>Close()</c>. The plugin writes the
    /// old signature back so a patch aimed at it resolves - without that, Harmony throws the whole patch
    /// class away, and Backpack lost its canvas handling five times over because of it. But an empty
    /// method nobody calls is a patch that never runs.
    ///
    /// So the game calls it: a postfix on the real <c>Open</c> passes the station and <c>true</c>, one on
    /// <c>Close</c> passes null and <c>false</c>. A mod's postfix then sees exactly what it used to - the
    /// same instance, the same flag, at the same moment.
    ///
    /// NULL FOR THE STATION ON CLOSE, and it has to be: <c>Close()</c> takes no station and the screen
    /// does not keep one. Every use of that argument in the mods this was read against is on the way IN
    /// (Backpack reads only <c>open</c> and <c>__instance</c>), but a mod that dereferences it while
    /// closing would get a null it never got before. That is the one place this is not the old behaviour,
    /// and it is named here rather than discovered later.
    ///
    /// Runs after every mod has loaded, so nothing about the order of patches matters: Polyfill's postfix
    /// on <c>Open</c> and the mod's postfix on <c>SetIsOpen</c> are on different methods.
    /// </remarks>
    internal sealed class SplitScreenPatches : Fix
    {
        internal override string Id => "split-screen-patches";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a patch on a station screen's SetIsOpen runs again, from the two methods that replaced it";

        internal override string StandsDownBecause
            => "a mod patching SetIsOpen on a station screen will register and never fire, because 0.4.6 "
             + "split that method in two and calls neither under the old name.";

        private static readonly Dictionary<MethodBase, MethodInfo> Hooks = new();
        private static readonly HashSet<string> _complained = new();
        private static MelonLogger.Instance _log;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            int wired = 0;
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.splitscreens");

            foreach (var entry in SplitScreens.All)
            {
                var type = AccessTools.TypeByName(entry.Type);
                if (type == null) continue;

                var hook = Declared(type, "SetIsOpen");
                if (hook == null) continue;                     // the plugin did not put it back

                // Nothing patched it, so there is nothing to carry. Wiring it anyway would add two calls
                // per station open for no reader.
                if (!IsPatched(hook)) continue;

                // Up the base chain, because two of these five were renamed as well: the type the mod
                // names is Polyfill's stand-in and Open lives on what it derives from. Patching the
                // inherited MethodInfo patches the real one, which is the whole point.
                var station = AccessTools.TypeByName(entry.Station);
                if (station == null) continue;

                var open = AccessTools.Method(type, "Open", new[] { station });
                var close = AccessTools.Method(type, "Close", Type.EmptyTypes);
                if (open == null || close == null) continue;

                try
                {
                    Hooks[open] = hook;
                    Hooks[close] = hook;

                    harmony.Patch(open, postfix: new HarmonyMethod(
                        typeof(SplitScreenPatches), nameof(AfterOpen)));
                    harmony.Patch(close, postfix: new HarmonyMethod(
                        typeof(SplitScreenPatches), nameof(AfterClose)));
                    wired++;
                }
                catch (Exception e)
                {
                    log.Warning($"[fix] split-screen-patches: {type.Name}: {e.Message}");
                }
            }

            if (wired == 0) return false;
            log.Msg($"[fix] split-screen-patches: {wired} station screen(s) call SetIsOpen again, so a "
                  + "patch aimed at it runs at the moment it used to.");
            return true;
        }

        private static MethodInfo Declared(Type type, string name)
        {
            foreach (var method in type.GetMethods(AccessTools.all))
                if (method.Name == name && method.DeclaringType == type) return method;
            return null;
        }

        /// <summary>Did anything other than Polyfill patch it? Wiring for nobody costs two calls a frame.</summary>
        private static bool IsPatched(MethodBase method)
        {
            try
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(method);
                if (info == null) return false;
                foreach (var owner in info.Owners)
                    if (!owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) return true;
                return false;
            }
            catch { return false; }
        }

        private static void AfterOpen(object __instance, object[] __args) => Call(__instance, __args, true);

        private static void AfterClose(object __instance) => Call(__instance, null, false);

        /// <summary>
        /// Hand the old method the values it used to be handed.
        /// </summary>
        /// <remarks>
        /// Invoked by reflection rather than called: the signature differs per screen - four take a third
        /// argument and one does not - and the station type is different every time. Two reflected calls
        /// when a station screen opens is nothing next to what opening one does anyway.
        /// </remarks>
        private static void Call(object instance, object[] arguments, bool open)
        {
            try
            {
                if (instance == null) return;

                MethodInfo hook = null;
                foreach (var pair in Hooks)
                    if (pair.Key.DeclaringType != null
                        && pair.Key.DeclaringType.IsInstanceOfType(instance)
                        && pair.Key.Name == (open ? "Open" : "Close"))
                    { hook = pair.Value; break; }
                if (hook == null) return;

                var wanted = hook.GetParameters();
                var values = new object[wanted.Length];
                values[0] = open && arguments is { Length: > 0 } ? arguments[0] : null;
                values[1] = open;
                if (wanted.Length > 2) values[2] = true;        // removeUI, the default the old callers used

                hook.Invoke(instance, values);
            }
            catch (Exception e)
            {
                // Once per screen, and said out loud. This method exists so a mod's patch stops failing
                // silently; swallowing our own failure here would be the same bug one level down, and
                // from the outside the two look identical - the mod is loaded, nothing throws, and the
                // feature is simply absent.
                if (_complained.Add(instance.GetType().Name + "/" + (open ? "Open" : "Close")))
                    _log?.Warning("[fix] split-screen-patches: calling SetIsOpen on "
                                + instance.GetType().Name + " threw " + e.GetType().Name + ": "
                                + e.Message + ". A patch on it is not running.");
            }
        }
    }
}
