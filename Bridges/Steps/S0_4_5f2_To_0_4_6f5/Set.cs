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

        internal override IEnumerable<TypeRename> DeclareRenames() => Renamed_;

        private const string Stations = "Il2CppScheduleOne.UI.Stations.";

        private const string StationSweep = "0.4.6 factored the station screens onto a shared "
                                          + "StationInterface<T> base and renamed the four that still said "
                                          + "Canvas; same members, same namespace, one word different";

        /// <summary>
        /// Types the game renamed outright, which no name match can follow.
        /// </summary>
        /// <remarks>
        /// Four station screens plus the handover price control. All five are one-word renames within the
        /// same namespace, verified against both API dumps rather than guessed from the spelling: the old
        /// name exists in 0.4.5f2 and not in 0.4.6f12, the new one the other way round, and the members
        /// line up.
        ///
        /// <c>HandoverScreenPriceSelector</c> is the odd one and worth the note: it did not just get a
        /// name, it got a JOB. The class stopped being about prices and became the game's general amount
        /// box, so <c>Price</c> is <c>SelectedAmount</c> and <c>SetPrice</c> is <c>SetAmount</c>. The type
        /// resolving is what lets a mod's method compile at all; the members under their old names are a
        /// separate question this does not answer.
        /// </remarks>
        private static readonly List<TypeRename> Renamed_ = new()
        {
            Pair(Stations +"MixingStationCanvas",    Stations + "MixingStationInterface"),
            Pair(Stations +"ChemistryStationCanvas", Stations + "ChemistryStationInterface"),
            Pair(Stations +"CauldronCanvas",         Stations + "CauldronInterface"),
            Pair(Stations +"DryingRackCanvas",       Stations + "DryingRackInterface"),

            new TypeRename
            {
                Assembly = "Assembly-CSharp",
                OldFullName = "Il2CppScheduleOne.UI.Handover.HandoverScreenPriceSelector",
                NewFullName = "Il2CppScheduleOne.UI.AmountSelector",
                Because = "the price control became the game's general amount box in 0.4.6 and moved out "
                        + "of the handover namespace; HandoverScreen.PriceSelector is an AmountSelector now",
            },
        };

        private static TypeRename Pair(string oldFullName, string newFullName)
            => new()
            {
                Assembly = "Assembly-CSharp",
                OldFullName = oldFullName,
                NewFullName = newFullName,
                Because = StationSweep,
            };

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

        private const string Storage = "Il2CppScheduleOne.UI.StorageMenu";
        private const string Owner = "Il2CppScheduleOne.ItemFramework.IItemSlotOwner";
        private const string Text = "System.String";
        private static readonly object[] OneCallback = { null };

        private const string Speed = "npc.NPCData.Movement, which is where NPCMovement's own getter reads "
                                   + "it from (NPCMovement.cs:170-172)";

        private const string StationCanvas = "0.4.6 pulled the canvas off every station screen into the "
                                           + "shared StationInterface<T> base and renamed it _canvas";

        private const string Avatar = "Il2CppScheduleOne.AvatarFramework.Avatar";
        private const string PlayerType = "Il2CppScheduleOne.PlayerScripts.Player";

        private const string PlayerToggle = "Player.cs:1423-1441 until 0.4.5f2; 0.4.6 deleted both and "
                                          + "rewrote its own three callers to register a UI element "
                                          + "instead, leaving every line these ran still there";
        private const string Number = "System.Single";

        private const string Pickpocketed = "NPCInventory.cs:40 until 0.4.5f2; the same InteractableObject "
                                          + "under the name the private field carries now, which "
                                          + "NPCInventory.cs:338-348 sets the pickpocket state on";

        /// <summary>
        /// Why dropping <c>bodyOnly</c> is exact for the value everybody passes.
        /// </summary>
        /// <remarks>
        /// The flag meant "stop before the accessories": 0.4.5f2 returned early at Avatar.cs:305 when it was
        /// true, and otherwise ran the accessory loop below it. 0.4.6 has no flag and always runs that loop
        /// (Avatar.cs:292-297), so the two-argument form IS the old <c>bodyOnly: false</c> - the default,
        /// and what a call that names it explicitly almost always passes.
        ///
        /// The difference is worth stating rather than burying: a caller that passed TRUE now gets the
        /// accessories shaped as well. That is the game's own behaviour on this build and not something
        /// invented here, but it is more than the old call did.
        /// </remarks>
        private const string ShapeKeys = "bodyOnly stopped the method before the accessory loop; 0.4.6 "
                                       + "dropped the flag and always runs that loop, so the two-argument "
                                       + "form is what passing false always did (Avatar.cs:292-297)";

        private const string NameSplit = "NPC.cs:63-69 until 0.4.5f2, now BasicInfo.cs:4-7";
        private const string Summon = "NPC.cs:116 until 0.4.5f2, now Interaction.cs:8 - and the game reads it "
                                    + "from there in NPCEnterableBuilding.cs:96";
        private const string SlotCount = "NPCInventory.cs:45 until 0.4.5f2, now Inventory.InventorySlotCount, "
                                       + "which NPCInventory.cs:62 builds the slots from";
        private const string Renamed = "NPCInventory.cs:51-65 until 0.4.5f2; the value kept its meaning and "
                                     + "lost its old name in Inventory.cs";
        private const string Pickpocket = "NPCInventory.cs:47 until 0.4.5f2, now Inventory.CanBePickpocketed, "
                                        + "read at NPCInventory.cs:372";

        private const string Counteroffer = "Il2CppScheduleOne.UI.Phone.CounterofferInterface";

        private static readonly string[] PriceSelector = { "PriceSelector" };
        private static readonly string[] DealerData = { "DealerData" };

        private const string Movement = "Il2CppScheduleOne.NPCs.NPCMovement";

        /// <summary>NPCMovement is a component, so its path starts at its own back-reference to the NPC -
        /// the same one its surviving getters use.</summary>
        private static readonly string[] NpcSpeed = { "npc", "NPCData", "Movement" };
        private static readonly string[] SupplierData = { "SupplierData" };

        private const string LobbyType = "Il2CppScheduleOne.Networking.Lobby";

        /// <summary>The interop array wrapper, by simple name - see <c>StructArray</c> for why not by its
        /// full one.</summary>
        private const string StructArrayName = "Il2CppStructArray`1";

        /// <summary>
        /// Why <c>_inputField</c> and not <c>_tmpInputField</c>, settled rather than assumed.
        /// </summary>
        /// <remarks>
        /// <c>AmountSelector</c> carries both and prefers the TextMeshPro one when it is set, so which one
        /// this screen uses decides whether the repair hands back a control or a null. It is not answerable
        /// from a running game - the counteroffer screen is not in the scene until it is opened - and it
        /// does not have to be: the value is serialized, and the prefab is readable offline.
        ///
        /// <code>
        /// Player.prefab, the AmountSelector that CounterofferInterface.PriceSelector points at:
        ///   _inputField:    {fileID: 114179318410282125}   -> m_TextComponent, m_ContentType,
        ///                                                     m_CharacterLimit: 8  (UnityEngine.UI.InputField)
        ///   _tmpInputField: {fileID: 0}                    -> null
        /// </code>
        ///
        /// The lesson is worth more than the answer: "no instance in the scene" means the probe cannot see
        /// it, not that nothing can. What a component is WIRED to lives in the prefab, and a question about
        /// wiring belongs there.
        /// </remarks>
        private const string PriceBox = "CounterofferInterface.cs:20 held the price box directly until "
                                      + "0.4.5f2; 0.4.6 wraps it in an AmountSelector, whose _inputField "
                                      + "is the same control (AmountSelector.cs:19)";

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
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.Management.ManagementInterface",
                OldName = "get_NPCSelector",
                ParameterCount = 0,
                Because = "0.4.6 removed the NPC selector screen with no replacement and left its own stub "
                        + "behind (NPCFieldUI.cs:79 logs \"NPCSelector not implemented\"). The only use is "
                        + "a null check for \"is that screen open\", and a screen that does not exist is "
                        + "not open - see Removed.cs for why answering costs less than refusing",
                Emit = Removed.EmitNpcSelectorGetter,
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

            // The counteroffer screen kept its price box and changed what the box IS. Reported as
            // "DealOptimizer generates errors over and over and the interface is blank", which is exactly
            // what a MissingMethodException in the mod's Subscribe() looks like from the outside.
            Moved(Counteroffer, "PriceInput", Read, PriceSelector, "_inputField", PriceBox),

            // Walk and run speed became read-only views over the NPC's data object. Writing one wrote the
            // value the getter still reads, so the write goes to the same place.
            Moved(Movement, "WalkSpeed", Write, NpcSpeed, "WalkSpeed", Speed),
            Moved(Movement, "RunSpeed", Write, NpcSpeed, "SprintSpeed", Speed),

            Moved("Il2CppScheduleOne.Economy.Supplier", "OnlineShopItems", Read, SupplierData,
                  "DeliveryShopListings",
                  "the same PhoneShopInterface.Listing[]; 0.4.6 keeps it on SupplierNPCData and "
                + "Supplier.SupplierData is the way in"),

            // The station screens that KEPT their name still lost this one to the new shared base.
            FromBase(Stations + "PackagingStationCanvas", "Canvas", "_canvas", StationCanvas),
            FromBase(Stations + "BrickPressCanvas", "Canvas", "_canvas", StationCanvas),
            FromBase(Stations + "LabOvenCanvas", "Canvas", "_canvas", StationCanvas),
            FromBase(Stations + "MushroomSpawnStationInterface", "Canvas", "_canvas", StationCanvas),

            // 0.4.6 put a service between the lobby and Steam and dropped every CSteamID off the public
            // surface. The values themselves did not go anywhere - each one is still exactly derivable, and
            // the first is derivable from the same expression 0.4.5f2 used.
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = LobbyType,
                OldName = "get_LobbySteamID",
                ParameterCount = 0,
                Because = "Lobby.cs:59 until 0.4.5f2 was `new CSteamID(LobbyID)`, and LobbyID is still here",
                Emit = EmitLobbySteamId,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = LobbyType,
                OldName = "get_LocalPlayerID",
                ParameterCount = 0,
                Because = "the field held this client's own Steam id, which is what SteamUser.GetSteamID() "
                        + "answers; 0.4.6 stopped keeping a copy of it on Lobby",
                Emit = EmitLocalPlayerId,
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = LobbyType,
                OldName = "get_Players",
                ParameterCount = 0,
                Because = "the same ids, rebuilt from GetLobbyMemberIDs() - which SteamLobbyService.cs:178 "
                        + "builds out of the very array this used to be",
                Emit = EmitLobbyPlayers,
            },
            Moved("Il2CppScheduleOne.Economy.Dealer", "DealerType", Read, DealerData, "DealerType",
                  "Dealer.cs held the type itself until 0.4.5f2; 0.4.6 keeps every dealer-specific value "
                + "on DealerNPCData and Dealer.DealerData is the way in"),

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
            NowCalled("Il2CppScheduleOne.UI.StorageMenu", "get_CloseButton", 0, "get_CloseButtonContainer",
                    "the same RectTransform, renamed when the button got its own container "
                  + "(StorageMenu.cs:24)"),
            NowCalled(Inv, "get_PickpocketIntObj", 0, "get__interactable", Pickpocketed),

            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = Storage,
                OldName = "get_onClosed",
                ParameterCount = 0,
                Because = "StorageMenu.cs:35 until 0.4.5f2 was a UnityEvent the menu fired when it closed; "
                        + "0.4.6 keeps a private Action instead, which nothing can subscribe to from "
                        + "outside - so the event is put back and Polyfill fires it",
                Emit = EmitStorageClosedEvent,
            },
            NowCalled(Inv, "set_PickpocketIntObj", 1, "set__interactable", Pickpocketed),

            // A method that LOST an argument, which is the mirror of the three above and needs its own
            // shape: the old form has one parameter too many rather than one too few.
            Dropped(Avatar, "ApplyShapeKeys", new[] { Number, Number }, "System.Boolean", ShapeKeys),

            // One method that became two. See Contract/SplitScreens for why an EMPTY body is the right
            // one and where the other half of the repair lives.
            SplitInTwo(0), SplitInTwo(1), SplitInTwo(2), SplitInTwo(3), SplitInTwo(4),

            // Two static methods 0.4.6 deleted whose every line still exists. Rebuilt rather than pointed
            // somewhere, because there is nowhere to point: the game replaced the CALLS with a different
            // mechanism (PlayerCamera.AddActiveUIElement) and left the five things they did untouched.
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = PlayerType,
                OldName = "Activate",
                ParameterCount = 0,
                Because = PlayerToggle,
                Emit = (module, type) => EmitPlayerToggle(module, type, "Activate", on: true),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = PlayerType,
                OldName = "Deactivate",
                ParameterCount = 1,
                Because = PlayerToggle,
                Emit = (module, type) => EmitPlayerToggle(module, type, "Deactivate", on: false),
            },

            // Methods that gained a trailing parameter. The old form is genuinely gone while the name is
            // not, so these are the only rules allowed to add an overload.
            Defaulted(Camera, "FreeMouse", NoParameters, new object[] { true }, Crosshair),
            Defaulted(Camera, "LockMouse", NoParameters, new object[] { true }, Crosshair),
            // ALL THREE Opens took a callback, and two of them take three arguments in different orders.
            // Only the first was bridged, and because a bridge was matched on the name and the count alone,
            // it answered for the other as well - so a mod calling Open(title, subtitle, owner) was handed
            // Open(owner, title, subtitle) and threw MissingMethodException inside its own try/catch. That
            // is Backpack's B key: "Error toggling backpack: Method not found" (Support #17).
            Defaulted(Storage, "Open", new[] { Owner, Text, Text }, OneCallback,
                      "Open took a callback in 0.4.6 and the old three-argument form passed none "
                    + "(StorageMenu.cs:56)"),
            Defaulted(Storage, "Open", new[] { Text, Text, Owner }, OneCallback,
                      "the same callback, on the overload that names the storage last "
                    + "(StorageMenu.cs:62)"),
            Defaulted(Storage, "Open", new[] { "Il2CppScheduleOne.Storage.StorageEntity" }, OneCallback,
                      "the same callback, on the overload that takes a storage entity "
                    + "(StorageMenu.cs:50)"),

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
                // The old parameter types ARE the caller's signature, so they double as the tie-break
                // between two overloads of the same arity.
                ParameterTypes = leading,
                AllowOverload = true,
                Because = because,
                Emit = (module, type) => EmitWithDefaults(module, type, name, leading, defaults),
            };

        /// <summary>
        /// <c>Player.Activate()</c> and <c>Player.Deactivate(bool)</c>, rebuilt line for line.
        /// </summary>
        /// <remarks>
        /// The only bridge here that reconstructs a whole BODY, and it is allowed because there is nothing
        /// to interpret: all five statements are still on this build, and the pair is symmetric, so the
        /// two methods differ by one boolean. 0.4.5f2:
        /// <code>
        /// Activate()               Deactivate(bool freeMouse)
        ///   camera.SetCanLook(true)  camera.SetCanLook(false); camera.ResetRotation()
        ///   movement.CanMove = true  movement.CanMove = false
        ///   inventory.Set(true)      inventory.Set(false)
        ///   hud.Crosshair(true)      -
        ///   camera.LockMouse()       if (freeMouse) camera.FreeMouse()
        /// </code>
        /// <c>LockMouse</c> and <c>FreeMouse</c> gained a crosshair flag in 0.4.6, and <c>true</c> is what
        /// the call did before it existed - the same reading the Defaulted rule for them already carries.
        ///
        /// WHAT THIS DOES NOT DO, and it belongs in the report rather than in a hope: the GAME does not
        /// call these any more. A mod that CALLS them works exactly as before; a mod that PATCHES them
        /// sees its patch fire only when another mod calls them. The reason to write them anyway is that
        /// Harmony discards a patch CLASS when one target is missing, and Backpack keeps its save and load
        /// hooks in the same class as its Activate patch - so this one gap was taking the backpack's
        /// persistence with it.
        /// </remarks>
        private static MethodDefinition EmitPlayerToggle(ModuleDefinition module, TypeDefinition player,
                                                         string name, bool on)
        {
            var camera = Singleton(module, "Il2CppScheduleOne.PlayerScripts.PlayerCamera", player: true);
            var movement = Singleton(module, "Il2CppScheduleOne.PlayerScripts.PlayerMovement", player: true);
            var inventory = Singleton(module, "Il2CppScheduleOne.PlayerScripts.PlayerInventory", player: true);
            var hud = Singleton(module, "Il2CppScheduleOne.UI.HUD", player: false);
            if (camera.Get == null || movement.Get == null || inventory.Get == null || hud.Get == null)
                return null;

            var look = Method(camera.Type, "SetCanLook", 1);
            var canMove = Method(movement.Type, "set_CanMove", 1);
            var enable = Method(inventory.Type, "SetInventoryEnabled", 1);
            var crosshair = Method(hud.Type, "SetCrosshairVisible", 1);
            var mouse = Method(camera.Type, on ? "LockMouse" : "FreeMouse", 1);
            var reset = Method(camera.Type, "ResetRotation", 0);
            if (look == null || canMove == null || enable == null || crosshair == null || mouse == null
                || (!on && reset == null))
                return null;

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            if (!on)
                method.Parameters.Add(new ParameterDefinition("freeMouse", ParameterAttributes.None,
                                          module.TypeSystem.Boolean));

            var il = method.Body.GetILProcessor();
            var flag = on ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;

            il.Emit(OpCodes.Call, camera.Get);
            il.Emit(flag);
            il.Emit(OpCodes.Callvirt, module.ImportReference(look));

            if (!on)
            {
                il.Emit(OpCodes.Call, camera.Get);
                il.Emit(OpCodes.Callvirt, module.ImportReference(reset));
            }

            il.Emit(OpCodes.Call, movement.Get);
            il.Emit(flag);
            il.Emit(OpCodes.Callvirt, module.ImportReference(canMove));

            il.Emit(OpCodes.Call, inventory.Get);
            il.Emit(flag);
            il.Emit(OpCodes.Callvirt, module.ImportReference(enable));

            if (on)
            {
                il.Emit(OpCodes.Call, hud.Get);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Callvirt, module.ImportReference(crosshair));

                il.Emit(OpCodes.Call, camera.Get);
                il.Emit(OpCodes.Ldc_I4_1);                      // the crosshair flag LockMouse() implied
                il.Emit(OpCodes.Callvirt, module.ImportReference(mouse));
            }
            else
            {
                var skip = il.Create(OpCodes.Ret);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Brfalse, skip);
                il.Emit(OpCodes.Call, camera.Get);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Callvirt, module.ImportReference(mouse));
                il.Append(skip);
                return method;
            }

            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary><c>Singleton&lt;T&gt;.Instance</c> or <c>PlayerSingleton&lt;T&gt;.Instance</c>.</summary>
        private static (TypeDefinition Type, MethodReference Get) Singleton(ModuleDefinition module,
                                                                            string fullName, bool player)
        {
            var type = module.GetType(fullName);
            if (type == null) return (null, null);

            string holder = "Il2CppScheduleOne.DevUtilities." + (player ? "PlayerSingleton`1" : "Singleton`1");
            var open = module.GetType(holder);
            var get = Method(open, "get_Instance", 0);
            if (get == null || !get.IsStatic) return (type, null);

            var instance = new GenericInstanceType(module.ImportReference(open));
            instance.GenericArguments.Add(module.ImportReference(type));
            return (type, Against(module, get, instance));
        }

        /// <summary>
        /// The open-or-close method of one station screen, put back as a place to hang a patch on.
        /// </summary>
        /// <remarks>
        /// THE BODY IS EMPTY AND THAT IS THE POINT. Every other bridge answers a CALL, so its body does
        /// the thing the old member did. Nothing calls this one - 0.4.6 split it into <c>Open</c> and
        /// <c>Close</c> and rewrote its own callers - so a body here would be a second, competing way to
        /// open a station screen, reachable only by a mod that has no idea it exists.
        ///
        /// What it is for is the patch. Harmony discards an entire patch class when one target cannot be
        /// resolved, so this single gap took Backpack's whole canvas handling with it, five times over.
        /// The method existing is what lets those classes register; the postfix in
        /// <c>ModFixes/SplitScreenPatches.cs</c> is what makes them fire, by calling this from the real
        /// <c>Open</c> and <c>Close</c>.
        ///
        /// Refuses unless the type, the station type and both replacements are on this build, so a game
        /// that splits them differently gets nothing rather than an empty method nobody can explain.
        /// </remarks>
        private static Bridge SplitInTwo(int index)
        {
            var entry = Contract.SplitScreens.All[index];
            var parameters = entry.HasRemoveUi
                ? new[] { entry.Station, "System.Boolean", "System.Boolean" }
                : new[] { entry.Station, "System.Boolean" };

            return new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = entry.Type,
                OldName = "SetIsOpen",
                ParameterCount = parameters.Length,
                ParameterTypes = parameters,
                AllowOverload = true,
                Because = "0.4.6 split SetIsOpen into Open and Close; this is where a patch aimed at the "
                        + "old name lands, and Polyfill calls it from both of them",
                Emit = (module, type) => EmitSplitHook(module, type, entry),
            };
        }

        /// <summary>The old signature, exact down to the parameter names, over an empty body.</summary>
        private static MethodDefinition EmitSplitHook(ModuleDefinition module, TypeDefinition type,
                                                      Contract.SplitScreens.Entry entry)
        {
            var station = module.GetType(entry.Station);
            if (station == null) return null;

            // Only worth having where the two methods it stands between actually exist - and they may be
            // one level up, because two of these five screens were ALSO renamed, so the type this is
            // written onto is the stand-in class and Open lives on what it derives from.
            if (MethodUp(type, "Open", 1) == null || MethodUp(type, "Close", 0) == null) return null;

            var method = new MethodDefinition("SetIsOpen",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);

            method.Parameters.Add(new ParameterDefinition(entry.StationName, ParameterAttributes.None,
                                      module.ImportReference(station)));
            method.Parameters.Add(new ParameterDefinition("open", ParameterAttributes.None,
                                      module.TypeSystem.Boolean));
            if (entry.HasRemoveUi)
                method.Parameters.Add(new ParameterDefinition("removeUI", ParameterAttributes.None,
                                          module.TypeSystem.Boolean));

            method.Body.GetILProcessor().Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// The method a mod calls, still there, having stopped taking an argument it used to.
        /// </summary>
        /// <remarks>
        /// The mirror of <see cref="Defaulted"/>, and it needs its own shape rather than a flag on that one:
        /// there the old call is missing an argument the new method wants, here it has one the new method
        /// will not take. Same consequence for the mod - the exact signature it names is gone - and the
        /// opposite repair.
        ///
        /// Only ever correct when the argument's meaning did not move somewhere else, which is a reading of
        /// both bodies and belongs in <paramref name="because"/>.
        /// </remarks>
        /// <param name="kept">The parameter types the new method still takes, by full name.</param>
        /// <param name="dropped">The type of the trailing parameter the old form carried.</param>
        private static Bridge Dropped(string declaringType, string name, string[] kept, string dropped,
                                      string because)
        {
            var old = new string[kept.Length + 1];
            Array.Copy(kept, old, kept.Length);
            old[kept.Length] = dropped;

            return new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = declaringType,
                OldName = name,
                ParameterCount = old.Length,
                ParameterTypes = old,
                AllowOverload = true,
                Because = because,
                Emit = (module, type) => EmitWithoutTrailing(module, type, name, kept, dropped),
            };
        }

        /// <summary>Emits the old signature, calls the shorter one, and lets the last argument fall away.</summary>
        private static MethodDefinition EmitWithoutTrailing(ModuleDefinition module, TypeDefinition type,
                                                            string name, string[] kept, string dropped)
        {
            MethodDefinition target = null;
            foreach (var candidate in type.Methods)
            {
                if (candidate.Name != name || candidate.Parameters.Count != kept.Length) continue;
                bool matches = true;
                for (int i = 0; i < kept.Length; i++)
                    if (candidate.Parameters[i].ParameterType.FullName != kept[i]) { matches = false; break; }
                if (!matches) continue;
                if (target != null) return null;              // more than one; choosing would be a guess
                target = candidate;
            }
            if (target == null || target.HasGenericParameters) return null;

            var extra = module.GetType(dropped) ?? Find(module, dropped);
            if (extra == null) return null;

            var method = new MethodDefinition(name,
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | (target.IsStatic ? MethodAttributes.Static : 0),
                module.ImportReference(target.ReturnType));

            foreach (var parameter in target.Parameters)
                method.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None,
                                          module.ImportReference(parameter.ParameterType)));
            method.Parameters.Add(new ParameterDefinition("dropped", ParameterAttributes.None,
                                      module.ImportReference(extra)));

            var il = method.Body.GetILProcessor();
            if (!target.IsStatic) il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < kept.Length; i++)
                il.Emit(OpCodes.Ldarg, method.Parameters[i]);
            il.Emit(target.IsStatic ? OpCodes.Call : OpCodes.Callvirt, module.ImportReference(target));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>A type by full name, from this module or anything it references.</summary>
        private static TypeReference Find(ModuleDefinition module, string fullName)
        {
            switch (fullName)
            {
                case "System.Boolean": return module.TypeSystem.Boolean;
                case "System.Single": return module.TypeSystem.Single;
                case "System.Int32": return module.TypeSystem.Int32;
                case "System.String": return module.TypeSystem.String;
            }
            return null;
        }

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

        /// <summary>
        /// The storage menu's closing event, put back as something a mod can subscribe to.
        /// </summary>
        /// <remarks>
        /// A TYPE CHANGE RATHER THAN A RENAME, which is why no rule reaches it: <c>public UnityEvent
        /// onClosed</c> became <c>private Action _onClosedCallback</c>, set by whoever calls Open. A mod
        /// that subscribes and unsubscribes around its own screen has nothing to hold on to any more:
        /// <code>
        /// storageMenu.onClosed.AddListener(closeAction);          // OverTheCounter, ManagerSpawner
        /// storageMenu.Open(inventory.Cast&lt;IItemSlotOwner&gt;(), text, "");
        /// </code>
        /// and the missing member throws inside the coroutine, so the cleanup in <c>closeAction</c> never
        /// runs and the NPC it was interacting with stays stuck. Reported exactly that way: "they became
        /// unresponsive".
        ///
        /// A STATIC EVENT IS THE RIGHT SHAPE HERE, and only because of what StorageMenu is: it derives from
        /// <c>Singleton&lt;StorageMenu&gt;</c>, so there is one for the whole game and one event cannot be
        /// confused with another's. A per-instance one would need a table keyed by the native pointer,
        /// because two interop wrappers around the same object are different managed objects and a managed
        /// field on one is invisible to the other.
        ///
        /// Firing it is the other half, and it lives in <c>ModFixes/StorageMenuClosedEvent.cs</c>: the game
        /// no longer has anything that would.
        /// </remarks>
        private static MethodDefinition EmitStorageClosedEvent(ModuleDefinition module, TypeDefinition menu)
        {
            var unityEvent = Referenced(module, "UnityEngine.Events.UnityEvent");
            if (unityEvent == null) return null;

            MethodDefinition constructor = null;
            foreach (var candidate in unityEvent.Methods)
                if (candidate.IsConstructor && candidate.Parameters.Count == 0 && !candidate.IsStatic)
                    constructor = candidate;
            if (constructor == null || Method(unityEvent, "Invoke", 0) == null) return null;

            var type = module.ImportReference(unityEvent);

            // Named so it cannot collide with anything the game or a mod declares, and so a reader of the
            // assembly can see at a glance that it is not the game's.
            var field = new FieldDefinition("<polyfill>onClosed",
                FieldAttributes.Public | FieldAttributes.Static, type);
            menu.Fields.Add(field);

            var method = new MethodDefinition("get_onClosed",
                MethodAttributes.Public | MethodAttributes.HideBySig, type);

            var il = method.Body.GetILProcessor();
            var ready = il.Create(OpCodes.Ret);

            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brtrue, ready);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Newobj, module.ImportReference(constructor));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stsfld, field);
            il.Append(ready);
            return method;
        }

        /// <summary>A type from one of the assemblies this module already references.</summary>
        private static TypeDefinition Referenced(ModuleDefinition module, string fullName)
        {
            var here = module.GetType(fullName);
            if (here != null) return here;

            foreach (var reference in module.AssemblyReferences)
            {
                try
                {
                    var assembly = module.AssemblyResolver?.Resolve(reference);
                    var found = assembly?.MainModule?.GetType(fullName);
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
        }

        /// <summary><c>new CSteamID(this.LobbyID)</c> - the 0.4.5f2 body, unchanged.</summary>
        private static MethodDefinition EmitLobbySteamId(ModuleDefinition module, TypeDefinition lobby)
        {
            var id = Getter(lobby, "LobbyID");
            var steamId = SteamType(module, "CSteamID");
            if (id == null || steamId == null) return null;

            MethodDefinition constructor = null;
            foreach (var candidate in steamId.Methods)
                if (candidate.IsConstructor && candidate.Parameters.Count == 1
                    && candidate.Parameters[0].ParameterType.MetadataType == MetadataType.UInt64)
                    constructor = candidate;
            if (constructor == null) return null;

            var method = new MethodDefinition("get_LobbySteamID",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.ImportReference(steamId));

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(id));
            il.Emit(OpCodes.Newobj, module.ImportReference(constructor));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary><c>SteamUser.GetSteamID()</c>, which is where the removed field's value came from.</summary>
        private static MethodDefinition EmitLocalPlayerId(ModuleDefinition module, TypeDefinition lobby)
        {
            var user = SteamType(module, "SteamUser");
            var steamId = SteamType(module, "CSteamID");
            var get = Method(user, "GetSteamID", 0);
            if (steamId == null || get == null || !get.IsStatic) return null;

            var method = new MethodDefinition("get_LocalPlayerID",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.ImportReference(steamId));

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Call, module.ImportReference(get));
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>
        /// The lobby's Steam ids as an array again, rebuilt from the list that replaced it.
        /// </summary>
        /// <remarks>
        /// <c>SteamLobbyService.cs:178-189</c> builds that list out of its own <c>CSteamID[]</c> by writing
        /// <c>m_SteamID.ToString()</c> for every slot that is not <c>Nil</c>, so reading it back is the
        /// inverse of one method rather than an interpretation of a design.
        ///
        /// ONE DIFFERENCE, AND IT IS WORTH STATING: the old array had a fixed length of four with empty
        /// slots left as <c>CSteamID.Nil</c>, and this one has no holes. Every use of it filters those out
        /// first - the game's own <c>PlayerCount</c> counted non-Nil entries - so a shorter array with the
        /// same members is what the callers were computing anyway. What it is NOT is a stable slot index,
        /// and a mod using <c>Players[2]</c> as "the third player's seat" would be reading something else.
        ///
        /// An id that does not parse is skipped rather than turned into zero: zero is <c>Nil</c>, and Nil
        /// in a list of real players is a member that is not there.
        /// </remarks>
        private static MethodDefinition EmitLobbyPlayers(ModuleDefinition module, TypeDefinition lobby)
        {
            var ids = Method(lobby, "GetLobbyMemberIDs", 0);
            var steamId = SteamType(module, "CSteamID");
            if (ids == null || steamId == null) return null;

            // THE CALLS HAVE TO NAME List<string>, NOT List<T>. Cecil resolves the return type to the open
            // definition, and a reference built from that describes a method on the open generic - which is
            // not a method any instance can be called through. Building both against the instantiation the
            // getter actually returns is the whole difference between IL that runs and IL that does not.
            var listInstance = ids.ReturnType as GenericInstanceType;
            var list = ids.ReturnType?.Resolve();
            var countDefinition = Getter(list, "Count");
            var itemDefinition = Method(list, "get_Item", 1);
            if (listInstance == null || countDefinition == null || itemDefinition == null) return null;

            var text = listInstance.GenericArguments[0];
            var count = Against(module, countDefinition, module.ImportReference(listInstance));
            var item = Against(module, itemDefinition, module.ImportReference(listInstance));
            if (text.MetadataType != MetadataType.String) return null;

            MethodDefinition constructor = null;
            foreach (var candidate in steamId.Methods)
                if (candidate.IsConstructor && candidate.Parameters.Count == 1
                    && candidate.Parameters[0].ParameterType.MetadataType == MetadataType.UInt64)
                    constructor = candidate;
            if (constructor == null) return null;

            var parse = TryParse(module);
            var array = StructArray(module, steamId, out var arraySize, out var arraySet);
            if (parse == null || array == null || arraySize == null || arraySet == null) return null;

            var method = new MethodDefinition("get_Players",
                MethodAttributes.Public | MethodAttributes.HideBySig, array);

            var listSlot = new VariableDefinition(module.ImportReference(ids.ReturnType));
            var resultSlot = new VariableDefinition(array);
            var readSlot = new VariableDefinition(module.TypeSystem.Int32);
            var writeSlot = new VariableDefinition(module.TypeSystem.Int32);
            var valueSlot = new VariableDefinition(module.TypeSystem.UInt64);
            foreach (var slot in new[] { listSlot, resultSlot, readSlot, writeSlot, valueSlot })
                method.Body.Variables.Add(slot);
            method.Body.InitLocals = true;

            var il = method.Body.GetILProcessor();
            var empty = il.Create(OpCodes.Ldnull);
            var test = il.Create(OpCodes.Ldloc, readSlot);
            var next = il.Create(OpCodes.Ldloc, readSlot);
            var body = il.Create(OpCodes.Nop);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, module.ImportReference(ids));
            il.Emit(OpCodes.Stloc, listSlot);
            il.Emit(OpCodes.Ldloc, listSlot);
            il.Emit(OpCodes.Brfalse, empty);

            // A list of n ids can yield at most n usable ones, so one allocation of that size is enough and
            // the unused tail stays Nil - which is what the old array's spare slots held.
            il.Emit(OpCodes.Ldloc, listSlot);
            il.Emit(OpCodes.Callvirt, count);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Newobj, arraySize);
            il.Emit(OpCodes.Stloc, resultSlot);

            il.Emit(OpCodes.Br, test);
            il.Append(body);
            il.Emit(OpCodes.Ldloc, listSlot);
            il.Emit(OpCodes.Ldloc, readSlot);
            il.Emit(OpCodes.Callvirt, item);
            il.Emit(OpCodes.Ldloca, valueSlot);
            il.Emit(OpCodes.Call, parse);
            il.Emit(OpCodes.Brfalse, next);

            il.Emit(OpCodes.Ldloc, resultSlot);
            il.Emit(OpCodes.Ldloc, writeSlot);
            il.Emit(OpCodes.Ldloc, valueSlot);
            il.Emit(OpCodes.Newobj, module.ImportReference(constructor));
            il.Emit(OpCodes.Callvirt, arraySet);
            il.Emit(OpCodes.Ldloc, writeSlot);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, writeSlot);

            il.Append(next);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, readSlot);

            il.Append(test);
            il.Emit(OpCodes.Ldloc, listSlot);
            il.Emit(OpCodes.Callvirt, count);
            il.Emit(OpCodes.Blt, body);

            il.Emit(OpCodes.Ldloc, resultSlot);
            il.Emit(OpCodes.Ret);

            il.Append(empty);
            il.Emit(OpCodes.Ret);
            return method;
        }

        /// <summary>A type out of the Steamworks interop assembly, or null when it is not installed.</summary>
        private static TypeDefinition SteamType(ModuleDefinition module, string name)
        {
            foreach (var reference in module.AssemblyReferences)
            {
                if (reference.Name.IndexOf("steamworks", StringComparison.OrdinalIgnoreCase) < 0) continue;
                try
                {
                    var assembly = module.AssemblyResolver?.Resolve(reference);
                    var found = assembly?.MainModule?.GetType("Il2CppSteamworks." + name);
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
        }

        /// <summary><c>ulong.TryParse(string, out ulong)</c>.</summary>
        private static MethodReference TryParse(ModuleDefinition module)
        {
            var type = module.TypeSystem.UInt64.Resolve();
            if (type == null) return null;
            foreach (var candidate in type.Methods)
                if (candidate.Name == "TryParse" && candidate.IsStatic && candidate.Parameters.Count == 2
                    && candidate.Parameters[0].ParameterType.MetadataType == MetadataType.String
                    && candidate.Parameters[1].ParameterType.IsByReference)
                    return module.ImportReference(candidate);
            return null;
        }

        /// <summary>
        /// <c>Il2CppStructArray&lt;T&gt;</c>, plus the two members needed to fill one.
        /// </summary>
        /// <remarks>
        /// FOUND BY ITS OWN NAME, NOT BY ITS ADDRESS, and that is not fussiness. The plugin is checked in
        /// CI for any mention of the interop RUNTIME - it loads before the game exists, and a reference to
        /// that assembly would be a boot failure rather than a bug report. Writing the namespace down even
        /// as a string trips that check, correctly: a byte scan cannot tell a literal from a reference, and
        /// the one thing worse than a false alarm there is a rule people learn to work around.
        ///
        /// Searching the assembly Il2CppInterop generated the game against also survives that namespace
        /// moving, which naming it would not.
        /// </remarks>
        private static TypeReference StructArray(ModuleDefinition module, TypeDefinition element,
                                                 out MethodReference size, out MethodReference set)
        {
            size = null; set = null;

            TypeDefinition array = null;
            foreach (var reference in module.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("Il2CppInterop", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var assembly = module.AssemblyResolver?.Resolve(reference);
                    foreach (var candidate in assembly?.MainModule?.Types
                                              ?? (IEnumerable<TypeDefinition>)Array.Empty<TypeDefinition>())
                        if (candidate.Name == StructArrayName) { array = candidate; break; }
                }
                catch { }
                if (array != null) break;
            }
            if (array == null || array.GenericParameters.Count != 1) return null;

            var imported = module.ImportReference(array);
            if (imported == null) return null;

            var instance = new GenericInstanceType(imported);
            instance.GenericArguments.Add(module.ImportReference(element));

            MethodDefinition sizeDefinition = null, setDefinition = null;
            foreach (var candidate in array.Methods)
            {
                if (candidate.IsConstructor && candidate.Parameters.Count == 1
                    && candidate.Parameters[0].ParameterType.MetadataType == MetadataType.Int64)
                    sizeDefinition = candidate;
                if (candidate.Name == "set_Item" && candidate.Parameters.Count == 2)
                    setDefinition = candidate;
            }
            if (sizeDefinition == null) return null;

            // set_Item is declared on the base Il2CppArrayBase<T>, so look one level up when it is not here.
            if (setDefinition == null)
            {
                TypeDefinition above = null;
                try { above = array.BaseType?.Resolve(); } catch { }
                if (above == null || above.GenericParameters.Count != 1) return null;

                foreach (var candidate in above.Methods)
                    if (candidate.Name == "set_Item" && candidate.Parameters.Count == 2) setDefinition = candidate;
                if (setDefinition == null) return null;

                var baseInstance = new GenericInstanceType(module.ImportReference(above));
                baseInstance.GenericArguments.Add(module.ImportReference(element));
                set = Against(module, setDefinition, baseInstance);
            }
            else set = Against(module, setDefinition, instance);

            size = Against(module, sizeDefinition, instance);
            return instance;
        }

        /// <summary>
        /// The same method, named against a generic instantiation instead of the open type.
        /// </summary>
        /// <remarks>
        /// THE SIGNATURE KEEPS T AND THE DECLARING TYPE CARRIES THE ARGUMENT. That is the metadata rule for
        /// a member reference on a generic instance, and getting it backwards fails twice over. Substituting
        /// by hand into the signature produced
        /// <c>MissingMethodException: 'System.String List`1.get_Item(Int32)'</c> - the runtime looks for a
        /// method whose signature says <c>!0</c> and finds none saying <c>System.String</c>. Importing the
        /// bare parameter instead throws inside Cecil's own importer, which has no context to resolve it
        /// against. So neither is touched: the types are taken from the definition exactly as written, and
        /// only the owner is the instantiation.
        /// </remarks>
        private static MethodReference Against(ModuleDefinition module, MethodDefinition method,
                                               TypeReference owner)
        {
            var reference = new MethodReference(method.Name, method.ReturnType, owner)
            {
                HasThis = method.HasThis,
                ExplicitThis = method.ExplicitThis,
                CallingConvention = method.CallingConvention,
            };

            foreach (var parameter in method.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            foreach (var parameter in method.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(parameter.Name, reference));
            return reference;
        }

        /// <summary>
        /// The value moved up to a base class and took a new name with it.
        /// </summary>
        /// <remarks>
        /// Inheritance alone needs no repair - the runtime walks the hierarchy, which is why an inherited
        /// member is not reported missing at all. This is the case where the game ALSO renamed it on the
        /// way up: 0.4.6 pulled <c>Canvas</c> off every station screen into
        /// <c>StationInterface&lt;T&gt;._canvas</c>, so the old name resolves nowhere and the new one is not
        /// on the type the mod names.
        /// </remarks>
        private static Bridge FromBase(string declaringType, string oldName, string baseName, string because)
            => new()
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = declaringType,
                OldName = "get_" + oldName,
                ParameterCount = 0,
                Because = because,
                Emit = (module, type) => EmitFromBase(module, type, "get_" + oldName, "get_" + baseName),
            };

        /// <summary>
        /// Emits <c>this.&lt;base&gt;.Target</c>, which is one instruction and one careful reference.
        /// </summary>
        /// <remarks>
        /// THE BASE IS A GENERIC INSTANCE and that is the whole difficulty. <c>PackagingStationCanvas</c>
        /// derives from <c>StationInterface&lt;PackagingStationCanvas&gt;</c>, so a call to the member has
        /// to name that instantiation rather than the open type - the definition Cecil resolves to. Hence
        /// the hand-built MethodReference against <c>type.BaseType</c>, which already carries the arguments.
        ///
        /// Refuses on anything it cannot state exactly: a target further than the immediate base under a
        /// second set of generic arguments, or a value whose type IS a generic parameter. Both are
        /// substitutions, and a substitution done wrong emits IL that verifies and returns the wrong thing.
        /// </remarks>
        private static MethodDefinition EmitFromBase(ModuleDefinition module, TypeDefinition type,
                                                     string accessor, string target)
        {
            var baseReference = type?.BaseType;
            if (baseReference == null) return null;

            TypeDefinition baseType = null;
            try { baseType = baseReference.Resolve(); } catch { }

            var found = Method(baseType, target, 0);
            if (found == null || found.IsStatic || found.ReturnType == null) return null;
            if (found.ReturnType.ContainsGenericParameter) return null;

            var returns = module.ImportReference(found.ReturnType);
            var call = new MethodReference(found.Name, returns, module.ImportReference(baseReference))
            {
                HasThis = true,
            };

            var method = new MethodDefinition(accessor,
                MethodAttributes.Public | MethodAttributes.HideBySig, returns);

            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, call);
            il.Emit(OpCodes.Ret);
            return method;
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

        /// <summary>A method on this type or anything it derives from.</summary>
        private static MethodDefinition MethodUp(TypeDefinition type, string name, int parameters)
        {
            for (var current = type; current != null; )
            {
                var found = Method(current, name, parameters);
                if (found != null) return found;

                TypeDefinition next = null;
                try { next = current.BaseType?.Resolve(); } catch { }
                if (next == current) return null;
                current = next;
            }
            return null;
        }

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
