using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Can this method be patched at all, or would asking take the game with it?
    /// </summary>
    /// <remarks>
    /// HARMONY PINS THE TARGET BEFORE ANY PREFIX OR TRANSPILER RUNS. `Harmony.Patch` reaches
    /// `ManagedMethodPatcher.DetourTo` -> `new ILHook(...)` -> `Pin` -> `RuntimeHelpers.PrepareMethod`,
    /// so the JIT has to compile the original. A body naming something the assemblies have not got
    /// cannot be compiled - and inside MonoMod's JIT hook that is not a `TypeLoadException` anybody can
    /// catch. It is `Fatal error. Internal CLR error. (0x80131506)` and the process is gone before the
    /// next line of the log is written.
    ///
    /// A player on 0.4.6f13 with ninety mods lost the game at startup that way, twice, and the log ends
    /// mid-sentence at the fix that asked. Nothing in it points at the mod underneath; it reads as
    /// "Polyfill crashes".
    ///
    /// SO THE BODY IS READ FIRST. Harmony's own reader builds the instruction list through Cecil without
    /// going near the JIT, and it resolves every member reference on the way - an unreadable body throws
    /// a normal exception here, and a reference it cannot resolve arrives as a null operand. Both are
    /// answers this can act on.
    ///
    /// WHY NOT CHECK THE MOD'S DEPENDENCIES INSTEAD. That was the first attempt and it was wrong: a
    /// 72-mod test slot is missing the same dependency as the player's machine and patches the same
    /// method without trouble, so standing down on a missing reference would switch off repairs that
    /// work. The question is not what the mod declares, it is whether THIS method's body resolves.
    /// </remarks>
    internal static class Patchable
    {
        /// <summary>
        /// True when the body resolves and Harmony can be asked to patch it.
        /// </summary>
        /// <param name="id">The fix asking, so a refusal names who stood down.</param>
        internal static bool Check(MethodBase target, string id, MelonLogger.Instance log)
        {
            if (target == null) return false;

            List<CodeInstruction> body;
            try
            {
                body = PatchProcessor.GetOriginalInstructions(target);
            }
            catch (Exception e)
            {
                log.Warning($"[fix] {id}: {Name(target)} cannot be read on this install, so it was left "
                          + "alone - patching a method whose body does not resolve takes the game down "
                          + "instead of throwing. Usually a dependency of that mod is missing or is the "
                          + "wrong branch. (" + e.Message + ")");
                return false;
            }

            if (body == null)
            {
                log.Warning($"[fix] {id}: {Name(target)} has no readable body on this install, so it was "
                          + "left alone.");
                return false;
            }

            for (int i = 0; i < body.Count; i++)
            {
                var instruction = body[i];
                if (!NamesAMember(instruction.opcode) || instruction.operand != null) continue;

                // The reader hands back null for a reference it could not resolve rather than throwing,
                // so this is the same fault arriving quietly. Named with its index because "somewhere in
                // this method" is not something an author can act on.
                log.Warning($"[fix] {id}: {Name(target)} names something this install has not got at "
                          + $"instruction {i} ({instruction.opcode}), so it was left alone. Patching it "
                          + "would take the game down rather than throw. Usually a dependency of that mod "
                          + "is missing or is the wrong branch.");
                return false;
            }

            return true;
        }

        /// <summary>Does this opcode carry a member reference that has to resolve?</summary>
        private static bool NamesAMember(OpCode opcode)
            => opcode == OpCodes.Call || opcode == OpCodes.Callvirt || opcode == OpCodes.Newobj
            || opcode == OpCodes.Ldfld || opcode == OpCodes.Stfld
            || opcode == OpCodes.Ldsfld || opcode == OpCodes.Stsfld
            || opcode == OpCodes.Ldftn || opcode == OpCodes.Ldvirtftn;

        private static string Name(MethodBase method)
        {
            try { return method.DeclaringType?.FullName + "." + method.Name; }
            catch { return method.Name; }
        }
    }
}
