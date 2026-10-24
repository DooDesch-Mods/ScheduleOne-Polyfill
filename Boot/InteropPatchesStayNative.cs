using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// A Harmony patch on a game method hooks the game's native code and nothing else.
    /// </summary>
    /// <remarks>
    /// Il2CppInterop 1.5.1 patches a game method in two places. <c>Il2CppDetourMethodPatcher.DetourTo</c> hooks
    /// the IL2CPP function, which is where every call from the game and every call from a mod arrives, and
    /// then also lays a MonoMod <c>Detour</c> over the managed interop method, so a call from a mod skips the
    /// trip through <c>il2cpp_runtime_invoke</c>. The second one is a shortcut, not a second patch: the native
    /// hook alone already runs the patch for both kinds of caller.
    ///
    /// The shortcut is what kills the process. MonoMod finds where to write its jump by walking the method's
    /// precode, and that walk is a guess against tiered compilation. When it guesses wrong it writes the jump
    /// over the precode itself. The method still runs, but the precode no longer names its method, and coreclr
    /// reads that name back whenever it builds a class that inherits the slot. Measured with Police Response
    /// Overhaul 1.1.6, which patches <c>NPC.SetVisible(bool, bool)</c> (vtable slot 66) next to S1API: in each
    /// of 4 dead launches the precode behind that slot started with <c>FF 25</c>, MonoMod's jump; in each of
    /// the 7 that lived it was the runtime's own <c>E9 .. 5F</c>. The first later load of an NPC subclass dies
    /// in <c>ClassLoader::PropagateCovariantReturnMethodImplSlots</c> (coreclr.dll+0x9685a, 0xc0000005), which
    /// a mod sees as <c>Assembly.GetTypes()</c> taking the game down with no exception and no log line.
    ///
    /// So the shortcut is left out, and the patch runs through the native hook alone. A mod calling a
    /// patched game method pays a few more jumps per call. Nothing else differs, except one case
    /// where the result now agrees with the game: a mod calling a patched virtual method on an object whose
    /// class overrides it natively reaches the override, as a call from the game does, instead of the
    /// patch on the base method.
    ///
    /// Only that one construction is taken out. If <c>DetourTo</c> is not shaped as read here, it is left
    /// as it is and the log says so.
    /// </remarks>
    internal static class InteropPatchesStayNative
    {
        private const string Id = "doodesch.polyfill.interopnative";
        private const string PatcherType =
            "Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher, Il2CppInterop.HarmonySupport";
        private const string DetourType = "MonoMod.RuntimeDetour.Detour";

        private static MelonLogger.Instance _log;
        private static bool _rewritten;
        private static int _skipped;

        internal static void Install(MelonLogger.Instance log)
        {
            _log = log;
            try
            {
                var patcher = Type.GetType(PatcherType, false);
                if (patcher == null)
                {
                    log.Msg("[harmony] no Il2CppInterop patcher in this MelonLoader, so there is no managed "
                          + "shortcut to leave out.");
                    return;
                }

                var target = AccessTools.DeclaredMethod(patcher, "DetourTo", new[] { typeof(MethodBase) });
                if (target == null)
                {
                    log.Warning("[harmony] Il2CppInterop's patcher has no DetourTo(MethodBase), so a patch on a "
                              + "game method can still break the method's precode and crash a later type load.");
                    return;
                }

                new HarmonyLib.Harmony(Id).Patch(
                    target, transpiler: new HarmonyMethod(typeof(InteropPatchesStayNative), nameof(Rewrite)));

                if (_rewritten)
                    log.Msg("[harmony] patches on game methods hook the game's native code only; Il2CppInterop's "
                          + "managed shortcut is left out.");
            }
            catch (Exception e)
            {
                log.Warning("[harmony] could not keep patches on game methods native-only, so one can still break "
                          + "the method's precode and crash a later type load: " + e.Message);
            }
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();

            int create = -1, apply = -1, creates = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].opcode == OpCodes.Newobj && list[i].operand is ConstructorInfo ctor
                    && ctor.DeclaringType?.FullName == DetourType && ctor.GetParameters().Length == 2)
                {
                    creates++;
                    create = i;
                }
            }
            if (creates == 1)
                for (int i = create + 1; i < list.Count; i++)
                    if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call)
                        && list[i].operand is MethodInfo method && method.Name == "Apply"
                        && method.DeclaringType?.FullName == DetourType)
                    {
                        apply = i;
                        break;
                    }

            if (creates != 1 || apply < 0)
            {
                _log?.Warning($"[harmony] Il2CppInterop's DetourTo is not the shape this reads ({creates} managed "
                            + $"detour(s) built, Apply {(apply < 0 ? "not found" : "found")}), so it is left as it "
                            + "is and a patch on a game method can still crash a later type load.");
                return list;
            }

            // The two MethodBase arguments go to Skip instead of the constructor, and a null stands where the
            // Detour was: it is stored, its Apply becomes a pop, and the cache that kept it alive keeps a null.
            list[create].opcode = OpCodes.Call;
            list[create].operand = AccessTools.Method(typeof(InteropPatchesStayNative), nameof(Skip));
            list.Insert(create + 1, new CodeInstruction(OpCodes.Ldnull));
            list[apply + 1].opcode = OpCodes.Pop;
            list[apply + 1].operand = null;

            _rewritten = true;
            return list;
        }

        private static void Skip(MethodBase original, MethodBase replacement)
        {
            int count = Interlocked.Increment(ref _skipped);
            if (count == 1)
                _log?.Msg($"[harmony] {original?.DeclaringType?.Name}.{original?.Name} is patched on its native code "
                        + "only, as every later game method will be.");
#if DEBUG
            else if (count % 50 == 0)
                _log?.Msg($"[debug] [harmony] {count} game method patch(es) kept native-only so far.");
#endif
        }
    }
}
