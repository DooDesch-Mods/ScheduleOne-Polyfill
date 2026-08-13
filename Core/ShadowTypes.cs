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
                                              string targetFullName, out string refusal,
                                              string targetAssembly = null)
        {
            refusal = null;

            var target = Resolve(module, targetFullName, targetAssembly);
            if (target == null)
            { refusal = "the type it became is not where it was said to be"; return null; }
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
            Made[target.FullName] = shadow;
            return shadow;
        }

        /// <summary>
        /// What this pass has put back, keyed by the full name of the type it stands in for.
        /// </summary>
        /// <remarks>
        /// A record of what WE made, deliberately, rather than a search for "a type deriving from that one".
        /// The game has plenty of its own subclasses, and handing a mod one of those under the old name
        /// would be a wrong repair dressed as a right one. Cleared per module by <see cref="Begin"/>, and
        /// safe to keep only because each pass starts from the untouched copy, so no shadow outlives it.
        /// </remarks>
        private static readonly Dictionary<string, TypeDefinition> Made
            = new(StringComparer.Ordinal);

        internal static void Begin() => Made.Clear();

        /// <summary>The shadow standing in for <paramref name="type"/>, if this pass made one.</summary>
        internal static TypeDefinition Shadowing(ModuleDefinition module, TypeReference type)
            => type != null && Made.TryGetValue(type.FullName, out var shadow) ? shadow : null;

        /// <summary>
        /// Is a renamed type buried inside this one, where standing in for it would not work?
        /// </summary>
        /// <remarks>
        /// A shadow can be handed to anything expecting the type it derives from, which is what makes a
        /// plain parameter or return value work. Wrapped in something else it stops being true:
        /// <c>List&lt;Old&gt;</c> is not a <c>List&lt;New&gt;</c>, because a generic class is invariant, and
        /// <c>Old[]</c> is covariant to <c>New[]</c> only until the callee stores a plain New in it and the
        /// array throws. Both are refused with a reason rather than emitted and hoped for.
        /// </remarks>
        internal static bool BuriesAShadow(TypeReference type)
        {
            if (type == null) return false;

            if (type is GenericInstanceType generic)
            {
                foreach (var argument in generic.GenericArguments)
                    if (Made.ContainsKey(argument.FullName) || BuriesAShadow(argument)) return true;
                return false;
            }

            var element = (type as ArrayType)?.ElementType ?? (type as ByReferenceType)?.ElementType;
            if (element == null) return false;
            return Made.ContainsKey(element.FullName) || BuriesAShadow(element);
        }

        /// <summary>
        /// Turn the value on the stack into the shadow around the same pointer. Null stays null.
        /// </summary>
        /// <remarks>
        /// The null branch is not politeness. <c>Il2CppObjectBase.Pointer</c> on a null reference throws,
        /// and a getter that legitimately answers "there is no weather yet" would become a crash at the one
        /// moment a mod is most likely to ask.
        /// </remarks>
        internal static bool EmitRewrap(ModuleDefinition module, ILProcessor il, TypeDefinition shadow,
                                        out string refusal)
        {
            refusal = null;

            var pointer = PointerGetter(shadow);
            var constructor = PointerConstructor(shadow);
            if (pointer == null) { refusal = "Il2CppObjectBase.Pointer is not on it"; return false; }
            if (constructor == null) { refusal = "the shadow has no pointer constructor"; return false; }

            var keepNull = il.Create(OpCodes.Ret);

            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse_S, keepNull);          // null in, null out

            il.Emit(OpCodes.Call, module.ImportReference(pointer));
            il.Emit(OpCodes.Newobj, module.ImportReference(constructor));
            il.Emit(OpCodes.Ret);

            il.Append(keepNull);                            // the duplicate is the null being returned
            return true;
        }

        /// <summary>
        /// The type a name became, in this assembly or in the one it moved to.
        /// </summary>
        /// <remarks>
        /// The cross-assembly half is what makes a moved-AND-renamed type repairable at all. A forwarder
        /// cannot carry a new name, so the only thing left is a class here that derives from the class
        /// there - and for that the target has to be read out of the other file. Cecil imports across
        /// assemblies on its own and adds the reference; all that is needed is the definition.
        /// </remarks>
        internal static TypeDefinition Resolve(ModuleDefinition module, string fullName, string assembly)
        {
            if (string.IsNullOrEmpty(fullName)) return null;

            var here = module.GetType(fullName);
            if (here != null) return here;
            if (string.IsNullOrEmpty(assembly)
                || string.Equals(assembly, module.Assembly?.Name?.Name, StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                // Interop assemblies are all 0.0.0.0 and unsigned, so a bare name is the whole identity.
                var reference = new AssemblyNameReference(assembly, new Version(0, 0, 0, 0));
                return module.AssemblyResolver?.Resolve(reference)?.MainModule?.GetType(fullName);
            }
            catch { return null; }
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
