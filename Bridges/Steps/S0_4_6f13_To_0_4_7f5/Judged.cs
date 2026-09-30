using Mono.Cecil;
using Mono.Cecil.Cil;
using static Polyfill.Bridges.Shapes;
using static Polyfill.Bridges.Steps.S0_4_6f13_To_0_4_7f5.Forwards;

namespace Polyfill.Bridges.Steps.S0_4_6f13_To_0_4_7f5
{
    /// <summary>
    /// 0.4.7 members whose job moved onto a different mechanism, rebuilt on that mechanism.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Forwards"/>, none of these has a 0.4.7 member doing the same job under another name.
    /// Each one does what the 0.4.6 body did with what 0.4.7 has instead - the speed controller, the
    /// conversation map, the humanoid rig - and its <c>Because</c> names both sides.
    /// </remarks>
    internal sealed class Judged : BridgeSet
    {
        internal override string Step => "0.4.6f13 -> 0.4.7f5 (judged)";
        internal override string From => "0.4.7f5";
        internal override string VerifiedTo => "0.4.7f6";

        /// <summary>The speed control a paused NPC carries; unique enough that no game or mod id collides.</summary>
        private const string PauseControl = "polyfill_pause";

        /// <summary>The id 0.4.6's three-argument SetMovementSpeed defaulted to.</summary>
        private const string OldCombatControl = "combat";

        /// <summary>The id 0.4.7's CombatBehaviour adds, and the only one EndCombat removes (CombatBehaviour.cs:241).</summary>
        private const string CombatControl = "CombatBehaviour";

        private const string CullCheck = "UpdateAnimationActive";
        private const string Messaging = "Il2CppScheduleOne.Messaging.MessagingManager";

        private const string ByConversation = "0.4.6 keyed conversations by the NPC's id; 0.4.7 keys them by "
            + "MSGConversation.ConversationId (MessagingManager.Register, MessagingManager.cs:67 on 0.4.7f6), so the "
            + "NPC is found by its id with NPCManager.GetNPC and its conversation's id is passed on. When no NPC "
            + "or conversation is found the bridge logs it and returns, as 0.4.6 did";

        internal override IEnumerable<Bridge> Declare() => new[]
        {
            // ---- pausing an NPC ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Movement, OldName = "PauseMovement", ParameterCount = 0,
                Because = SpeedStack + "; pausing is a top-priority absolute speed control of 0, so the NPC keeps its "
                        + "path and stays still until it is removed, and, as 0.4.6 did (NPCMovement.cs:1015-1028 on "
                        + "0.4.6f13), the agent is stopped and its velocity zeroed so it halts at once",
                Emit = (module, movement) => EmitPause(module, movement, pause: true),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Movement, OldName = "ResumeMovement", ParameterCount = 0,
                Because = SpeedStack + "; resuming removes the pause control and un-stops the agent, and the stack "
                        + "falls back to whatever was driving the NPC before",
                Emit = (module, movement) => EmitPause(module, movement, pause: false),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Movement, OldName = "get_IsPaused", ParameterCount = 0,
                Because = SpeedStack + "; 0.4.6's IsPaused was the flag PauseMovement set and ResumeMovement cleared, "
                        + "so it is whether the pause control those two manage is on the stack",
                Emit = EmitIsPaused,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Movement, OldName = "VisibilityChange", ParameterCount = 1,
                Because = "0.4.6's VisibilityChange(visible) showed or hid the NPC's capsule collider. 0.4.7's "
                        + "OnVisibilityChange also enables the agent only on the server (NPCMovement.cs:557-561 on "
                        + "0.4.7f6), so this toggles the collider alone, as 0.4.6 did",
                Emit = EmitVisibilityChange,
            },

            // ---- messages ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Messaging, OldName = "ReceiveMessage", ParameterCount = 3,
                Because = "0.4.6's ReceiveMessage was the ObserversRpc that delivered a message on every client; its "
                        + "0.4.7 successor is SendMessage_Client(m, notify, id) (MessagingManager.cs:93 on 0.4.7f6). "
                        + ByConversation,
                Emit = (module, manager) => EmitDeliverToNpc(module, manager, "ReceiveMessage", "SendMessage_Client"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Messaging, OldName = "SendMessage", ParameterCount = 3,
                Because = "0.4.6's SendMessage was the ServerRpc that fanned a message out through ReceiveMessage; its "
                        + "0.4.7 successor is SendMessage_Server(m, notify, id), which calls SendMessage_Client "
                        + "(MessagingManager.cs:86, :242 on 0.4.7f6). " + ByConversation,
                Emit = (module, manager) => EmitDeliverToNpc(module, manager, "SendMessage", "SendMessage_Server"),
            },

            // ---- trash ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Trash.TrashItem",
                OldName = "get_CurrentProperty", ParameterCount = 0,
                Because = "0.4.7's trash no longer records a property. 0.4.6 answered the owned property whose bounds "
                        + "contain the item (TrashItem.cs:249-263 on 0.4.6f13), and 0.4.7 still has both halves "
                        + "(Property.OwnedProperties, Property.DoBoundsContainPoint on 0.4.7f6), null outside every "
                        + "owned property",
                Emit = EmitTrashProperty,
            },

            // ---- avatar culling (NetEye's security cameras) ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Animation, OldName = "get_AllowCulling", ParameterCount = 0,
                Because = Culling + "; culling is allowed while that repeating check is running",
                Emit = EmitGetAllowCulling,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Animation, OldName = "set_AllowCulling", ParameterCount = 1,
                Because = Culling + "; disallowing cancels the check and un-culls the avatar the way the check itself "
                        + "does (body on, impostor off, IsCulled false, OnAvatarCullingChange(false)), allowing restarts it",
                Emit = EmitSetAllowCulling,
            },

            // ---- avatar bones and body layers (Personnel, Police Response Overhaul) ----
            RigBone("get_HeadBone", 10, "HumanBodyBones.Head"),
            RigBone("get_LeftFootBone", 5, "HumanBodyBones.LeftFoot"),
            RigBone("get_RightFootBone", 6, "HumanBodyBones.RightFoot"),
            RigBone("get_LowerSpine", 7, "HumanBodyBones.Spine, the first bone above the hips. 0.4.7's own "
                    + "AttachmentAnchorProviderComponent names the bones above Hips LowerSpine, MiddleSpine and "
                    + "UpperSpine (AttachmentAnchorProviderComponent.cs:12-21 on 0.4.7f6), which the humanoid rig "
                    + "calls Spine, Chest and UpperChest"),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = AvatarType, OldName = "SetBodyLayer", ParameterCount = 3,
                Because = "0.4.6 painted body layers one slot at a time (SetBodyLayer); 0.4.7 dresses an avatar as a whole "
                        + "through Avatar.Appearance and clears the slots it does not use itself, so a call that clears a "
                        + "slot has nothing left to do",
                Emit = EmitSetBodyLayer,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = AvatarType, OldName = "get_UseCombinedLayer", ParameterCount = 0,
                Because = Layers + "; it answers true, the 0.4.6 field's default",
                Emit = (module, avatar) =>
                {
                    var method = new MethodDefinition("get_UseCombinedLayer",
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                        module.TypeSystem.Boolean);
                    var il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Ret);
                    return AsProperty(avatar, method);
                },
            },
            Empty(AvatarType, "set_UseCombinedLayer", 1, m => m.TypeSystem.Void, Layers, m => m.TypeSystem.Boolean),
            Empty(AvatarType, "ApplyBodyLayerSettings", 2, m => m.TypeSystem.Void, Layers,
                  m => m.GetType("Il2CppScheduleOne.AvatarFramework.AvatarSettings"), m => m.TypeSystem.Int32),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = AvatarType, OldName = "EnableRagdoll", ParameterCount = 2,
                AllowOverload = true,   // beside 0.4.7's EnableRagdoll(), which takes no force
                Because = "split in two on 0.4.7: EnableRagdoll() takes no force and EnableRagdollAndApplyForce(point, "
                        + "direction) is the old two-argument call (Avatar.cs:256-264 on 0.4.7f6)",
                Emit = (module, avatar) => EmitForwardArgs(module, avatar, "EnableRagdoll", "EnableRagdollAndApplyForce", 2),
            },

            // ---- combat speed (Police Response Overhaul) ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Combat.CombatBehaviour",
                OldName = "SetMovementSpeed", ParameterCount = 3,
                AllowOverload = true,   // beside 0.4.7's SetMovementSpeed(normalizedSpeed)
                Because = "0.4.7's SetMovementSpeed(normalizedSpeed) adds the \"CombatBehaviour\" speed control at "
                        + "priority 5, and EndCombat removes that id (CombatBehaviour.cs:241, :381-388 on 0.4.7f6). "
                        + "0.4.6's took a speed between walk and run speed and an id defaulting to \"combat\"; the "
                        + "speed is converted to 0.4.7's fraction of the top speed, Lerp(walk, max, speed) / max, and "
                        + "\"combat\" becomes \"CombatBehaviour\" so ending combat still takes it off",
                Emit = EmitSetMovementSpeed,
            },
        };

        // ---------------------------------------------------------------- pausing

        private static MethodDefinition EmitPause(ModuleDefinition module, TypeDefinition movement, bool pause)
        {
            var getController = Controller(movement, out var controller);
            MethodDefinition target = null;
            if (controller != null)
                foreach (var m in controller.Methods)
                    if (pause && m.Name == "AddSpeedControl" && m.Parameters.Count == 4
                        && m.Parameters[0].ParameterType.MetadataType == MetadataType.String) { target = m; break; }
                    else if (!pause && m.Name == "RemoveSpeedControl" && m.Parameters.Count == 1) { target = m; break; }
            var getAgent = Getter(movement, "_agent");
            var agent = getAgent?.ReturnType?.Resolve();
            var onNavMesh = Getter(agent, "isOnNavMesh");
            var setStopped = Method(agent, "set_isStopped", 1);
            var setVelocity = Method(agent, "set_velocity", 1);
            var zero = Getter(setVelocity?.Parameters[0].ParameterType.Resolve(), "zero");
            if (getController == null || target == null || getAgent == null || onNavMesh == null
                || setStopped == null || setVelocity == null || zero == null) return null;

            var method = new MethodDefinition(pause ? "PauseMovement" : "ResumeMovement",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            var il = method.Body.GetILProcessor();
            var ret = il.Create(OpCodes.Ret);
            var agentStep = il.Create(OpCodes.Ldarg_0);
            var have = il.Create(OpCodes.Ldstr, PauseControl);
            var noAgent = il.Create(OpCodes.Pop);

            // var c = SpeedController; if (c != null) c.AddSpeedControl("polyfill_pause", 0f, int.MaxValue, Absolute)
            //                                        / c.RemoveSpeedControl("polyfill_pause");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Br, agentStep);
            il.Append(have);
            if (pause)
            {
                il.Emit(OpCodes.Ldc_R4, 0f);
                il.Emit(OpCodes.Ldc_I4, int.MaxValue);
                il.Emit(OpCodes.Ldc_I4_1);                                  // EType.Absolute
            }
            il.Emit(target.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, target);

            // var a = _agent; if (a != null && a.isOnNavMesh) { a.isStopped = pause; if (pause) a.velocity = zero; }
            il.Append(agentStep);
            il.Emit(OpCodes.Call, getAgent);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, noAgent);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Call, module.ImportReference(onNavMesh));
            il.Emit(OpCodes.Brfalse, noAgent);
            if (pause) il.Emit(OpCodes.Dup);
            il.Emit(pause ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, module.ImportReference(setStopped));
            if (pause)
            {
                il.Emit(OpCodes.Call, module.ImportReference(zero));
                il.Emit(OpCodes.Call, module.ImportReference(setVelocity));
            }
            il.Emit(OpCodes.Br, ret);
            il.Append(noAgent);
            il.Append(ret);
            return method;
        }

        private static MethodDefinition EmitIsPaused(ModuleDefinition module, TypeDefinition movement)
        {
            var getController = Controller(movement, out var controller);
            var exists = Method(controller, "DoesSpeedControlExist", 1);
            if (getController == null || exists == null) return null;
            var method = new MethodDefinition("get_IsPaused",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Boolean);
            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Ldstr, PauseControl);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(exists.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, exists);
            il.Emit(OpCodes.Ret);
            return AsProperty(movement, method);
        }

        private static MethodDefinition EmitVisibilityChange(ModuleDefinition module, TypeDefinition movement)
        {
            var getCollider = Getter(movement, "_capsuleCollider");
            var getGameObject = MethodUp(getCollider?.ReturnType?.Resolve(), "get_gameObject", 0);
            var setActive = Method(getGameObject?.ReturnType?.Resolve(), "SetActive", 1);
            if (getCollider == null || getGameObject == null || setActive == null) return null;
            var method = new MethodDefinition("VisibilityChange",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            var visible = new ParameterDefinition("visible", ParameterAttributes.None, module.TypeSystem.Boolean);
            method.Parameters.Add(visible);
            var il = method.Body.GetILProcessor();
            var none = il.Create(OpCodes.Pop);
            var ret = il.Create(OpCodes.Ret);
            // var c = _capsuleCollider; if (c != null) c.gameObject.SetActive(visible);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getCollider);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Call, module.ImportReference(getGameObject));
            il.Emit(OpCodes.Ldarg, visible);
            il.Emit(OpCodes.Call, module.ImportReference(setActive));
            il.Emit(OpCodes.Br, ret);
            il.Append(none);
            il.Append(ret);
            return method;
        }

        // ---------------------------------------------------------------- messages

        private static MethodDefinition EmitDeliverToNpc(ModuleDefinition module, TypeDefinition manager,
                                                         string name, string successor)
        {
            var npcManager = module.GetType("Il2CppScheduleOne.NPCs.NPCManager");
            var getNpc = Method(npcManager, "GetNPC", 1);
            var npc = getNpc?.ReturnType?.Resolve();
            var getConversation = Getter(npc, "MSGConversation");
            var getId = Getter(getConversation?.ReturnType?.Resolve(), "ConversationId");
            var send = Method(manager, successor, 3);
            if (getNpc == null || !getNpc.IsStatic || getConversation == null || getId == null || send == null)
                return null;

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            var message = new ParameterDefinition("m", ParameterAttributes.None, module.ImportReference(send.Parameters[0].ParameterType));
            var notify = new ParameterDefinition("notify", ParameterAttributes.None, module.TypeSystem.Boolean);
            var id = new ParameterDefinition("id", ParameterAttributes.None, module.TypeSystem.String);
            method.Parameters.Add(message);
            method.Parameters.Add(notify);
            method.Parameters.Add(id);
            var conversation = new VariableDefinition(module.ImportReference(getConversation.ReturnType));
            method.Body.Variables.Add(conversation);
            method.Body.InitLocals = true;

            var il = method.Body.GetILProcessor();
            var notFound = il.Create(OpCodes.Pop);
            var found = il.Create(OpCodes.Ldarg_0);
            // var n = NPCManager.GetNPC(id); var c = n?.MSGConversation; if (c == null) { log; return; }
            il.Emit(OpCodes.Ldarg, id);
            il.Emit(OpCodes.Call, getNpc);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, notFound);
            il.Emit(OpCodes.Call, getConversation);
            il.Emit(OpCodes.Stloc, conversation);
            il.Emit(OpCodes.Ldloc, conversation);
            il.Emit(OpCodes.Brtrue, found);
            il.Emit(OpCodes.Ldnull);
            il.Append(notFound);
            if (!EmitWarning(module, il, $"[Polyfill] MessagingManager.{name}: NPC not found with ID ", id)) return null;
            il.Emit(OpCodes.Ret);
            // this.<successor>(m, notify, c.ConversationId);
            il.Append(found);
            il.Emit(OpCodes.Ldarg, message);
            il.Emit(OpCodes.Ldarg, notify);
            il.Emit(OpCodes.Ldloc, conversation);
            il.Emit(OpCodes.Call, getId);
            il.Emit(send.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, send);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>Emits <c>Console.LogWarning(text + argument)</c> through the game's own console.</summary>
        /// <remarks>
        /// The console takes the interop's object type. Its string type is found beside it and the conversion
        /// read off that type, so no interop runtime name has to be written here.
        /// </remarks>
        private static bool EmitWarning(ModuleDefinition module, ILProcessor il, string text, ParameterDefinition argument)
        {
            var console = module.GetType("Il2CppScheduleOne.Console");
            var log = Method(console, "LogWarning", 2);
            var objectType = log?.Parameters[0].ParameterType.Resolve();
            var stringType = objectType?.Module.GetType(objectType.Namespace, "String");
            MethodDefinition convert = null;
            if (stringType != null)
                foreach (var m in stringType.Methods)
                    if (m.Name == "op_Implicit" && m.Parameters.Count == 1
                        && m.Parameters[0].ParameterType.MetadataType == MetadataType.String
                        && m.ReturnType.Resolve() == stringType) { convert = m; break; }
            if (log == null || !log.IsStatic || convert == null) return false;

            var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String) { HasThis = false };
            concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

            il.Emit(OpCodes.Ldstr, text);
            il.Emit(OpCodes.Ldarg, argument);
            il.Emit(OpCodes.Call, concat);
            il.Emit(OpCodes.Call, module.ImportReference(convert));
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, log);
            return true;
        }

        // ---------------------------------------------------------------- trash

        /// <summary>
        /// <c>TrashItem.CurrentProperty</c>: the first owned property whose bounds contain the item, or null.
        /// </summary>
        private static MethodDefinition EmitTrashProperty(ModuleDefinition module, TypeDefinition trash)
        {
            var propertyType = module.GetType("Il2CppScheduleOne.Property.Property");
            var getAll = Getter(propertyType, "OwnedProperties");
            var list = getAll?.ReturnType as GenericInstanceType;
            var listDef = list?.ElementType.Resolve();
            var getCount = Getter(listDef, "Count");
            var getItem = Method(listDef, "get_Item", 1);
            var contains = Method(propertyType, "DoBoundsContainPoint", 1);
            var getTransform = MethodUp(trash, "get_transform", 0);
            var getPosition = Getter(getTransform?.ReturnType?.Resolve(), "position");
            if (getAll == null || !getAll.IsStatic || getCount == null || getItem == null || contains == null
                || getTransform == null || getPosition == null) return null;

            var method = new MethodDefinition("get_CurrentProperty",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(propertyType));
            var body = method.Body;
            var all = new VariableDefinition(module.ImportReference(list));
            var point = new VariableDefinition(module.ImportReference(getPosition.ReturnType));
            var i = new VariableDefinition(module.TypeSystem.Int32);
            var count = new VariableDefinition(module.TypeSystem.Int32);
            var candidate = new VariableDefinition(module.ImportReference(propertyType));
            foreach (var v in new[] { all, point, i, count, candidate }) body.Variables.Add(v);
            body.InitLocals = true;

            var countRef = module.ImportReference(Against(module, getCount, list));
            var itemRef = module.ImportReference(Against(module, getItem, list));
            var il = body.GetILProcessor();
            var none = il.Create(OpCodes.Ldnull);
            var test = il.Create(OpCodes.Ldloc, i);
            var next = il.Create(OpCodes.Ldloc, i);
            var loop = il.Create(OpCodes.Ldloc, all);

            // var point = transform.position; var all = Property.OwnedProperties; if (all == null) return null;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(getTransform));
            il.Emit(OpCodes.Callvirt, module.ImportReference(getPosition));
            il.Emit(OpCodes.Stloc, point);
            il.Emit(OpCodes.Call, module.ImportReference(getAll));
            il.Emit(OpCodes.Stloc, all);
            il.Emit(OpCodes.Ldloc, all);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Ldloc, all);
            il.Emit(OpCodes.Callvirt, countRef);
            il.Emit(OpCodes.Stloc, count);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, i);
            il.Emit(OpCodes.Br, test);

            // var p = all[i]; if (p != null && p.DoBoundsContainPoint(point)) return p;
            il.Append(loop);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Callvirt, itemRef);
            il.Emit(OpCodes.Stloc, candidate);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Ldloc, point);
            il.Emit(contains.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, module.ImportReference(contains));
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Ret);

            il.Append(next);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, i);
            il.Append(test);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Blt, loop);

            il.Append(none);
            il.Emit(OpCodes.Ret);
            return AsProperty(trash, method);
        }

        // ---------------------------------------------------------------- culling

        private static MethodDefinition EmitGetAllowCulling(ModuleDefinition module, TypeDefinition animation)
        {
            var getAvatar = AvatarOf(animation, out var avatar);
            var isInvoking = MethodUp(avatar, "IsInvoking", 1);
            if (getAvatar == null || isInvoking == null) return null;
            var method = new MethodDefinition("get_AllowCulling",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Boolean);
            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Ldstr, CullCheck);
            // var a = avatar; return a == null ? true : a.IsInvoking("UpdateAnimationActive");
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getAvatar);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Call, module.ImportReference(isInvoking));
            il.Emit(OpCodes.Ret);
            return AsProperty(animation, method);
        }

        private static MethodDefinition EmitSetAllowCulling(ModuleDefinition module, TypeDefinition animation)
        {
            var getAvatar = AvatarOf(animation, out var avatar);
            var isInvoking = MethodUp(avatar, "IsInvoking", 1);
            var invokeRepeating = MethodUp(avatar, "InvokeRepeating", 3);
            var cancelInvoke = MethodUp(avatar, "CancelInvoke", 1);
            var getCulled = Getter(avatar, "IsCulled");
            var setCulled = Method(avatar, "set_IsCulled", 1);
            var getBody = Getter(avatar, "BodyContainer");
            var getGameObject = MethodUp(getBody?.ReturnType?.Resolve(), "get_gameObject", 0);
            var setActive = Method(getGameObject?.ReturnType?.Resolve(), "SetActive", 1);
            var getImpostor = Getter(avatar, "_impostor");
            var disable = Method(getImpostor?.ReturnType?.Resolve(), "Disable", 0);
            var getEvent = Getter(avatar, "OnAvatarCullingChange");
            var eventType = getEvent?.ReturnType as GenericInstanceType;
            var invoke = Method(eventType?.ElementType.Resolve(), "Invoke", 1);
            if (getAvatar == null || isInvoking == null || invokeRepeating == null || cancelInvoke == null
                || getCulled == null || setCulled == null || getBody == null || getGameObject == null || setActive == null
                || getImpostor == null || disable == null || getEvent == null || invoke == null) return null;

            var method = new MethodDefinition("set_AllowCulling",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Void);
            var value = new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.Boolean);
            method.Parameters.Add(value);
            var a = new VariableDefinition(module.ImportReference(avatar));
            method.Body.Variables.Add(a);
            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();
            var ret = il.Create(OpCodes.Ret);
            var disallow = il.Create(OpCodes.Ldloc, a);
            var noImpostor = il.Create(OpCodes.Nop);
            var noEvent = il.Create(OpCodes.Pop);

            // var a = avatar; if (a == null) return;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getAvatar);
            il.Emit(OpCodes.Stloc, a);
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Brfalse, ret);
            // if (value) { if (!a.IsInvoking(check)) a.InvokeRepeating(check, 0f, 0.1f); return; }
            il.Emit(OpCodes.Ldarg, value);
            il.Emit(OpCodes.Brfalse, disallow);
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Ldstr, CullCheck);
            il.Emit(OpCodes.Call, module.ImportReference(isInvoking));
            il.Emit(OpCodes.Brtrue, ret);
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Ldstr, CullCheck);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ldc_R4, 0.1f);
            il.Emit(OpCodes.Call, module.ImportReference(invokeRepeating));
            il.Emit(OpCodes.Br, ret);
            // a.CancelInvoke(check); if (!a.IsCulled) return;
            il.Append(disallow);
            il.Emit(OpCodes.Ldstr, CullCheck);
            il.Emit(OpCodes.Call, module.ImportReference(cancelInvoke));
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Call, getCulled);
            il.Emit(OpCodes.Brfalse, ret);
            // a.IsCulled = false; a.BodyContainer.gameObject.SetActive(true);
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, setCulled);
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Call, getBody);
            il.Emit(OpCodes.Call, module.ImportReference(getGameObject));
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, module.ImportReference(setActive));
            // var imp = a._impostor; if (imp != null) imp.Disable();
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Call, getImpostor);
            il.Emit(OpCodes.Dup);
            var popImpostor = il.Create(OpCodes.Pop);
            il.Emit(OpCodes.Brfalse, popImpostor);
            il.Emit(OpCodes.Call, module.ImportReference(disable));
            il.Emit(OpCodes.Br, noImpostor);
            il.Append(popImpostor);
            il.Append(noImpostor);
            // var e = a.OnAvatarCullingChange; if (e != null) e.Invoke(false);
            il.Emit(OpCodes.Ldloc, a);
            il.Emit(OpCodes.Call, getEvent);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, noEvent);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, module.ImportReference(Against(module, invoke, eventType)));
            il.Emit(OpCodes.Br, ret);
            il.Append(noEvent);
            il.Append(ret);
            return AsProperty(animation, method);
        }

        // ---------------------------------------------------------------- avatar bones and body layers

        /// <summary>A bone getter 0.4.7 removed, answered from the avatar's humanoid rig.</summary>
        /// <remarks>
        /// 0.4.6 kept these bones as fields on Avatar; 0.4.7 kept HipBone, the shoulders and two spine bones and
        /// dropped them. The rig still has them: Avatar.Animation._animator is the humanoid Animator
        /// (AvatarAnimation.cs on 0.4.7f6), and GetBoneTransform names each one.
        /// </remarks>
        private static Bridge RigBone(string name, int humanBone, string bone)
            => new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = AvatarType, OldName = name, ParameterCount = 0,
                Because = "0.4.7 dropped the bone fields it no longer read; the humanoid rig still has the bone, and "
                        + "Avatar.Animation._animator.GetBoneTransform finds it: " + bone,
                Emit = (module, avatar) => EmitRigBone(module, avatar, name, humanBone),
            };

        private static MethodDefinition EmitRigBone(ModuleDefinition module, TypeDefinition avatar, string name, int humanBone)
        {
            var getAnimation = Getter(avatar, "Animation");
            var getAnimator = Getter(getAnimation?.ReturnType?.Resolve(), "_animator");
            var getBone = Method(getAnimator?.ReturnType?.Resolve(), "GetBoneTransform", 1);
            if (getAnimation == null || getAnimator == null || getBone == null) return null;

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(getBone.ReturnType));
            var il = method.Body.GetILProcessor();
            var none = il.Create(OpCodes.Pop);
            // var an = Animation; if (an == null) return null; var a = an._animator; if (a == null) return null;
            // return a.GetBoneTransform(bone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getAnimation);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Call, getAnimator);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Ldc_I4, humanBone);
            il.Emit(OpCodes.Call, module.ImportReference(getBone));
            il.Emit(OpCodes.Ret);
            il.Append(none);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            return AsProperty(avatar, method);
        }

        private static MethodDefinition EmitSetBodyLayer(ModuleDefinition module, TypeDefinition avatar)
        {
            var color = TypeIn(module, "UnityEngine.CoreModule", "UnityEngine", "Color");
            if (color == null) return null;
            color.IsValueType = true;
            var method = new MethodDefinition("SetBodyLayer", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("index", ParameterAttributes.None, module.TypeSystem.Int32));
            method.Parameters.Add(new ParameterDefinition("path", ParameterAttributes.None, module.TypeSystem.String));
            method.Parameters.Add(new ParameterDefinition("color", ParameterAttributes.None, color));
            method.Body.GetILProcessor().Emit(OpCodes.Ret);
            return method;
        }

        private static MethodDefinition EmitForwardArgs(ModuleDefinition module, TypeDefinition type, string oldName,
                                                        string target, int count)
        {
            var to = Method(type, target, count);
            if (to == null || to.IsStatic) return null;
            var method = new MethodDefinition(oldName, MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.ImportReference(to.ReturnType));
            foreach (var p in to.Parameters)
                method.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None, module.ImportReference(p.ParameterType)));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 1; i <= count; i++) il.Emit(OpCodes.Ldarg, method.Parameters[i - 1]);
            il.Emit(to.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, to);
            il.Emit(OpCodes.Ret);
            return method;
        }

        // ---------------------------------------------------------------- combat speed

        private static MethodDefinition EmitSetMovementSpeed(ModuleDefinition module, TypeDefinition combat)
        {
            var getNpc = MethodUp(combat, "get_Npc", 0);
            var getMovement = Getter(getNpc?.ReturnType?.Resolve(), "Movement");
            var movement = getMovement?.ReturnType?.Resolve();
            var getController = Controller(movement, out var controller);
            var getWalk = Getter(movement, "DefaultWalkSpeed");
            var getMax = Getter(controller, "_maxSpeed");
            var add = Method(controller, "AddSpeedControl", 4);
            if (getNpc == null || getMovement == null || getController == null || getWalk == null || getMax == null
                || add == null) return null;

            var method = new MethodDefinition("SetMovementSpeed", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            var speed = new ParameterDefinition("speed", ParameterAttributes.None, module.TypeSystem.Single);
            var id = new ParameterDefinition("id", ParameterAttributes.None, module.TypeSystem.String);
            var priority = new ParameterDefinition("priority", ParameterAttributes.None, module.TypeSystem.Int32);
            method.Parameters.Add(speed);
            method.Parameters.Add(id);
            method.Parameters.Add(priority);
            var m = new VariableDefinition(module.ImportReference(getMovement.ReturnType));
            var c = new VariableDefinition(module.ImportReference(getController.ReturnType));
            var walk = new VariableDefinition(module.TypeSystem.Single);
            var max = new VariableDefinition(module.TypeSystem.Single);
            var v = new VariableDefinition(module.TypeSystem.Single);
            foreach (var local in new[] { m, c, walk, max, v }) method.Body.Variables.Add(local);
            method.Body.InitLocals = true;

            var equals = new MethodReference("op_Equality", module.TypeSystem.Boolean, module.TypeSystem.String) { HasThis = false };
            equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            equals.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

            var il = method.Body.GetILProcessor();
            var ret = il.Create(OpCodes.Ret);
            var none = il.Create(OpCodes.Pop);
            var notBelow = il.Create(OpCodes.Ldloc, v);
            var notAbove = il.Create(OpCodes.Ldloc, m);
            var noMax = il.Create(OpCodes.Ldloc, v);
            var converted = il.Create(OpCodes.Stloc, v);
            var keepId = il.Create(OpCodes.Ldloc, c);

            // var m = Npc?.Movement; var c = m?.SpeedController; if (c == null) return;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(getNpc));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Call, getMovement);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Stloc, m);
            il.Emit(OpCodes.Ldloc, m);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Stloc, c);

            // var v = Mathf.Clamp01(speed);   (0.4.6 lerped with it, and Lerp clamps)
            il.Emit(OpCodes.Ldarg, speed);
            il.Emit(OpCodes.Stloc, v);
            il.Emit(OpCodes.Ldloc, v);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Bge_Un, notBelow);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Stloc, v);
            il.Append(notBelow);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Ble_Un, notAbove);
            il.Emit(OpCodes.Ldc_R4, 1f);
            il.Emit(OpCodes.Stloc, v);

            // var walk = m.DefaultWalkSpeed; var max = c._maxSpeed;
            // v = max > 0 ? (walk + (max - walk) * v) / max : v;
            il.Append(notAbove);
            il.Emit(OpCodes.Call, getWalk);
            il.Emit(OpCodes.Stloc, walk);
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Call, getMax);
            il.Emit(OpCodes.Stloc, max);
            il.Emit(OpCodes.Ldloc, max);
            il.Emit(OpCodes.Ldc_R4, 0f);
            il.Emit(OpCodes.Ble_Un, noMax);
            il.Emit(OpCodes.Ldloc, walk);
            il.Emit(OpCodes.Ldloc, max);
            il.Emit(OpCodes.Ldloc, walk);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Ldloc, v);
            il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldloc, max);
            il.Emit(OpCodes.Div);
            il.Emit(OpCodes.Br, converted);
            il.Append(noMax);
            il.Append(converted);

            // if (id == "combat") id = "CombatBehaviour";
            il.Emit(OpCodes.Ldarg, id);
            il.Emit(OpCodes.Ldstr, OldCombatControl);
            il.Emit(OpCodes.Call, equals);
            il.Emit(OpCodes.Brfalse, keepId);
            il.Emit(OpCodes.Ldstr, CombatControl);
            il.Emit(OpCodes.Starg, id);

            // c.AddSpeedControl(id, v, priority, SpeedControl.EType.Normalized);
            il.Append(keepId);
            il.Emit(OpCodes.Ldarg, id);
            il.Emit(OpCodes.Ldloc, v);
            il.Emit(OpCodes.Ldarg, priority);
            il.Emit(OpCodes.Ldc_I4_0);   // SpeedControl.EType.Normalized
            il.Emit(OpCodes.Call, add);
            il.Emit(OpCodes.Br, ret);
            il.Append(none);
            il.Append(ret);
            return method;
        }
    }
}
