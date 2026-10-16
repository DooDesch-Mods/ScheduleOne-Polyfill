using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Boot
{
    /// <summary>
    /// Two patches on methods IL2CPP compiled to one function share one hook instead of each hooking it.
    /// </summary>
    /// <remarks>
    /// IL2CPP folds methods with identical machine code into one function: <c>Bungalow.Awake</c>,
    /// <c>SewerOffice.Awake</c>, <c>RV.Awake</c>, <c>MotelRoom.Awake</c> and <c>Sweatshop.Awake</c> are one
    /// address on 0.4.7f12. Harmony keys patches by method, so a patch on <c>Bungalow.Awake</c> and another on
    /// <c>SewerOffice.Awake</c> are two targets, and Il2CppInterop hooks the same address twice. The second hook
    /// takes the first one as the code to call back into, every call re-enters, and the game dies of a stack
    /// overflow (0xc00000fd) with nothing in the log. Ultimate Mod Menu 3.0 patches the Awake of five property
    /// classes this way and loading any save ended there; a probe mod with only the two Awake patches died the
    /// same way.
    ///
    /// The fix is at the one place every patch passes through, <c>PatchProcessor.Patch</c>. The first method
    /// patched on a function keeps the hook. A later patch on another method with the same function is put on
    /// that first method instead, and Harmony merges it there like any second patch on one method. The class it
    /// was written for is told to the mod (<see cref="FoldedAims"/>), whose PatchesOnFoldedCode then runs it for
    /// objects of that class rather than for the class whose method it now sits on.
    ///
    /// The same patch method listed on two folded targets (one postfix for every property) is merged into one
    /// entry, so it runs once per call as it would on Mono, where each class has its own Awake.
    ///
    /// The method's native function is read the way the interop does it: every generated method loads its
    /// <c>NativeMethodInfoPtr_</c> field, and an Il2CppMethodInfo starts with its function pointer. The plugin
    /// does not reference Il2CppInterop, so the field is found in the method's IL.
    /// </remarks>
    internal static class FoldedTargetsShareOneDetour
    {
        private const string Id = "doodesch.polyfill.foldedtargets";

        private static MelonLogger.Instance _log;
        private static FieldInfo _original, _instance;
        private static FieldInfo[] _patches;

        /// <summary>Native function -> the first method patched on it, which holds the hook.</summary>
        private static readonly Dictionary<IntPtr, MethodBase> Holder = new();
        private static readonly HashSet<string> Warned = new();

        internal static void Install(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var patch = AccessTools.Method(typeof(PatchProcessor), nameof(PatchProcessor.Patch));
                _original = AccessTools.Field(typeof(PatchProcessor), "original");
                _instance = AccessTools.Field(typeof(PatchProcessor), "instance");
                _patches = new[] { "prefix", "postfix", "transpiler", "finalizer", "ilmanipulator" }
                    .Select(name => AccessTools.Field(typeof(PatchProcessor), name))
                    .ToArray();
                if (patch == null || _original == null || _instance == null || _patches.Any(f => f == null))
                {
                    log.Warning("[harmony] PatchProcessor is not shaped as expected, so two patches on methods the game "
                              + "compiled to one function can still crash it.");
                    return;
                }

                var harmony = new HarmonyLib.Harmony(Id);
                harmony.Patch(patch, prefix: new HarmonyMethod(typeof(FoldedTargetsShareOneDetour), nameof(OneHook)));
                // Unpatch has to find the patch where it was put.
                foreach (var unpatch in typeof(PatchProcessor).GetMethods().Where(m => m.Name == nameof(PatchProcessor.Unpatch)))
                    harmony.Patch(unpatch, prefix: new HarmonyMethod(typeof(FoldedTargetsShareOneDetour), nameof(SameHolder)));

                log.Msg("[harmony] patches on methods the game compiled to one function share one hook.");
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not make patches on shared game code share one hook: " + e.Message);
            }
        }

        private static void OneHook(PatchProcessor __instance)
        {
            MethodBase original = null;
            try
            {
                original = _original.GetValue(__instance) as MethodBase;
                if (original == null) return;
                var function = NativeFunction(original);
                if (function == IntPtr.Zero) return;

                MethodBase holder;
                lock (Holder)
                {
                    if (!Holder.TryGetValue(function, out holder)) { Holder[function] = original; return; }
                }
                if (Same(holder, original))
                {
                    // A patch arriving directly on the holder may already be there, moved from a folded twin.
                    MergeInto(__instance, holder, original, moved: false);
                    return;
                }

                string owner = (_instance.GetValue(__instance) as HarmonyLib.Harmony)?.Id ?? "a mod";
                if (!SameShape(holder, original, out string why))
                {
                    // Patching it as asked would hook the function a second time. Pointing the processor at the
                    // holder with nothing in it rebuilds the holder as it is and adds no hook.
                    _original.SetValue(__instance, holder);
                    foreach (var field in _patches) field.SetValue(__instance, null);
                    _log?.Error($"[harmony] {owner}: {Describe(original)} is the same game code as {Describe(holder)}, "
                              + $"which is already patched, and its patch cannot join that one ({why}). A second hook on "
                              + "one function crashes the game, so this patch was left out.");
                    return;
                }

                _original.SetValue(__instance, holder);
                MergeInto(__instance, holder, original, moved: true);
                _log?.Msg($"[harmony] {owner}: {Describe(original)} is the same game code as {Describe(holder)}, which is "
                        + "already patched; the patch joins it there, since a second hook on one function crashes the game.");
            }
            catch (Exception e)
            {
                WarnOnce("fail:" + Describe(original), $"[harmony] could not check {Describe(original)} for shared game code, "
                       + "so it is patched as asked: " + e.Message);
            }
        }

        /// <summary>
        /// Notes which class each patch was written for, and drops a patch method the holder already carries from the
        /// same owner, so a postfix listed on two folded targets runs once per call.
        /// </summary>
        private static void MergeInto(PatchProcessor processor, MethodBase holder, MethodBase original, bool moved)
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(holder);
            string owner = (_instance.GetValue(processor) as HarmonyLib.Harmony)?.Id;
            foreach (var field in _patches)
            {
                if (field.GetValue(processor) is not HarmonyMethod wanted || wanted.method == null) continue;
                bool movedBefore = FoldedAims.Has(wanted.method);
                // Only patches a move is part of are touched; Harmony's own handling of a repeated patch stays.
                if (!moved && !movedBefore) continue;

                bool carried = info != null && Carries(info, owner, wanted.method);
                // A patch on the holder that no move put there was registered on the holder itself.
                if (!moved || (carried && !movedBefore)) FoldedAims.Add(wanted.method, holder.DeclaringType);
                if (moved) FoldedAims.Add(wanted.method, original.DeclaringType);
                if (carried) field.SetValue(processor, null);
            }
        }

        private static bool Carries(Patches info, string owner, MethodInfo method)
        {
            foreach (var list in new IEnumerable<Patch>[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers, info.ILManipulators })
                foreach (var patch in list)
                    if (patch.owner == owner && patch.PatchMethod == method) return true;
            return false;
        }

        /// <summary>An unpatch of a method whose patches were moved goes to the method that holds them.</summary>
        private static void SameHolder(PatchProcessor __instance)
        {
            try
            {
                var original = _original.GetValue(__instance) as MethodBase;
                if (original == null) return;
                var function = NativeFunction(original);
                if (function == IntPtr.Zero) return;
                MethodBase holder;
                lock (Holder) if (!Holder.TryGetValue(function, out holder)) return;
                if (!Same(holder, original)) _original.SetValue(__instance, holder);
            }
            catch (Exception e)
            {
                WarnOnce("unpatch", "[harmony] could not point an unpatch at the method holding the patches, so it "
                       + "goes to the method asked: " + e.Message);
            }
        }

        /// <summary>
        /// Harmony binds a patch's parameters by name and type against the method it sits on, so a patch can only
        /// move to a method with the same parameters and return type.
        /// </summary>
        private static bool SameShape(MethodBase holder, MethodBase original, out string why)
        {
            why = null;
            if (holder.IsStatic != original.IsStatic) { why = "one is static"; return false; }
            var returnHolder = (holder as MethodInfo)?.ReturnType ?? typeof(void);
            var returnOriginal = (original as MethodInfo)?.ReturnType ?? typeof(void);
            if (returnHolder != returnOriginal) { why = "the return types differ"; return false; }
            var a = holder.GetParameters();
            var b = original.GetParameters();
            if (a.Length != b.Length) { why = "the parameter counts differ"; return false; }
            for (int i = 0; i < a.Length; i++)
                if (a[i].ParameterType != b[i].ParameterType || a[i].Name != b[i].Name)
                {
                    why = $"parameter {i + 1} is {b[i].ParameterType.Name} {b[i].Name} there, {a[i].ParameterType.Name} {a[i].Name} here";
                    return false;
                }
            return true;
        }

        /// <summary>The function a generated interop method runs, or zero for anything else.</summary>
        private static IntPtr NativeFunction(MethodBase method)
        {
            var field = MethodInfoPointer(method);
            if (field == null) return IntPtr.Zero;
            var info = (IntPtr)field.GetValue(null);
            return info == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(info);   // Il2CppMethodInfo starts with its methodPointer
        }

        /// <summary>The <c>NativeMethodInfoPtr_</c> field the method's body loads.</summary>
        private static FieldInfo MethodInfoPointer(MethodBase method)
        {
            var type = method.DeclaringType;
            if (type == null || type.IsGenericType || method.IsGenericMethod || !InteropAssembly(type.Assembly)) return null;
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return null;
            var module = method.Module;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x7E) continue;                     // ldsfld
                FieldInfo field;
                try { field = module.ResolveField(BitConverter.ToInt32(il, i + 1)); }
                catch { continue; }                               // a byte that only looked like ldsfld
                if (field != null && field.IsStatic && field.FieldType == typeof(IntPtr) && field.DeclaringType == type
                    && field.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal))
                    return field;
            }
            return null;
        }

        private static bool InteropAssembly(Assembly assembly)
        {
            var location = assembly.Location;
            return !string.IsNullOrEmpty(location)
                && location.Replace('\\', '/').Contains("/Il2CppAssemblies/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Same(MethodBase a, MethodBase b)
            => a.Module == b.Module && a.MetadataToken == b.MetadataToken;

        private static string Describe(MethodBase method)
            => method == null ? "a method" : $"{method.DeclaringType?.Name}.{method.Name}";

        private static void WarnOnce(string key, string message)
        {
            lock (Warned) if (!Warned.Add(key)) return;
            _log?.Warning(message);
        }
    }
}
