using Mono.Cecil;
using Mono.Cecil.Cil;
using static Polyfill.Bridges.Shapes;

namespace Polyfill.Bridges.Steps.S0_4_6f13_To_0_4_7f5
{
    internal sealed class Production : BridgeSet
    {
        internal override string Step => "0.4.6f13 -> 0.4.7f5 (production)";
        internal override string From => "0.4.7f5";
        internal override string VerifiedTo => "0.4.7f12";

        private static readonly string[] TrashArguments =
        {
            "System.String", "UnityEngine.Vector3", "UnityEngine.Quaternion",
            "UnityEngine.Vector3", "System.String", "System.Boolean",
        };

        internal override IEnumerable<Bridge> Declare() => new[]
        {
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.NPCs.NPCMovement",
                OldName = "get_npc",
                ParameterCount = 0,
                Unprompted = true,
                Because = "NPCMovement.npc (NPCMovement.cs:120 on 0.4.6f13) is now _npc "
                        + "(IL2CPP NPCMovement.cs:1720 on 0.4.7f12); ProduceMore reads it through AccessTools.Property, "
                        + "so its request has no member reference for the load check to find",
                Emit = (module, type) => Forwards.EmitGetterRename(module, type, "get_npc", "_npc"),
            },
            new Bridge
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = "Il2CppScheduleOne.Trash.TrashManager",
                OldName = "CreateTrashItem",
                ParameterCount = 6,
                ParameterTypes = TrashArguments,
                AllowOverload = true,
                Because = "0.4.7's five-argument CreateTrashItem sends the same item and returns its local copy "
                        + "(TrashManager.cs:154-161 on 0.4.7f7); the old startKinematic flag only changed that local "
                        + "copy (TrashManager.cs:120-127 and :150-175 on 0.4.6f13), so it is retained on the returned Rigidbody",
                Emit = EmitTrashItem,
            },
        };

        private static MethodDefinition EmitTrashItem(ModuleDefinition module, TypeDefinition manager)
        {
            var candidates = manager.Methods.Where(m => m.Name == "CreateTrashItem" && !m.IsStatic
                && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(TrashArguments.Take(5))).ToArray();
            if (candidates.Length != 1) return null;
            var target = candidates[0];
            var body = Getter(target.ReturnType.Resolve(), "_rigidbody");
            var kinematic = body == null ? null : Method(body.ReturnType.Resolve(), "set_isKinematic", 1);
            if (body == null || kinematic == null
                || kinematic.Parameters[0].ParameterType.MetadataType != MetadataType.Boolean) return null;

            var method = new MethodDefinition("CreateTrashItem", MethodAttributes.Public | MethodAttributes.HideBySig,
                module.ImportReference(target.ReturnType));
            foreach (var parameter in target.Parameters)
                method.Parameters.Add(new ParameterDefinition(parameter.Name, ParameterAttributes.None,
                    module.ImportReference(parameter.ParameterType)));
            method.Parameters.Add(new ParameterDefinition("startKinematic", ParameterAttributes.None, module.TypeSystem.Boolean));
            var item = new VariableDefinition(module.ImportReference(target.ReturnType));
            method.Body.Variables.Add(item);
            method.Body.InitLocals = true;
            var il = method.Body.GetILProcessor();
            var done = il.Create(OpCodes.Ldloc, item);
            il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < target.Parameters.Count; i++) il.Emit(OpCodes.Ldarg, method.Parameters[i]);
            il.Emit(OpCodes.Call, module.ImportReference(target));
            il.Emit(OpCodes.Stloc, item);
            il.Emit(OpCodes.Ldarg, method.Parameters[5]);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, item);
            il.Emit(OpCodes.Brfalse, done);
            il.Emit(OpCodes.Ldloc, item);
            il.Emit(OpCodes.Call, module.ImportReference(body));
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(kinematic));
            il.Append(done);
            il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
