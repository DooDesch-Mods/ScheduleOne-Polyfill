using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A patch written for a method that has since lost an argument runs again, from the method the game
    /// calls now.
    /// </summary>
    /// <remarks>
    /// 0.4.7's <c>Customer.ProcessHandover</c> dropped its leading outcome argument with the enum
    /// (Customer.cs:1374 on 0.4.6f13, :1375 on 0.4.7f6). A patch that names <c>outcome</c> cannot bind to
    /// the new method - Harmony will not fill a parameter the target lacks - so the lookup sends it to the
    /// stand-in the plugin put back (Contract/ReshapedMethods), and its class registers. This is the half
    /// that makes it fire: the game only ever calls the four-argument method, so that one calls the
    /// stand-in's patches, with the dropped argument filled in the way every 0.4.6 caller filled it.
    ///
    /// The patches are CALLED, not moved, for the reason PatchesOnSplitMethods gives. And not twice: a mod
    /// that calls the stand-in runs its patches there, and the stand-in's body then calls the real method,
    /// so the relay stands down while a stand-in call is under way.
    /// </remarks>
    internal sealed class PatchesOnDroppedArguments : Fix
    {
        internal override string Id => "patches-on-dropped-arguments";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "a patch on a method 0.4.7 took an argument from runs again when the game calls that method";

        internal override string StandsDownBecause
            => "a mod patching a method 0.4.7 took an argument from (the deal handover) registers and never "
             + "fires.";

        private sealed class Entry
        {
            internal string Type;
            internal string Name;
            internal int StandInArity;
            internal string Dropped;

            /// <summary>What every caller of the old method passed, given the parameter's type.</summary>
            internal Func<Type, object> Value;

            internal string Because;
        }

        private static readonly Entry[] Entries =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.Economy.Customer",
                Name = "ProcessHandover",
                StandInArity = 5,
                Dropped = "outcome",
                // Finalize = 1: the only value 0.4.6 ever passed (Customer.cs:1347, :1767,
                // RequestProductBehaviour.cs:365 on 0.4.6f13).
                Value = type => Enum.ToObject(type, 1),
                Because = "every 0.4.6 caller passed Finalize",
            },
        };

        private sealed class Relay
        {
            internal Entry Entry;
            internal object DroppedValue;
            internal string[] RealNames;
            internal readonly List<MethodInfo> Before = new();
            internal readonly List<MethodInfo> After = new();
        }

        private static readonly Dictionary<MethodBase, Relay> Relays = new();
        private static MelonLogger.Instance _log;

        [ThreadStatic] private static int _inStandIn;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.droppedarguments");
            int wired = 0;

            foreach (var entry in Entries)
            {
                string label = entry.Type + "." + entry.Name;
                var type = AccessTools.TypeByName(entry.Type);
                if (type == null) { log.Warning($"[fix] {Id}: {label}: the type is not on this build."); continue; }

                var methods = type.GetMethods(AccessTools.all).Where(m => m.DeclaringType == type && m.Name == entry.Name).ToList();
                var standIn = methods.FirstOrDefault(m => m.GetParameters().Length == entry.StandInArity);
                var real = methods.FirstOrDefault(m => m.GetParameters().Length == entry.StandInArity - 1);
                if (standIn == null)
                {
                    log.Msg($"[fix] {Id}: {label}: no mod needed the old form, so nothing patches it.");
                    continue;
                }
                if (real == null) { log.Warning($"[fix] {Id}: {label}: the method the game calls now is not here."); continue; }

                var droppedParameter = standIn.GetParameters().FirstOrDefault(p => p.Name == entry.Dropped);
                if (droppedParameter == null) { log.Warning($"[fix] {Id}: {label}: the stand-in has no '{entry.Dropped}'."); continue; }

                var relay = new Relay
                {
                    Entry = entry,
                    DroppedValue = entry.Value(droppedParameter.ParameterType),
                    RealNames = real.GetParameters().Select(p => p.Name).ToArray(),
                };
                if (!Collect(standIn, relay, label)) continue;

                Relays[real] = relay;
                harmony.Patch(standIn,
                    prefix: new HarmonyMethod(typeof(PatchesOnDroppedArguments), nameof(EnterStandIn)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(PatchesOnDroppedArguments), nameof(LeaveStandIn)));
                harmony.Patch(real,
                    prefix: new HarmonyMethod(typeof(PatchesOnDroppedArguments), nameof(RunBefore)),
                    postfix: new HarmonyMethod(typeof(PatchesOnDroppedArguments), nameof(RunAfter)));
                wired++;
                log.Msg($"[fix] {Id}: {label}: {relay.Before.Count} prefix(es) and {relay.After.Count} postfix(es) "
                      + $"now run when the game calls it, with {entry.Dropped} as {relay.DroppedValue} ({entry.Because}).");
            }
            return wired > 0;
        }

        private static bool Collect(MethodInfo standIn, Relay relay, string label)
        {
            HarmonyLib.Patches info;
            try { info = HarmonyLib.Harmony.GetPatchInfo(standIn); }
            catch (Exception e) { _log.Warning($"[fix] patches-on-dropped-arguments: {label}: " + e.Message); return false; }
            if (info == null) { _log.Msg($"[fix] patches-on-dropped-arguments: {label}: nothing patches the old form."); return false; }

            var names = new HashSet<string>(relay.RealNames, StringComparer.Ordinal) { "__instance", relay.Entry.Dropped };
            void Take(IEnumerable<HarmonyLib.Patch> patches, List<MethodInfo> into, string kind)
            {
                foreach (var patch in patches)
                {
                    if (patch.owner != null && patch.owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) continue;
                    var missing = patch.PatchMethod.GetParameters().FirstOrDefault(p => !names.Contains(p.Name));
                    if (!patch.PatchMethod.IsStatic || missing != null)
                    {
                        _log.Warning($"[fix] patches-on-dropped-arguments: {patch.owner}'s {kind} on {label} takes "
                                   + $"'{missing?.Name}', which cannot be filled from the new method. Left alone.");
                        continue;
                    }
                    into.Add(patch.PatchMethod);
                }
            }
            Take(info.Prefixes, relay.Before, "prefix");
            Take(info.Postfixes, relay.After, "postfix");
            if (relay.Before.Count + relay.After.Count == 0)
            {
                _log.Msg($"[fix] patches-on-dropped-arguments: {label}: no mod patch to relay.");
                return false;
            }
            return true;
        }

        private static void EnterStandIn() => _inStandIn++;

        private static Exception LeaveStandIn(Exception __exception)
        {
            _inStandIn--;
            return __exception;
        }

        private static bool RunBefore(object __instance, object[] __args, MethodBase __originalMethod)
        {
            if (_inStandIn > 0 || !Relays.TryGetValue(__originalMethod, out var relay)) return true;
            bool runOriginal = true;
            foreach (var patch in relay.Before)
            {
                var arguments = Arguments(patch, relay, __instance, __args);
                var result = Call(patch, arguments);
                if (result is false) runOriginal = false;
                WriteBack(patch, relay, arguments, __args);
            }
            return runOriginal;
        }

        private static void RunAfter(object __instance, object[] __args, MethodBase __originalMethod)
        {
            if (_inStandIn > 0 || !Relays.TryGetValue(__originalMethod, out var relay)) return;
            foreach (var patch in relay.After) Call(patch, Arguments(patch, relay, __instance, __args));
        }

        private static object[] Arguments(MethodInfo patch, Relay relay, object instance, object[] args)
        {
            var wanted = patch.GetParameters();
            var values = new object[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                string name = wanted[i].Name;
                if (name == "__instance") values[i] = instance;
                else if (name == relay.Entry.Dropped) values[i] = relay.DroppedValue;
                else values[i] = args[Array.IndexOf(relay.RealNames, name)];
            }
            return values;
        }

        /// <summary>A prefix that changed an argument through ref changes it for the game's method too.</summary>
        private static void WriteBack(MethodInfo patch, Relay relay, object[] values, object[] args)
        {
            var wanted = patch.GetParameters();
            for (int i = 0; i < wanted.Length; i++)
            {
                if (!wanted[i].ParameterType.IsByRef) continue;
                int at = Array.IndexOf(relay.RealNames, wanted[i].Name);
                if (at >= 0) args[at] = values[i];
            }
        }

        private static object Call(MethodInfo patch, object[] arguments)
        {
            try { return patch.Invoke(null, arguments); }
            catch (Exception e)
            {
                _log?.Warning($"[fix] patches-on-dropped-arguments: {patch.DeclaringType?.Name}.{patch.Name} threw: "
                            + (e.InnerException ?? e).Message);
                return null;
            }
        }
    }
}
