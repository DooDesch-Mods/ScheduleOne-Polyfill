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

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Customer,
                OldName = "ProcessHandover",
                ParameterCount = 5,
                AllowOverload = true,
                Creates = HandoverOutcome,
                Because = "the outcome argument went with the enum: 0.4.6 read it only to show the deal "
                        + "popup on Finalize (Customer.cs:1424 on 0.4.6f13), every caller passed Finalize "
                        + "(Customer.cs:1347, :1767, RequestProductBehaviour.cs:365), and 0.4.7 shows the "
                        + "popup unconditionally (Customer.cs:1420-1424 on 0.4.7f6)",
                Emit = EmitProcessHandover,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = HandoverScreen,
                OldName = "ClearCustomerSlots",
                ParameterCount = 1,
                Because = "the flag became the choice of method: 0.4.6 ClearCustomerSlots(true) put each item "
                        + "back into the inventory and cleared the slot, (false) only cleared it "
                        + "(HandoverScreen.cs:349-366 on 0.4.6f13); 0.4.7 has ReturnCustomerItems and "
                        + "DestroyCustomerItems for the two (HandoverScreen.cs:273-312 on 0.4.7f6)",
                Emit = EmitClearCustomerSlots,
            },

            // THE PLAYER LOAD PATH WAS REBUILT, and a patch on either end of it lost its target. The two
            // stand-ins below exist so a patch CLASS registers - Harmony drops the whole class over one
            // missing target, and OG Backpack keeps its save hook (WriteData) in the same class as its
            // Load hook, so without them a backpack is never written to the save. What the patches then
            // get to see is the Report half's job: PlayerLoadRelay calls them at the moment 0.4.7 does the
            // work, with the arguments translated.
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.PlayerScripts.Player",
                OldName = "Load",
                ParameterCount = 2,
                ParameterTypes = new[] { "Il2CppScheduleOne.Persistence.Datas.PlayerData", "System.String" },
                Because = "0.4.6 loaded the host's own player through Player.Load(data, containerPath) "
                        + "(PlayerManager.cs:108-126 on 0.4.6f13); 0.4.7 loads every player through "
                        + "SetPlayerData_Client (Player.cs:2768-2811 on 0.4.7f6), and Polyfill relays a "
                        + "patch on the old method there",
                Emit = EmitPlayerLoadStandIn,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = PlayerManager,
                OldName = "TryGetPlayerData",
                ParameterCount = 6,
                AllowOverload = true,
                Because = "0.4.6 handed a joining player's save back as five out values "
                        + "(PlayerManager.cs:163 on 0.4.6f13); 0.4.7 bundles them into one FullPlayerData "
                        + "and takes whether the asker is the host (PlayerManager.cs:152-240 on 0.4.7f6)",
                Emit = EmitTryGetPlayerDataStandIn,
            },

            // THE AVATAR LOST ITS SETTINGS OBJECT. 0.4.6 drew a look from one AvatarSettings
            // (Avatar.cs:328-354 on 0.4.6f13); 0.4.7 draws it from a NakedAppearance plus an outfit through
            // Avatar.Appearance (AvatarAppearance.cs:96-147 on 0.4.7f6). The legacy assets still load and
            // point at their 0.4.7 object (AvatarLayer.cs:26, Accessory.cs:32), so the translation is
            // possible, but it is far past what a body of IL should carry: these are targets, and
            // AvatarSettingsBridge in the Report half gives them their behaviour.
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = AvatarType,
                OldName = "get_CurrentSettings",
                ParameterCount = 0,
                Because = AvatarSettingsGone,
                Emit = EmitCurrentSettingsStandIn,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = AvatarType,
                OldName = "LoadAvatarSettings",
                ParameterCount = 1,
                ParameterTypes = new[] { AvatarSettingsType },
                Because = AvatarSettingsGone,
                Emit = EmitLoadAvatarSettingsStandIn,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.Framework.Appearance",
                OldName = "set_AvatarSettings",
                ParameterCount = 1,
                ParameterTypes = new[] { AvatarSettingsType },
                Because = "0.4.6 NPC data carried the AvatarSettings an NPC is set up with (Appearance.cs:10-15, "
                        + "NPC.cs:335-344 on 0.4.6f13); 0.4.7 carries a DefaultAppearance and a DefaultOutfit "
                        + "(Appearance.cs:10-16, NPC.cs:536-546 on 0.4.7f6), and Polyfill translates one into "
                        + "the other",
                Emit = EmitDataAvatarSettingsStandIn,
            },

            // SLEEP MOVED OFF THE CLOCK. 0.4.6 kept the sleep hooks on TimeManager: two public Action fields
            // and IsSleepInProgress, raised in its StartSleep RPC (TimeManager.cs:65-67, 104, 802-840 on
            // 0.4.6f13). 0.4.7 moved all three to SleepController, raised in the same places - its own
            // StartSleep RPC on every peer, OnSleepEnd after the clock skipped (SleepController.cs:60,
            // 95-97, 217-280 on 0.4.7f6). S1API reads them in a static initialiser, so without these
            // every S1API time call throws and takes the mods built on it along.
            OnSleepController("get_onSleepStart", 0, "get_OnSleepStart"),
            OnSleepController("set_onSleepStart", 1, "set_OnSleepStart"),
            OnSleepController("get_onSleepEnd", 0, "get_OnSleepEnd"),
            OnSleepController("set_onSleepEnd", 1, "set_OnSleepEnd"),
            OnSleepController("get_IsSleepInProgress", 0, "get_IsSleepInProgress"),

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.Messaging.MSGConversation",
                OldName = ".ctor",
                ParameterCount = 2,
                ParameterTypes = new[] { "Il2CppScheduleOne.NPCs.NPC", "System.String" },
                AllowOverload = true,
                Because = "a conversation used to belong to an NPC and register under it "
                        + "(MSGConversation.cs:111-119, MessagingManager.cs:78-88 on 0.4.6f13); 0.4.7 builds it "
                        + "from the NPC's contact info and registers it under an id, and names that id after "
                        + "the NPC's network object (MSGConversation.cs:113-120, MessagingManager.cs:67-78, "
                        + "NPC.cs:618 on 0.4.7f6)",
                Emit = EmitConversationForNpc,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPCManager",
                OldName = "get_NPCContainer",
                ParameterCount = 0,
                Because = "0.4.6 parented every NPC under NPCManager.NPCContainer (NPC.cs:444, :1704 on "
                        + "0.4.6f13); 0.4.7 has no container and puts each NPC back under the parent it was "
                        + "placed with (NPC.cs:437-442, :3302 on 0.4.7f6), so there is nothing to hand back",
                Emit = EmitNoNpcContainer,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Movement,
                OldName = "CanMove",
                ParameterCount = 0,
                Because = "the check became a read-only property of the same name: CanMove is true unless the "
                        + "NPC is ragdolled, in a building or in a vehicle (NPCMovement.cs:193-203 on "
                        + "0.4.7f6), and the movement code gates on it where it gated on the method "
                        + "(NPCMovement.cs:775, :831)",
                Emit = EmitCanMoveMethod,
            },

            HandBridge("get_RightHandContainer"),
            HandBridge("get_LeftHandContainer"),
            HandBridge("get_RightHandAlignmentPoint"),
            HandBridge("get_LeftHandAlignmentPoint"),

            // The server half of the handover lost the same leading outcome as ProcessHandover. 0.4.7 calls
            // it from exactly one place, ProcessHandover, which is where 0.4.6 forwarded its own outcome -
            // and every 0.4.6 caller of that passed Finalize (see the ProcessHandover rule above).
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Customer,
                OldName = "ProcessHandoverServerSide",
                ParameterCount = 7,
                AllowOverload = true,
                Because = "the outcome argument went with the enum, as on ProcessHandover: 0.4.7 calls the "
                        + "server half only from ProcessHandover (Customer.cs:1438 on 0.4.7f6), where 0.4.6 "
                        + "forwarded the outcome every caller passed as Finalize",
                Emit = (module, customer) => EmitDroppedOutcome(module, customer, "ProcessHandoverServerSide",
                                                                name => name == "ProcessHandoverServerSide"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Customer,
                OldName = "RpcLogic___ProcessHandoverServerSide_3760244802",
                ParameterCount = 7,
                Because = "FishNet names the RPC body after a hash of the signature, so dropping the outcome "
                        + "renamed it (RpcLogic___ProcessHandoverServerSide_3315874220 on 0.4.7f6); it is the "
                        + "same body ProcessHandoverServerSide runs on the server",
                Emit = (module, customer) => EmitDroppedOutcome(module, customer,
                    "RpcLogic___ProcessHandoverServerSide_3760244802",
                    name => name.StartsWith("RpcLogic___ProcessHandoverServerSide_", StringComparison.Ordinal)),
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = HandoverScreen,
                OldName = "get_CurrentContract",
                ParameterCount = 0,
                Because = "0.4.7 split the handover screen into modes and the contract moved onto the contract "
                        + "mode: set when a contract handover opens and cleared when it closes "
                        + "(HandoverScreenContractMode.cs:25, :46, :81 on 0.4.7f6), and never set by the "
                        + "sample, offer or special-customer modes, so it is null exactly when no contract "
                        + "handover is open",
                Emit = EmitCurrentContract,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.AvatarFramework.Avatar",
                OldName = "get_RagdollRBs",
                ParameterCount = 0,
                Because = "0.4.6 kept the ragdoll's rigidbodies in a permanent RagdollRBs field; 0.4.7 builds a "
                        + "Ragdoll when the avatar goes down and drops it when it gets up (Avatar.cs:115-117, "
                        + "Ragdoll.cs:23 on 0.4.7f6), so the same rigidbodies exist exactly while Ragdolled is "
                        + "true and there are none to hand back otherwise",
                Emit = EmitRagdollRigidbodies,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPC",
                OldName = "SendTextMessage",
                ParameterCount = 1,
                Because = "0.4.7 has no SendTextMessage; its own NPCs text the player with "
                        + "MSGConversation.SendMessage(new Message(text, ESenderType.Other)) and the default "
                        + "notify and network (the Dealer robbery and cash texts on 0.4.7f6), so that is what "
                        + "the old call does",
                Emit = EmitSendTextMessage,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.Messaging.MSGConversation",
                OldName = "get_sender",
                ParameterCount = 0,
                Because = "0.4.6's conversation held the NPC it belonged to; 0.4.7's holds only its "
                        + "MessageContactInfo (MSGConversation.cs:35, :113-115 on 0.4.7f6) and the NPC holds the "
                        + "conversation instead (NPC.MSGConversation, set in AssignConversationMessage, "
                        + "NPC.cs:270, :629-638). The NPC is the one whose MSGConversation is this; the "
                        + "syncvar-accessor heuristic's get__sender hands back the contact info, which is not "
                        + "what a caller naming NPC get_sender() resolves to",
                Emit = EmitConversationSender,
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.Dialogue.DialogueController",
                OldName = "get_IntObj",
                ParameterCount = 0,
                Because = "renamed in place, and the game says so: [FormerlySerializedAs(\"IntObj\")] private "
                        + "InteractableObject _interactable (DialogueController.cs:82-83 on 0.4.7f6)",
                Emit = (module, controller) => EmitGetterForward(module, controller, "get_IntObj", "_interactable"),
            },

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.Messaging.MSGConversation",
                OldName = "get_messageHistory",
                ParameterCount = 0,
                Because = "the message list is _messageHistory on 0.4.7 (MSGConversation.cs:27, read by every "
                        + "history method); MessageHistory is now a const int, the history limit "
                        + "(MSGConversation.cs:23), which is why the casing rule and the underscore rule "
                        + "disagreed and neither was chosen",
                Emit = (module, conversation) => EmitGetterForward(module, conversation, "get_messageHistory", "_messageHistory"),
            },

            Defaulted("Il2CppScheduleOne.DevUtilities.IconGenerator", "GeneratePackagingIcon",
                      new[] { "System.String", "System.String" }, new object[] { 512 },
                      "0.4.7 gave GeneratePackagingIcon a trailing iconSize defaulting to 512 "
                      + "(IconGenerator.cs:79 on 0.4.7f6)"),

            // Both kept their fade time and gained a completion callback after it; null is what the
            // parameter defaults to, and the body only hands it to the fade coroutine (BlackOverlay.cs
            // Open/Close(float fadeTime = 0.5f, Action onComplete = null) on 0.4.7f6). Listed in
            // GrownOverloads too, so a patch on the old signature moves to the method the game calls.
            Defaulted("Il2CppScheduleOne.UI.BlackOverlay", "Open", new[] { "System.Single" }, new object[] { null },
                      "0.4.7 gave BlackOverlay.Open a completion callback after the fade time, defaulting to null "
                      + "and only passed on to the fade (BlackOverlay.cs on 0.4.7f6)"),
            Defaulted("Il2CppScheduleOne.UI.BlackOverlay", "Close", new[] { "System.Single" }, new object[] { null },
                      "0.4.7 gave BlackOverlay.Close the same completion callback as Open, defaulting to null "
                      + "(BlackOverlay.cs on 0.4.7f6)"),
        };

        private const string Animation = "Il2CppScheduleOne.AvatarFramework.Animation.AvatarAnimation";

        /// <summary>
        /// The hand transforms moved from the animation component up onto the avatar that owns it.
        /// </summary>
        /// <remarks>
        /// Same transforms, not look-alikes: Avatar serialises them and hands each back unchanged
        /// (Avatar.cs:51-60, :101-107 on 0.4.7f6), and AvatarAnimation keeps that avatar in its own field,
        /// taken from the same GameObject in Awake (AvatarAnimation.cs:81, :125).
        /// </remarks>
        private static Bridge HandBridge(string getter) => new Bridge
        {
            Assembly = "Assembly-CSharp",
            DeclaringType = Animation,
            OldName = getter,
            ParameterCount = 0,
            Because = "the hand containers and alignment points moved from AvatarAnimation onto Avatar, which "
                    + "serialises and returns them as they are (Avatar.cs:51-60, :101-107 on 0.4.7f6); "
                    + "AvatarAnimation reaches it through its avatar field (AvatarAnimation.cs:81, :125)",
            Emit = (module, animation) => EmitAvatarForward(module, animation, getter),
        };

        /// <summary>
        /// The old form of a handover method that took the outcome first: calls the six-argument one the
        /// game has now, dropping the outcome.
        /// </summary>
        /// <remarks>
        /// The target is found by a predicate on its name rather than the name itself, because a FishNet RPC
        /// body carries a signature hash that the dropped argument changed. Exactly one six-argument match
        /// taking the item list first, or nothing: choosing between two would be a guess.
        /// </remarks>
        private static MethodDefinition EmitDroppedOutcome(ModuleDefinition module, TypeDefinition customer,
                                                           string oldName, Func<string, bool> isTarget)
        {
            var screen = module.GetType(HandoverScreen);
            if (screen == null) return null;

            MethodDefinition target = null;
            foreach (var candidate in customer.Methods)
            {
                // Six parameters is what rules the stand-in out: it takes seven, and for the server half it
                // shares the target's name, so the name cannot be what excludes it.
                if (!isTarget(candidate.Name) || candidate.Parameters.Count != 6) continue;
                if (!candidate.Parameters[0].ParameterType.Name.StartsWith("List", StringComparison.Ordinal)) continue;
                if (target != null) return null;
                target = candidate;
            }
            if (target == null || target.IsStatic) return null;

            var outcome = OutcomeEnum(module, screen);

            var method = new MethodDefinition(oldName,
                MethodAttributes.Public | MethodAttributes.HideBySig, module.ImportReference(target.ReturnType));
            method.Parameters.Add(new ParameterDefinition("outcome", ParameterAttributes.None, outcome));
            foreach (var parameter in target.Parameters)
                method.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None,
                                                              module.ImportReference(parameter.ParameterType)));

            // this.<target>(items, handoverByPlayer, totalPayment, productList, satisfaction, dealerObject);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 1; i < method.Parameters.Count; i++) il.Emit(OpCodes.Ldarg, method.Parameters[i]);
            il.Emit(target.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, module.ImportReference(target));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>HandoverScreen.CurrentContract</c>: the contract mode's contract, null without one.
        /// </summary>
        /// <summary>
        /// <c>Avatar.RagdollRBs</c>: the active ragdoll's rigidbodies, null while standing.
        /// </summary>
        /// <remarks>
        /// Null, not an empty array, because a caller that reads it while standing is asking for parts that do
        /// not exist on 0.4.7 - Yoink, the one mod measured, ragdolls the NPC first and treats null as "not
        /// grippable". A fresh Ragdoll per knock-down means an array read before an NPC stands up and goes
        /// down again is that earlier ragdoll's, which the permanent 0.4.6 field never was.
        /// </remarks>
        private static MethodDefinition EmitRagdollRigidbodies(ModuleDefinition module, TypeDefinition avatar)
        {
            var getRagdoll = Getter(avatar, "ActiveRagdoll");
            var ragdoll = getRagdoll?.ReturnType?.Resolve();
            var getBodies = Getter(ragdoll, "Rigidbodies");
            if (getRagdoll == null || getBodies == null) return null;

            var method = new MethodDefinition("get_RagdollRBs",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(getBodies.ReturnType));

            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Call, getBodies);

            // var r = ActiveRagdoll; return r == null ? null : r.Rigidbodies;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getRagdoll);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>NPC.SendTextMessage(text)</c>: the NPC's conversation sends it as the NPC, as 0.4.7's own texts do.
        /// </summary>
        /// <remarks>
        /// Nothing when the NPC has no conversation yet - 0.4.7 assigns one once the local player exists
        /// (NPC.CreateMessageConversationWhenLocalPlayerExists), and a text sent before that has nowhere to go.
        /// </remarks>
        private static MethodDefinition EmitSendTextMessage(ModuleDefinition module, TypeDefinition npc)
        {
            var getConversation = Getter(npc, "MSGConversation");
            var conversation = getConversation?.ReturnType?.Resolve();
            var send = Method(conversation, "SendMessage", 3);
            var message = send?.Parameters[0].ParameterType.Resolve();
            MethodDefinition create = null;
            if (message != null)
                foreach (var candidate in message.Methods)
                    if (candidate.IsConstructor && !candidate.IsStatic && candidate.Parameters.Count == 4
                        && candidate.Parameters[0].ParameterType.MetadataType == MetadataType.String)
                    { create = candidate; break; }
            if (getConversation == null || send == null || create == null) return null;

            var method = new MethodDefinition("SendTextMessage",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            var text = new ParameterDefinition("text", ParameterAttributes.None, module.TypeSystem.String);
            method.Parameters.Add(text);

            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Ldarg, text);

            // var c = MSGConversation; if (c == null) return;
            // c.SendMessage(new Message(text, ESenderType.Other, endOfGroup: false, messageId: -1), true, true);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getConversation);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Ldc_I4_1);                                   // ESenderType.Other
            il.Emit(OpCodes.Ldc_I4_0);                                   // _endOfGroup: false
            il.Emit(OpCodes.Ldc_I4_M1);                                  // _messageId: -1
            il.Emit(OpCodes.Newobj, create);
            il.Emit(OpCodes.Ldc_I4_1);                                   // notify: true
            il.Emit(OpCodes.Ldc_I4_1);                                   // network: true
            il.Emit(send.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, send);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>A getter under its old name that returns the one it was renamed to, unchanged.</summary>
        private static MethodDefinition EmitGetterForward(ModuleDefinition module, TypeDefinition type,
                                                          string oldName, string member)
        {
            var target = Getter(type, member);
            if (target == null) return null;
            var method = new MethodDefinition(oldName,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName
                    | (target.IsStatic ? MethodAttributes.Static : 0),
                module.ImportReference(target.ReturnType));
            var il = method.Body.GetILProcessor();
            if (!target.IsStatic) il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>MSGConversation.sender</c>: the registered NPC whose conversation this is, or null.
        /// </summary>
        /// <remarks>
        /// Compared by native pointer, because two interop shells around the same conversation are two managed
        /// objects. A walk over NPCManager.NPCRegistry per call - a hundred-odd NPCs, and the mods measured ask
        /// once per message, not per frame. Null for a conversation no NPC holds (a player-made one), which is
        /// the honest answer; 0.4.6 had no such conversations to ask about.
        /// </remarks>
        private static MethodDefinition EmitConversationSender(ModuleDefinition module, TypeDefinition conversation)
        {
            var manager = module.GetType("Il2CppScheduleOne.NPCs.NPCManager");
            var getRegistry = Getter(manager, "NPCRegistry");
            var registry = getRegistry?.ReturnType as GenericInstanceType;
            var list = registry?.ElementType.Resolve();
            var npc = registry?.GenericArguments.Count == 1 ? registry.GenericArguments[0].Resolve() : null;
            var getCount = Getter(list, "Count");
            var getItem = Method(list, "get_Item", 1);
            var getConversation = Getter(npc, "MSGConversation");
            var pointer = Polyfill.Core.ShadowTypes.PointerGetter(conversation);
            if (getRegistry == null || !getRegistry.IsStatic || getCount == null || getItem == null
                || getConversation == null || pointer == null) return null;

            var method = new MethodDefinition("get_sender",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(registry.GenericArguments[0]));
            var body = method.Body;
            var listLocal = new VariableDefinition(module.ImportReference(registry));
            var me = new VariableDefinition(module.TypeSystem.IntPtr);
            var i = new VariableDefinition(module.TypeSystem.Int32);
            var count = new VariableDefinition(module.TypeSystem.Int32);
            var candidate = new VariableDefinition(module.ImportReference(registry.GenericArguments[0]));
            foreach (var v in new[] { listLocal, me, i, count, candidate }) body.Variables.Add(v);
            body.InitLocals = true;

            var countRef = module.ImportReference(Against(module, getCount, registry));
            var itemRef = module.ImportReference(Against(module, getItem, registry));
            var il = body.GetILProcessor();

            var none = il.Create(OpCodes.Ldnull);
            var test = il.Create(OpCodes.Ldloc, i);
            var next = il.Create(OpCodes.Ldloc, i);

            // var me = this.Pointer; var list = NPCManager.NPCRegistry; if (list == null) return null;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(pointer));
            il.Emit(OpCodes.Stloc, me);
            il.Emit(OpCodes.Call, getRegistry);
            il.Emit(OpCodes.Stloc, listLocal);
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Brfalse, none);

            // for (int i = 0, count = list.Count; i < count; i++)
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Callvirt, countRef);
            il.Emit(OpCodes.Stloc, count);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stloc, i);
            il.Emit(OpCodes.Br, test);

            // var c = list[i]; if (c != null && c.MSGConversation != null && c.MSGConversation.Pointer == me) return c;
            var body0 = il.Create(OpCodes.Ldloc, listLocal);
            il.Append(body0);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Callvirt, itemRef);
            il.Emit(OpCodes.Stloc, candidate);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Call, getConversation);
            il.Emit(OpCodes.Dup);
            var drop = il.Create(OpCodes.Pop);
            il.Emit(OpCodes.Brfalse, drop);
            il.Emit(OpCodes.Call, module.ImportReference(pointer));
            il.Emit(OpCodes.Ldloc, me);
            il.Emit(OpCodes.Ceq);                                        // IntPtr is a native int: no call needed
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc, candidate);
            il.Emit(OpCodes.Ret);
            il.Append(drop);
            il.Emit(OpCodes.Br, next);

            // i++
            il.Append(next);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, i);
            il.Append(test);
            il.Emit(OpCodes.Ldloc, count);
            il.Emit(OpCodes.Blt, body0);

            il.Append(none);
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static MethodDefinition EmitCurrentContract(ModuleDefinition module, TypeDefinition screen)
        {
            var getMode = Getter(screen, "_contractMode");
            var mode = getMode?.ReturnType?.Resolve();
            var getContract = Getter(mode, "CurrentContract");
            if (getMode == null || getContract == null) return null;

            var method = new MethodDefinition("get_CurrentContract",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(getContract.ReturnType));

            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Call, getContract);

            // var m = _contractMode; return m == null ? null : m.CurrentContract;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getMode);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>NPCMovement.CanMove()</c>, the method: the property that replaced it.
        /// </summary>
        private static MethodDefinition EmitCanMoveMethod(ModuleDefinition module, TypeDefinition movement)
        {
            var getCanMove = Getter(movement, "CanMove");
            if (getCanMove == null) return null;

            var method = new MethodDefinition("CanMove",
                MethodAttributes.Public | MethodAttributes.HideBySig,
                module.TypeSystem.Boolean);

            // return this.CanMove;
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getCanMove);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>AvatarAnimation.get_X()</c>: the same getter on the avatar.
        /// </summary>
        /// <remarks>
        /// Null before the avatar is known, which is what the old serialised field read as on a component
        /// nobody had wired up - a getter that throws there would break the mod on the one frame where the
        /// old code simply read nothing.
        /// </remarks>
        private static MethodDefinition EmitAvatarForward(ModuleDefinition module, TypeDefinition animation,
                                                          string getter)
        {
            var getAvatar = Getter(animation, "avatar");
            var avatar = getAvatar?.ReturnType?.Resolve();
            var target = Method(avatar, getter, 0);
            if (getAvatar == null || target == null) return null;

            var method = new MethodDefinition(getter,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(target.ReturnType));

            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Call, target);

            // var a = avatar; return a == null ? null : a.X;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getAvatar);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Ret);
            return method;
        }

        private const string SleepControllerType = "Il2CppScheduleOne.GameTime.SleepController";

        private const string SleepMoved = "0.4.6 raised the sleep hooks on TimeManager (TimeManager.cs:65-67, "
            + "104, 802-840 on 0.4.6f13); 0.4.7 raises them on SleepController at the same points "
            + "(SleepController.cs:60, 95-97, 217-280 on 0.4.7f6)";

        private static Bridge OnSleepController(string oldName, int arity, string target) => new Bridge
        {
            Assembly = "Assembly-CSharp",
            DeclaringType = "Il2CppScheduleOne.GameTime.TimeManager",
            OldName = oldName,
            ParameterCount = arity,
            Because = SleepMoved,
            Emit = (module, clock) => EmitOnSleepController(module, clock, oldName, arity, target),
        };

        /// <summary>
        /// A TimeManager accessor that reads or writes the same member on the SleepController instance.
        /// </summary>
        /// <remarks>
        /// With no SleepController yet (the menu), a getter answers the default and a setter drops the
        /// value - which is what the old field held before a save loaded, since TimeManager clears both
        /// hooks on its own start (TimeManager.cs:179-180 on 0.4.6f13).
        /// </remarks>
        private static MethodDefinition EmitOnSleepController(ModuleDefinition module, TypeDefinition clock,
                                                              string oldName, int arity, string targetName)
        {
            var sleep = module.GetType(SleepControllerType);
            var target = Method(sleep, targetName, arity);
            var open = module.GetType("Il2CppScheduleOne.DevUtilities.NetworkSingleton`1");
            var getInstance = Method(open, "get_Instance", 0);
            if (target == null || getInstance == null || !getInstance.IsStatic) return null;

            var owner = new GenericInstanceType(module.ImportReference(open));
            owner.GenericArguments.Add(module.ImportReference(sleep));
            var instance = Against(module, getInstance, owner);

            var returns = arity == 0 ? module.ImportReference(target.ReturnType) : module.TypeSystem.Void;
            var method = new MethodDefinition(oldName,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, returns);
            if (arity == 1)
                method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None,
                                                              module.ImportReference(target.Parameters[0].ParameterType)));

            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Nop);
            il.Emit(OpCodes.Call, module.ImportReference(instance));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            if (arity == 0) EmitDefault(method, il, returns);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            if (arity == 1) il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(target));
            il.Emit(OpCodes.Ret);

            // The old member was a field or a property; either way reflection looks for a property now.
            string name = oldName.Substring(4);
            PropertyDefinition property = null;
            foreach (var existing in clock.Properties)
                if (existing.Name == name) property = existing;
            if (property == null)
            {
                property = new PropertyDefinition(name, PropertyAttributes.None,
                                                  arity == 0 ? returns : method.Parameters[0].ParameterType);
                clock.Properties.Add(property);
            }
            if (arity == 0) property.GetMethod = method;
            else property.SetMethod = method;
            return method;
        }

        /// <summary>
        /// <c>new MSGConversation(npc, contactName)</c>, built the way 0.4.7 builds an NPC's conversation.
        /// </summary>
        /// <remarks>
        /// The contact is the NPC's own (name, id, mugshot, the two messaging flags), with the name the old
        /// constructor was given, which is what the list and the notifications showed in 0.4.6. The id is
        /// the one the game gives its own NPC conversations, <c>"messageconversation_" + ObjectId</c>, and
        /// that is deliberate: messages travel between peers by this id, and the object id is the part every
        /// peer agrees on. It also keeps 0.4.6's rule of one conversation per NPC - a second one for the
        /// same NPC is refused by the registry with an error, exactly as the NPC-keyed map refused it.
        /// </remarks>
        private static MethodDefinition EmitConversationForNpc(ModuleDefinition module, TypeDefinition conversation)
        {
            var npc = module.GetType("Il2CppScheduleOne.NPCs.NPC");
            var contact = module.GetType("Il2CppScheduleOne.Messaging.MessageContactInfo");
            var getData = Getter(npc, "NPCData");
            MethodDefinition fromData = null, target = null;
            foreach (var candidate in contact?.Methods ?? new Mono.Collections.Generic.Collection<MethodDefinition>())
                if (candidate.IsConstructor && candidate.Parameters.Count == 1
                    && candidate.Parameters[0].ParameterType.FullName == getData?.ReturnType.FullName)
                    fromData = candidate;
            foreach (var candidate in conversation.Methods)
                if (candidate.IsConstructor && candidate.Parameters.Count == 2
                    && candidate.Parameters[0].ParameterType.FullName == contact?.FullName)
                    target = candidate;
            var setName = Method(contact, "set__name", 1);
            if (npc == null || getData == null || fromData == null || target == null || setName == null) return null;

            // NPC.NetworkObject.ObjectId, named against the FishNet references this module already carries.
            TypeReference behaviour = null, networkObject = null;
            foreach (var reference in module.GetTypeReferences())
            {
                if (reference.FullName == "Il2CppFishNet.Object.NetworkBehaviour") behaviour = reference;
                if (reference.FullName == "Il2CppFishNet.Object.NetworkObject") networkObject = reference;
            }
            if (behaviour == null || networkObject == null) return null;
            var getNetworkObject = new MethodReference("get_NetworkObject", networkObject, behaviour) { HasThis = true };
            var getObjectId = new MethodReference("get_ObjectId", module.TypeSystem.Int32, networkObject) { HasThis = true };
            var toText = new MethodReference("ToString", module.TypeSystem.String, module.TypeSystem.Int32) { HasThis = true };
            var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String);
            concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

            var method = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("_npc", ParameterAttributes.None, module.ImportReference(npc)));
            method.Parameters.Add(new ParameterDefinition("_contactName", ParameterAttributes.None, module.TypeSystem.String));
            var id = new VariableDefinition(module.TypeSystem.Int32);
            method.Body.Variables.Add(id);
            method.Body.InitLocals = true;

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            // var info = new MessageContactInfo(npc.NPCData); info._name = contactName;
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(getData));
            il.Emit(OpCodes.Newobj, module.ImportReference(fromData));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, module.ImportReference(setName));
            // "messageconversation_" + npc.NetworkObject.ObjectId
            il.Emit(OpCodes.Ldstr, "messageconversation_");
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(getNetworkObject));
            il.Emit(OpCodes.Callvirt, module.ImportReference(getObjectId));
            il.Emit(OpCodes.Stloc, id);
            il.Emit(OpCodes.Ldloca_S, id);
            il.Emit(OpCodes.Call, module.ImportReference(toText));
            il.Emit(OpCodes.Call, module.ImportReference(concat));
            // : this(info, id)
            il.Emit(OpCodes.Call, module.ImportReference(target));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary><c>NPCManager.NPCContainer</c>: null, which every caller already had to expect.</summary>
        /// <remarks>
        /// The field was scene wiring and could be unset; S1API, the one reported caller, checks for null
        /// and then leaves the NPC where it is - which is what 0.4.7 does with its own NPCs.
        /// </remarks>
        private static MethodDefinition EmitNoNpcContainer(ModuleDefinition module, TypeDefinition manager)
        {
            // The Transform reference this module already carries, so it points at the right assembly.
            TypeReference transform = null;
            foreach (var reference in module.GetTypeReferences())
                if (reference.FullName == "UnityEngine.Transform") { transform = reference; break; }
            if (transform == null) return null;

            var method = new MethodDefinition("get_NPCContainer",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, transform);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            manager.Properties.Add(new PropertyDefinition("NPCContainer", PropertyAttributes.None,
                                                          method.ReturnType) { GetMethod = method });
            return method;
        }

        private const string AvatarType = "Il2CppScheduleOne.AvatarFramework.Avatar";
        private const string AvatarSettingsType = "Il2CppScheduleOne.AvatarFramework.AvatarSettings";

        private const string AvatarSettingsGone = "0.4.6 applied a look from one AvatarSettings and kept it as "
            + "CurrentSettings (Avatar.cs:328-354 on 0.4.6f13); 0.4.7 applies a NakedAppearance and an outfit "
            + "through Avatar.Appearance (AvatarAppearance.cs:96-147 on 0.4.7f6), and Polyfill translates one "
            + "into the other";

        /// <summary><c>Avatar.CurrentSettings</c>, answering nothing until the Report half answers for it.</summary>
        /// <remarks>
        /// Gets its PropertyDefinition, so a mod that looks the property up by name finds it as well.
        /// </remarks>
        private static MethodDefinition EmitCurrentSettingsStandIn(ModuleDefinition module, TypeDefinition avatar)
        {
            var settings = module.GetType(AvatarSettingsType);
            if (settings == null) return null;
            foreach (var property in avatar.Properties)
                if (property.Name == "CurrentSettings") return null;

            var getter = new MethodDefinition("get_CurrentSettings",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(settings));
            var il = getter.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            avatar.Properties.Add(new PropertyDefinition("CurrentSettings", PropertyAttributes.None,
                                                         module.ImportReference(settings)) { GetMethod = getter });
            return getter;
        }

        /// <summary>The NPC data's <c>AvatarSettings</c> setter, with no body of its own.</summary>
        private static MethodDefinition EmitDataAvatarSettingsStandIn(ModuleDefinition module, TypeDefinition appearance)
        {
            var settings = module.GetType(AvatarSettingsType);
            if (settings == null) return null;
            foreach (var property in appearance.Properties)
                if (property.Name == "AvatarSettings") return null;

            var setter = new MethodDefinition("set_AvatarSettings",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.TypeSystem.Void);
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None,
                                                          module.ImportReference(settings)));
            setter.Body.GetILProcessor().Emit(OpCodes.Ret);

            appearance.Properties.Add(new PropertyDefinition("AvatarSettings", PropertyAttributes.None,
                                                             module.ImportReference(settings)) { SetMethod = setter });
            return setter;
        }

        /// <summary><c>Avatar.LoadAvatarSettings(settings)</c>, with no body of its own.</summary>
        private static MethodDefinition EmitLoadAvatarSettingsStandIn(ModuleDefinition module, TypeDefinition avatar)
        {
            var settings = module.GetType(AvatarSettingsType);
            if (settings == null) return null;

            var method = new MethodDefinition("LoadAvatarSettings",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("settings", ParameterAttributes.None,
                                                          module.ImportReference(settings)));
            method.Body.GetILProcessor().Emit(OpCodes.Ret);
            return method;
        }

        private const string PlayerManager = "Il2CppScheduleOne.PlayerScripts.PlayerManager";

        /// <summary>
        /// <c>Player.Load(data, containerPath)</c>, with no body.
        /// </summary>
        /// <remarks>
        /// Nothing in 0.4.7 calls it and nothing it did can be done from here: positioning, inventory,
        /// appearance, clothing and variables all arrive through SetPlayerData_Client now. It is a target
        /// for a patch to bind to, and PlayerLoadRelay is what makes that patch run.
        /// </remarks>
        private static MethodDefinition EmitPlayerLoadStandIn(ModuleDefinition module, TypeDefinition player)
        {
            var data = module.GetType("Il2CppScheduleOne.Persistence.Datas.PlayerData");
            if (data == null) return null;

            var method = new MethodDefinition("Load",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("data", ParameterAttributes.None, data));
            method.Parameters.Add(new ParameterDefinition("containerPath", ParameterAttributes.None,
                                                          module.TypeSystem.String));
            method.Body.GetILProcessor().Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>PlayerManager.TryGetPlayerData(code, out data, out inventory, out appearance, out clothing,
        /// out variables)</c>, answered from the 0.4.7 lookup.
        /// </summary>
        /// <remarks>
        /// Asked as a joining player, which is what the old method was for (Player.cs:2698 on 0.4.6f13).
        /// The appearance comes back empty: 0.4.7 reads it into a PlayerAppearance object and no longer
        /// holds the JSON the old out value carried.
        /// </remarks>
        private static MethodDefinition EmitTryGetPlayerDataStandIn(ModuleDefinition module, TypeDefinition manager)
        {
            MethodDefinition lookup = null;
            foreach (var candidate in manager.Methods)
                if (candidate.Name == "TryGetPlayerData" && candidate.Parameters.Count == 3
                    && candidate.Parameters[2].ParameterType.IsByReference)
                    lookup = candidate;
            if (lookup == null) return null;

            var full = ((ByReferenceType)lookup.Parameters[2].ParameterType).ElementType.Resolve();
            var getBasic = Getter(full, "BasicData");
            var getInventory = Getter(full, "InventoryString");
            var getClothing = Getter(full, "ClothingString");
            var getVariables = Getter(full, "Variables");
            if (full == null || getBasic == null || getInventory == null || getClothing == null || getVariables == null)
                return null;

            var method = new MethodDefinition("TryGetPlayerData",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Boolean);
            method.Parameters.Add(new ParameterDefinition("playerCode", ParameterAttributes.None, module.TypeSystem.String));
            void Out(string name, TypeReference type)
                => method.Parameters.Add(new ParameterDefinition(name, ParameterAttributes.Out,
                                                                 new ByReferenceType(module.ImportReference(type))));
            Out("data", getBasic.ReturnType);
            Out("inventoryString", module.TypeSystem.String);
            Out("appearanceString", module.TypeSystem.String);
            Out("clothingString", module.TypeSystem.String);
            Out("variables", getVariables.ReturnType);

            var slot = new VariableDefinition(module.ImportReference(full));
            var found = new VariableDefinition(module.TypeSystem.Boolean);
            method.Body.Variables.Add(slot);
            method.Body.Variables.Add(found);
            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();

            // found = this.TryGetPlayerData(playerCode, false, out slot);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloca_S, slot);
            il.Emit(OpCodes.Call, module.ImportReference(lookup));
            il.Emit(OpCodes.Stloc, found);

            // every out value first gets the answer the old method gave for "no data"
            il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stind_Ref);
            foreach (int index in new[] { 3, 4, 5 })
            { il.Emit(OpCodes.Ldarg, method.Parameters[index - 1]); il.Emit(OpCodes.Ldstr, ""); il.Emit(OpCodes.Stind_Ref); }
            il.Emit(OpCodes.Ldarg, method.Parameters[5]); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stind_Ref);

            // and then, when there was data, what the bundle holds
            var done = il.Create(OpCodes.Ldloc, found);
            il.Emit(OpCodes.Ldloc, slot);
            il.Emit(OpCodes.Brfalse_S, done);
            void Unpack(int argument, MethodDefinition getter)
            {
                il.Emit(OpCodes.Ldarg, method.Parameters[argument - 1]);
                il.Emit(OpCodes.Ldloc, slot);
                il.Emit(OpCodes.Call, module.ImportReference(getter));
                il.Emit(OpCodes.Stind_Ref);
            }
            Unpack(2, getBasic);
            Unpack(3, getInventory);
            Unpack(5, getClothing);
            Unpack(6, getVariables);
            il.Append(done);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>HandoverScreen.ClearCustomerSlots(returnToOriginals)</c>: return or destroy, by the flag.
        /// </summary>
        /// <remarks>
        /// The old method also refreshed the screen once at the end, with the per-slot events held off while
        /// it cleared. 0.4.7 has no batch refresh to call: every customer slot reports its own change
        /// (HandoverScreen.cs:124-126), so the screen hears about each item as it goes, and ends in the
        /// same state.
        /// </remarks>
        private static MethodDefinition EmitClearCustomerSlots(ModuleDefinition module, TypeDefinition screen)
        {
            var giveBack = Method(screen, "ReturnCustomerItems", 0);
            var destroy = Method(screen, "DestroyCustomerItems", 0);
            if (giveBack == null || destroy == null) return null;

            var method = new MethodDefinition("ClearCustomerSlots",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("returnToOriginals", ParameterAttributes.None,
                                                          module.TypeSystem.Boolean));

            // if (returnToOriginals) ReturnCustomerItems(); else DestroyCustomerItems();
            var il = method.Body.GetILProcessor();
            var otherwise = il.Create(OpCodes.Ldarg_0);
            var done = il.Create(OpCodes.Ret);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse_S, otherwise);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(giveBack));
            il.Emit(OpCodes.Br_S, done);
            il.Append(otherwise);
            il.Emit(OpCodes.Call, module.ImportReference(destroy));
            il.Append(done);
            return method;
        }

        /// <summary>
        /// Types 0.4.7 renamed outright, which no name match can follow.
        /// </summary>
        /// <remarks>
        /// <c>DialogueContainer</c> is the dialogue graph asset: a ScriptableObject holding the node, branch
        /// and link lists and the lookups over them. 0.4.7 calls it <c>Conversation</c>, in the same
        /// namespace, with the same three lists, the same allowExit pair and the same six methods in the
        /// same order (DialogueContainer.cs:9-60 on 0.4.6f13, Conversation.cs:9-60 on 0.4.7f6). The game's own
        /// log line still calls it by the old name (DialogueHandler.cs:181).
        /// </remarks>
        internal override IEnumerable<TypeRename> DeclareRenames() => new[]
        {
            new TypeRename
            {
                Assembly = "Assembly-CSharp",
                OldFullName = "Il2CppScheduleOne.Dialogue.DialogueContainer",
                NewFullName = "Il2CppScheduleOne.Dialogue.Conversation",
                Because = "the dialogue graph asset was renamed in place: 0.4.6f13 DialogueContainer and 0.4.7f6 "
                        + "Conversation are the same ScriptableObject with the same members in the same order",
            },
        };

        private const string Customer = "Il2CppScheduleOne.Economy.Customer";
        private const string HandoverScreen = "Il2CppScheduleOne.UI.Handover.HandoverScreen";
        private const string HandoverOutcome = HandoverScreen + "/EHandoverOutcome";

        /// <summary>
        /// <c>Customer.ProcessHandover(outcome, contract, items, handoverByPlayer, giveBonuses)</c>, with the
        /// outcome dropped.
        /// </summary>
        /// <remarks>
        /// 0.4.7 took the result enum off the handover screen, because the screen now has one callback for
        /// submitting and another for cancelling. A cancelled handover never reached ProcessHandover even in
        /// 0.4.6 - the game only ever passed Finalize - so the four-argument method is the old one with
        /// Finalize. What a mod could have passed differently is Cancelled, and all that changed in 0.4.6
        /// was the deal popup; that one difference is not reproduced.
        ///
        /// The enum is put back beside it, with the values it had (Cancelled 0, Finalize 1), so the
        /// argument can be named at all. A mod holding it in a patch signature or a local needs the type
        /// to load before any of its methods compile.
        /// </remarks>
        private static MethodDefinition EmitProcessHandover(ModuleDefinition module, TypeDefinition customer)
        {
            var screen = module.GetType(HandoverScreen);
            if (screen == null) return null;

            MethodDefinition target = null;
            foreach (var candidate in customer.Methods)
            {
                if (candidate.Name != "ProcessHandover" || candidate.Parameters.Count != 4) continue;
                if (target != null) return null;
                target = candidate;
            }
            if (target == null) return null;

            var outcome = OutcomeEnum(module, screen);

            var method = new MethodDefinition("ProcessHandover",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("outcome", ParameterAttributes.None, outcome));
            foreach (var parameter in target.Parameters)
                method.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None,
                                                              module.ImportReference(parameter.ParameterType)));

            // this.ProcessHandover(contract, items, handoverByPlayer, giveBonuses);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 2; i <= 5; i++) il.Emit(OpCodes.Ldarg, method.Parameters[i - 1]);
            il.Emit(OpCodes.Callvirt, module.ImportReference(target));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary><c>HandoverScreen.EHandoverOutcome</c>, as 0.4.6 had it, made once per module.</summary>
        private static TypeDefinition OutcomeEnum(ModuleDefinition module, TypeDefinition screen)
        {
            foreach (var nested in screen.NestedTypes)
                if (nested.Name == "EHandoverOutcome") return nested;

            var outcome = new TypeDefinition("", "EHandoverOutcome",
                TypeAttributes.NestedPublic | TypeAttributes.Sealed,
                new TypeReference("System", "Enum", module, module.TypeSystem.CoreLibrary));
            outcome.Fields.Add(new FieldDefinition("value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
                module.TypeSystem.Int32));

            string[] names = { "Cancelled", "Finalize" };
            for (int value = 0; value < names.Length; value++)
                outcome.Fields.Add(new FieldDefinition(names[value],
                    FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal
                        | FieldAttributes.HasDefault, outcome) { Constant = value });

            screen.NestedTypes.Add(outcome);
            return outcome;
        }

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
