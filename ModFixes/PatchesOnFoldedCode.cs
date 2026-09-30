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
    /// Il2CppObject, so no call into the runtime - and compares it with the patched class. When the object is
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

        /// <summary>
        /// The native class each guarded patch declares for <c>__instance</c>: <see cref="AnyObject"/> when it
        /// takes any object, zero when it takes none.
        /// </summary>
        private static readonly Dictionary<MethodBase, IntPtr> Accepts = new();
        private static readonly IntPtr AnyObject = new IntPtr(-1);
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
                try
                {
                    if (Folded.Add(method))
                    {
                        lock (WantedByType) WantedByType[method.DeclaringType] = klass;
                        _harmony.Patch(method,
                            prefix: new HarmonyMethod(typeof(PatchesOnFoldedCode), nameof(Enter)) { priority = Priority.First + 100 },
                            postfix: new HarmonyMethod(typeof(PatchesOnFoldedCode), nameof(Leave)) { priority = Priority.Last - 100 },
                            finalizer: new HarmonyMethod(typeof(PatchesOnFoldedCode), nameof(LeaveOnThrow)) { priority = Priority.Last - 100 });
                    }
                    var info = HarmonyLib.Harmony.GetPatchInfo(method);
                    int before = guarded;
                    foreach (var patch in info.Prefixes.Concat(info.Postfixes))
                    {
                        if (patch.owner == HarmonyId || !Guarded.Add(patch.PatchMethod)) continue;
                        var accepts = Accepted(patch.PatchMethod);
                        lock (Accepts) Accepts[patch.PatchMethod] = accepts;
                        if (accepts != IntPtr.Zero && accepts != klass)
                            _log.Msg($"[fix] {Id}: {patch.owner}'s {patch.PatchMethod.DeclaringType?.Name}.{patch.PatchMethod.Name} "
                                   + $"takes any {(accepts == AnyObject ? "object" : ClassName(accepts))} as __instance, so it "
                                   + "still runs for every class of that kind sharing the code.");
                        var guard = patch.PatchMethod.ReturnType == typeof(bool) ? nameof(GuardBool) : nameof(GuardVoid);
                        _harmony.Patch(patch.PatchMethod, prefix: new HarmonyMethod(typeof(PatchesOnFoldedCode), guard));
                        guarded++;
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
            catch { return false; }
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
            catch { return "?"; }
        }

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
            catch { }
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
                _log?.Msg($"[fix] patches-on-folded-code: a {type.Name} method was called on a {ClassName(actual)} "
                        + "through shared code; mod patches written for the one class stood down for it.");
            return foreign;
        }

        private static void Leave() { if (_depth > 0) _depth--; }

        private static Exception LeaveOnThrow(Exception __exception)
        {
            // The postfix does not run on a throw; balance the depth here instead.
            if (__exception != null && _depth > 0) _depth--;
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
            if (!InForeignCall) return false;
            IntPtr accepts;
            lock (Accepts) Accepts.TryGetValue(patch, out accepts);
            if (accepts == IntPtr.Zero) return true;
            if (accepts == AnyObject) return false;
            var actual = _actual?[_depth - 1] ?? IntPtr.Zero;
            return actual == IntPtr.Zero || !IL2CPP.il2cpp_class_is_assignable_from(accepts, actual);
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
            catch { return IntPtr.Zero; }
        }

        private static bool GuardVoid(MethodBase __originalMethod) => !StandsDown(__originalMethod);

        private static bool GuardBool(ref bool __result, MethodBase __originalMethod)
        {
            if (!StandsDown(__originalMethod)) return true;
            __result = true;   // a mod prefix that did not run lets the game's method run
            return false;
        }
    }
}
