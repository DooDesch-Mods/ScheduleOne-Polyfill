using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Bridges.Steps.S0_4_6f13_To_0_4_7f5
{
    /// <summary>
    /// What 0.4.7 took away that a name match cannot follow, put back as the member the game has now.
    /// </summary>
    /// <remarks>
    /// Read against the Mono build of 0.4.7f6 with its method bodies, not guessed from the stripped
    /// archive. Every rule names the lines it rests on.
    /// </remarks>
    internal sealed class Set : BridgeSet
    {
        internal override string Step => "0.4.6f13 -> 0.4.7f5";

        /// <summary>0.4.7f5 is the build these names went away in.</summary>
        internal override string From => "0.4.7f5";

        /// <summary>Read against 0.4.7f6, the newest beta build.</summary>
        internal override string VerifiedTo => "0.4.7f6";

        private const string Movement = "Il2CppScheduleOne.NPCs.NPCMovement";

        private const string SpeedMultiplier = "the multiplier moved off the movement component onto its speed "
            + "controller: 0.4.6 multiplied the agent speed by NPCMovement.MoveSpeedMultiplier "
            + "(NPCMovement.cs:407), 0.4.7 multiplies MoveSpeed by NPCSpeedController._speedMultiplier "
            + "(NPCSpeedController.cs:117), which SetSpeedMultiplier writes";

        internal override IEnumerable<Bridge> Declare() => new[]
        {
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Movement,
                OldName = "get_MoveSpeedMultiplier",
                ParameterCount = 0,
                Because = SpeedMultiplier,
                Emit = EmitMoveSpeedMultiplierGetter,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Movement,
                OldName = "set_MoveSpeedMultiplier",
                ParameterCount = 1,
                Because = SpeedMultiplier,
                Emit = EmitMoveSpeedMultiplierSetter,
            },
        };

        /// <summary>
        /// <c>NPCMovement.MoveSpeedMultiplier</c>, read: the controller's own multiplier.
        /// </summary>
        /// <remarks>
        /// One on an NPC with no controller, which is what the old property started at and what the
        /// controller starts at too (<c>_speedMultiplier = 1f</c>, NPCSpeedController.cs:9). A getter that throws on a half-built NPC
        /// would break the mod on the one frame where the old code simply read a default.
        /// </remarks>
        private static MethodDefinition EmitMoveSpeedMultiplierGetter(ModuleDefinition module, TypeDefinition movement)
        {
            var getController = Getter(movement, "SpeedController");
            var controller = getController?.ReturnType?.Resolve();
            var getMultiplier = Getter(controller, "_speedMultiplier");
            if (getController == null || getMultiplier == null) return null;

            var method = new MethodDefinition("get_MoveSpeedMultiplier",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.TypeSystem.Single);

            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Call, getMultiplier);

            // var c = SpeedController; return c == null ? 1f : c._speedMultiplier;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>NPCMovement.MoveSpeedMultiplier = v</c>: <c>SpeedController.SetSpeedMultiplier(v)</c>.
        /// </summary>
        /// <remarks>
        /// Not a stack entry, unlike the 0.4.6 speed bridges. The old property was one plain value that the
        /// last writer owned, and the game's own writers moved to exactly this call, setting it on start and
        /// back to 1 on end: Athletic.cs:29 and :51, Calming.cs:14 and :28, Energizing.cs:22 and :42,
        /// Sedating.cs:21 and :40. The method is a plain store plus a recalculation
        /// (NPCSpeedController.cs:37-41). A mod writing it now shares that one value with the game the same
        /// way it did before.
        /// </remarks>
        private static MethodDefinition EmitMoveSpeedMultiplierSetter(ModuleDefinition module, TypeDefinition movement)
        {
            var getController = Getter(movement, "SpeedController");
            var controller = getController?.ReturnType?.Resolve();
            var set = Method(controller, "SetSpeedMultiplier", 1);
            if (getController == null || set == null) return null;

            var method = new MethodDefinition("set_MoveSpeedMultiplier",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Single));

            var il = method.Body.GetILProcessor();
            var done = il.Create(OpCodes.Ret);
            var have = il.Create(OpCodes.Ldarg_1);

            // var c = SpeedController; if (c == null) return; c.SetSpeedMultiplier(value);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br_S, done);
            il.Append(have);                       // stack: c, value
            il.Emit(OpCodes.Callvirt, set);
            il.Append(done);
            return method;
        }

        private static MethodDefinition Getter(TypeDefinition type, string member)
            => Method(type, "get_" + member, 0);

        private static MethodDefinition Method(TypeDefinition type, string name, int parameters)
        {
            if (type == null) return null;
            foreach (var method in type.Methods)
                if (method.Name == name && method.Parameters.Count == parameters) return method;
            return null;
        }
    }
}
