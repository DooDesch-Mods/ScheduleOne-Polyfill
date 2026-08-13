using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod built before 0.4.6 asks for the key it always asked for, and gets the one that now sits at
    /// that number.
    /// </summary>
    /// <remarks>
    /// <c>ButtonCode</c> is an enum, and an enum in a caller's assembly is a NUMBER. 0.4.6 deleted fourteen
    /// entries out of the middle of it:
    /// <code>
    /// 0.4.5f2   ... Sprint, Escape, Back, Interact, Submit, TogglePhone, VehicleToggleLights, ...
    /// 0.4.6     ... Sprint,               Interact, Submit,              VehicleToggleLights, ...
    /// </code>
    /// So <c>Interact</c>, which every mod that does anything on a key press asks for, was compiled as 12
    /// and 12 is now <c>VehicleToggleLights</c> - the H key. Measured on two mods: T.H.M reads it three
    /// times and OverTheCounter four, and in both the symptom is the same. The item is in your hand, the
    /// model is drawn, and pressing E does nothing.
    ///
    /// Nothing about that is visible to Polyfill's usual machinery. Both sides are valid: the mod asks for a
    /// number, the game has that number, and the call returns cleanly. This is the case the README lists as
    /// unrepairable by rule - a reordered enum as a bare number - and it is repairable by a fix, because a
    /// fix is allowed to know which mod it is looking at.
    ///
    /// THE MAPPING IS BY NAME, NOT BY NUMBER. The old order below is a fixed fact about builds up to
    /// 0.4.5f2; the new positions are read off the installed game. A future build that moves something else
    /// is therefore handled by the same code, and a build that has not moved anything makes this stand down
    /// on its own.
    ///
    /// A button the game DELETED cannot be pointed anywhere. Those become a number no key ever produces, so
    /// the check simply never fires - and the log names it, because "OverTheCounter closes that panel with
    /// Escape and 0.4.6 has no Escape button" is the sentence its author needs.
    /// </remarks>
    internal static class ButtonCodeShift
    {
        /// <summary>
        /// ScheduleOne.GameInput.ButtonCode as it stood up to 0.4.5f2, in order. Position IS the value a mod
        /// of that era compiled.
        /// </summary>
        private static readonly string[] OldOrder =
        {
            "PrimaryClick", "SecondaryClick", "TertiaryClick", "Forward", "Backward", "Left", "Right",
            "Jump", "Crouch", "Sprint", "Escape", "Back", "Interact", "Submit", "TogglePhone",
            "VehicleToggleLights", "VehicleHandbrake", "RotateLeft", "RotateRight", "ManagementMode",
            "OpenMap", "OpenJournal", "OpenTexts", "QuickMove", "ToggleFlashlight", "ViewAvatar", "Reload",
            "InventoryLeft", "InventoryRight", "Holster", "VehicleResetCamera", "SkateboardDismount",
            "SkateboardMount", "TogglePauseMenu",
        };

        /// <summary>A value no button has, so a check against a deleted button is false rather than wrong.</summary>
        private const int Nowhere = -1;

        private static int[] _map;
        private static MelonLogger.Instance _log;

        /// <summary>Old value to new value, or null when this game orders them exactly as 0.4.5f2 did.</summary>
        internal static int[] Map(MelonLogger.Instance log)
        {
            if (_map != null) return _map;
            _log = log;

            var names = InstalledOrder();
            if (names == null || names.Length == 0) return null;

            bool moved = names.Length != OldOrder.Length;
            var map = new int[OldOrder.Length];
            for (int old = 0; old < OldOrder.Length; old++)
            {
                map[old] = Nowhere;
                for (int now = 0; now < names.Length; now++)
                    if (string.Equals(names[now], OldOrder[old], StringComparison.Ordinal)) { map[old] = now; break; }
                if (map[old] != old) moved = true;
            }

            return _map = moved ? map : null;
        }

        private static string[] InstalledOrder()
        {
            try
            {
                var type = typeof(Il2CppScheduleOne.GameInput).GetNestedType("ButtonCode");
                return type == null ? null : Enum.GetNames(type);
            }
            catch { return null; }
        }

        internal static string OldName(int value)
            => value >= 0 && value < OldOrder.Length ? OldOrder[value] : value.ToString();

        /// <summary>
        /// Rewrite every button number a method hands to GameInput.
        /// </summary>
        /// <remarks>
        /// Only a constant that is ABOUT TO BE PASSED to one of the GetButton calls is touched, which is why
        /// this looks one instruction ahead instead of rewriting every 12 it finds. A method that computes
        /// its button code rather than spelling it out is left alone and reported, because guessing at a
        /// value that is not in the IL would be exactly the inference this project refuses.
        /// </remarks>
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions,
                                                               string who)
        {
            var code = new List<CodeInstruction>(instructions);
            int[] map = _map;
            if (map == null) return code;

            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (!TakesAButton(code[i + 1])) continue;
                if (!Constant(code[i], out int old)) continue;
                if (old < 0 || old >= map.Length) continue;

                int now = map[old];
                if (now == old) continue;

                // The instruction is EDITED, not replaced. A CodeInstruction carries the labels branches
                // jump to and the exception blocks it opens; a fresh object drops both, and the method then
                // fails to compile with nothing but "IL Compile Error (unknown location)" to go on.
                var replacement = Load(now);
                code[i].opcode = replacement.opcode;
                code[i].operand = replacement.operand;

                _pointed.Add((old, now));
            }
            return code;
        }

        /// <summary>What the last transpile changed. Read by Apply once Harmony has accepted the method,
        /// because a rewrite that does not compile is not a repair and must not be reported as one.</summary>
        private static readonly List<(int Old, int Now)> _pointed = new();

        private static bool TakesAButton(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) return false;
            return instruction.operand is MethodInfo method
                && method.DeclaringType == typeof(Il2CppScheduleOne.GameInput)
                && method.Name.StartsWith("GetButton", StringComparison.Ordinal);
        }

        /// <summary>The integer an instruction pushes, in any of the shapes the compiler emits for one.</summary>
        private static bool Constant(CodeInstruction instruction, out int value)
        {
            value = 0;
            var op = instruction.opcode;
            if (op == OpCodes.Ldc_I4) { value = (int)instruction.operand; return true; }
            if (op == OpCodes.Ldc_I4_S) { value = Convert.ToInt32(instruction.operand); return true; }
            if (op == OpCodes.Ldc_I4_M1) { value = -1; return true; }

            for (int small = 0; small <= 8; small++)
                if (op == Short(small)) { value = small; return true; }
            return false;
        }

        private static CodeInstruction Load(int value)
            => value >= 0 && value <= 8 ? new CodeInstruction(Short(value))
             : value >= sbyte.MinValue && value <= sbyte.MaxValue
                 ? new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)value)
                 : new CodeInstruction(OpCodes.Ldc_I4, value);

        private static OpCode Short(int value) => value switch
        {
            0 => OpCodes.Ldc_I4_0, 1 => OpCodes.Ldc_I4_1, 2 => OpCodes.Ldc_I4_2, 3 => OpCodes.Ldc_I4_3,
            4 => OpCodes.Ldc_I4_4, 5 => OpCodes.Ldc_I4_5, 6 => OpCodes.Ldc_I4_6, 7 => OpCodes.Ldc_I4_7,
            _ => OpCodes.Ldc_I4_8,
        };

        /// <summary>
        /// Put the rewrite on the named methods of one mod. Returns how many took it.
        /// </summary>
        /// <remarks>
        /// Methods are named rather than found by sweeping the assembly. Enumerating a mod's types to look
        /// for button reads would be a wider net for a narrower gain, and every method here was read before
        /// it was listed - which is the difference between a fix and a heuristic.
        /// </remarks>
        internal static int Apply(MelonLogger.Instance log, string who, string assembly,
                                  params (string Type, string Method)[] targets)
        {
            if (Map(log) == null)
            {
                log.Msg($"[fix] {who}: this game orders the buttons the way the mod expects, so nothing "
                      + "needed pointing anywhere.");
                return 0;
            }

            Assembly found = null;
            foreach (var one in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(one.GetName()?.Name, assembly, StringComparison.OrdinalIgnoreCase))
                { found = one; break; }
            if (found == null) return 0;

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            int patched = 0;

            foreach (var (typeName, methodName) in targets)
            {
                try
                {
                    var type = found.GetType(typeName, false);
                    var method = type == null ? null : AccessTools.Method(type, methodName);
                    if (method == null)
                    {
                        log.Warning($"[fix] {who}: {typeName}.{methodName} is not where it was.");
                        continue;
                    }

                    Current = who;
                    _pointed.Clear();
                    harmony.Patch(method,
                        transpiler: new HarmonyMethod(typeof(ButtonCodeShift), nameof(Rewrite)));

                    // Only now. Harmony compiles the rewritten body inside Patch, and a body it rejects
                    // leaves the method exactly as it was.
                    foreach (var (old, now) in _pointed)
                    {
                        if (now == Nowhere)
                            log.Warning($"[fix] {who}: {typeName} asks for the {OldName(old)} button, which "
                                      + "this build of the game does not have any more, so that key does "
                                      + "nothing.");
                        else
                            log.Msg($"[fix] {who}: {typeName}.{methodName} asked for button {old}, which was "
                                  + $"{OldName(old)} and is now {now}. Pointed at {now}.");
                    }
                    if (_pointed.Count > 0) patched++;
                }
                catch (Exception e)
                {
                    log.Warning($"[fix] {who}: {typeName}.{methodName} could not be pointed: {e.Message}");
                }
            }
            return patched;
        }

        /// <summary>Which mod the transpiler is running for. Set immediately before each Patch call, which
        /// Harmony carries out synchronously.</summary>
        private static string Current = "?";

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
            => Transpile(instructions, Current);
    }
}
