using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Check
{
    /// <summary>
    /// Methods a mod names by STRING: Harmony targets and reflection lookups.
    /// </summary>
    /// <remarks>
    /// A member reference is checked by the compiler's own metadata. A name in a string is not: a renamed
    /// Harmony target compiles clean and fails at <c>PatchAll</c>, and that failure takes the whole patch
    /// class with it. These are the two shapes that cover nearly every mod:
    ///
    /// <c>[HarmonyPatch(typeof(T), "Name")]</c>, on the class, on the method, or split across both; and a
    /// lookup in code - <c>AccessTools.Method(typeof(T), "Name")</c>, <c>typeof(T).GetMethod("Name")</c> -
    /// read off the instructions as the <c>ldtoken</c> and <c>ldstr</c> right before the call.
    ///
    /// A target built any other way (a <c>TargetMethod()</c>, a name from a variable) is counted and not
    /// guessed at.
    /// </remarks>
    internal static class PatchTargets
    {
        internal sealed class Target
        {
            internal TypeReference Type;
            internal string Name;
            internal int ParameterCount = -1;     // -1: the mod named no parameters
            internal string From;
            internal bool InCode;                 // a lookup in a method body, not an attribute
        }

        private const string HarmonyPatch = "HarmonyLib.HarmonyPatch";

        // MethodType as HarmonyLib numbers it.
        private const int Getter = 1, Setter = 2, Constructor = 3, StaticConstructor = 4;

        internal static List<Target> Read(ModuleDefinition module, out int unprovable)
        {
            var found = new List<Target>();
            unprovable = 0;
            foreach (var type in All(module.Types))
            {
                // An attribute whose enum argument lives in an assembly the resolver cannot find throws on
                // read. That class is named and counted rather than taking the whole check down with it.
                try { FromAttributes(type, found, ref unprovable); }
                catch (AssemblyResolutionException e)
                {
                    unprovable++;
                    Console.Error.WriteLine($"warning: patch attributes on {type.FullName} not read, "
                                          + $"{e.AssemblyReference.Name} was not found (pass --refs <folder>)");
                }
                foreach (var method in type.Methods)
                    if (method.HasBody) FromCode(method, found);
            }
            return found;
        }

        private static IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types)
        {
            foreach (var type in types)
            {
                yield return type;
                foreach (var nested in All(type.NestedTypes)) yield return nested;
            }
        }

        private sealed class Partial
        {
            internal TypeReference Type;
            internal string Name;
            internal int Kind;
            internal int ParameterCount = -1;

            internal Partial Over(Partial inner) => new()
            {
                Type = inner.Type ?? Type,
                Name = inner.Name ?? Name,
                Kind = inner.Kind != 0 ? inner.Kind : Kind,
                ParameterCount = inner.ParameterCount >= 0 ? inner.ParameterCount : ParameterCount,
            };
        }

        private static void FromAttributes(TypeDefinition type, List<Target> found, ref int unprovable)
        {
            var onClass = Merge(type.CustomAttributes);
            if (onClass == null) return;

            if (type.Methods.Any(m => m.Name is "TargetMethod" or "TargetMethods"))
            {
                unprovable++;
                return;
            }

            bool perMethod = false;
            foreach (var method in type.Methods)
            {
                var own = Merge(method.CustomAttributes);
                if (own == null) continue;
                perMethod = true;
                Add(onClass.Over(own), $"[HarmonyPatch] on {type.FullName}::{method.Name}", found, ref unprovable);
            }
            if (!perMethod) Add(onClass, $"[HarmonyPatch] on {type.FullName}", found, ref unprovable);
        }

        private static Partial Merge(IEnumerable<CustomAttribute> attributes)
        {
            Partial merged = null;
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.FullName != HarmonyPatch) continue;
                merged ??= new Partial();
                foreach (var argument in attribute.ConstructorArguments)
                {
                    switch (argument.Value)
                    {
                        case TypeReference t: merged.Type ??= t; break;
                        case string s: merged.Name = s; break;
                        case CustomAttributeArgument[] list
                            when argument.Type is ArrayType array && array.ElementType.FullName == "System.Type":
                            merged.ParameterCount = list.Length;
                            break;
                        default:
                            if (argument.Type.FullName == "HarmonyLib.MethodType" && argument.Value is int kind)
                                merged.Kind = kind;
                            break;
                    }
                }
            }
            return merged;
        }

        private static void Add(Partial target, string from, List<Target> found, ref int unprovable)
        {
            if (target.Type == null) { unprovable++; return; }
            string name = target.Kind switch
            {
                Getter when target.Name != null => "get_" + target.Name,
                Setter when target.Name != null => "set_" + target.Name,
                Constructor => ".ctor",
                StaticConstructor => ".cctor",
                _ => target.Name,
            };
            if (name == null) { unprovable++; return; }
            found.Add(new Target { Type = target.Type, Name = name, ParameterCount = target.ParameterCount, From = from });
        }

        /// <summary>The lookups in a method body whose type and name are both constants.</summary>
        private static void FromCode(MethodDefinition method, List<Target> found)
        {
            var code = method.Body.Instructions;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].OpCode.Code is not (Code.Call or Code.Callvirt)) continue;
                if (code[i].Operand is not MethodReference call) continue;

                string prefix = Lookup(call);
                if (prefix == null) continue;

                // Walk back to the name, passing a parameter array on the way, and accept the type only as
                // `ldtoken T; call GetTypeFromHandle; ldstr "Name"` with nothing in between. Anything
                // looser reads `typeof(int)` out of the parameter array as the target, or the type of the
                // statement before when the mod passed a variable.
                string name = null;
                TypeReference type = null;
                int parameters = -1;
                for (int back = i - 1; back >= 2 && back >= i - 40; back--)
                {
                    var op = code[back];
                    if (op.OpCode.Code == Code.Newarr && op.Operand is TypeReference element
                        && element.FullName == "System.Type" && Constant(code[back - 1], out int n))
                        parameters = n;
                    if (op.OpCode.Code != Code.Ldstr) continue;

                    name = (string)op.Operand;
                    if (code[back - 1].Operand is MethodReference handle && handle.Name == "GetTypeFromHandle"
                        && code[back - 2].OpCode.Code == Code.Ldtoken && code[back - 2].Operand is TypeReference t)
                        type = t;
                    break;
                }
                if (name == null || type == null) continue;

                found.Add(new Target
                {
                    Type = type, Name = prefix + name, ParameterCount = parameters, InCode = true,
                    From = $"{call.DeclaringType.Name}.{call.Name} in {method.DeclaringType.FullName}::{method.Name}",
                });
            }
        }

        /// <summary>The accessor prefix a lookup adds to the name it is given, or null when it is not one.</summary>
        private static string Lookup(MethodReference call)
        {
            string owner = call.DeclaringType.FullName;
            if (owner == "HarmonyLib.AccessTools")
                return call.Name switch
                {
                    "Method" or "DeclaredMethod" => "",
                    "PropertyGetter" or "DeclaredPropertyGetter" => "get_",
                    "PropertySetter" or "DeclaredPropertySetter" => "set_",
                    _ => null,
                };
            if (owner == "System.Type" && call.Name == "GetMethod") return "";
            return null;
        }

        private static bool Constant(Instruction op, out int value)
        {
            value = op.OpCode.Code switch
            {
                Code.Ldc_I4_0 => 0, Code.Ldc_I4_1 => 1, Code.Ldc_I4_2 => 2, Code.Ldc_I4_3 => 3,
                Code.Ldc_I4_4 => 4, Code.Ldc_I4_5 => 5, Code.Ldc_I4_6 => 6, Code.Ldc_I4_7 => 7,
                Code.Ldc_I4_8 => 8,
                Code.Ldc_I4_S => (sbyte)op.Operand,
                Code.Ldc_I4 => (int)op.Operand,
                _ => -1,
            };
            return value >= 0;
        }
    }
}
