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
        private static TypeReference ReferenceArray(ModuleDefinition module)
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
