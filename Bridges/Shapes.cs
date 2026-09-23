using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Bridges
{
    /// <summary>
    /// The shapes a bridge is built from, shared by every step.
    /// </summary>
    /// <remarks>
    /// A step folder holds the decisions - which member, why, with which value, cited against the source.
    /// How a decision turns into IL is the same for every game version, so it lives here once. A second
    /// copy per step would drift, and a fix to one would leave the other emitting the old mistake.
    /// </remarks>
    internal static class Shapes
    {
        /// <summary>
        /// The short form of an overload the game took away, calling the long one it kept.
        /// </summary>
        /// <param name="leading">The old parameter types, by full name - the caller's signature, which also
        /// tells two overloads of the same arity apart.</param>
        /// <param name="defaults">What each new trailing parameter gets. It has to be what the method did
        /// before the parameter existed, read out of the game's own source and cited in the rule - an
        /// interop assembly carries no default values to read it from.</param>
        internal static Bridge Defaulted(string declaringType, string name, string[] leading,
                                         object[] defaults, string because)
            => new()
            {
                Assembly = "Assembly-CSharp",
                DeclaringType = declaringType,
                OldName = name,
                ParameterCount = leading.Length,
                ParameterTypes = leading,
                AllowOverload = true,
                Because = because,
                Emit = (module, type) => EmitWithDefaults(module, type, name, leading, defaults),
            };

        /// <summary>The short form of an overload, calling the long one with the values it used to imply.</summary>
        internal static MethodDefinition EmitWithDefaults(ModuleDefinition module, TypeDefinition type,
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
        internal static bool PushConstant(ILProcessor il, object value, TypeReference expected)
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

        /// <summary>Pushes the zero value of <paramref name="type"/>, whatever kind of type it is.</summary>
        internal static void EmitDefault(MethodDefinition method, ILProcessor il, TypeReference type)
        {
            if (!type.IsValueType) { il.Emit(OpCodes.Ldnull); return; }

            var slot = new VariableDefinition(type);
            method.Body.Variables.Add(slot);
            il.Emit(OpCodes.Ldloca_S, slot);
            il.Emit(OpCodes.Initobj, type);
            il.Emit(OpCodes.Ldloc_S, slot);
        }

        internal static MethodDefinition Getter(TypeDefinition type, string member)
            => Method(type, "get_" + member, 0);

        /// <summary>A method on this type or anything it derives from.</summary>
        internal static MethodDefinition MethodUp(TypeDefinition type, string name, int parameters)
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

        internal static MethodDefinition Method(TypeDefinition type, string name, int parameters)
        {
            if (type == null) return null;
            foreach (var method in type.Methods)
                if (method.Name == name && method.Parameters.Count == parameters) return method;
            return null;
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
        internal static MethodReference Against(ModuleDefinition module, MethodDefinition method,
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

        internal static TypeDefinition Nested(TypeDefinition type, string name)
        {
            if (type == null) return null;
            foreach (var nested in type.NestedTypes)
                if (nested.Name == name) return nested;
            return null;
        }
    }
}
