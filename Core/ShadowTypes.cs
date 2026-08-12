using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Core
{
    /// <summary>
    /// The type that moved without leaving the assembly - put back as a class of its own.
    /// </summary>
    /// <remarks>
    /// A type that moved to ANOTHER assembly is an <c>ExportedType</c> row and the CLR does the rest. A type
    /// that only changed namespace cannot be: a forwarder says "this name lives somewhere else", and pointing
    /// one at the assembly it already sits in leaves the loader nowhere to go. Measured, and it fails hard -
    /// the name stays unresolvable and the process dies at the mod's compiled call instead of at a lookup.
    ///
    /// What works instead is a real type under the old name, deriving from the new one. It is not a copy and
    /// holds nothing of its own: every interop class is a managed shell around a native pointer, so a subclass
    /// built from that same pointer IS the object, reachable under both names. Assignment to anything
    /// expecting the new type is then plain inheritance and needs no conversion at all.
    ///
    /// What it does not give back is construction. Only the pointer constructor is rebuilt, because that is
    /// the only one whose meaning is beyond doubt; a mod that calls <c>new</c> on the old name still finds
    /// nothing, and that is reported rather than approximated.
    /// </remarks>
    internal static class ShadowTypes
    {
        /// <summary>Puts <paramref name="oldNamespace"/>.<paramref name="oldName"/> back as a subclass of
        /// where the type lives now, or says why it cannot.</summary>
        internal static TypeDefinition TryAdd(ModuleDefinition module, string oldNamespace, string oldName,
                                              string targetFullName, out string refusal)
        {
            refusal = null;

            var target = targetFullName == null ? null : module.GetType(targetFullName);
            if (target == null)
            { refusal = "the type it became is not in this assembly after all"; return null; }
            if (target.IsInterface || target.IsEnum || target.IsValueType)
            { refusal = $"{target.FullName} is not a class"; return null; }
            if (target.IsSealed)
            { refusal = $"{target.FullName} is sealed, so nothing can stand in for it"; return null; }
            if (target.HasGenericParameters)
            { refusal = $"{target.FullName} is generic"; return null; }

            var fromPointer = PointerConstructor(target);
            if (fromPointer == null)
            { refusal = $"{target.FullName} has no pointer constructor to build on"; return null; }

            var shadow = new TypeDefinition(oldNamespace, oldName,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
                module.ImportReference(target));

            var constructor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            constructor.Parameters.Add(new ParameterDefinition("pointer", ParameterAttributes.None,
                                                               module.TypeSystem.IntPtr));

            var il = constructor.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, module.ImportReference(fromPointer));
            il.Emit(OpCodes.Ret);

            shadow.Methods.Add(constructor);
            module.Types.Add(shadow);
            return shadow;
        }

        private static MethodDefinition PointerConstructor(TypeDefinition type)
        {
            foreach (var candidate in type.Methods)
                if (candidate.IsConstructor && !candidate.IsStatic && candidate.Parameters.Count == 1
                    && candidate.Parameters[0].ParameterType.MetadataType == MetadataType.IntPtr)
                    return candidate;
            return null;
        }

        /// <summary>
        /// <c>Il2CppObjectBase.Pointer</c>, found by walking up rather than by name, so it keeps working when
        /// Il2CppInterop moves it.
        /// </summary>
        internal static MethodDefinition PointerGetter(TypeDefinition type)
        {
            for (var current = type; current != null; )
            {
                foreach (var method in current.Methods)
                    if (method.Name == "get_Pointer" && method.Parameters.Count == 0) return method;
                TypeDefinition next = null;
                try { next = current.BaseType?.Resolve(); } catch { }
                if (next == current) return null;
                current = next;
            }
            return null;
        }

        /// <summary>A method of a generic type, named through one instantiation of it.</summary>
        internal static MethodReference On(GenericInstanceType instance, MethodDefinition definition)
        {
            var reference = new MethodReference(definition.Name, definition.ReturnType, instance)
            {
                HasThis = definition.HasThis,
                ExplicitThis = definition.ExplicitThis,
                CallingConvention = definition.CallingConvention,
            };
            // The definition's own parameter types, which for a generic type are !0, !1 - Cecil substitutes
            // them against the instance when it writes the reference. Passing the substituted types here
            // instead produces a signature no method has.
            foreach (var parameter in definition.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            return reference;
        }
    }
}
