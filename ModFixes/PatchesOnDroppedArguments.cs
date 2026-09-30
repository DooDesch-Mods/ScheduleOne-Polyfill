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

            /// <summary>
            /// When the method the game calls now has a different name, what that name starts with.
            /// </summary>
            /// <remarks>
            /// A FishNet RPC body is named after a hash of its signature, so dropping an argument renamed it:
            /// RpcLogic___ProcessHandoverServerSide_3760244802 became ..._3315874220. Matched by prefix so the
            /// next hash change does not need a new entry. Null means the same name as the stand-in.
            /// </remarks>
            internal string RealPrefix;

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

            // The server half: 0.4.7 calls it only from ProcessHandover (Customer.cs:1438 on 0.4.7f6), which
            // is where 0.4.6 forwarded that same Finalize.
            new Entry
            {
                Type = "Il2CppScheduleOne.Economy.Customer",
                Name = "ProcessHandoverServerSide",
                StandInArity = 7,
                Dropped = "outcome",
                Value = type => Enum.ToObject(type, 1),
                Because = "it only ever received ProcessHandover's outcome, and that was always Finalize",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.Economy.Customer",
                Name = "RpcLogic___ProcessHandoverServerSide_3760244802",
                RealPrefix = "RpcLogic___ProcessHandoverServerSide_",
                StandInArity = 7,
                Dropped = "outcome",
                Value = type => Enum.ToObject(type, 1),
                Because = "the RPC body of the server half, renamed by FishNet when its signature lost the outcome",
            },

            // A trash bag's startKinematic went: 0.4.7 has no such option and creates every bag as a physics
            // object (TrashManager.cs:203-211 on 0.4.7f6), which is what the old call did with false.
            // Production Expansion Reborn 1.0.2B throws its cleaner-station bags from a patch on the old form.
            new Entry
            {
                Type = "Il2CppScheduleOne.Trash.TrashManager",
                Name = "CreateTrashBag",
                StandInArity = 7,
                Dropped = "startKinematic",
                Value = _ => false,
                Because = "0.4.7 creates every bag as a physics object, which is the old call with false",
            },
        };

        private sealed class Relay
        {
            internal Entry Entry;
            internal object DroppedValue;
            internal int DroppedPosition;
            internal string[] RealNames;
            internal bool HasResult;
            internal readonly List<MethodInfo> Before = new();
            internal readonly List<MethodInfo> After = new();
        }

        private static readonly Dictionary<MethodBase, Relay> Relays = new();
        private static MelonLogger.Instance _log;

        // The relay behind each stand-in, so entering one stand-in suppresses only its own relay. One shared
        // counter suppressed every relay: the old five-argument ProcessHandover runs the real
        // ProcessHandoverServerSide inside it, and a patch relayed onto that did not fire - where 0.4.6 ran both.
        private static readonly Dictionary<MethodBase, Relay> StandIns = new();
        [ThreadStatic] private static Dictionary<Relay, int> _inStandIn;

        private static bool InStandIn(Relay relay)
            => _inStandIn != null && _inStandIn.TryGetValue(relay, out int depth) && depth > 0;

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

                var declared = type.GetMethods(AccessTools.all).Where(m => m.DeclaringType == type).ToList();
                var methods = declared.Where(m => m.Name == entry.Name).ToList();
                var standIn = methods.FirstOrDefault(m => m.GetParameters().Length == entry.StandInArity);
                var reals = (entry.RealPrefix == null
                        ? methods
                        : declared.Where(m => m.Name != entry.Name
                                              && m.Name.StartsWith(entry.RealPrefix, StringComparison.Ordinal)))
                    .Where(m => m.GetParameters().Length == entry.StandInArity - 1).ToList();
                if (reals.Count > 1)
                {
                    log.Warning($"[fix] {Id}: {label}: {reals.Count} methods could be the one the game calls now; "
                              + "choosing would be a guess.");
                    continue;
                }
                var real = reals.FirstOrDefault();
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
                    DroppedPosition = droppedParameter.Position,
                    RealNames = real.GetParameters().Select(p => p.Name).ToArray(),
                    HasResult = real.ReturnType != typeof(void),
                };
                if (!Collect(standIn, relay, label)) continue;

                Relays[real] = relay;
                StandIns[standIn] = relay;
                harmony.Patch(standIn,
                    prefix: new HarmonyMethod(typeof(PatchesOnDroppedArguments), nameof(EnterStandIn)) { priority = Priority.First },
                    finalizer: new HarmonyMethod(typeof(PatchesOnDroppedArguments), nameof(LeaveStandIn)));
                harmony.Patch(real,
                    prefix: new HarmonyMethod(typeof(PatchesOnDroppedArguments), nameof(RunBefore)),
                    postfix: new HarmonyMethod(typeof(PatchesOnDroppedArguments),
                                               relay.HasResult ? nameof(RunAfterWithResult) : nameof(RunAfter)));
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
            for (int i = 0; i < relay.Entry.StandInArity; i++) names.Add("__" + i);   // by position, as Harmony allows
            void Take(IEnumerable<HarmonyLib.Patch> patches, List<MethodInfo> into, string kind)
            {
                // The game's result reaches a postfix; a prefix asking for it would be setting it, which is not relayed.
                bool result = kind == "postfix" && relay.HasResult;
                foreach (var patch in patches)
                {
                    if (patch.owner != null && patch.owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) continue;
                    var missing = patch.PatchMethod.GetParameters()
                        .FirstOrDefault(p => !names.Contains(p.Name) && !(result && p.Name == "__result"));
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

        private static void EnterStandIn(MethodBase __originalMethod)
        {
            if (!StandIns.TryGetValue(__originalMethod, out var relay)) return;
            var depths = _inStandIn ??= new Dictionary<Relay, int>();
            depths.TryGetValue(relay, out int depth);
            depths[relay] = depth + 1;
        }

        private static Exception LeaveStandIn(Exception __exception, MethodBase __originalMethod)
        {
            if (StandIns.TryGetValue(__originalMethod, out var relay) && _inStandIn != null
                && _inStandIn.TryGetValue(relay, out int depth) && depth > 0)
                _inStandIn[relay] = depth - 1;
            return __exception;
        }

        private static bool RunBefore(object __instance, object[] __args, MethodBase __originalMethod)
        {
            if (!Relays.TryGetValue(__originalMethod, out var relay) || InStandIn(relay)) return true;
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
            if (!Relays.TryGetValue(__originalMethod, out var relay) || InStandIn(relay)) return;
            foreach (var patch in relay.After) Call(patch, Arguments(patch, relay, __instance, __args));
        }

        /// <summary><see cref="RunAfter"/> for a method with a result, which its postfixes may read.</summary>
        private static void RunAfterWithResult(object __instance, object[] __args, MethodBase __originalMethod, object __result)
        {
            if (!Relays.TryGetValue(__originalMethod, out var relay) || InStandIn(relay)) return;
            foreach (var patch in relay.After) Call(patch, Arguments(patch, relay, __instance, __args, __result));
        }

        /// <summary>
        /// Where a patch parameter's value is in the game's call: an index into its arguments, -1 for the dropped
        /// one, or -2 for none.
        /// </summary>
        /// <remarks>
        /// By name, or by position as <c>__N</c> - Production Expansion Reborn writes its patch as <c>__0</c> to
        /// <c>__6</c>. A position counts the stand-in's parameters, so everything after the dropped one sits one
        /// place earlier in the game's call.
        /// </remarks>
        private static int Source(Relay relay, string name)
        {
            if (name == relay.Entry.Dropped) return -1;
            if (name.Length > 2 && name.StartsWith("__", StringComparison.Ordinal)
                && int.TryParse(name.Substring(2), out int position) && position >= 0 && position < relay.Entry.StandInArity)
            {
                if (position == relay.DroppedPosition) return -1;
                return position < relay.DroppedPosition ? position : position - 1;
            }
            int at = Array.IndexOf(relay.RealNames, name);
            return at < 0 ? -2 : at;
        }

        private static object[] Arguments(MethodInfo patch, Relay relay, object instance, object[] args, object result = null)
        {
            var wanted = patch.GetParameters();
            var values = new object[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                string name = wanted[i].Name;
                if (name == "__instance") values[i] = instance;
                else if (name == "__result") values[i] = result;
                else
                {
                    int at = Source(relay, name);
                    if (at == -1) { values[i] = relay.DroppedValue; continue; }
                    values[i] = at < 0
                        ? (wanted[i].HasDefaultValue ? wanted[i].DefaultValue : null)
                        : Fit(args[at], wanted[i].ParameterType, patch);
                }
            }
            return values;
        }

        /// <summary>The argument as the patch declared it, where the two spell the same list differently.</summary>
        /// <remarks>
        /// A patch written as <c>List&lt;ItemInstance&gt; items</c> with <c>using System.Collections.Generic</c>
        /// declares a managed list; the game hands over an Il2CppSystem one. Harmony would never have bound that
        /// parameter, but the relay calls the patch itself, and reflection refuses the call outright
        /// (Cartel Influence Enhancements 0.3.0, on every handover). A copy of the items is what the patch reads;
        /// changes it makes to the copy do not reach the game, as they never could have.
        /// </remarks>
        private static object Fit(object value, Type wanted, MethodInfo patch)
        {
            if (value == null || wanted.IsInstanceOfType(value)) return value;
            try
            {
                var type = value.GetType();
                if (wanted.IsGenericType && wanted.GetGenericTypeDefinition() == typeof(List<>)
                    && type.IsGenericType && type.FullName?.StartsWith("Il2CppSystem.Collections.Generic.List`1") == true)
                {
                    var count = (int)type.GetProperty("Count").GetValue(value);
                    var item = type.GetProperty("Item");
                    var copy = (System.Collections.IList)Activator.CreateInstance(wanted);
                    for (int i = 0; i < count; i++) copy.Add(item.GetValue(value, new object[] { i }));
                    return copy;
                }
            }
            catch (Exception e)
            {
                _log?.Warning($"[fix] patches-on-dropped-arguments: {patch.DeclaringType?.Name}.{patch.Name}: could not "
                            + $"copy the game's {value.GetType().Name} into the {wanted.Name} it declares, so it gets "
                            + "the game's object and may refuse it: " + (e.InnerException ?? e).Message);
            }
            return value;
        }

        /// <summary>A prefix that changed an argument through ref changes it for the game's method too.</summary>
        private static void WriteBack(MethodInfo patch, Relay relay, object[] values, object[] args)
        {
            var wanted = patch.GetParameters();
            for (int i = 0; i < wanted.Length; i++)
            {
                if (!wanted[i].ParameterType.IsByRef) continue;
                int at = Source(relay, wanted[i].Name);
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
