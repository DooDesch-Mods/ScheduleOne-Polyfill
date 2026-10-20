using Mono.Cecil;
using Mono.Cecil.Cil;
using static Polyfill.Bridges.Shapes;

namespace Polyfill.Bridges.Steps.S0_4_6f13_To_0_4_7f5
{
    /// <summary>
    /// 0.4.7 members whose value the game no longer holds per object, kept per object by Polyfill's mod, and
    /// the calls that came with them.
    /// </summary>
    /// <remarks>
    /// A VALUE THAT HAS NOWHERE TO LIVE ON THE OBJECT IS KEPT BESIDE IT. 0.4.6's ConsumeProductBehaviour
    /// carried a UnityEvent for each NPC and its DialogueController a dialogue for each NPC; 0.4.7 has an
    /// Action instead of the event and one dialogue for the whole game. A managed field on the interop type
    /// cannot hold the value: two wrappers around the same native object are two managed objects, and a field
    /// on one is invisible to the other. So the value goes into a table keyed by the native pointer, and that
    /// table lives in Polyfill's mod, which can name interop types.
    ///
    /// What is emitted here is only the doorway: a static delegate field on the type, named
    /// <c>&lt;polyfill&gt;get_X</c> or <c>&lt;polyfill&gt;set_X</c>, and an accessor that calls it with
    /// <c>this</c>. The mod fills the field when it applies the matching fix. Not filled - the fix disabled or
    /// refused - the getter answers null and the setter does nothing, which is what a mod reading an absent
    /// value already copes with.
    ///
    /// Harmony is not the way in. A patch on a bridge takes HarmonyX's managed route, and that route can kill
    /// the process in the JIT while it installs (see Boot/BulkTargetsSkipBridges.cs).
    /// </remarks>
    internal sealed class Held : BridgeSet
    {
        internal override string Step => "0.4.6f13 -> 0.4.7f5 (held)";
        internal override string From => "0.4.7f5";
        internal override string VerifiedTo => "0.4.7f12";

        private const string Controller = "Il2CppScheduleOne.Dialogue.DialogueController";
        private const string Consume = "Il2CppScheduleOne.NPCs.Behaviour.ConsumeProductBehaviour";
        private const string Eyes = "Il2CppScheduleOne.AvatarFramework.EyeController";

        /// <summary>The slot the GenericDialogue getter in <see cref="Forwards"/> asks first.</summary>
        internal const string GenericDialogueGet = "<polyfill>get_GenericDialogue";

        private const string ConsumeDone = "ConsumeProductBehaviour.cs:43 on 0.4.6f13 was a UnityEvent each NPC "
            + "raised when it finished using a product (:162-231, after the animation); 0.4.7 raises "
            + "Action<ProductItemInstance> OnProductConsumed at the same point (ConsumeProductBehaviour.cs:319 on "
            + "0.4.7f12), which a UnityEvent listener cannot hear - so the event is put back per NPC and Polyfill "
            + "raises it from OnProductConsumed";

        internal override IEnumerable<Bridge> Declare() => new[]
        {
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Controller,
                OldName = "set_GenericDialogue", ParameterCount = 1,
                Because = "0.4.6's controller opened its own GenericDialogue field (DialogueController.cs:82, "
                        + ":129-147 on 0.4.6f13); 0.4.7 opens the one conversation the DialogueManager holds for "
                        + "every NPC (DialogueController.cs:112, :143-161 on 0.4.7f12), so the value is kept per "
                        + "controller and Polyfill opens it in place of the shared one for that NPC",
                Emit = EmitSetGenericDialogue,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Consume,
                OldName = "get_onConsumeDone", ParameterCount = 0,
                Because = ConsumeDone,
                Emit = (module, behaviour) => EmitHeldGetter(module, behaviour, "get_onConsumeDone",
                    Forwards.TypeIn(module, "UnityEngine.CoreModule", "UnityEngine.Events", "UnityEvent")),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Consume,
                OldName = "set_onConsumeDone", ParameterCount = 1,
                Because = ConsumeDone,
                Emit = (module, behaviour) => EmitHeldSetter(module, behaviour, "set_onConsumeDone",
                    Forwards.TypeIn(module, "UnityEngine.CoreModule", "UnityEngine.Events", "UnityEvent")),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Consume,
                OldName = "SendProduct", ParameterCount = 2,
                Because = "0.4.6's SendProduct(product, removeFromInventory) was a server RPC that stored the "
                        + "product the next Activate consumes (ConsumeProductBehaviour.cs:66-78, :321-324 on "
                        + "0.4.6f13); 0.4.7's SetProduct_Server(product, removeFromInventory) is the same RPC "
                        + "storing it for OnActivateOrResume (ConsumeProductBehaviour.cs:86-108, :245-249 on 0.4.7f12)",
                Emit = (module, behaviour) => EmitForward2(module, behaviour, "SendProduct", "SetProduct_Server"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Eyes,
                OldName = "SetEyeballTint", ParameterCount = 2,
                Because = "0.4.6's SetEyeballTint(col, overrideDefault) set both eyeballs to col at the eye's "
                        + "default emission 0.115 and made it the colour a reset returns to only when asked "
                        + "(EyeController.cs:112-121, Eye.cs:99-106 on 0.4.6f13); 0.4.7's SetEyeballColor(color, "
                        + "emission, setDefault) does the same with the emission spelled out (EyeController.cs:136-153 "
                        + "on 0.4.7f12)",
                Emit = EmitSetEyeballTint,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Eyes,
                OldName = "ForceBlink", ParameterCount = 0,
                Because = "renamed: 0.4.6's ForceBlink blinked both eyes and restarted the blink timer "
                        + "(EyeController.cs:214-219 on 0.4.6f13); 0.4.7's Blink does both (EyeController.cs:270-275 "
                        + "on 0.4.7f12)",
                Emit = (module, eyes) => Forwards.EmitForward(module, eyes, "ForceBlink", "Blink"),
            },
        };

        /// <summary>
        /// <c>DialogueController.GenericDialogue</c>, written: handed to Polyfill's mod for this controller.
        /// </summary>
        /// <remarks>
        /// Takes the type the mod names, the <c>DialogueContainer</c> put back over <c>Conversation</c>; without
        /// that type there is nothing a call could match, and the setter is not written.
        /// </remarks>
        private static MethodDefinition EmitSetGenericDialogue(ModuleDefinition module, TypeDefinition controller)
        {
            var shared = Getter(controller, "_genericConversation");
            var container = shared == null ? null : Core.ShadowTypes.Shadowing(module, shared.ReturnType);
            if (container == null) return null;
            return EmitHeldSetter(module, controller, "set_GenericDialogue", container);
        }

        private static MethodDefinition EmitSetEyeballTint(ModuleDefinition module, TypeDefinition eyes)
        {
            var target = Method(eyes, "SetEyeballColor", 3);
            if (target == null || target.IsStatic
                || target.Parameters[0].ParameterType.FullName != "UnityEngine.Color"
                || target.Parameters[1].ParameterType.FullName != "System.Single"
                || target.Parameters[2].ParameterType.FullName != "System.Boolean")
                return null;

            var method = new MethodDefinition("SetEyeballTint", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("col", ParameterAttributes.None,
                                                          module.ImportReference(target.Parameters[0].ParameterType)));
            method.Parameters.Add(new ParameterDefinition("overrideDefault", ParameterAttributes.None,
                                                          module.ImportReference(target.Parameters[2].ParameterType)));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_R4, 0.115f);     // Eye.SetEyeballColor's default emission on 0.4.6 and 0.4.7
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(target.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
            return method;
        }

        internal static MethodDefinition EmitForward2(ModuleDefinition module, TypeDefinition type, string oldName,
                                                      string target)
        {
            var to = Method(type, target, 2);
            if (to == null || to.IsStatic) return null;
            var method = new MethodDefinition(oldName, MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.ImportReference(to.ReturnType));
            foreach (var p in to.Parameters)
                method.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None,
                                                              module.ImportReference(p.ParameterType)));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(to.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, to);
            il.Emit(OpCodes.Ret);
            return method;
        }

        // ---------------------------------------------------------------- the doorway

        /// <summary>
        /// A getter that asks the slot <c>&lt;polyfill&gt;get_X</c>, a <c>Func&lt;object, object&gt;</c>, with
        /// <c>this</c> and returns its answer as <paramref name="returns"/>; null while the slot is empty.
        /// </summary>
        internal static MethodDefinition EmitHeldGetter(ModuleDefinition module, TypeDefinition type, string name,
                                                        TypeReference returns)
        {
            if (returns == null) return null;
            var func = Closed(module, "Func", 2);
            var slot = Slot(type, "<polyfill>" + name, func);
            var invoke = Invoke(func, ((GenericInstanceType)func).ElementType.GenericParameters[1], 1);

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(returns));
            var il = method.Body.GetILProcessor();
            var asked = il.Create(OpCodes.Ldarg_0);

            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, asked);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.Append(asked);
            il.Emit(OpCodes.Callvirt, invoke);
            il.Emit(OpCodes.Castclass, module.ImportReference(returns));
            il.Emit(OpCodes.Ret);
            return Forwards.AsProperty(type, method);
        }

        /// <summary>
        /// A setter that hands <c>this</c> and the value to the slot <c>&lt;polyfill&gt;set_X</c>, an
        /// <c>Action&lt;object, object&gt;</c>; nothing while the slot is empty.
        /// </summary>
        internal static MethodDefinition EmitHeldSetter(ModuleDefinition module, TypeDefinition type, string name,
                                                        TypeReference value)
        {
            if (value == null) return null;
            var action = Closed(module, "Action", 2);
            var slot = Slot(type, "<polyfill>" + name, action);
            var invoke = Invoke(action, module.TypeSystem.Void, 2);

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None,
                                                          module.ImportReference(value)));
            var il = method.Body.GetILProcessor();
            var asked = il.Create(OpCodes.Ldarg_0);

            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, asked);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.Append(asked);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, invoke);
            il.Emit(OpCodes.Ret);
            return Forwards.AsProperty(type, method);
        }

        /// <summary>The getter slot for a member, a <c>Func&lt;object, object&gt;</c> field, made once.</summary>
        internal static FieldDefinition GetterSlot(ModuleDefinition module, TypeDefinition type, string name)
            => Slot(type, name, Closed(module, "Func", 2));

        /// <summary>That slot's <c>Invoke(object)</c>.</summary>
        internal static MethodReference GetterInvoke(ModuleDefinition module)
        {
            var func = Closed(module, "Func", 2);
            return Invoke(func, ((GenericInstanceType)func).ElementType.GenericParameters[1], 1);
        }

        private static FieldDefinition Slot(TypeDefinition type, string name, TypeReference of)
        {
            foreach (var field in type.Fields)
                if (field.Name == name) return field;
            // Named so it cannot collide with anything the game or a mod declares, and so a reader of the
            // assembly can see at a glance that it is not the game's.
            var slot = new FieldDefinition(name, FieldAttributes.Public | FieldAttributes.Static, of);
            type.Fields.Add(slot);
            return slot;
        }

        /// <summary><c>System.Func`2</c> or <c>System.Action`2</c> closed over <c>object</c>.</summary>
        private static TypeReference Closed(ModuleDefinition module, string name, int arity)
        {
            var open = new TypeReference("System", name + "`" + arity, module, module.TypeSystem.CoreLibrary);
            for (int i = 0; i < arity; i++) open.GenericParameters.Add(new GenericParameter(open));
            var closed = new GenericInstanceType(open);
            for (int i = 0; i < arity; i++) closed.GenericArguments.Add(module.TypeSystem.Object);
            return closed;
        }

        private static MethodReference Invoke(TypeReference closed, TypeReference returns, int parameters)
        {
            var open = ((GenericInstanceType)closed).ElementType;
            var invoke = new MethodReference("Invoke", returns, closed) { HasThis = true };
            for (int i = 0; i < parameters; i++)
                invoke.Parameters.Add(new ParameterDefinition(open.GenericParameters[i]));
            return invoke;
        }
    }
}
