using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod's patch on a method whose machine code IL2CPP shares with another class runs only for its own class.
    /// </summary>
    /// <remarks>
    /// IL2CPP builds the game with identical-code folding: methods whose compiled bodies are the same bytes
    /// become one function. A patch can only detour an address, so a patch on one of them runs whenever ANY of
    /// them is called - with that call's object handed to the mod as if it were the patched class. On 0.4.7f7
    /// <c>LoadingDock.SetStaticOccupant</c> shares its body with 141 unrelated setters (store a pointer at
    /// field 0x28, write barrier), and <c>BagTrashCanBehaviour.SetTargetTrashCan</c> with
    /// <c>Customer.set_AssignedDealer</c>. A postfix written for the trash can then runs when the player
    /// assigns a customer to a dealer, reads the customer as a trash-can behaviour, and takes the game down
    /// (Production Expansion Reborn 1.0.2B). Another stack-overflowed loading a save (Employee Tweaks).
    ///
    /// NOTHING HERE IS A LIST. Which methods are folded changes with every build, so it is read from the
    /// running game: every method IL2CPP knows is enumerated once, natively, and only the patched ones whose
    /// function is also another class's method are guarded. A method folded only with methods of its own
    /// class (an RPC's writer and logic halves, say) cannot be told apart by its object and is left alone.
    ///
    /// The guard is a first-running prefix that reads the object's native class - the first word of every
    /// Il2CppObject, so no runtime call beyond the GC-handle lookup <c>Pointer</c> is - and compares it with
    /// the patched class. The prefix's <c>__instance</c> costs nothing extra: Il2CppInterop's patcher detours
    /// the native function and its native-to-managed trampoline already wraps the raw <c>this</c> as the
    /// declaring type (<c>Il2CppObjectPool.Get&lt;T&gt;</c>) for every call, as soon as any mod patch is on the
    /// method; HarmonyX hands that wrapper over as a plain <c>ldarg.0</c>, no box, no cast. When the object is
    /// not one, every other mod's prefix and postfix on that method stands down for the call - unless the
    /// patch's own <c>__instance</c> takes that object: a postfix on <c>Bungalow.Awake</c> declared for any
    /// <c>Property</c> is written for the Sweatshop too, and still runs for it. The game's own code runs as it
    /// would unpatched either way. The verdict per pair of classes is cached, so the steady-state
    /// cost is a pointer read and a compare.
    /// </remarks>
    internal sealed class PatchesOnFoldedCode : Fix
    {
        internal override string Id => "patches-on-folded-code";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => "*";

        internal override string What
            => "a mod's patch on a method IL2CPP shares with other classes runs only for its own class";

        private const string HarmonyId = "doodesch.polyfill.foldedcode";

        private static MelonLogger.Instance _log;
        private static HarmonyLib.Harmony _harmony;

        /// <summary>Native function -> every class with a method compiled to it, for functions shared by two or more.</summary>
        private static Dictionary<IntPtr, List<IntPtr>> _shared;

        private static readonly HashSet<MethodBase> Folded = new();
        private static readonly HashSet<MethodInfo> Guarded = new();

        /// <summary>Wanted native class by the patched method's interop type: the trampoline wraps the object as exactly that type.</summary>
        private static readonly Dictionary<Type, IntPtr> WantedByType = new();

        // Nesting: bit n of _foreignMask says whether the call at depth n is foreign. No allocation per call.
        [ThreadStatic] private static int _depth;
        [ThreadStatic] private static ulong _foreignMask;
        [ThreadStatic] private static IntPtr[] _actual;      // the foreign object's class, per depth

        /// <summary>What the guard knows and counts about one guarded mod patch.</summary>
        private sealed class PatchStats
        {
            public string Owner, Label;
            /// <summary>The native class the patch declares for <c>__instance</c>: <see cref="AnyObject"/> for any object, zero for none.</summary>
            public IntPtr Accepts;
            /// <summary>For a postfix that returns a value: the index of the parameter it passes through.</summary>
            public int PassThrough = -1;
            public int Own, Down;
            public bool Warned;
        }

        private static readonly Dictionary<MethodBase, PatchStats> Stats = new();
        private static readonly IntPtr AnyObject = new IntPtr(-1);

        /// <summary>A patch stood down this many times without once running for its own class is reported.</summary>
        private const int WarnAfterStandDowns = 100;

        /// <summary>Wanted class -> how many other classes have called it through shared code.</summary>
        private static readonly Dictionary<IntPtr, (string name, int classes)> ForeignCallers = new();
        private static readonly HashSet<string> Reported = new();
        [ThreadStatic] private static Dictionary<(IntPtr, IntPtr), bool> _verdicts;
        [ThreadStatic] private static Type _lastType;
        [ThreadStatic] private static IntPtr _lastWanted;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            _harmony ??= new HarmonyLib.Harmony(HarmonyId);
            int guarded = Guard();
            // Some mods patch only once a save is up; what they add is looked at again then.
            MelonEvents.OnSceneWasLoaded.Subscribe((_, scene) =>
            {
                if (scene != MainSceneLatch.GameScene) return;
                int more = Guard();
                if (more > 0) _log.Msg($"[fix] {Id}: {more} more mod patch(es) guarded after the game scene loaded.");
            });
            MelonEvents.OnApplicationQuit.Subscribe(Summarise);
            return guarded > 0 || Folded.Count > 0;
        }

        private int Guard()
        {
            var candidates = new List<(MethodBase method, IntPtr function, IntPtr klass)>();
            foreach (var method in HarmonyLib.Harmony.GetAllPatchedMethods())
            {
                if (method.IsStatic || method.IsConstructor || Folded.Contains(method)) continue;
                if (!typeof(Il2CppObjectBase).IsAssignableFrom(method.DeclaringType)) continue;
                if (!NativeOf(method, out var function, out var klass)) continue;
                candidates.Add((method, function, klass));
            }
            if (candidates.Count == 0) return 0;

            if (_shared == null)
            {
                var clock = Stopwatch.StartNew();
                _shared = SharedFunctions(out int methods);
                _log.Msg($"[fix] {Id}: read {methods} native methods in {clock.ElapsedMilliseconds} ms; "
                       + $"{_shared.Count} functions are shared by more than one class.");
            }

            int guarded = 0;
            foreach (var (method, function, klass) in candidates)
            {
                if (!_shared.TryGetValue(function, out var classes)) continue;
                var foreign = classes.FirstOrDefault(other => other != klass && !IL2CPP.il2cpp_class_is_assignable_from(klass, other));
                if (foreign == IntPtr.Zero) continue;   // folded only within its own class hierarchy

                string label = method.DeclaringType.Name + "." + method.Name;
                if (classes.Any(other => other != klass && IL2CPP.il2cpp_class_is_valuetype(other)))
                {
                    // A struct method's `this` is a pointer into the struct, not an object: its first word is not a class.
                    ReportOnce(label, $"{label} shares its code with a struct's method, whose object cannot be told from a class's; "
                                    + "mod patches on it are left to run for every caller.");
                    continue;
                }
                try
                {
                    // The patch methods first, the folded method after: patching the folded method rebuilds
                    // Harmony's wrapper, and a patch method small enough for the JIT to inline there would
                    // no longer be reached through the detour that guards it.
                    var info = HarmonyLib.Harmony.GetPatchInfo(method);
                    int before = guarded;
                    foreach (var patch in info.Prefixes)
                    {
                        if (!Register(patch, klass, label, postfix: false)) continue;
                        var guard = patch.PatchMethod.ReturnType == typeof(bool) ? nameof(GuardBool) : nameof(GuardVoid);
                        _harmony.Patch(patch.PatchMethod, prefix: new HarmonyMethod(typeof(PatchesOnFoldedCode), guard));
                        guarded++;
                    }
                    foreach (var patch in info.Postfixes)
                    {
                        if (!Register(patch, klass, label, postfix: true)) continue;
                        var returned = patch.PatchMethod.ReturnType;
                        HarmonyMethod guard;
                        if (returned == typeof(void)) guard = new HarmonyMethod(typeof(PatchesOnFoldedCode), nameof(GuardVoid));
                        else if (Stats[patch.PatchMethod].PassThrough >= 0)
                            guard = new HarmonyMethod(typeof(PatchesOnFoldedCode).GetMethod(nameof(GuardPassThrough), BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(returned));
                        else
                        {
                            ReportOnce(patch.PatchMethod.ToString(), $"{patch.owner}'s {patch.PatchMethod.DeclaringType?.Name}.{patch.PatchMethod.Name} "
                                     + "returns a value but passes none of its parameters through, so it cannot be stood down and runs for every class.");
                            continue;
                        }
                        _harmony.Patch(patch.PatchMethod, prefix: guard);
                        guarded++;
                    }
                    if (Folded.Add(method))
                    {
                        lock (WantedByType) WantedByType[method.DeclaringType] = klass;
                        _harmony.Patch(method,
                            prefix: new HarmonyMethod(typeof(PatchesOnFoldedCode), nameof(Enter)) { priority = Priority.First + 100 },
                            finalizer: new HarmonyMethod(typeof(PatchesOnFoldedCode), nameof(Leave)) { priority = Priority.Last - 100 });
                    }
                    if (guarded > before)
                        _log.Msg($"[fix] {Id}: {label} shares its code with {ClassName(foreign)} "
                               + $"({classes.Count - 1} other class(es)); {guarded - before} mod patch(es) now run only for {method.DeclaringType.Name}.");
                }
                catch (Exception e)
                {
                    _log.Warning($"[fix] {Id}: {label}: " + e.Message);
                }
            }
            return guarded;
        }

        /// <summary>Notes a mod patch for guarding; false when it is ours or already guarded.</summary>
        /// <remarks>
        /// What a patch accepts depends on its own <c>__instance</c> only, not on which folded method it sits on,
        /// so a patch method listed under several folded targets (<c>TargetMethods()</c>) is registered once.
        /// </remarks>
        private static bool Register(HarmonyLib.Patch patch, IntPtr klass, string label, bool postfix)
        {
            if (patch.owner == HarmonyId) return false;
            if (!Guarded.Add(patch.PatchMethod))
            {
                // The same patch method on another folded target (TargetMethods()): one guard serves both, as what
                // it accepts depends on its own __instance. The target is added to what it is named by in warnings.
                lock (Stats)
                    if (Stats.TryGetValue(patch.PatchMethod, out var known) && !known.Label.Split(", ").Contains(label))
                        known.Label += ", " + label;
                return false;
            }
            var stats = new PatchStats { Owner = patch.owner, Label = label, Accepts = Accepted(patch.PatchMethod) };
            if (postfix && patch.PatchMethod.ReturnType != typeof(void))
            {
                // Harmony hands a returning postfix its result back as the first parameter of the return type.
                var parameters = patch.PatchMethod.GetParameters();
                int named = Array.FindIndex(parameters, p => p.Name == "__result");
                stats.PassThrough = named >= 0 && parameters[named].ParameterType == patch.PatchMethod.ReturnType ? named
                    : Array.FindIndex(parameters, p => p.ParameterType == patch.PatchMethod.ReturnType);
            }
            lock (Stats) Stats[patch.PatchMethod] = stats;
            if (stats.Accepts != IntPtr.Zero && stats.Accepts != klass)
                _log.Msg($"[fix] patches-on-folded-code: {patch.owner}'s {patch.PatchMethod.DeclaringType?.Name}.{patch.PatchMethod.Name} "
                       + $"takes any {(stats.Accepts == AnyObject ? "object" : ClassName(stats.Accepts))} as __instance, so it "
                       + "still runs for every class of that kind sharing the code.");
            return true;
        }

        /// <summary>Says once per <paramref name="key"/>, so a check that fails on every call is one line.</summary>
        private static void ReportOnce(string key, string message)
        {
            lock (Reported) if (!Reported.Add(key)) return;
            _log?.Warning($"[fix] patches-on-folded-code: {message}");
        }

        /// <summary>The native function a generated method runs, and the class that declares it.</summary>
        private static bool NativeOf(MethodBase method, out IntPtr function, out IntPtr klass)
        {
            function = klass = IntPtr.Zero;
            try
            {
                var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
                if (field == null) return false;
                var info = (IntPtr)field.GetValue(null);
                if (info == IntPtr.Zero) return false;
                function = Marshal.ReadIntPtr(info);            // Il2CppMethodInfo starts with its methodPointer
                klass = IL2CPP.il2cpp_method_get_class(info);
                return function != IntPtr.Zero && klass != IntPtr.Zero;
            }
            catch (Exception e)
            {
                ReportOnce("native:" + method, $"could not read the native function of {method.DeclaringType?.Name}.{method.Name} "
                         + $"({e.GetType().Name}: {e.Message}); mod patches on it are not guarded.");
                return false;
            }
        }

        /// <summary>Every method in every loaded image, grouped by native function; only shared functions are kept.</summary>
        private static Dictionary<IntPtr, List<IntPtr>> SharedFunctions(out int methods)
        {
            methods = 0;
            var first = new Dictionary<IntPtr, IntPtr>();
            var shared = new Dictionary<IntPtr, List<IntPtr>>();
            UIntPtr count = UIntPtr.Zero;
            var assemblies = DomainAssemblies(IL2CPP.il2cpp_domain_get(), ref count);
            for (ulong a = 0; a < (ulong)count; a++)
            {
                var image = IL2CPP.il2cpp_assembly_get_image(Marshal.ReadIntPtr(assemblies, (int)a * IntPtr.Size));
                uint classes = IL2CPP.il2cpp_image_get_class_count(image);
                for (uint c = 0; c < classes; c++)
                {
                    var klass = IL2CPP.il2cpp_image_get_class(image, c);
                    if (klass == IntPtr.Zero) continue;
                    IntPtr iter = IntPtr.Zero, method;
                    while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                    {
                        methods++;
                        var function = Marshal.ReadIntPtr(method);
                        if (function == IntPtr.Zero) continue;
                        if (!first.TryGetValue(function, out var owner)) { first[function] = klass; continue; }
                        if (owner == klass) continue;
                        if (!shared.TryGetValue(function, out var list)) shared[function] = list = new List<IntPtr> { owner };
                        if (!list.Contains(klass)) list.Add(klass);
                    }
                }
            }
            return shared;
        }

        /// <summary><c>il2cpp_domain_get_assemblies</c>, with its size_t count and the array as a plain pointer.</summary>
        [DllImport("GameAssembly", EntryPoint = "il2cpp_domain_get_assemblies", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr DomainAssemblies(IntPtr domain, ref UIntPtr size);

        private static string ClassName(IntPtr klass)
        {
            try { return Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(klass)) ?? "?"; }
            catch (Exception e)
            {
                ReportOnce("classname", $"could not read a class name ({e.GetType().Name}: {e.Message}); logged as \"?\".");
                return "?";
            }
        }

        /// <summary>
        /// Opens the frame for a call of a folded method: foreign or not, by the object's native class.
        /// </summary>
        /// <remarks>
        /// <c>__instance</c> is the managed wrapper that Il2CppInterop's native-to-managed trampoline makes for
        /// every call of a patched method whatever the patches ask for (<c>Il2CppDetourMethodPatcher</c> converts
        /// the <c>this</c> argument unconditionally), so taking it here adds no wrapping; typed as
        /// <c>object</c> or as an interop type HarmonyX emits the same <c>ldarg.0</c>. A struct's <c>this</c> would
        /// reach that trampoline as a pointer into the struct, not an object - the same hazard for the mod's own
        /// patch - which is why code shared with a value type's method is not guarded at all (see <see cref="Guard"/>).
        /// </remarks>
        private static void Enter(object __instance)
        {
            bool foreign = false;
            IntPtr actualClass = IntPtr.Zero;
            try
            {
                if (__instance is Il2CppObjectBase o)
                {
                    IntPtr ptr = o.Pointer;
                    if (ptr != IntPtr.Zero)
                    {
                        var type = o.GetType();
                        IntPtr wanted;
                        if (type == _lastType) wanted = _lastWanted;
                        else
                        {
                            lock (WantedByType) WantedByType.TryGetValue(type, out wanted);
                            _lastType = type;
                            _lastWanted = wanted;
                        }
                        IntPtr actual = wanted == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(ptr);
                        if (actual != IntPtr.Zero && actual != wanted) foreign = Foreign(wanted, actual, type);
                        actualClass = actual;
                    }
                }
            }
            catch (Exception e)
            {
                // The frame counts as not foreign, so every patch runs for this call. Say so, once.
                ReportOnce("enter", $"reading an object's class failed ({e.GetType().Name}: {e.Message}); "
                         + "mod patches run unguarded for the calls it fails on.");
            }
            if (_depth < 64)
            {
                if (foreign)
                {
                    _foreignMask |= 1UL << _depth;
                    (_actual ??= new IntPtr[64])[_depth] = actualClass;
                }
                else _foreignMask &= ~(1UL << _depth);
            }
            _depth++;
        }

        private static bool Foreign(IntPtr wanted, IntPtr actual, Type type)
        {
            var verdicts = _verdicts ??= new Dictionary<(IntPtr, IntPtr), bool>();
            if (verdicts.TryGetValue((wanted, actual), out bool known)) return known;
            bool foreign = !IL2CPP.il2cpp_class_is_assignable_from(wanted, actual);
            verdicts[(wanted, actual)] = foreign;
            if (foreign)
            {
                int classes;
                lock (ForeignCallers)
                {
                    ForeignCallers.TryGetValue(wanted, out var seen);
                    classes = seen.classes + 1;
                    ForeignCallers[wanted] = (type.Name, classes);
                }
                // One line per patched method, on its first foreign caller; the count follows in Summarise.
                if (classes == 1)
                    _log?.Msg($"[fix] patches-on-folded-code: a {type.Name} method was called on a {ClassName(actual)} "
                            + "through shared code; mod patches written for the one class stood down for it.");
            }
            return foreign;
        }

        /// <summary>
        /// Pops the frame. A finalizer, because HarmonyX emits the postfixes that return a value after the void
        /// ones whatever their priority, so only a finalizer runs after every postfix - and on a throw too, and
        /// whether or not a mod's finalizer swallows the exception.
        /// </summary>
        private static Exception Leave(Exception __exception)
        {
            if (_depth > 0) _depth--;
            return __exception;
        }

        private static bool InForeignCall => _depth > 0 && _depth <= 64 && (_foreignMask & (1UL << (_depth - 1))) != 0;

        /// <summary>
        /// Should this mod patch stand down for the current call? Only on a foreign object, and only when the
        /// patch's own <c>__instance</c> does not take it.
        /// </summary>
        /// <remarks>
        /// A patch declares what it accepts. Production Expansion Reborn's postfix on
        /// <c>BagTrashCanBehaviour.SetTargetTrashCan</c> takes a <c>BagTrashCanBehaviour</c>, so a Customer
        /// reaching it through the shared setter is foreign to it. Employee Tweaks patches
        /// <c>Bungalow.Awake</c> - folded with the Sweatshop's, the motel room's and the others' - with a
        /// postfix that takes any <c>Property</c>: it is written for all of them, and standing it down for the
        /// Sweatshop took that property's employee setup away. A patch that names no <c>__instance</c> makes no
        /// claim and stands down.
        /// </remarks>
        private static bool StandsDown(MethodBase patch)
        {
            PatchStats stats;
            lock (Stats) Stats.TryGetValue(patch, out stats);
            if (!InForeignCall)
            {
                if (stats != null) Interlocked.Increment(ref stats.Own);
                return false;
            }
            if (stats == null || stats.Accepts == AnyObject) return stats == null;
            bool down = stats.Accepts == IntPtr.Zero;
            if (!down)
            {
                var actual = _actual?[_depth - 1] ?? IntPtr.Zero;
                down = actual != IntPtr.Zero && !IL2CPP.il2cpp_class_is_assignable_from(stats.Accepts, actual);
            }
            if (down) NoteStoodDown(stats);
            return down;
        }

        /// <summary>
        /// IL2CPP can inline a small method at its call sites, so a class's own calls may never reach the shared
        /// function; a patch that only ever ran because another class came through it then runs for nothing once
        /// this guard stands it down. A patch stood down many times without running for its own class is named.
        /// </summary>
        private static void NoteStoodDown(PatchStats stats)
        {
            if (Interlocked.Increment(ref stats.Down) < WarnAfterStandDowns || stats.Warned || Volatile.Read(ref stats.Own) > 0) return;
            lock (stats)
            {
                if (stats.Warned) return;
                stats.Warned = true;
            }
            _log?.Warning($"[fix] patches-on-folded-code: {stats.Owner}'s patch on {stats.Label} was stood down {stats.Down} times "
                        + "and has not run once for its own class. If the game inlines the call, the patch never ran for its class "
                        + "even before this - it may have only ever run for the other classes sharing the code.");
        }

        /// <summary>The native class a patch's <c>__instance</c> is declared as.</summary>
        private static IntPtr Accepted(MethodInfo patch)
        {
            var parameter = patch.GetParameters().FirstOrDefault(p => p.Name == "__instance");
            if (parameter == null) return IntPtr.Zero;
            var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
            if (type == typeof(object) || type == typeof(Il2CppObjectBase) || !typeof(Il2CppObjectBase).IsAssignableFrom(type))
                return AnyObject;
            try
            {
                var store = typeof(Il2CppClassPointerStore<>).MakeGenericType(type);
                var pointer = (IntPtr)store.GetField("NativeClassPtr").GetValue(null);
                return pointer == IntPtr.Zero ? IntPtr.Zero : pointer;
            }
            catch (Exception e)
            {
                ReportOnce("accepted:" + patch, $"could not read the class {patch.DeclaringType?.Name}.{patch.Name} declares for __instance "
                         + $"({e.GetType().Name}: {e.Message}); it is treated as taking none.");
                return IntPtr.Zero;
            }
        }

        private static bool GuardVoid(MethodBase __originalMethod) => !StandsDown(__originalMethod);

        private static bool GuardBool(ref bool __result, MethodBase __originalMethod)
        {
            if (!StandsDown(__originalMethod)) return true;
            __result = true;   // a mod prefix that did not run lets the game's method run
            return false;
        }

        /// <summary>
        /// A postfix that returns a value (<c>T Postfix(T __result)</c>): Harmony stores what it returns as the
        /// method's result, so standing it down hands the value it was given back unchanged, never a default.
        /// </summary>
        private static bool GuardPassThrough<T>(ref T __result, object[] __args, MethodBase __originalMethod)
        {
            if (!StandsDown(__originalMethod)) return true;
            PatchStats stats;
            lock (Stats) Stats.TryGetValue(__originalMethod, out stats);
            if (stats == null || stats.PassThrough < 0 || stats.PassThrough >= __args.Length) return true;
            __result = (T)__args[stats.PassThrough];
            return false;
        }

        /// <summary>One line per patched method that other classes called through shared code, with how many.</summary>
        internal static void Summarise()
        {
            lock (ForeignCallers)
                foreach (var (name, classes) in ForeignCallers.Values)
                    _log?.Msg($"[fix] patches-on-folded-code: {name} was reached through shared code by {classes} other class(es).");
        }
    }
}
