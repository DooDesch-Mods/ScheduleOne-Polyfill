using Mono.Cecil;
using Mono.Cecil.Cil;
using static Polyfill.Bridges.Shapes;

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

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Movement,
                OldName = "Warp",
                ParameterCount = 1,
                ParameterTypes = new[] { "UnityEngine.Vector3" },
                AllowOverload = true,
                Because = "the rotation became a second parameter; passing the NPC's own rotation is the "
                        + "one-argument Warp, which never turned it (NPCMovement.cs:615-645 on 0.4.6f13, "
                        + "NPCMovement.cs:423-457 on 0.4.7f6)",
                Emit = EmitWarpKeepingRotation,
            },
        };

        /// <summary>
        /// <c>Warp(Vector3)</c>: <c>Warp(position, transform.rotation)</c>.
        /// </summary>
        /// <remarks>
        /// NOT THE DEFAULT, although the default is what the signature offers and what the game's own
        /// one-argument callers get. 0.4.7 guards the turn with <c>if (rotation != default(Quaternion))</c>
        /// (NPCMovement.cs:453), and Unity compares quaternions by their dot product being close to 1. The
        /// dot product of the zero quaternion with anything is 0, so the guard is never false: every call
        /// with the default writes a zero quaternion into the rotation, which Unity turns into identity.
        ///
        /// Measured with a probe on 0.4.7f6: an NPC facing 3.61 degrees faced 0 after a warp that passed the
        /// default. The 0.4.6 method never touched the rotation at all, so the faithful forward passes the
        /// rotation the NPC already has - the guard then writes back what was there.
        /// </remarks>
        private static MethodDefinition EmitWarpKeepingRotation(ModuleDefinition module, TypeDefinition movement)
        {
            MethodDefinition target = null;
            foreach (var candidate in movement.Methods)
            {
                if (candidate.Name != "Warp" || candidate.Parameters.Count != 2) continue;
                if (candidate.Parameters[0].ParameterType.FullName != "UnityEngine.Vector3") continue;
                if (candidate.Parameters[1].ParameterType.FullName != "UnityEngine.Quaternion") continue;
                target = candidate;
            }
            var getTransform = MethodUp(movement, "get_transform", 0);
            var getRotation = Getter(getTransform?.ReturnType?.Resolve(), "rotation");
            if (target == null || getTransform == null || getRotation == null) return null;

            var method = new MethodDefinition("Warp",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("position", ParameterAttributes.None,
                                                          module.ImportReference(target.Parameters[0].ParameterType)));

            // this.Warp(position, this.transform.rotation);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(getTransform));
            il.Emit(OpCodes.Callvirt, module.ImportReference(getRotation));
            il.Emit(OpCodes.Call, module.ImportReference(target));
            il.Emit(OpCodes.Ret);
            return method;
        }

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
    }
}
