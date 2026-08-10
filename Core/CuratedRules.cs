using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Core
{
    /// <summary>
    /// Repairs that a rule cannot derive and a person had to read the game to write.
    /// </summary>
    /// <remarks>
    /// Renames are mechanical: the old name is put back and it calls the new one. These are not. The member
    /// did not move, it was DISSOLVED - the value it held is now computed somewhere else out of two other
    /// members, or the write it performed is now an entry on a stack. Nothing in the metadata says so; it
    /// took reading the decompiled bodies of both versions to know.
    ///
    /// So each one is written out by hand, cites where it came from, and is emitted only when the analysis
    /// actually finds that member missing and a mod actually asks for it. If any piece a rule needs is not
    /// on this build of the game, the rule refuses rather than emitting something approximate.
    ///
    /// This is also the honest boundary of the whole project. A curated rule is a person deciding that two
    /// different pieces of code mean the same thing. No amount of diffing produces that, and a wrong one is
    /// worse than a missing one, because it runs.
    /// </remarks>
    internal static class CuratedRules
    {
        /// <summary>The label every stack entry Polyfill pushes is filed under, so it can be found again.</summary>
        internal const string StackLabel = "Polyfill";

        internal sealed class Rule
        {
            internal string Assembly;
            internal string DeclaringType;
            internal string OldName;
            internal int ParameterCount;
            internal string Because;
            internal Func<ModuleDefinition, TypeDefinition, MethodDefinition> Emit;
        }

        private static readonly List<Rule> All = new()
        {
            new Rule
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPCMovement",
                OldName = "get_MovementSpeedScale",
                ParameterCount = 0,
                Because = "the field was folded into UpdateSpeed(); the same value is now "
                        + "SpeedController.ActiveSpeedControl.speed * SpeedController.SpeedMultiplier",
                Emit = EmitMovementSpeedScaleGetter,
            },
            new Rule
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPCMovement",
                OldName = "set_MovementSpeedScale",
                ParameterCount = 1,
                Because = "writing the field wrote the BASE speed the controller derived; the base slot is "
                        + "now a priority-0 entry on the speed stack, which everything the game does outranks",
                Emit = EmitMovementSpeedScaleSetter,
            },
            new Rule
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPC",
                OldName = "OverrideAggression",
                ParameterCount = 1,
                Because = "Aggression became a read-only view over AggressionController; vanilla's own "
                        + "migration of this call is MethInstance.cs:51",
                Emit = EmitOverrideAggression,
            },
        };

        internal static Rule Find(string assembly, string declaringType, string oldName, int parameterCount)
        {
            foreach (var rule in All)
                if (rule.OldName == oldName && rule.DeclaringType == declaringType
                    && rule.ParameterCount == parameterCount
                    && string.Equals(rule.Assembly, assembly, StringComparison.OrdinalIgnoreCase))
                    return rule;
            return null;
        }

        /// <summary>
        /// <c>NPCMovement.MovementSpeedScale</c> - the normalized walk-to-run position, 0.4.5f2 and earlier.
        /// </summary>
        /// <remarks>
        /// It was a plain public field, written every frame by NPCSpeedController and read once by
        /// NPCMovement. 0.4.6 merged producer and consumer into UpdateSpeed(), so the field went away while
        /// the quantity did not: `ActiveSpeedControl.speed * SpeedMultiplier` is the same expression that
        /// used to be stored in it.
        ///
        /// Returns 0 when the speed stack is not up yet - ActiveSpeedControl is only populated in
        /// NPCSpeedController.Awake, and the old field read 0 before that too.
        /// </remarks>
        private static MethodDefinition EmitMovementSpeedScaleGetter(ModuleDefinition module, TypeDefinition movement)
        {
            var getSpeedController = Getter(movement, "SpeedController");
            var controller = getSpeedController?.ReturnType?.Resolve();
            var getActive = Getter(controller, "ActiveSpeedControl");
            var getMultiplier = Getter(controller, "SpeedMultiplier");
            var getSpeed = Getter(getActive?.ReturnType?.Resolve(), "speed");
            if (getSpeedController == null || getActive == null || getMultiplier == null || getSpeed == null)
                return null;

            var method = new MethodDefinition("get_MovementSpeedScale",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Single);

            var il = method.Body.GetILProcessor();
            var zero = il.Create(OpCodes.Ldc_R4, 0f);
            var compute = il.Create(OpCodes.Nop);

            // var c = SpeedController; if (c == null) return 0;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getSpeedController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, compute);
            il.Emit(OpCodes.Pop);
            il.Append(zero);
            il.Emit(OpCodes.Ret);

            // if (c.ActiveSpeedControl == null) return 0;
            il.Append(compute);
            il.Emit(OpCodes.Callvirt, getActive);
            var haveControl = il.Create(OpCodes.Callvirt, getSpeed);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, haveControl);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ret);

            // return ActiveSpeedControl.speed * SpeedController.SpeedMultiplier;
            il.Append(haveControl);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getSpeedController);
            il.Emit(OpCodes.Callvirt, getMultiplier);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ret);

            return method;
        }

        /// <summary>
        /// <c>NPCMovement.MovementSpeedScale = v</c> - the write half, and the one that needed a decision.
        /// </summary>
        /// <remarks>
        /// The old field was TRANSIENT: NPCSpeedController overwrote it from the speed stack on the next
        /// update, so a mod's write survived until the game next had an opinion. The new stack is STICKY -
        /// an entry stays until somebody removes it. Those are not the same thing, and forwarding one to the
        /// other naively leaves an NPC permanently altered.
        ///
        /// What makes it faithful is the PRIORITY. The value the old field held was the base the controller
        /// derived, and the base slot is priority 0 - the same as vanilla's own "default" entry. Every
        /// behaviour the game runs sits above it (footpatrol 1, fleeing 2, searching 6, combat 10, cowering
        /// 80, seated 100), so a write here can never outrank the game, exactly as a write to the old field
        /// could not survive the game wanting something else.
        ///
        /// The entry is removed before it is re-added, so repeated writes replace rather than pile up, and
        /// everything lands under one label so it can be found and taken back off.
        /// </remarks>
        private static MethodDefinition EmitMovementSpeedScaleSetter(ModuleDefinition module, TypeDefinition movement)
        {
            var getSpeedController = Getter(movement, "SpeedController");
            var controller = getSpeedController?.ReturnType?.Resolve();
            var add = Method(controller, "AddSpeedControl", 1);
            var remove = Method(controller, "RemoveSpeedControl", 1);
            var control = Nested(controller, "SpeedControl");
            var constructor = Method(control, ".ctor", 3);
            if (getSpeedController == null || add == null || remove == null || constructor == null) return null;

            var method = new MethodDefinition("set_MovementSpeedScale",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None,
                                                          module.TypeSystem.Single));

            var il = method.Body.GetILProcessor();
            var done = il.Create(OpCodes.Ret);

            // var c = SpeedController; if (c == null) return;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getSpeedController);
            il.Emit(OpCodes.Dup);
            var have = il.Create(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br_S, done);

            // c.RemoveSpeedControl(label); c.AddSpeedControl(new SpeedControl(label, 0, value));
            il.Append(have);                       // stack: c, c
            il.Emit(OpCodes.Ldstr, StackLabel);
            il.Emit(OpCodes.Callvirt, remove);     // stack: c
            il.Emit(OpCodes.Ldstr, StackLabel);
            il.Emit(OpCodes.Ldc_I4_0);             // priority 0 - the base slot, under everything the game does
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Callvirt, add);
            il.Append(done);
            return method;
        }

        /// <summary>
        /// <c>NPC.OverrideAggression(float)</c> - a plain field write until 0.4.6.
        /// </summary>
        /// <remarks>
        /// Aggression is now a read-only view over a labelled stack, and the game migrated its own call site
        /// the same way: MethInstance pushes an Override entry and pops it by label. The label matters -
        /// unlike the old field write, a stack entry stays until somebody removes it - so everything Polyfill
        /// pushes is filed under one name and can be taken back off.
        /// </remarks>
        private static MethodDefinition EmitOverrideAggression(ModuleDefinition module, TypeDefinition npc)
        {
            var getController = Getter(npc, "AggressionController");
            var stack = getController?.ReturnType?.Resolve();
            var add = Method(stack, "Add", 1);
            var entry = Nested(stack, "StackEntry");
            var mode = Nested(stack, "EStackMode");
            var constructor = Method(entry, ".ctor", 4);
            if (getController == null || add == null || constructor == null || mode == null) return null;

            var method = new MethodDefinition("OverrideAggression",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("aggression", ParameterAttributes.None,
                                                          module.TypeSystem.Single));

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Ldstr, StackLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_1);        // EStackMode.Override
            il.Emit(OpCodes.Ldc_I4_0);        // order
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Callvirt, add);
            il.Emit(OpCodes.Ret);
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

        private static TypeDefinition Nested(TypeDefinition type, string name)
        {
            if (type == null) return null;
            foreach (var nested in type.NestedTypes)
                if (nested.Name == name) return nested;
            return null;
        }
    }
}
