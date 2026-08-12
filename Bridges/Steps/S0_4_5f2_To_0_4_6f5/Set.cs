using Mono.Cecil;
using Mono.Cecil.Cil;
using Polyfill.Core;

namespace Polyfill.Bridges.Steps.S0_4_5f2_To_0_4_6f5
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
    internal sealed class Set : BridgeSet
    {
        internal override string Step => "0.4.5f2 -> 0.4.6f5";

        /// <summary>0.4.6f5 is the build these names went away in; below it the real members are still
        /// there and none of this is needed.</summary>
        internal override string From => "0.4.6f5";

        /// <summary>Read against 0.4.6f12. Newer than that they still run, and say they were not checked.</summary>
        internal override string VerifiedTo => "0.4.6f12";

        internal override IEnumerable<Bridge> Declare() => All;

        /// <summary>The label every stack entry Polyfill pushes is filed under, so it can be found again.</summary>
        internal const string StackLabel = "Polyfill";

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

        private static readonly string[] NoParameters = Array.Empty<string>();

        private const string NameSplit = "NPC.cs:63-69 until 0.4.5f2, now BasicInfo.cs:4-7";
        private const string Summon = "NPC.cs:116 until 0.4.5f2, now Interaction.cs:8 - and the game reads it "
                                    + "from there in NPCEnterableBuilding.cs:96";
        private const string SlotCount = "NPCInventory.cs:45 until 0.4.5f2, now Inventory.InventorySlotCount, "
                                       + "which NPCInventory.cs:62 builds the slots from";
        private const string Renamed = "NPCInventory.cs:51-65 until 0.4.5f2; the value kept its meaning and "
                                     + "lost its old name in Inventory.cs";
        private const string Pickpocket = "NPCInventory.cs:47 until 0.4.5f2, now Inventory.CanBePickpocketed, "
                                        + "read at NPCInventory.cs:372";

        private const string Emitter = "Il2CppScheduleOne.VoiceOver.VOEmitter";
        private const string Camera = "Il2CppScheduleOne.PlayerScripts.PlayerCamera";
        private const string Clipboard = "Il2CppScheduleOne.Tools.ManagementClipboard";
        private const string Customer = "Il2CppScheduleOne.Economy.CustomerData";

        private const string VoDatabase = "VOEmitter.cs:12 until 0.4.5f2 held one serialized Database field, "
                                        + "and Play() read it; 0.4.6 split it into a default and a current one "
                                        + "and Play() reads _currentDatabase";
        private const string VoPitch = "VOEmitter.cs:15 until 0.4.5f2; Play() multiplied PitchMultiplier by the "
                                     + "runtime one, and 0.4.6 multiplies _defaultPitch by _runtimePitchMultiplier "
                                     + "in the same expression";
        private const string Crosshair = "the crosshair became a parameter; true is what the call did before it "
                                       + "existed (PlayerCamera.cs:483-490)";

        private static readonly List<Bridge> All = new()
        {
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPCMovement",
                OldName = "get_MovementSpeedScale",
                ParameterCount = 0,
                Because = "the field was folded into UpdateSpeed(); the same value is now "
                        + "SpeedController.ActiveSpeedControl.speed * SpeedController.SpeedMultiplier",
                Emit = EmitMovementSpeedScaleGetter,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPCMovement",
                OldName = "set_MovementSpeedScale",
                ParameterCount = 1,
                Because = "writing the field wrote the BASE speed the controller derived; the base slot is "
                        + "now a priority-0 entry on the speed stack, which everything the game does outranks",
                Emit = EmitMovementSpeedScaleSetter,
            },
            new Bridge
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

            // Members that were renamed past what a name rule can see: the new name shares no stem with the
            // old one, or it stopped being an accessor. Each one is a member of the same type holding the
            // same value, read out of both versions of the body.
            NowCalled(Emitter, "get_Database", 0, "get__currentDatabase", VoDatabase),
            NowCalled(Emitter, "set_Database", 1, "set__currentDatabase", VoDatabase),
            NowCalled(Emitter, "get_PitchMultiplier", 0, "get__defaultPitch", VoPitch),
            NowCalled(Emitter, "set_PitchMultiplier", 1, "SetDefaultPitch", VoPitch),
            NowCalled("Il2CppScheduleOne.UI.Relations.RelationCircle", "get_AssignedNPC_ID", 0, "get_NPCId",
                    "RelationCircle.cs:23 until 0.4.5f2 cached the id in a field; NPCId reads it off the "
                  + "assigned NPC and returns string.Empty for none, which is what the field held"),
            NowCalled("Il2CppScheduleOne.PlayerScripts.Health.PlayerHealth", "get_MAX_HEALTH", 0, "get_MaxHealth",
                    "the same constant, renamed to PascalCase (PlayerHealth.cs:18)"),

            // Methods that gained a trailing parameter. The old form is genuinely gone while the name is
            // not, so these are the only rules allowed to add an overload.
            Defaulted(Camera, "FreeMouse", NoParameters, new object[] { true }, Crosshair),
            Defaulted(Camera, "LockMouse", NoParameters, new object[] { true }, Crosshair),
            Defaulted("Il2CppScheduleOne.UI.StorageMenu", "Open",
                      new[] { "Il2CppScheduleOne.ItemFramework.IItemSlotOwner", "System.String", "System.String" },
                      new object[] { null },
                      "Open took a callback in 0.4.6 and the old three-argument form passed none "
                    + "(StorageMenu.cs:56)"),

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Clipboard,
                OldName = "Close",
                ParameterCount = 1,
                AllowOverload = true,
                Because = "Close(preserveState) became two methods; the flag is now which one you call "
                        + "(ManagementClipboard.cs:103-113)",
                Emit = EmitClipboardClose,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Customer,
                OldName = "GetOrderDays",
                ParameterCount = 2,
                AllowOverload = true,
                Because = "it stopped returning the list and started filling one it is handed "
                        + "(CustomerData.cs:92)",
                Emit = EmitGetOrderDays,
            },

            // ExitAction changed namespace without leaving Assembly-CSharp, so ShadowTypes puts the old name
            // back as a subclass. These two are what a mod does with it: hand it to App.Exit, and wrap that
            // method in the delegate the game's exit list takes.
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.UI.App`1",
                OldName = "Exit",
                ParameterCount = 1,
                AllowOverload = true,
                Because = "ExitAction moved from ScheduleOne.DevUtilities to ScheduleOne; this takes the name "
                        + "the mod knows and hands it to the one method there is",
                Emit = EmitAppExit,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.GameInput/ExitDelegate",
                OldName = "op_Implicit",
                ParameterCount = 1,
                AllowOverload = true,
                Because = "an Action of the old ExitAction cannot be an Action of the new one - the conversion "
                        + "goes the wrong way for a delegate - so this wraps it rather than casting it",
                Emit = EmitExitDelegateConversion,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Supplier,
                OldName = "get_meetingGreeting",
                ParameterCount = 0,
                Because = MeetingWhy,
                Emit = (module, type) => EmitFromController(module, type, "get_meetingGreeting",
                    "GreetingOverrides", "Greeting", MeetingGreetingLine),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Supplier,
                OldName = "get_meetingChoice",
                ParameterCount = 0,
                Because = MeetingWhy,
                Emit = (module, type) => EmitFromController(module, type, "get_meetingChoice",
                    "Choices", "ChoiceText", (module2, method, il, giveUp) => il.Emit(OpCodes.Ldstr, "Yes")),
            },
        };

        private const string ExitActionOld = "Il2CppScheduleOne.DevUtilities.ExitAction";
        private const string Supplier = "Il2CppScheduleOne.Economy.Supplier";
        private const string Controller = "Il2CppScheduleOne.Dialogue.DialogueController";

        private const string MeetingWhy =
            "Supplier.cs:186-199 until 0.4.5f2 kept the greeting and the choice in fields; 0.4.6 builds "
          + "the same two objects as locals in Start() and hands them to the DialogueController, so they "
          + "are still there and only the way to them is gone";

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
        private static Bridge Moved(string declaringType, string oldName, bool write,
                                  string[] hops, string target, string because)
        {
            string accessor = (write ? "set_" : "get_") + oldName;
            return new Bridge
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

        /// <summary>
        /// The same member on the same type under a name no rule could have guessed.
        /// </summary>
        /// <remarks>
        /// A name heuristic only fires when the two names still share something - a case flip, an
        /// underscore, a backing field. These do not: <c>Database</c> became <c>_currentDatabase</c> and
        /// <c>set_PitchMultiplier</c> became <c>SetDefaultPitch</c>. What makes them safe anyway is that
        /// each one was read out of both versions of the method that USES the value, so the claim is not
        /// "these names look alike" but "these two lines compute the same thing".
        /// </remarks>
        private static Bridge NowCalled(string declaringType, string oldName, int parameterCount,
                                    string newName, string because)
            => new()
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = declaringType,
                OldName = oldName,
                ParameterCount = parameterCount,
                Because = because,
                Emit = (module, type) => EmitCall(module, type, oldName, newName, parameterCount),
            };

        /// <summary>
        /// The method a mod calls, still there, now insisting on arguments it used to fill in itself.
        /// </summary>
        /// <remarks>
        /// The constants are written down here rather than read off the target, because an interop assembly
        /// carries no default values - IL2CPP resolves them at the call site and Il2CppInterop emits the
        /// parameter bare. So the value has to come from the game's own source, and it is cited in the rule.
        /// </remarks>
        /// <param name="leading">The parameter types the old form had, by full name. Needed whenever the
        /// name carries more than one overload of the new arity - StorageMenu has two four-argument
        /// Opens, and picking by count alone would pick whichever came first.</param>
        private static Bridge Defaulted(string declaringType, string name, string[] leading,
                                      object[] defaults, string because)
            => new()
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = declaringType,
                OldName = name,
                ParameterCount = leading.Length,
                AllowOverload = true,
                Because = because,
                Emit = (module, type) => EmitWithDefaults(module, type, name, leading, defaults),
            };

        /// <summary>A method with the target's signature under the old name, whose body is one call.</summary>
        private static MethodDefinition EmitCall(ModuleDefinition module, TypeDefinition type,
                                                 string oldName, string newName, int parameterCount)
        {
            var target = Method(type, newName, parameterCount);
            if (target == null || target.HasGenericParameters) return null;

            var method = new MethodDefinition(oldName,
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | (target.IsStatic ? MethodAttributes.Static : 0),
                module.ImportReference(target.ReturnType));

            foreach (var parameter in target.Parameters)
                method.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None,
                                                              module.ImportReference(parameter.ParameterType)));

            var il = method.Body.GetILProcessor();
            if (!target.IsStatic) il.Emit(OpCodes.Ldarg_0);
            foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, module.ImportReference(target));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>The short form of an overload, calling the long one with the values it used to imply.</summary>
        private static MethodDefinition EmitWithDefaults(ModuleDefinition module, TypeDefinition type,
                                                         string name, string[] leading, object[] defaults)
        {
            int parameterCount = leading.Length;
            MethodDefinition target = null;
            foreach (var candidate in type.Methods)
            {
                if (candidate.Name != name || candidate.Parameters.Count != parameterCount + defaults.Length)
                    continue;
                bool matches = true;
                for (int i = 0; i < parameterCount; i++)
                    if (candidate.Parameters[i].ParameterType.FullName != leading[i]) { matches = false; break; }
                if (!matches) continue;
                if (target != null) return null;              // more than one; choosing would be a guess
                target = candidate;
            }
            if (target == null || target.HasGenericParameters) return null;

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | (target.IsStatic ? MethodAttributes.Static : 0),
                module.ImportReference(target.ReturnType));

            for (int i = 0; i < parameterCount; i++)
                method.Parameters.Add(new ParameterDefinition(target.Parameters[i].Name, ParameterAttributes.None,
                                          module.ImportReference(target.Parameters[i].ParameterType)));

            var il = method.Body.GetILProcessor();
            if (!target.IsStatic) il.Emit(OpCodes.Ldarg_0);
            foreach (var parameter in method.Parameters) il.Emit(OpCodes.Ldarg, parameter);

            for (int i = 0; i < defaults.Length; i++)
            {
                var expected = target.Parameters[parameterCount + i].ParameterType;
                if (!PushConstant(il, defaults[i], expected)) return null;
            }

            il.Emit(OpCodes.Call, module.ImportReference(target));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>Puts a literal on the stack, and refuses anything whose type it cannot match exactly.</summary>
        private static bool PushConstant(ILProcessor il, object value, TypeReference expected)
        {
            if (value == null && !expected.IsValueType) { il.Emit(OpCodes.Ldnull); return true; }
            switch (value)
            {
                case bool flag when expected.MetadataType == MetadataType.Boolean:
                    il.Emit(flag ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); return true;
                case int number when expected.MetadataType == MetadataType.Int32:
                    il.Emit(OpCodes.Ldc_I4, number); return true;
                case float number when expected.MetadataType == MetadataType.Single:
                    il.Emit(OpCodes.Ldc_R4, number); return true;
                case string text when expected.MetadataType == MetadataType.String:
                    il.Emit(OpCodes.Ldstr, text); return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// <c>ManagementClipboard.Close(bool preserveState)</c> - one method with a flag until 0.4.5f2.
        /// </summary>
        /// <remarks>
        /// 0.4.6 turned the flag into the choice of method: <c>Close()</c> drops the state,
        /// <c>CloseAndPreserveState()</c> keeps it, and both end in the same <c>OnClose()</c>. So the
        /// argument stops being data and becomes the branch.
        /// </remarks>
        private static MethodDefinition EmitClipboardClose(ModuleDefinition module, TypeDefinition clipboard)
        {
            var close = Method(clipboard, "Close", 0);
            var preserve = Method(clipboard, "CloseAndPreserveState", 0);
            if (close == null || preserve == null) return null;

            var method = new MethodDefinition("Close", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("preserveState", ParameterAttributes.None,
                                                          module.TypeSystem.Boolean));

            var il = method.Body.GetILProcessor();
            var drop = il.Create(OpCodes.Ldarg_0);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse_S, drop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(preserve));
            il.Emit(OpCodes.Ret);

            il.Append(drop);
            il.Emit(OpCodes.Call, module.ImportReference(close));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>CustomerData.GetOrderDays(dependence, relationship)</c> - it used to hand back the list.
        /// </summary>
        /// <remarks>
        /// 0.4.6 takes the list as a third argument and fills it, which is the usual allocation-free rewrite:
        /// same computation, the caller owns the storage. The old shape is the new one with a list made here,
        /// and the list type is taken from the parameter so it cannot be anything but what the game expects.
        /// </remarks>
        private static MethodDefinition EmitGetOrderDays(ModuleDefinition module, TypeDefinition customer)
        {
            var target = Method(customer, "GetOrderDays", 3);
            if (target == null) return null;

            var listType = module.ImportReference(target.Parameters[2].ParameterType);
            var listDefinition = listType.Resolve();
            if (listDefinition == null) return null;

            MethodDefinition constructor = null;
            foreach (var candidate in listDefinition.Methods)
                if (candidate.IsConstructor && !candidate.IsStatic && candidate.Parameters.Count == 0)
                { constructor = candidate; break; }
            if (constructor == null) return null;

            // On a generic instance the constructor has to be named through THAT instance, or the newobj
            // allocates the open List<T> and the assignment to List<EDay> is a type mismatch at load.
            MethodReference create = listType is GenericInstanceType
                ? new MethodReference(constructor.Name, module.TypeSystem.Void, listType) { HasThis = true }
                : module.ImportReference(constructor);

            var method = new MethodDefinition("GetOrderDays",
                MethodAttributes.Public | MethodAttributes.HideBySig, listType);
            method.Parameters.Add(new ParameterDefinition("dependence", ParameterAttributes.None,
                                                          module.ImportReference(target.Parameters[0].ParameterType)));
            method.Parameters.Add(new ParameterDefinition("normalizedRelationship", ParameterAttributes.None,
                                                          module.ImportReference(target.Parameters[1].ParameterType)));

            var days = new VariableDefinition(listType);
            method.Body.Variables.Add(days);
            method.Body.InitLocals = true;

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Newobj, create);
            il.Emit(OpCodes.Stloc, days);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, days);
            il.Emit(OpCodes.Call, module.ImportReference(target));
            il.Emit(OpCodes.Ldloc, days);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>App&lt;T&gt;.Exit(ExitAction)</c> under the namespace the type used to have.
        /// </summary>
        /// <remarks>
        /// The shadow derives from the real ExitAction, so the body is the call and nothing else - no cast,
        /// no conversion. Emitted only when the shadow is actually in the module, which means only when the
        /// type repair above it succeeded.
        /// </remarks>
        private static MethodDefinition EmitAppExit(ModuleDefinition module, TypeDefinition app)
        {
            var shadow = module.GetType(ExitActionOld);
            var real = Method(app, "Exit", 1);
            if (shadow == null || real == null) return null;
            if (real.Parameters[0].ParameterType.FullName != shadow.BaseType?.FullName) return null;

            var method = new MethodDefinition("Exit", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("action", ParameterAttributes.None,
                                                          module.ImportReference(shadow)));

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, real);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>Action&lt;old ExitAction&gt;</c> as the delegate the game's exit list actually holds.
        /// </summary>
        /// <remarks>
        /// This one cannot be a forwarding call, and the reason is worth writing down. The shadow IS the new
        /// type, so an ExitAction can be handed to anything expecting the old name - but a DELEGATE inverts
        /// that. <c>Action&lt;T&gt;</c> is contravariant, so <c>Action&lt;new&gt;</c> converts to
        /// <c>Action&lt;old&gt;</c> and never the other way, which is exactly the direction needed here.
        /// Rebuilding the delegate off its target and method fails for the same reason.
        ///
        /// So the action is wrapped: a small class holds it, and its Invoke takes the real ExitAction the
        /// game raises and hands the mod the same native object under the name the mod was compiled against.
        /// Same pointer, same object, one managed hop.
        /// </remarks>
        private static MethodDefinition EmitExitDelegateConversion(ModuleDefinition module, TypeDefinition exitDelegate)
        {
            var shadow = module.GetType(ExitActionOld);
            if (shadow == null) return null;

            // The existing conversion, which also supplies a correctly scoped Action`1 to build on.
            MethodDefinition existing = null;
            foreach (var candidate in exitDelegate.Methods)
                if (candidate.Name == "op_Implicit" && candidate.Parameters.Count == 1
                    && candidate.Parameters[0].ParameterType is GenericInstanceType)
                { existing = candidate; break; }
            if (existing == null) return null;

            var actionOfReal = (GenericInstanceType)existing.Parameters[0].ParameterType;
            if (actionOfReal.GenericArguments.Count != 1) return null;
            if (actionOfReal.GenericArguments[0].FullName != shadow.BaseType?.FullName) return null;

            var actionDefinition = actionOfReal.ElementType.Resolve();
            var invokeDefinition = Method(actionDefinition, "Invoke", 1);
            MethodDefinition actionConstructor = null;
            foreach (var candidate in actionDefinition?.Methods ?? new Mono.Collections.Generic.Collection<MethodDefinition>())
                if (candidate.IsConstructor && candidate.Parameters.Count == 2) { actionConstructor = candidate; break; }
            if (invokeDefinition == null || actionConstructor == null) return null;

            var pointer = ShadowTypes.PointerGetter(shadow.BaseType.Resolve());
            var fromPointer = Method(shadow, ".ctor", 1);
            if (pointer == null || fromPointer == null) return null;

            var actionOfShadow = new GenericInstanceType(actionOfReal.ElementType);
            actionOfShadow.GenericArguments.Add(module.ImportReference(shadow));

            var shim = BuildExitShim(module, exitDelegate, shadow, actionOfShadow, invokeDefinition,
                                     pointer, fromPointer, actionOfReal);
            if (shim == null) return null;

            var method = new MethodDefinition("op_Implicit",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName, module.ImportReference(exitDelegate));
            method.Parameters.Add(new ParameterDefinition("action", ParameterAttributes.None, actionOfShadow));

            var il = method.Body.GetILProcessor();
            var wrap = il.Create(OpCodes.Ldarg_0);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Brtrue_S, wrap);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            il.Append(wrap);
            il.Emit(OpCodes.Newobj, MethodOf(shim, ".ctor"));
            il.Emit(OpCodes.Ldftn, MethodOf(shim, "Invoke"));
            il.Emit(OpCodes.Newobj, ShadowTypes.On(actionOfReal, actionConstructor));
            il.Emit(OpCodes.Call, existing);
            il.Emit(OpCodes.Ret);

            exitDelegate.NestedTypes.Add(shim);
            return method;
        }

        /// <summary>The wrapper class: it holds the mod's action and re-labels what the game hands it.</summary>
        private static TypeDefinition BuildExitShim(ModuleDefinition module, TypeDefinition owner,
                                                    TypeDefinition shadow, GenericInstanceType actionOfShadow,
                                                    MethodDefinition invokeDefinition, MethodDefinition pointer,
                                                    MethodDefinition fromPointer, GenericInstanceType actionOfReal)
        {
            var shim = new TypeDefinition(null, "<Polyfill>ExitActionShim",
                TypeAttributes.NestedPublic | TypeAttributes.Class | TypeAttributes.Sealed
                    | TypeAttributes.BeforeFieldInit, module.TypeSystem.Object);

            var held = new FieldDefinition("action", FieldAttributes.Public, actionOfShadow);
            shim.Fields.Add(held);

            var objectConstructor = new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object)
            { HasThis = true };

            var constructor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            constructor.Parameters.Add(new ParameterDefinition("action", ParameterAttributes.None, actionOfShadow));
            var make = constructor.Body.GetILProcessor();
            make.Emit(OpCodes.Ldarg_0);
            make.Emit(OpCodes.Call, objectConstructor);
            make.Emit(OpCodes.Ldarg_0);
            make.Emit(OpCodes.Ldarg_1);
            make.Emit(OpCodes.Stfld, held);
            make.Emit(OpCodes.Ret);

            var invoke = new MethodDefinition("Invoke", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            invoke.Parameters.Add(new ParameterDefinition("exitAction", ParameterAttributes.None,
                                                          actionOfReal.GenericArguments[0]));

            var il = invoke.Body.GetILProcessor();
            var call = il.Create(OpCodes.Callvirt, ShadowTypes.On(actionOfShadow, invokeDefinition));
            var rewrap = il.Create(OpCodes.Call, module.ImportReference(pointer));

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, held);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, rewrap);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Br_S, call);

            il.Append(rewrap);
            il.Emit(OpCodes.Newobj, fromPointer);

            il.Append(call);
            il.Emit(OpCodes.Ret);

            shim.Methods.Add(constructor);
            shim.Methods.Add(invoke);
            return shim;
        }

        private static MethodDefinition MethodOf(TypeDefinition type, string name)
        {
            foreach (var method in type.Methods)
                if (method.Name == name) return method;
            return null;
        }

        /// <summary>
        /// A member the game stopped keeping, found again in the list it was handed to.
        /// </summary>
        /// <remarks>
        /// Until 0.4.5f2 the Supplier held its meeting greeting and its meeting choice in fields, and a mod
        /// reached them there. 0.4.6 builds the identical two objects as locals in <c>Start()</c> and gives
        /// them to the DialogueController; nothing else changed about them. They are still in the game, and
        /// only the way in was removed.
        ///
        /// So the way in is rebuilt as a search of that list. What is searched FOR is not a guess: the
        /// greeting is matched on the line the game itself looks up to build it - the same
        /// <c>Database.GetLine(Generic, "supplier_meeting_greeting")</c> call, emitted here - and the
        /// choice on the text the game gives it. Index would have been shorter and would have been a guess;
        /// for a Supplier the game happens to add exactly one of each today, and "happens to" is the part
        /// that stops being true one update later.
        ///
        /// Every step is null-guarded, and a miss returns null, which is what the callers already handle -
        /// a mod holding a null greeting simply does not touch it.
        /// </remarks>
        private static MethodDefinition EmitFromController(
            ModuleDefinition module, TypeDefinition owner, string accessor,
            string listProperty, string itemProperty,
            Action<ModuleDefinition, MethodDefinition, ILProcessor, Instruction> pushWanted)
        {
            var getHandler = GetterUp(owner, "DialogueHandler");
            var handler = getHandler?.ReturnType?.Resolve();
            var controller = module.GetType(Controller);
            var getList = Getter(controller, listProperty);
            var getComponent = GenericGetComponent(module, handler, controller);
            if (getHandler == null || getList == null || getComponent == null) return null;

            if (getList.ReturnType is not GenericInstanceType list) return null;
            var listDefinition = list.Resolve();
            var getCount = Getter(listDefinition, "Count");
            var getItem = Method(listDefinition, "get_Item", 1);
            var item = list.GenericArguments[0].Resolve();
            var getText = Getter(item, itemProperty);
            if (getCount == null || getItem == null || getText == null) return null;

            var method = new MethodDefinition(accessor, MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.ImportReference(item));
            var wanted = new VariableDefinition(module.TypeSystem.String);
            var entries = new VariableDefinition(module.ImportReference(list));
            var index = new VariableDefinition(module.TypeSystem.Int32);
            var current = new VariableDefinition(module.ImportReference(item));
            method.Body.Variables.Add(wanted);
            method.Body.Variables.Add(entries);
            method.Body.Variables.Add(index);
            method.Body.Variables.Add(current);
            method.Body.InitLocals = true;

            var il = method.Body.GetILProcessor();
            var giveUp = il.Create(OpCodes.Pop);          // every guard branches here with one value left
            var next = il.Create(OpCodes.Ldloc, index);
            var test = il.Create(OpCodes.Ldloc, index);
            var body = il.Create(OpCodes.Ldloc, entries);

            pushWanted(module, method, il, giveUp);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, giveUp);
            il.Emit(OpCodes.Stloc, wanted);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(getHandler));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, giveUp);
            il.Emit(OpCodes.Callvirt, getComponent);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, giveUp);
            il.Emit(OpCodes.Callvirt, module.ImportReference(getList));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, giveUp);
            il.Emit(OpCodes.Stloc, entries);

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, index);
            il.Emit(OpCodes.Br, test);

            il.Append(body);                                        // ldloc entries
            il.Emit(OpCodes.Ldloc, index);
            il.Emit(OpCodes.Callvirt, ShadowTypes.On(list, getItem));
            il.Emit(OpCodes.Stloc, current);
            il.Emit(OpCodes.Ldloc, current);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, current);
            il.Emit(OpCodes.Callvirt, module.ImportReference(getText));
            il.Emit(OpCodes.Ldloc, wanted);
            il.Emit(OpCodes.Call, StringEquality(module));
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, current);
            il.Emit(OpCodes.Ret);

            il.Append(next);                                        // ldloc index
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, index);

            il.Append(test);                                        // ldloc index
            il.Emit(OpCodes.Ldloc, entries);
            il.Emit(OpCodes.Callvirt, ShadowTypes.On(list, getCount));
            il.Emit(OpCodes.Blt, body);

            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            il.Append(giveUp);                                      // pop
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>DialogueHandler.Database.GetLine(Generic, "supplier_meeting_greeting")</c>, which is the line
        /// the game itself builds the greeting from.
        /// </summary>
        private static void MeetingGreetingLine(ModuleDefinition module, MethodDefinition method,
                                                ILProcessor il, Instruction giveUp)
        {
            var getHandler = GetterUp(method.DeclaringType, "DialogueHandler");
            var getDatabase = Getter(getHandler?.ReturnType?.Resolve(), "Database");
            var getLine = Method(getDatabase?.ReturnType?.Resolve(), "GetLine", 2);
            if (getHandler == null || getDatabase == null || getLine == null)
            { il.Emit(OpCodes.Ldnull); return; }

            // The module number is read out of THIS build's enum rather than written down, because a value
            // that moved would silently look up the wrong table.
            var module_ = getLine.Parameters[0].ParameterType.Resolve();
            int generic = 0;
            bool found = false;
            foreach (var field in module_?.Fields ?? new Mono.Collections.Generic.Collection<FieldDefinition>())
                if (field.Name == "Generic" && field.HasConstant)
                { generic = Convert.ToInt32(field.Constant); found = true; break; }
            if (!found) { il.Emit(OpCodes.Ldnull); return; }

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(getHandler));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, giveUp);
            il.Emit(OpCodes.Callvirt, module.ImportReference(getDatabase));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, giveUp);
            il.Emit(OpCodes.Ldc_I4, generic);
            il.Emit(OpCodes.Ldstr, "supplier_meeting_greeting");
            il.Emit(OpCodes.Callvirt, module.ImportReference(getLine));
        }

        /// <summary><c>GetComponent&lt;T&gt;()</c> on the component the handler is.</summary>
        /// <remarks>
        /// The generic form and not the one taking a Type: an interop cast of the Component that returns
        /// hands back a wrapper of the DECLARED type, and casting that down gives null. The generic call is
        /// what a compiler would emit and what Il2CppInterop builds the right wrapper for.
        /// </remarks>
        private static MethodReference GenericGetComponent(ModuleDefinition module, TypeDefinition from,
                                                           TypeDefinition wanted)
        {
            if (from == null || wanted == null) return null;
            for (var current = from; current != null; )
            {
                foreach (var candidate in current.Methods)
                {
                    if (candidate.Name != "GetComponent" || candidate.Parameters.Count != 0) continue;
                    if (candidate.GenericParameters.Count != 1) continue;
                    var instance = new GenericInstanceMethod(module.ImportReference(candidate));
                    instance.GenericArguments.Add(module.ImportReference(wanted));
                    return instance;
                }
                TypeDefinition next = null;
                try { next = current.BaseType?.Resolve(); } catch { }
                if (next == current) return null;
                current = next;
            }
            return null;
        }

        private static MethodReference StringEquality(ModuleDefinition module)
        {
            var text = module.TypeSystem.String.Resolve();
            foreach (var candidate in text.Methods)
                if (candidate.Name == "op_Equality" && candidate.Parameters.Count == 2)
                    return module.ImportReference(candidate);
            return null;
        }

        /// <summary>A getter on this type or anything it derives from.</summary>
        private static MethodDefinition GetterUp(TypeDefinition type, string member)
        {
            for (var current = type; current != null; )
            {
                var found = Getter(current, member);
                if (found != null) return found;
                TypeDefinition next = null;
                try { next = current.BaseType?.Resolve(); } catch { }
                if (next == current) return null;
                current = next;
            }
            return null;
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
