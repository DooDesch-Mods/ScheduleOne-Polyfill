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

        private const string Npc = "Il2CppScheduleOne.NPCs.NPC";
        private const string Inv = "Il2CppScheduleOne.NPCs.NPCInventory";

        private static readonly string[] BasicInfo = { "NPCData", "BasicInfo" };
        private static readonly string[] Appearance = { "NPCData", "Appearance" };
        private static readonly string[] Interaction = { "NPCData", "Interaction" };

        /// <summary>NPCInventory is a component, not the NPC, so its path starts at its own back-reference -
        /// the same one the component itself now reads through (NPCInventory.cs:62, 113, 122).</summary>
        private static readonly string[] Inventory = { "_npc", "NPCData", "Inventory" };

        private const bool Write = true;
        private const bool Read = false;

        private const string NameSplit = "NPC.cs:63-69 until 0.4.5f2, now BasicInfo.cs:4-7";
        private const string Summon = "NPC.cs:116 until 0.4.5f2, now Interaction.cs:8 - and the game reads it "
                                    + "from there in NPCEnterableBuilding.cs:96";
        private const string SlotCount = "NPCInventory.cs:45 until 0.4.5f2, now Inventory.InventorySlotCount, "
                                       + "which NPCInventory.cs:62 builds the slots from";
        private const string Renamed = "NPCInventory.cs:51-65 until 0.4.5f2; the value kept its meaning and "
                                     + "lost its old name in Inventory.cs";
        private const string Pickpocket = "NPCInventory.cs:47 until 0.4.5f2, now Inventory.CanBePickpocketed, "
                                        + "read at NPCInventory.cs:372";

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

            // 0.4.6 emptied NPC and NPCInventory of their loose configuration fields and put the values in
            // NPCData objects (NPCData.cs). Nothing was dropped and nothing changed meaning - the game reads
            // the same values back out of the same places it used to write them, so putting the old member
            // back as a walk down that path is the whole repair.
            Moved(Npc, "ID", Write, BasicInfo, "ID", NameSplit),
            Moved(Npc, "FirstName", Write, BasicInfo, "FirstName", NameSplit),
            Moved(Npc, "LastName", Write, BasicInfo, "LastName", NameSplit),
            Moved(Npc, "MugshotSprite", Write, Appearance, "Mugshot",
                  "NPC.cs:71 until 0.4.5f2, now Appearance.Mugshot"),
            Moved(Npc, "CanBeSummoned", Write, Interaction, "CanBeSummoned", Summon),
            Moved(Npc, "CanBeSummoned", Read, Interaction, "CanBeSummoned", Summon),
            Moved(Inv, "SlotCount", Write, Inventory, "InventorySlotCount", SlotCount),
            Moved(Inv, "SlotCount", Read, Inventory, "InventorySlotCount", SlotCount),
            Moved(Inv, "ClearInventoryEachNight", Write, Inventory, "ClearInventoryOnNewDay", Renamed),
            Moved(Inv, "RandomCash", Write, Inventory, "RandomizeCash", Renamed),
            Moved(Inv, "RandomItems", Write, Inventory, "RandomizeInventory", Renamed),
            Moved(Inv, "CanBePickpocketed", Write, Inventory, "CanBePickpocketed", Pickpocket),
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

        /// <summary>
        /// A member that is still there and is reached differently: the old accessor is put back and walks
        /// <paramref name="hops"/> to wherever the value lives now.
        /// </summary>
        private static Rule Moved(string declaringType, string oldName, bool write,
                                  string[] hops, string target, string because)
        {
            string accessor = (write ? "set_" : "get_") + oldName;
            return new Rule
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = declaringType,
                OldName = accessor,
                ParameterCount = write ? 1 : 0,
                Because = because,
                Emit = (module, type) => EmitThrough(module, type, accessor, hops, target, write),
            };
        }

        /// <summary>
        /// Emits <c>this.A.B.Target</c> as a read or a write, and refuses if any step of that path is not on
        /// this build of the game.
        /// </summary>
        /// <remarks>
        /// The type of the rebuilt member is taken from the DESTINATION rather than named in the table, so a
        /// rule cannot claim a shape the game does not have - if the value changed type on the way, the
        /// accessor that comes out has the new type and the mod's call simply stays unresolved.
        ///
        /// Every hop is null-guarded. These paths run during NPC construction, when the components exist and
        /// the data object behind them may not yet, and a mod configuring an NPC one property at a time would
        /// otherwise turn a missing method into a crash - which is the same failure with a worse message.
        /// </remarks>
        private static MethodDefinition EmitThrough(ModuleDefinition module, TypeDefinition owner,
                                                    string accessor, string[] hops, string target, bool write)
        {
            var steps = new List<MethodDefinition>();
            var current = owner;
            foreach (string hop in hops)
            {
                var step = Getter(current, hop);
                if (step == null) return null;
                steps.Add(step);
                current = step.ReturnType?.Resolve();
                if (current == null) return null;
            }

            var destination = write ? Method(current, "set_" + target, 1) : Getter(current, target);
            if (destination == null) return null;

            var valueType = write ? destination.Parameters[0].ParameterType : destination.ReturnType;
            var returnType = write ? module.TypeSystem.Void : module.ImportReference(valueType);

            var method = new MethodDefinition(accessor, MethodAttributes.Public | MethodAttributes.HideBySig,
                                              returnType);
            if (write)
                method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None,
                                                              module.ImportReference(valueType)));

            var il = method.Body.GetILProcessor();
            var giveUp = il.Create(OpCodes.Nop);

            il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < steps.Count; i++)
            {
                il.Emit(i == 0 ? OpCodes.Call : OpCodes.Callvirt, module.ImportReference(steps[i]));
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse, giveUp);
            }

            if (write) il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(destination));
            il.Emit(OpCodes.Ret);

            // a hop was null: drop it and answer with nothing rather than throwing
            il.Append(giveUp);
            il.Emit(OpCodes.Pop);
            if (!write) EmitDefault(method, il, returnType);
            il.Emit(OpCodes.Ret);

            method.Body.InitLocals = true;
            return method;
        }

        /// <summary>Pushes the zero value of <paramref name="type"/>, whatever kind of type it is.</summary>
        private static void EmitDefault(MethodDefinition method, ILProcessor il, TypeReference type)
        {
            if (!type.IsValueType) { il.Emit(OpCodes.Ldnull); return; }

            var slot = new VariableDefinition(type);
            method.Body.Variables.Add(slot);
            il.Emit(OpCodes.Ldloca_S, slot);
            il.Emit(OpCodes.Initobj, type);
            il.Emit(OpCodes.Ldloc_S, slot);
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
