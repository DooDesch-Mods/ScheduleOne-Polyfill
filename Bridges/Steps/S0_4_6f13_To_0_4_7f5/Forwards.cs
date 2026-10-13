using Mono.Cecil;
using Mono.Cecil.Cil;
using static Polyfill.Bridges.Shapes;

namespace Polyfill.Bridges.Steps.S0_4_6f13_To_0_4_7f5
{
    /// <summary>
    /// 0.4.7 members that moved onto another member doing the same job, or that 0.4.7 has nothing behind.
    /// </summary>
    /// <remarks>
    /// Each one either forwards to the 0.4.7 member that took over the job, or - where 0.4.7 has nothing to do
    /// the job with - answers the empty value every caller already checks for. The reason for each is in its
    /// <c>Because</c>.
    /// </remarks>
    internal sealed class Forwards : BridgeSet
    {
        internal override string Step => "0.4.6f13 -> 0.4.7f5 (forwards)";
        internal override string From => "0.4.7f5";
        internal override string VerifiedTo => "0.4.7f6";

        internal const string Movement = "Il2CppScheduleOne.NPCs.NPCMovement";
        internal const string AvatarType = "Il2CppScheduleOne.AvatarFramework.Avatar";
        internal const string Animation = "Il2CppScheduleOne.AvatarFramework.Animation.AvatarAnimation";
        private const string Impostor = "Il2CppScheduleOne.Avatar.Impostors.AvatarImpostor";
        private const string Clothing = "Il2CppScheduleOne.Clothing.ClothingDefinition";
        private const string ApplicationType = "Il2CppScheduleOne.Clothing.EClothingApplicationType";
        private const string OldBelt = "Il2CppScheduleOne.AvatarFramework.PoliceBelt";

        internal const string SpeedStack = "0.4.7 replaced NPCMovement's walk/run speeds and pause flag with "
            + "NPCSpeedController: the highest-priority speed control sets the speed, normalized ones as a "
            + "fraction of _maxSpeed, times the multiplier (NPCSpeedController.cs RecalculateMoveSpeed on "
            + "0.4.7f6), and every change is pushed to the NavMeshAgent (NPCMovement.cs:620, :1593)";

        internal const string Culling = "0.4.7 moved culling onto Avatar: every 0.1s InvokeRepeating(\"UpdateAnimationActive\") "
            + "decides it from the player camera and sets IsCulled, hiding BodyContainer and showing the impostor "
            + "(Avatar.cs:128, :220-250 on 0.4.7f6); AvatarAnimation has no AllowCulling of its own";

        internal const string Layers = "0.4.7 dresses avatars in prefab AvatarObjects through Avatar.Appearance and has "
            + "no texture body layers, clothing paths or combined layer (AvatarAppearance.cs on 0.4.7f6); the member "
            + "is put back empty";

        private const string Belt = "0.4.7's officer keeps a ScheduleOne.Law.PoliceBelt in PoliceOfficer.PoliceBelt, "
            + "found in Awake (PoliceOfficer.cs:128, :161 on 0.4.7f6); the AvatarFramework.PoliceBelt the old field "
            + "held is no longer on an officer";

        private const string PlayerType = "Il2CppScheduleOne.PlayerScripts.Player";
        private const string ParkingSpot = "Il2CppScheduleOne.Map.ParkingSpot";
        private const string TrashItem = "Il2CppScheduleOne.Trash.TrashItem";

        private const string CanGoInContainer = "renamed: 0.4.7's TrashItem keeps the flag in "
            + "_canGoInTrashContainer, serialized under the old name, and reads it where the bag, the bin and "
            + "the grabber read the field (TrashItem.cs:52, :164, :185 on 0.4.6f13; :33-34, :71-73 on 0.4.7f6)";

        private const string DraggableType = "Il2CppScheduleOne.Dragging.Draggable";

        private const string Interactable = "renamed: 0.4.6's draggable held its InteractableObject in IntObj "
            + "(Draggable.cs:34, :84-87 on 0.4.6f13); 0.4.7 keeps it in _interactable and fills it itself in Awake "
            + "when it is empty (Draggable.cs:30, :88-102 on 0.4.7f7)";

        private const string CentreOfMass = "0.4.6's draggable read CreateCoM once, in Awake, to add a CenterOfMass "
            + "child it was then dragged by (Draggable.cs:39, :89-96 on 0.4.6f13); 0.4.7's draggable adds no such "
            + "child and is dragged by its own position or its serialized DragOrigin (Draggable.cs:34, :88-108, "
            + ":159-163 on 0.4.7f7), so the flag reads false and a value written to it has no reader";

        private const string Grabber = "Il2CppScheduleOne.Equipping.Equippable_TrashGrabber";

        private const string EquippedGrabber = "0.4.6's grabber kept the equipped one in a static Instance, set in "
            + "Equip, and IsEquipped was Instance != null (Equippable_TrashGrabber.cs:47-49, :62 on 0.4.6f13); 0.4.7 "
            + "has no static and asks the local inventory: isAnythingEquipped and Equippable is "
            + "Equippable_TrashGrabber (TrashItemDraggable.cs:65-78 on 0.4.7f6)";

        private const string ParentLot = "renamed: 0.4.7's ParkingSpot keeps its lot in _parentParentLot, read and "
            + "filled by the same Awake and Init that used ParentLot (ParkingSpot.cs:8, :20-38 on 0.4.6f13; "
            + ":12, :21-39 on 0.4.7f6)";

        internal override IEnumerable<Bridge> Declare() => new[]
        {
            // ---- NPC movement speeds ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Movement, OldName = "get_WalkSpeed", ParameterCount = 0,
                Because = SpeedStack + "; the walk speed is NPCData.Movement.WalkSpeed, which NPCMovement "
                        + "exposes as DefaultWalkSpeed (NPCMovement.cs:205)",
                Emit = EmitWalkSpeed,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Movement, OldName = "get_RunSpeed", ParameterCount = 0,
                Because = SpeedStack + "; the top speed is the controller's _maxSpeed (NPCData.Movement.MaxSpeed, "
                        + "7 by default beside a 1.8 walk), which a normalized control of 1 runs at",
                Emit = EmitRunSpeed,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Movement, OldName = "UpdateSpeed", ParameterCount = 0,
                Because = SpeedStack + "; the controller recalculates on every change, so updating the speed is "
                        + "re-applying the current multiplier, which recalculates and pushes it to the agent",
                Emit = EmitUpdateSpeed,
            },

            // ---- behaviours and messaging ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.NPCs.Behaviour.NPCBehaviour",
                OldName = "ConsumeProduct", ParameterCount = 2,
                Because = "renamed: 0.4.7 has ConsumeProduct_Server(product, removeFromInventory) with the same "
                        + "arguments (NPCBehaviour.cs:163 on 0.4.7f6)",
                Emit = EmitConsumeProduct,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Messaging.MSGConversation",
                OldName = "set_contactName", ParameterCount = 1,
                Because = "0.4.7 reads a conversation's name from its MessageContactInfo (ContactName => _sender.Name, "
                        + "MSGConversation.cs:35, :79 on 0.4.7f6); renaming is writing _name into a copy of _sender and "
                        + "storing the copy back",
                Emit = EmitSetContactName,
            },

            // ---- avatar culling and impostors (NetEye's security cameras) ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Animation, OldName = "get_IsAvatarCulled", ParameterCount = 0,
                Because = Culling + "; culled is Avatar.IsCulled",
                Emit = EmitIsAvatarCulled,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Impostor, OldName = "DisableImpostor", ParameterCount = 0,
                Because = "0.4.7's impostor is Enable()/Disable() (AvatarImpostor.cs on 0.4.7f6)",
                Emit = (module, impostor) => EmitForward(module, impostor, "DisableImpostor", "Disable"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Impostor, OldName = "get_HasTexture", ParameterCount = 0,
                Because = "0.4.7's impostor records whether a texture was set in _impostorSet (SetTexture, "
                        + "AvatarImpostor.cs on 0.4.7f6)",
                Emit = (module, impostor) => EmitGetterRename(module, impostor, "get_HasTexture", "_impostorSet"),
            },
            NullGetter(AvatarType, "get_BodyMeshes", ReferenceArrayOf("UnityEngine.CoreModule", "UnityEngine", "SkinnedMeshRenderer")),
            NullGetter(AvatarType, "get_ShapeKeyMeshes", ReferenceArrayOf("UnityEngine.CoreModule", "UnityEngine", "SkinnedMeshRenderer")),
            NullGetter(AvatarType, "get_FaceMesh", module => TypeIn(module, "UnityEngine.CoreModule", "UnityEngine", "SkinnedMeshRenderer")),

            // ---- clothing and the police belt (Police Response Overhaul) ----
            // 0.4.6 dressed avatars in texture layers - a clothing item named a texture by path and an
            // application type. 0.4.7 dresses them in prefab AvatarObjects through Avatar.Appearance
            // (ClothingDefinition.ClothingAvatarObject, AvatarAppearance.cs on 0.4.7f6), so no path is left to
            // answer. Every caller in the mod checks the path for null before painting.
            Empty(Clothing, "get_ClothingAssetPath", 0, m => m.TypeSystem.String, Layers),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Clothing, OldName = "get_ApplicationType", ParameterCount = 0,
                Creates = ApplicationType,
                Because = Layers + "; the enum is put back so the member can name it, and every item answers "
                        + "BodyLayer (0), the one kind a caller cannot paint without a path",
                Emit = (module, clothing) =>
                {
                    var type = ApplicationTypeEnum(module);
                    var method = new MethodDefinition("get_ApplicationType",
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, type);
                    var il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ret);
                    return AsProperty(clothing, method);
                },
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.PlayerScripts.PlayerClothing",
                OldName = "InsertClothing", ParameterCount = 1,
                Because = "renamed: 0.4.7's PlayerClothing.InsertClothingItem(ClothingInstance) puts an item into the "
                        + "slot its definition names, as InsertClothing did (PlayerClothing.cs:47 on 0.4.7f6)",
                Emit = (module, clothing) => EmitForward1(module, clothing, "InsertClothing", "InsertClothingItem"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Police.PoliceOfficer", OldName = "get_belt",
                ParameterCount = 0,
                Because = Belt + "; the old field answers null, so a caller that fills it when empty does nothing",
                Emit = (module, officer) => AsProperty(officer, EmitEmpty(module, "get_belt", module.GetType(OldBelt))),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Police.PoliceOfficer", OldName = "set_belt",
                ParameterCount = 1,
                Because = Belt + "; the officer finds its own belt in Awake, so storing one has nothing to do",
                Emit = (module, officer) => AsProperty(officer, EmitEmpty(module, "set_belt", module.TypeSystem.Void, module.GetType(OldBelt))),
            },

            // ---- storage ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Storage.StorableItemInstance",
                OldName = "get_StoredItem", ParameterCount = 0,
                Because = "renamed on 0.4.7f7: the IL2CPP interop of 0.4.7f7 has StorableItemInstance.StoredItemPrefab "
                        + "(still virtual, overridden by ProductItemInstance) and no StoredItem. 0.4.7f6 still has "
                        + "StoredItem (StorableItemInstance.cs:11), so there the member is present and nothing is added",
                Emit = (module, item) => EmitGetterRename(module, item, "get_StoredItem", "StoredItemPrefab"),
            },

            // ---- properties a mod builds itself (ChemicalPlant) ----
            Empty("Il2CppScheduleOne.Property.Property", "set_EmployeeContainer", 1, m => m.TypeSystem.Void,
                  "0.4.6's Property declared a public Transform EmployeeContainer and no game code read or wrote it "
                  + "(Property.cs:83 is its only mention in Assembly-CSharp on 0.4.6f13); 0.4.7 dropped the field "
                  + "(Property.cs on 0.4.7f6), so a mod that fills it in while it builds a property has nothing "
                  + "left to tell the game",
                  module => TypeIn(module, "UnityEngine.CoreModule", "UnityEngine", "Transform")),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = ParkingSpot, OldName = "set_ParentLot", ParameterCount = 1,
                Because = ParentLot,
                Emit = (module, spot) => EmitSetterRename(module, spot, "set_ParentLot", "_parentParentLot"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = ParkingSpot, OldName = "get_ParentLot", ParameterCount = 0,
                Because = ParentLot,
                Emit = (module, spot) => EmitGetterRename(module, spot, "get_ParentLot", "_parentParentLot"),
            },
            Empty(TrashItem, "set_CurrentProperty", 1, m => m.TypeSystem.Void,
                  "0.4.6's trash kept the owned property it lies in as a cache that only its own RecheckProperty "
                  + "read and refreshed (TrashItem.cs:66, :249-263 on 0.4.6f13); 0.4.7's trash records no property "
                  + "and the getter put back for it answers from the bounds each time, so a value written here "
                  + "has no reader",
                  module => module.GetType("Il2CppScheduleOne.Property.Property")),
            Empty(Movement, "WarpToNavMesh", 0, m => m.TypeSystem.Void,
                  "0.4.6's NPCMovement.WarpToNavMesh() had an empty body (NPCMovement.cs:964-966 on 0.4.6f13) and "
                  + "0.4.7 dropped the method (NPCMovement.cs on 0.4.7f6); the call did nothing and does nothing"),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem, OldName = "get_CanGoInContainer", ParameterCount = 0,
                Because = CanGoInContainer,
                Emit = (module, trash) => EmitGetterRename(module, trash, "get_CanGoInContainer", "_canGoInTrashContainer"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem, OldName = "set_CanGoInContainer", ParameterCount = 1,
                Because = CanGoInContainer,
                Emit = (module, trash) => EmitSetterRename(module, trash, "set_CanGoInContainer", "_canGoInTrashContainer"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.GameTime.TimeManager",
                OldName = "SetTimeAndSync", ParameterCount = 1,
                Because = "0.4.6's SetTimeAndSync(time) set the time on the host and sent it to everyone through "
                        + "SetTimeData_Client (TimeManager.cs:317-326 on 0.4.6f13); 0.4.7's SetTime_Server(time) makes "
                        + "the same host check and the same send, and that send runs on the host too and sets the "
                        + "time there (TimeManager.cs:196-198, :321-331, :614-623 on 0.4.7f6)",
                Emit = (module, time) => EmitForward1(module, time, "SetTimeAndSync", "SetTime_Server"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.PlayerScripts.Player",
                OldName = "SetVisibleToLocalPlayer", ParameterCount = 1,
                Because = "0.4.6's SetVisibleToLocalPlayer(vis) recorded the flag, moved the avatar to the Player or "
                        + "the Invisible layer and called SetThirdPersonMeshesVisibility(vis) (Player.cs:1231-1243 on "
                        + "0.4.6f13); 0.4.7's SetThirdPersonMeshesVisibility moves the avatar to that layer itself "
                        + "(Player.cs:1649-1669 on 0.4.7f6) and the flag is still on the player (Player.cs:446)",
                Emit = EmitSetVisibleToLocalPlayer,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Dialogue.DialogueController",
                OldName = "get_GenericDialogue", ParameterCount = 0,
                Because = "0.4.6's controller held the dialogue StartGenericDialogue opens in its GenericDialogue "
                        + "field (DialogueController.cs:82, :129-147 on 0.4.6f13); 0.4.7's controller opens the one "
                        + "the DialogueManager holds and reads it through _genericConversation "
                        + "(DialogueController.cs:112, :143-161 on 0.4.7f6)",
                Emit = EmitGenericDialogue,
            },

            // ---- trash ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Grabber, OldName = "get_Instance", ParameterCount = 0,
                Because = EquippedGrabber,
                Emit = (module, grabber) => EmitEquippedGrabber(module, grabber, "get_Instance", asFlag: false),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = Grabber, OldName = "get_IsEquipped", ParameterCount = 0,
                Because = EquippedGrabber,
                Emit = (module, grabber) => EmitEquippedGrabber(module, grabber, "get_IsEquipped", asFlag: true),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem, OldName = "SetPhysicsActive", ParameterCount = 1,
                Because = "0.4.6's trash rested kinematic and SetPhysicsActive(true) woke it: called when it was dragged "
                        + "or hit, undone a minute later (TrashItem.cs:91-107, :265-292 on 0.4.6f13). 0.4.7's trash is "
                        + "never kinematic and rests asleep, so nothing would wake a body made kinematic "
                        + "(TrashItem.cs:48-60, Draggable.cs:224-240 on 0.4.7f6): true makes it non-kinematic and "
                        + "wakes it, false puts it to sleep, which a drag or a hit ends as it did on 0.4.6",
                Emit = EmitSetPhysicsActive,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem, OldName = "get_Draggable", ParameterCount = 0,
                Because = "0.4.6's trash item held its Draggable in a public field (TrashItem.cs:41 on 0.4.6f13); "
                        + "0.4.7 keeps a TrashItemDraggable, which is a Draggable, in _draggable "
                        + "(TrashItem.cs:38, :51, TrashItemDraggable.cs:9 on 0.4.7f6)",
                Emit = EmitTrashDraggable,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem, OldName = "get_colliders", ParameterCount = 0,
                Because = "0.4.6's trash item kept GetComponentsInChildren<Collider>() in its colliders field "
                        + "(TrashItem.cs:54, :133-136 on 0.4.6f13); 0.4.7 keeps no such list (TrashItem.cs on "
                        + "0.4.7f6), so the getter collects them when asked",
                Emit = EmitTrashColliders,
            },
            Empty(TrashItem, "set_colliders", 1, m => m.TypeSystem.Void,
                  "0.4.6's trash item kept its colliders in a field a mod could fill (TrashItem.cs:54 on 0.4.6f13); "
                  + "0.4.7 keeps no list (TrashItem.cs on 0.4.7f7) and the getter put back for it collects them "
                  + "each time, so a list written here has no reader",
                  ReferenceArrayOf("UnityEngine.PhysicsModule", "UnityEngine", "Collider")),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem, OldName = "SetCollidersEnabled", ParameterCount = 1,
                Because = "0.4.6's SetCollidersEnabled(enabled) set enabled on each collider in the item's list "
                        + "(TrashItem.cs:294-304 on 0.4.6f13); 0.4.7 keeps no list, so the switch is the body's "
                        + "detectCollisions: off takes all of the body's colliders out of the simulation, on puts "
                        + "back those that are enabled and leaves one a mod disabled itself disabled",
                Emit = EmitSetCollidersEnabled,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem,
                OldName = "SetContinuousCollisionDetection", ParameterCount = 0,
                Because = "0.4.6's SetContinuousCollisionDetection() made the body continuous and active and went "
                        + "back to discrete a minute later (TrashItem.cs:266-281 on 0.4.6f13); 0.4.7 makes a body "
                        + "continuous while it is dragged and gives it its own mode back on release "
                        + "(Draggable.cs:224-242, :266-275 on 0.4.7f7), so the body goes continuous, non-kinematic "
                        + "and awake here and the next drag puts its mode back",
                Emit = EmitSetContinuousCollisionDetection,
            },
            Empty(TrashItem, "InitializeSaveable", 0, m => m.TypeSystem.Void,
                  "0.4.6's trash item was a saveable of its own and InitializeSaveable() registered it with the "
                  + "SaveManager, from Start (TrashItem.cs:110-121 on 0.4.6f13); 0.4.7's trash is no saveable and "
                  + "the TrashManager writes every item on its list (TrashItem.cs:13, :105-108, "
                  + "TrashManager.cs:301-306 on 0.4.7f7), so there is nothing to register"),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = TrashItem, OldName = "set_Draggable", ParameterCount = 1,
                Because = "0.4.6's trash item held its Draggable in a public field a mod could write (TrashItem.cs:41 "
                        + "on 0.4.6f13); 0.4.7 holds a TrashItemDraggable in _draggable, finds it itself in Awake and "
                        + "only asks it whether it is being dragged (TrashItem.cs:38, :51, :80-83 on 0.4.7f7), so a "
                        + "draggable of that kind is stored and any other value leaves the one Awake found",
                Emit = EmitSetTrashDraggable,
            },

            // ---- the draggable a trash item is picked up by ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = DraggableType, OldName = "get_IntObj", ParameterCount = 0,
                Because = Interactable,
                Emit = (module, draggable) => EmitGetterRename(module, draggable, "get_IntObj", "_interactable"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = DraggableType, OldName = "set_IntObj", ParameterCount = 1,
                Because = Interactable,
                Emit = (module, draggable) => EmitSetterRename(module, draggable, "set_IntObj", "_interactable"),
            },
            Empty(DraggableType, "get_CreateCoM", 0, m => m.TypeSystem.Boolean, CentreOfMass),
            Empty(DraggableType, "set_CreateCoM", 1, m => m.TypeSystem.Void, CentreOfMass, m => m.TypeSystem.Boolean),
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Trash.TrashManager",
                OldName = "get_TRASH_ITEM_LIMIT", ParameterCount = 0,
                Because = "renamed: the 2000 item limit is TrashItemLimit on 0.4.7 (TrashManager.cs:33 on 0.4.6f13; "
                        + ":34 on 0.4.7f6)",
                Emit = EmitTrashItemLimit,
            },

            // ---- two renames (Tweakables) ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.PlayerScripts.PlayerMovement",
                OldName = "SetPlayerRotation", ParameterCount = 1,
                Because = "renamed: 0.4.6's SetPlayerRotation(Quaternion) turned the player to the rotation's yaw "
                        + "with the controller off (PlayerMovement.cs:683-689 on 0.4.6f13); 0.4.7f12 has "
                        + "SetRotation(Quaternion) in its place, beside the same LerpPlayerRotation",
                Emit = (module, movement) => EmitForward1(module, movement, "SetPlayerRotation", "SetRotation"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = "Il2CppScheduleOne.Product.ProductIconManager",
                OldName = "GenerateIcons", ParameterCount = 1,
                Because = "renamed: 0.4.6's GenerateIcons(productID) made one icon per packaging and returned the "
                        + "product's own sprite (ProductIconManager.cs:64-94 on 0.4.6f13); 0.4.7f12 calls it "
                        + "GenerateRuntimeIcons(productID), with the same return",
                Emit = (module, icons) => EmitForward1(module, icons, "GenerateIcons", "GenerateRuntimeIcons"),
            },

            // ---- the joining player's saved data (Ndrangheta) ----
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = PlayerType,
                OldName = "get_playerDataRetrieveReturned", ParameterCount = 0,
                Because = "renamed: 0.4.6's player raised playerDataRetrieveReturned once its saved data had been "
                        + "applied (Player.cs:447, :1476, :2746 on 0.4.6f13); 0.4.7 raises PlayerLoaded at the same "
                        + "point, the end of SetPlayerData_Client (Player.cs:448, :715-717, :2809 on 0.4.7f7)",
                Emit = (module, player) => EmitGetterRename(module, player, "get_playerDataRetrieveReturned", "PlayerLoaded"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = PlayerType,
                OldName = "RequestPlayerData", ParameterCount = 1, ParameterTypes = new[] { "System.String" },
                Because = "0.4.6's RequestPlayerData(playerCode) asked the server for the player's saved data "
                        + "(Player.cs:694, :810 on 0.4.6f13); 0.4.7's RequestPlayerData_Server(playerCode, isHost) "
                        + "asks the same and is told whether the asker is the host, which the game reads from "
                        + "InstanceFinder.IsHost (Player.cs:687, :823 on 0.4.7f7)",
                Emit = EmitRequestPlayerData,
            },
        };

        // ---------------------------------------------------------------- speeds

        internal static MethodDefinition Controller(TypeDefinition movement, out TypeDefinition controller)
        {
            var get = Getter(movement, "SpeedController");
            controller = get?.ReturnType?.Resolve();
            return get;
        }

        private static MethodDefinition EmitWalkSpeed(ModuleDefinition module, TypeDefinition movement)
        {
            var walk = Getter(movement, "DefaultWalkSpeed");
            if (walk == null) return null;
            var method = new MethodDefinition("get_WalkSpeed",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Single);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, walk);
            il.Emit(OpCodes.Ret);
            return AsProperty(movement, method);
        }

        private static MethodDefinition EmitRunSpeed(ModuleDefinition module, TypeDefinition movement)
        {
            var getController = Controller(movement, out var controller);
            var max = Getter(controller, "_maxSpeed");
            if (getController == null || max == null) return null;
            var method = new MethodDefinition("get_RunSpeed",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Single);
            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Call, max);
            // var c = SpeedController; return c == null ? 7f : c._maxSpeed;   (7 is the controller's own default)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_R4, 7f);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Ret);
            return AsProperty(movement, method);
        }

        private static MethodDefinition EmitUpdateSpeed(ModuleDefinition module, TypeDefinition movement)
        {
            var getController = Controller(movement, out var controller);
            var set = Method(controller, "SetSpeedMultiplier", 1);
            var multiplier = Getter(controller, "_speedMultiplier");
            if (getController == null || set == null || multiplier == null) return null;
            var method = new MethodDefinition("UpdateSpeed",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Dup);
            // var c = SpeedController; if (c == null) return; c.SetSpeedMultiplier(c._speedMultiplier);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getController);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Call, multiplier);
            il.Emit(set.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, set);
            il.Emit(OpCodes.Ret);
            return method;
        }

        // ---------------------------------------------------------------- consume, messages

        private static MethodDefinition EmitConsumeProduct(ModuleDefinition module, TypeDefinition behaviour)
        {
            var target = Method(behaviour, "ConsumeProduct_Server", 2);
            if (target == null || target.IsStatic) return null;
            var method = new MethodDefinition("ConsumeProduct",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            foreach (var p in target.Parameters)
                method.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None,
                                                              module.ImportReference(p.ParameterType)));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg, method.Parameters[0]);
            il.Emit(OpCodes.Ldarg, method.Parameters[1]);
            il.Emit(target.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, target);
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static MethodDefinition EmitSetContactName(ModuleDefinition module, TypeDefinition conversation)
        {
            var getSender = Getter(conversation, "_sender");
            var setSender = Method(conversation, "set__sender", 1);
            var info = getSender?.ReturnType?.Resolve();
            var setName = Method(info, "set__name", 1);
            if (getSender == null || setSender == null || setName == null) return null;

            var method = new MethodDefinition("set_contactName",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Void);
            var value = new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.String);
            method.Parameters.Add(value);
            var s = new VariableDefinition(module.ImportReference(getSender.ReturnType));
            method.Body.Variables.Add(s);
            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();
            // var s = _sender; s._name = value; _sender = s;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getSender);
            il.Emit(OpCodes.Stloc, s);
            il.Emit(info.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, s);
            il.Emit(OpCodes.Ldarg, value);
            il.Emit(OpCodes.Call, setName);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, s);
            il.Emit(OpCodes.Call, setSender);
            il.Emit(OpCodes.Ret);
            return AsProperty(conversation, method);
        }

        // ---------------------------------------------------------------- culling and impostors

        internal static MethodDefinition AvatarOf(TypeDefinition animation, out TypeDefinition avatar)
        {
            var get = Getter(animation, "avatar");
            avatar = get?.ReturnType?.Resolve();
            return get;
        }

        private static MethodDefinition EmitIsAvatarCulled(ModuleDefinition module, TypeDefinition animation)
        {
            var getAvatar = AvatarOf(animation, out var avatar);
            var culled = Getter(avatar, "IsCulled");
            if (getAvatar == null || culled == null) return null;
            var method = new MethodDefinition("get_IsAvatarCulled",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Boolean);
            var il = method.Body.GetILProcessor();
            var have = il.Create(OpCodes.Call, culled);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, getAvatar);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue_S, have);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.Append(have);
            il.Emit(OpCodes.Ret);
            return AsProperty(animation, method);
        }

        /// <summary>
        /// <c>Player.SetVisibleToLocalPlayer(vis)</c>: the flag, then the meshes and their layer.
        /// </summary>
        /// <remarks>
        /// 0.4.7 has no writer left for <c>avatarVisibleToLocalPlayer</c>, so the flag is set here the way the
        /// 0.4.6 method set it: a mod that hides the player and reads the flag back gets what it wrote.
        /// </remarks>
        private static MethodDefinition EmitSetVisibleToLocalPlayer(ModuleDefinition module, TypeDefinition player)
        {
            var flag = Method(player, "set_avatarVisibleToLocalPlayer", 1);
            var meshes = Method(player, "SetThirdPersonMeshesVisibility", 1);
            if (flag == null || meshes == null || flag.IsStatic || meshes.IsStatic) return null;
            var method = new MethodDefinition("SetVisibleToLocalPlayer", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("vis", ParameterAttributes.None, module.TypeSystem.Boolean));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, flag);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(meshes.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, meshes);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>Player.RequestPlayerData(playerCode)</c>: the 0.4.7 request, with the host flag the game passes.
        /// </summary>
        /// <remarks>
        /// InstanceFinder appears in no signature of this module, so the reference is built against the FishNet
        /// assembly the module already names.
        /// </remarks>
        private static MethodDefinition EmitRequestPlayerData(ModuleDefinition module, TypeDefinition player)
        {
            var request = Method(player, "RequestPlayerData_Server", 2);
            var finder = TypeIn(module, "Il2CppFishNet.Runtime", "Il2CppFishNet", "InstanceFinder");
            if (request == null || request.IsStatic || finder == null) return null;
            var isHost = new MethodReference("get_IsHost", module.TypeSystem.Boolean, finder);

            var method = new MethodDefinition("RequestPlayerData", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("playerCode", ParameterAttributes.None, module.TypeSystem.String));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, isHost);
            il.Emit(request.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, request);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// <c>DialogueController.GenericDialogue</c>, read: the conversation the controller opens, handed back
        /// under the name the type had.
        /// </summary>
        /// <remarks>
        /// A mod built for 0.4.6 asks for a getter that returns <c>DialogueContainer</c>, and a call matches on
        /// the return type too. The class put back under that name derives from <c>Conversation</c>, so the
        /// answer is rebuilt as that class around the same native object. Without that class there is nothing
        /// to return the answer as, and the getter is not written.
        ///
        /// There is no setter: 0.4.7 keeps one generic conversation for every controller, on the
        /// DialogueManager, so a value written for one NPC has nowhere to live.
        /// </remarks>
        private static MethodDefinition EmitGenericDialogue(ModuleDefinition module, TypeDefinition controller)
        {
            var to = Getter(controller, "_genericConversation");
            if (to == null || to.IsStatic) return null;
            var container = Core.ShadowTypes.Shadowing(module, to.ReturnType);
            if (container == null) return null;
            var method = new MethodDefinition("get_GenericDialogue",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, container);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, to);
            if (!Core.ShadowTypes.EmitRewrap(module, il, container, out _)) return null;
            return AsProperty(controller, method);
        }

        // ---------------------------------------------------------------- trash

        /// <summary>
        /// The trash grabber the local player holds, or whether there is one: the local inventory's equippable,
        /// when anything is equipped and it is a grabber.
        /// </summary>
        private static MethodDefinition EmitEquippedGrabber(ModuleDefinition module, TypeDefinition grabber,
                                                            string name, bool asFlag)
        {
            var inventory = module.GetType("Il2CppScheduleOne.PlayerScripts.PlayerInventory");
            var holder = module.GetType("Il2CppScheduleOne.DevUtilities.PlayerSingleton`1");
            var instance = Method(holder, "get_Instance", 0);
            var anything = Getter(inventory, "isAnythingEquipped");
            var equippable = Getter(inventory, "Equippable");
            var cast = GenericUp(module, grabber, "TryCast", grabber);
            if (instance == null || !instance.IsStatic || anything == null || equippable == null || cast == null)
                return null;

            var ofInventory = new GenericInstanceType(module.ImportReference(holder));
            ofInventory.GenericArguments.Add(module.ImportReference(inventory));

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                asFlag ? module.TypeSystem.Boolean : grabber);
            method.Body.InitLocals = true;
            var held = new VariableDefinition(inventory);
            var worn = new VariableDefinition(module.ImportReference(equippable.ReturnType));
            method.Body.Variables.Add(held);
            method.Body.Variables.Add(worn);

            var il = method.Body.GetILProcessor();
            var none = il.Create(asFlag ? OpCodes.Ldc_I4_0 : OpCodes.Ldnull);

            il.Emit(OpCodes.Call, Against(module, instance, ofInventory));
            il.Emit(OpCodes.Stloc, held);
            il.Emit(OpCodes.Ldloc, held);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Ldloc, held);
            il.Emit(OpCodes.Callvirt, anything);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Ldloc, held);
            il.Emit(OpCodes.Callvirt, equippable);
            il.Emit(OpCodes.Stloc, worn);
            il.Emit(OpCodes.Ldloc, worn);
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Ldloc, worn);
            il.Emit(OpCodes.Callvirt, cast);          // null when the equippable is something else
            if (asFlag)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Cgt_Un);
            }
            il.Emit(OpCodes.Ret);
            il.Append(none);
            il.Emit(OpCodes.Ret);
            return AsProperty(grabber, method);
        }

        private static MethodDefinition EmitSetPhysicsActive(ModuleDefinition module, TypeDefinition trash)
        {
            var body = Getter(trash, "_rigidbody");
            var rigidbody = body?.ReturnType?.Resolve();
            var kinematic = Method(rigidbody, "set_isKinematic", 1);
            var wake = Method(rigidbody, "WakeUp", 0);
            var sleep = Method(rigidbody, "Sleep", 0);
            if (body == null || body.IsStatic || kinematic == null || wake == null || sleep == null) return null;

            var method = new MethodDefinition("SetPhysicsActive", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("active", ParameterAttributes.None, module.TypeSystem.Boolean));
            var il = method.Body.GetILProcessor();
            var rest = il.Create(OpCodes.Ldarg_0);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, rest);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, module.ImportReference(kinematic));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Callvirt, module.ImportReference(wake));
            il.Emit(OpCodes.Ret);
            il.Append(rest);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Callvirt, module.ImportReference(sleep));
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static MethodDefinition EmitTrashDraggable(ModuleDefinition module, TypeDefinition trash)
        {
            var to = Getter(trash, "_draggable");
            var draggable = module.GetType("Il2CppScheduleOne.Dragging.Draggable");
            if (to == null || to.IsStatic || draggable == null) return null;

            bool derives = false;
            for (var current = to.ReturnType.Resolve(); current != null && !derives; current = current.BaseType?.Resolve())
                derives = current == draggable;
            if (!derives) return null;

            var method = new MethodDefinition("get_Draggable",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, draggable);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, to);
            il.Emit(OpCodes.Ret);
            return AsProperty(trash, method);
        }

        private static MethodDefinition EmitTrashColliders(ModuleDefinition module, TypeDefinition trash)
        {
            var collider = TypeIn(module, "UnityEngine.PhysicsModule", "UnityEngine", "Collider");
            var array = ReferenceArrayOf("UnityEngine.PhysicsModule", "UnityEngine", "Collider")(module);
            var collect = collider == null ? null : GenericUp(module, trash, "GetComponentsInChildren", collider);
            if (array == null || collect == null) return null;

            var method = new MethodDefinition("get_colliders",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, array);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, collect);
            il.Emit(OpCodes.Castclass, array);        // the interop hands an array of objects back as this type
            il.Emit(OpCodes.Ret);
            return AsProperty(trash, method);
        }

        private static MethodDefinition EmitSetCollidersEnabled(ModuleDefinition module, TypeDefinition trash)
        {
            var body = Getter(trash, "_rigidbody");
            var detect = Method(body?.ReturnType?.Resolve(), "set_detectCollisions", 1);
            if (body == null || body.IsStatic || detect == null) return null;

            var method = new MethodDefinition("SetCollidersEnabled", MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("enabled", ParameterAttributes.None, module.TypeSystem.Boolean));
            method.Body.InitLocals = true;
            var held = new VariableDefinition(module.ImportReference(body.ReturnType));
            method.Body.Variables.Add(held);

            var il = method.Body.GetILProcessor();
            var done = il.Create(OpCodes.Ret);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Stloc, held);
            il.Emit(OpCodes.Ldloc, held);
            il.Emit(OpCodes.Brfalse, done);           // 0.4.6's list did not need the body, so its absence is no error
            il.Emit(OpCodes.Ldloc, held);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(detect));
            il.Append(done);
            return method;
        }

        private static MethodDefinition EmitSetContinuousCollisionDetection(ModuleDefinition module, TypeDefinition trash)
        {
            var body = Getter(trash, "_rigidbody");
            var rigidbody = body?.ReturnType?.Resolve();
            var kinematic = Method(rigidbody, "set_isKinematic", 1);
            var mode = Method(rigidbody, "set_collisionDetectionMode", 1);
            var wake = Method(rigidbody, "WakeUp", 0);
            if (body == null || body.IsStatic || kinematic == null || mode == null || wake == null) return null;

            var method = new MethodDefinition("SetContinuousCollisionDetection",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            var il = method.Body.GetILProcessor();

            // Non-kinematic first: a kinematic body refuses the continuous mode.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Callvirt, module.ImportReference(kinematic));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Ldc_I4_1);                // CollisionDetectionMode.Continuous
            il.Emit(OpCodes.Callvirt, module.ImportReference(mode));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Callvirt, module.ImportReference(wake));
            il.Emit(OpCodes.Ret);
            return method;
        }

        private static MethodDefinition EmitSetTrashDraggable(ModuleDefinition module, TypeDefinition trash)
        {
            var to = Method(trash, "set__draggable", 1);
            var draggable = module.GetType(DraggableType);
            if (to == null || to.IsStatic || draggable == null) return null;

            var own = to.Parameters[0].ParameterType;
            var cast = GenericUp(module, draggable, "TryCast", own);
            if (cast == null) return null;

            var method = new MethodDefinition("set_Draggable",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, draggable));
            method.Body.InitLocals = true;
            var fitting = new VariableDefinition(module.ImportReference(own));
            method.Body.Variables.Add(fitting);

            var il = method.Body.GetILProcessor();
            var done = il.Create(OpCodes.Ret);

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, cast);          // null when the draggable is of another kind
            il.Emit(OpCodes.Stloc, fitting);
            il.Emit(OpCodes.Ldloc, fitting);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, fitting);
            il.Emit(OpCodes.Call, to);
            il.Append(done);
            return AsProperty(trash, method);
        }

        private static MethodDefinition EmitTrashItemLimit(ModuleDefinition module, TypeDefinition manager)
        {
            var to = Getter(manager, "TrashItemLimit");
            if (to == null || !to.IsStatic) return null;
            var method = new MethodDefinition("get_TRASH_ITEM_LIMIT",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(to.ReturnType));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Call, to);
            il.Emit(OpCodes.Ret);
            return AsProperty(manager, method);
        }

        /// <summary>A getter that answers null: the member exists so callers compile, and they null-check it.</summary>
        /// <remarks>
        /// 0.4.7 holds no mesh arrays; an avatar's renderers live under BodyContainer, which is shown and hidden as a
        /// whole. NetEye null-checks all three and only used them to force renderers on, which activating
        /// BodyContainer - its own first step - already does.
        /// </remarks>
        private static Bridge NullGetter(string declaringType, string name, Func<ModuleDefinition, TypeReference> returns)
            => new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = declaringType, OldName = name, ParameterCount = 0,
                Because = "0.4.7 keeps an avatar's renderers under BodyContainer and has no mesh arrays; the member is put "
                        + "back answering null so callers compile, and callers that null-check it carry on",
                Emit = (module, type) =>
                {
                    var returnType = returns(module);
                    if (returnType == null) return null;
                    var method = new MethodDefinition(name,
                        MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, returnType);
                    var il = method.Body.GetILProcessor();
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ret);
                    return AsProperty(type, method);
                },
            };

        // ---------------------------------------------------------------- shared shapes

        internal static MethodDefinition EmitForward(ModuleDefinition module, TypeDefinition type, string oldName, string target)
        {
            var to = Method(type, target, 0);
            if (to == null || to.IsStatic) return null;
            var method = new MethodDefinition(oldName, MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.ImportReference(to.ReturnType));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(to.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, to);
            il.Emit(OpCodes.Ret);
            return method;
        }

        internal static MethodDefinition EmitForward1(ModuleDefinition module, TypeDefinition type, string oldName, string target)
        {
            var to = Method(type, target, 1);
            if (to == null || to.IsStatic) return null;
            var method = new MethodDefinition(oldName, MethodAttributes.Public | MethodAttributes.HideBySig,
                                              module.ImportReference(to.ReturnType));
            method.Parameters.Add(new ParameterDefinition(to.Parameters[0].Name, ParameterAttributes.None,
                                                          module.ImportReference(to.Parameters[0].ParameterType)));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(to.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, to);
            il.Emit(OpCodes.Ret);
            return method;
        }

        internal static MethodDefinition EmitGetterRename(ModuleDefinition module, TypeDefinition type, string oldName, string member)
        {
            var to = Getter(type, member);
            if (to == null || to.IsStatic) return null;
            var method = new MethodDefinition(oldName,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(to.ReturnType));
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(to.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, to);   // an override answers for its own type
            il.Emit(OpCodes.Ret);
            return AsProperty(type, method);
        }

        internal static MethodDefinition EmitSetterRename(ModuleDefinition module, TypeDefinition type, string oldName, string member)
        {
            var method = EmitForward1(module, type, oldName, "set_" + member);
            if (method == null) return null;
            method.Attributes |= MethodAttributes.SpecialName;
            return AsProperty(type, method);
        }

        /// <summary>A member put back doing nothing: default of its return type, arguments ignored.</summary>
        internal static Bridge Empty(string declaringType, string name, int parameters, Func<ModuleDefinition, TypeReference> returns,
                                     string because, params Func<ModuleDefinition, TypeReference>[] parameterTypes)
            => new Bridge
            {
                Assembly = "Assembly-CSharp", DeclaringType = declaringType, OldName = name, ParameterCount = parameters,
                Because = because,
                Emit = (module, type) =>
                {
                    var args = new TypeReference[parameterTypes.Length];
                    for (int i = 0; i < args.Length; i++)
                        if ((args[i] = parameterTypes[i](module)) == null) return null;
                    return AsProperty(type, EmitEmpty(module, name, returns(module), args));
                },
            };

        internal static MethodDefinition EmitEmpty(ModuleDefinition module, string name, TypeReference returns,
                                                   params TypeReference[] parameters)
        {
            if (returns == null) return null;
            var attributes = MethodAttributes.Public | MethodAttributes.HideBySig;
            if (name.StartsWith("get_") || name.StartsWith("set_")) attributes |= MethodAttributes.SpecialName;
            var method = new MethodDefinition(name, attributes, module.ImportReference(returns));
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i] == null) return null;
                method.Parameters.Add(new ParameterDefinition("arg" + i, ParameterAttributes.None,
                                                              module.ImportReference(parameters[i])));
            }
            var il = method.Body.GetILProcessor();
            var r = returns.MetadataType;
            if (r == MetadataType.Boolean || r == MetadataType.Int32) il.Emit(OpCodes.Ldc_I4_0);
            else if (r != MetadataType.Void && !returns.IsValueType) il.Emit(OpCodes.Ldnull);
            else if (r != MetadataType.Void) return null;   // no struct defaults here
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary><c>Clothing.EClothingApplicationType</c>, as 0.4.6 had it, made once per module.</summary>
        private static TypeDefinition ApplicationTypeEnum(ModuleDefinition module)
        {
            var existing = module.GetType(ApplicationType);
            if (existing != null) return existing;
            var type = new TypeDefinition("Il2CppScheduleOne.Clothing", "EClothingApplicationType",
                TypeAttributes.Public | TypeAttributes.Sealed,
                new TypeReference("System", "Enum", module, module.TypeSystem.CoreLibrary));
            type.Fields.Add(new FieldDefinition("value__",
                FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, module.TypeSystem.Int32));
            string[] names = { "BodyLayer", "FaceLayer", "Accessory" };
            for (int value = 0; value < names.Length; value++)
                type.Fields.Add(new FieldDefinition(names[value],
                    FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault,
                    type) { Constant = value });
            module.Types.Add(type);
            return type;
        }

        /// <summary>
        /// Gives a bridged accessor its PropertyDefinition, so reflection by property name
        /// (<c>AccessTools.Property</c>) finds it as it found the 0.4.6 member. Null passes through.
        /// </summary>
        internal static MethodDefinition AsProperty(TypeDefinition type, MethodDefinition accessor)
        {
            if (accessor == null) return null;
            bool get = accessor.Name.StartsWith("get_", StringComparison.Ordinal);
            if (!get && !accessor.Name.StartsWith("set_", StringComparison.Ordinal)) return accessor;
            string name = accessor.Name.Substring(4);
            foreach (var existing in type.Properties)
                if (existing.Name == name)
                {
                    if (get) existing.GetMethod ??= accessor;
                    else existing.SetMethod ??= accessor;
                    return accessor;
                }
            var property = new PropertyDefinition(name, PropertyAttributes.None,
                                                  get ? accessor.ReturnType : accessor.Parameters[0].ParameterType);
            if (get) property.GetMethod = accessor;
            else property.SetMethod = accessor;
            type.Properties.Add(property);
            return accessor;
        }

        internal static TypeReference TypeIn(ModuleDefinition module, string assembly, string ns, string name)
        {
            foreach (var reference in module.AssemblyReferences)
                if (reference.Name == assembly) return new TypeReference(ns, name, module, reference);
            return null;
        }

        private static Func<ModuleDefinition, TypeReference> ReferenceArrayOf(string assembly, string ns, string name)
            => module =>
            {
                var element = TypeIn(module, assembly, ns, name);
                var array = ReferenceArray(module);
                if (element == null || array == null) return null;
                var instance = new GenericInstanceType(array);
                instance.GenericArguments.Add(element);
                return instance;
            };

        /// <summary>The interop's reference-array type, read off a member that already returns one.</summary>
        /// <remarks>
        /// Not named by its assembly: that name in Polyfill.Boot.dll fails the release guard, because the plugin
        /// loads before that assembly exists. Every interop module that hands back an array of objects returns
        /// this type, so the first such member carries a reference to it that is already resolved in this module.
        /// </remarks>
        internal static TypeReference ReferenceArray(ModuleDefinition module)
        {
            foreach (var type in module.GetTypes())
                foreach (var method in type.Methods)
                    if (method.ReturnType is GenericInstanceType returned
                        && returned.ElementType.Name == "Il2CppReferenceArray`1")
                        return returned.ElementType;
            return null;
        }
    }
}
