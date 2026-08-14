using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// Keep a mod's sweep over every loaded assembly out of the interop ones.
    /// </summary>
    /// <remarks>
    /// <c>Assembly.GetTypes()</c> on an IL2CPP interop assembly is a landmine, and this project has the
    /// minidump: it forces every type in the assembly to load, one of them will not, and the process dies
    /// inside coreclr. No exception, no log line, no crash message. <see cref="Dynamic.ReflectionFallback"/>
    /// defuses it for Harmony, which walks every assembly to resolve a type name.
    ///
    /// A mod doing the same walk by hand is not covered by that, and the failure is identical:
    /// <code>
    /// Fatal error. Internal CLR error. (0x80131506)
    ///    at System.Reflection.Assembly.GetTypes()
    ///    at CustomCommandsFramework.CustomCommandsHelper.GetSafeTypes(Assembly)
    ///    at CustomCommandsFramework.AutoRegisterCommands.RegisterCommands()
    /// </code>
    /// The mod is careful - it catches ReflectionTypeLoadException and everything else - and none of that
    /// helps, because a fatal runtime error is not an exception to catch.
    ///
    /// NOT A GENERAL PATCH ON <c>GetTypes</c>. Intercepting it for the whole process would cover every mod
    /// at once and is the wrong trade: MelonLoader and Il2CppInterop enumerate types themselves, and
    /// answering "none" to one of their calls would break a boot that works today, for everyone, to repair
    /// one that fails sometimes, for a few. Named methods only, one line each.
    ///
    /// AND NOT A MOD FIX, which is where this was nearly written. Those run on the first frame the game can
    /// answer them, and the call happens in <c>OnInitializeMelon</c> - long before. It has to be here, in
    /// the plugin, at the one point where the mod assemblies are loaded and none of them has been
    /// initialised yet.
    ///
    /// Answering with nothing is not an approximation. Nothing looks for game types this way: what the
    /// callers want is types from OTHER MODS carrying their own attribute, and an interop assembly has
    /// none. The mods that step on this are looking straight past the assembly that kills them.
    /// </remarks>
    internal static class InteropTypeSweep
    {
        private sealed class Sweep
        {
            internal string Type;
            internal string Method;
            internal string Mod;
        }

        /// <summary>
        /// Methods known to hand an interop assembly to <c>GetTypes</c>, by name.
        /// </summary>
        /// <remarks>
        /// One line per offender, added when a crash report names one. A list rather than a rule, for the
        /// same reason everything else here is: a rule would have to guess which sweeps are safe.
        /// </remarks>
        private static readonly Sweep[] Known =
        {
            new Sweep
            {
                Type = "CustomCommandsFramework.CustomCommandsHelper",
                Method = "GetSafeTypes",
                Mod = "Custom Commands Framework",
            },
        };

        private static MelonLogger.Instance _log;
        private static string _interop;
        private static int _skipped;

        internal static void Install(MelonLogger.Instance log, string interopDirectory)
        {
            _log = log;
            _interop = interopDirectory;
            if (string.IsNullOrEmpty(_interop)) return;

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.sweeps");

            foreach (var sweep in Known)
            {
                try
                {
                    var type = AccessTools.TypeByName(sweep.Type);
                    if (type == null) continue;                 // that mod is not installed

                    var target = AccessTools.Method(type, sweep.Method, new[] { typeof(Assembly) });
                    if (target == null)
                    {
                        log.Warning($"[sweep] {sweep.Mod} has no {sweep.Method}(Assembly) here, so its walk "
                                  + "over the interop assemblies is not guarded. If the game dies on "
                                  + "startup with no message, that is where to look.");
                        continue;
                    }

                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(InteropTypeSweep), nameof(Skip)));
                    log.Msg($"[sweep] {sweep.Mod} will not be handed an interop assembly to enumerate; "
                          + "doing that kills the process without a message.");
                }
                catch (Exception e)
                {
                    log.Warning($"[sweep] could not guard {sweep.Mod}: {e.Message}");
                }
            }
        }

        /// <summary>Answer an interop assembly with nothing, and let every other one through.</summary>
        private static bool Skip(Assembly assembly, ref IEnumerable<Type> __result)
        {
            if (!IsInterop(assembly)) return true;

            __result = Array.Empty<Type>();
            if (++_skipped == 1)
                _log?.Msg("[sweep] skipped the game's generated assemblies while a mod enumerated types. "
                        + "They carry no mod commands, and reading them that way is what takes the process "
                        + "down.");
            return false;
        }

        /// <summary>
        /// Is this one of MelonLoader's generated interop assemblies?
        /// </summary>
        /// <remarks>
        /// By folder rather than by name: the folder IS the definition, and a mod is free to be called
        /// anything. A dynamic assembly has no location and is never one of these.
        /// </remarks>
        private static bool IsInterop(Assembly assembly)
        {
            if (assembly == null) return false;
            try
            {
                if (assembly.IsDynamic) return false;
                string location = assembly.Location;
                if (string.IsNullOrEmpty(location)) return false;

                return string.Equals(Path.GetDirectoryName(location),
                                     _interop.TrimEnd(Path.DirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
