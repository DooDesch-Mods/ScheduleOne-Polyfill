using Mono.Cecil;

namespace Polyfill.Core
{
    /// <summary>
    /// The patch sites in one class, formed the way Harmony forms them.
    /// </summary>
    /// <remarks>
    /// Harmony patches the PATCH METHODS of a class, never the class. Each one aims at the class's
    /// <c>[HarmonyPatch]</c> merged with its own, and the method's values win (HarmonyX
    /// PatchClassProcessor: <c>patchMethod.info = containerAttributes.Merge(patchMethod.info)</c>, then
    /// PatchWithAttributes patches each method's merged target). A name on the class is therefore a
    /// target only for the patch methods that do not name their own.
    ///
    /// Reading the class name as a site of its own reported TotalDailyProfit 1.4.1 as blocked over
    /// <c>DailySummary::DailySummary</c>: the class says <c>[HarmonyPatch(typeof(DailySummary),
    /// "DailySummary")]</c>, its only Postfix says <c>[HarmonyPatch("Open")]</c>, and Harmony patches
    /// <c>Open</c>.
    ///
    /// Kept apart from HarmonyTargets because it needs nothing but Cecil, so the rule can be tested
    /// without the interop index.
    /// </remarks>
    internal static class PatchSites
    {
        internal const string PatchAttribute = "HarmonyLib.HarmonyPatch";

        internal sealed class Site
        {
            internal Spec Spec;
            internal string Name;
            internal List<MethodDefinition> Patches;
        }

        // HarmonyPatchType minus All: a method is a patch method by one of these names or by the
        // attribute `Harmony<name>` (AttributePatch.GetPatchType).
        private static readonly string[] PatchTypes =
            { "Prefix", "Postfix", "Transpiler", "Finalizer", "ReversePatch", "ILManipulator" };

        /// <summary>
        /// Every target Harmony will patch in <paramref name="type"/>.
        /// </summary>
        /// <param name="notChecked">
        /// Gets one line per <c>[HarmonyPatch]</c> that names no site here, with the reason, so the caller can
        /// log what was left out instead of it vanishing.
        /// </param>
        internal static List<Site> Sites(TypeDefinition type, List<string> notChecked)
        {
            var sites = new List<Site>();
            var onClass = Read(type.CustomAttributes);
            var inheriting = new List<MethodDefinition>();
            int patchMethods = 0;
            bool classNameUsed = false;

            if (type.HasMethods)
                foreach (var method in type.Methods)
                {
                    var onMethod = Read(method.CustomAttributes);
                    if (!IsPatchMethod(method))
                    {
                        // AttributePatch.Create returns null for it, so Harmony never patches its target.
                        if (onMethod != null)
                            notChecked.Add($"{type.FullName}.{method.Name} (not a patch method)");
                        continue;
                    }
                    patchMethods++;

                    if (onMethod == null || !Adds(onMethod))
                    {
                        if (onClass != null) inheriting.Add(method);
                        continue;
                    }

                    var merged = Merge(onClass, onMethod);
                    if (onMethod.MethodName == null && merged.MethodName != null) classNameUsed = true;
                    if (merged.MethodName == null)
                    {
                        notChecked.Add($"{type.FullName}.{method.Name} (no method name)");
                        continue;
                    }
                    sites.Add(new Site
                    {
                        Spec = merged, Name = type.FullName + "." + method.Name,
                        Patches = new List<MethodDefinition> { method },
                    });
                }

            if (onClass != null)
            {
                if (inheriting.Count > 0 && onClass.MethodName != null)
                    sites.Insert(0, new Site
                    {
                        Spec = onClass, Name = type.FullName,
                        Patches = inheriting.Where(CarriesArguments).ToList(),
                    });
                else if (inheriting.Count > 0)
                    notChecked.Add($"{type.FullName} (no method name)");
                else if (onClass.MethodName != null && patchMethods > 0 && !classNameUsed)
                    notChecked.Add($"{type.FullName} -> {onClass.MethodName} (every patch method names its own target)");
            }
            return sites;
        }

        /// <summary>Does Harmony take this method as a patch at all?</summary>
        internal static bool IsPatchMethod(MethodDefinition method)
        {
            if (PatchTypes.Contains(method.Name)) return true;
            if (!method.HasCustomAttributes) return false;
            foreach (var attribute in method.CustomAttributes)
            {
                string name = attribute.AttributeType?.FullName;
                if (name == null || !name.StartsWith("HarmonyLib.Harmony", StringComparison.Ordinal)) continue;
                if (PatchTypes.Contains(name.Substring("HarmonyLib.Harmony".Length))) return true;
            }
            return false;
        }

        /// <summary>Does the method's own attribute change anything about the target?</summary>
        private static bool Adds(Spec spec)
            => spec.DeclaringType != null || spec.TypeName != null || spec.MethodName != null
            || spec.ArgumentTypes != null || spec.ArgumentVariations != null || spec.MethodType != null;

        /// <summary>
        /// The patch methods whose parameters are checked against the original's.
        /// </summary>
        /// <remarks>
        /// <c>[HarmonyPatch(typeof(X), "Y")]</c> on the class and <c>[HarmonyPostfix]</c> on the method is
        /// the common shape, and it is the shape that hid the parameter-rename break: the attribute the
        /// target comes from is on the class, so the method carrying the argument names was never looked
        /// at. A transpiler or a reverse patch takes no argument by name, so only these three count.
        /// </remarks>
        private static bool CarriesArguments(MethodDefinition method)
        {
            if (method.Name is "Prefix" or "Postfix" or "Finalizer") return true;
            if (!method.HasCustomAttributes) return false;
            foreach (var attribute in method.CustomAttributes)
                if (attribute.AttributeType?.FullName is "HarmonyLib.HarmonyPrefix" or "HarmonyLib.HarmonyPostfix"
                    or "HarmonyLib.HarmonyFinalizer")
                    return true;
            return false;
        }

        internal sealed class Spec
        {
            internal TypeReference DeclaringType;
            internal string TypeName;
            internal string MethodName;
            internal List<TypeReference> ArgumentTypes;
            internal List<int> ArgumentVariations;
            internal int? MethodType;
        }

        internal static Spec Merge(Spec outer, Spec inner)
        {
            if (outer == null) return inner;
            return new Spec
            {
                DeclaringType = inner.DeclaringType ?? outer.DeclaringType,
                TypeName = inner.TypeName ?? outer.TypeName,
                MethodName = inner.MethodName ?? outer.MethodName,
                ArgumentTypes = inner.ArgumentTypes ?? outer.ArgumentTypes,
                ArgumentVariations = inner.ArgumentVariations ?? outer.ArgumentVariations,
                MethodType = inner.MethodType ?? outer.MethodType,
            };
        }

        /// <summary>
        /// Pull the target out of every [HarmonyPatch] on one member.
        /// </summary>
        /// <remarks>
        /// Harmony has a dozen constructor overloads and they are distinguished by argument TYPE, not by
        /// position, so that is how they are read here. The one genuine ambiguity is a bare string: with a
        /// Type argument present it is the method name, and without one, two strings mean type-then-method
        /// while a single string means the method name. That is the whole overload set.
        /// </remarks>
        internal static Spec Read(Mono.Collections.Generic.Collection<CustomAttribute> attributes)
        {
            Spec spec = null;
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType?.FullName != PatchAttribute) continue;
                spec ??= new Spec();

                var strings = new List<string>();
                foreach (var argument in attribute.ConstructorArguments)
                {
                    switch (argument.Value)
                    {
                        case TypeReference type:
                            spec.DeclaringType ??= type;
                            break;
                        case string text:
                            strings.Add(text);
                            break;
                        case CustomAttributeArgument[] array when IsTypeArray(argument.Type):
                            spec.ArgumentTypes ??= TypesIn(array);
                            break;
                        case CustomAttributeArgument[] array when IsVariationArray(argument.Type):
                            spec.ArgumentVariations ??= VariationsIn(array);
                            break;
                        case int enumValue when argument.Type?.Name == "MethodType":
                            spec.MethodType ??= enumValue;
                            break;
                    }
                }

                // One string is always the method name - both [HarmonyPatch(typeof(X), "Foo")] and the
                // bare [HarmonyPatch("Foo")] mean the method. Two strings are type then method.
                if (strings.Count == 1) spec.MethodName ??= strings[0];
                else if (strings.Count >= 2)
                {
                    spec.TypeName ??= strings[0];
                    spec.MethodName ??= strings[1];
                }

                foreach (var named in attribute.Properties) Apply(spec, named);
                foreach (var named in attribute.Fields) Apply(spec, named);
            }
            return spec;
        }

        internal static void Apply(Spec spec, CustomAttributeNamedArgument named)
        {
            switch (named.Name)
            {
                case "declaringType" when named.Argument.Value is TypeReference type:
                    spec.DeclaringType ??= type; break;
                case "methodName" when named.Argument.Value is string name:
                    spec.MethodName ??= name; break;
                case "methodType" when named.Argument.Value is int value:
                    spec.MethodType ??= value; break;
                case "argumentTypes" when named.Argument.Value is CustomAttributeArgument[] array:
                    spec.ArgumentTypes ??= TypesIn(array);
                    break;
                case "argumentVariations" when named.Argument.Value is CustomAttributeArgument[] array:
                    spec.ArgumentVariations ??= VariationsIn(array);
                    break;
            }
        }

        /// <summary>
        /// An EMPTY argument list is an answer, and reading it as silence cost two mods a release.
        /// </summary>
        /// <remarks>
        /// <c>[HarmonyPatch(typeof(SaveManager), "Save", new Type[] { })]</c> names the parameterless
        /// overload as precisely as any other list names its own. The old reading kept the list only when
        /// it had entries, so an empty one left the spec saying "no types given" - and this class then
        /// reported the two <c>Save</c> overloads as an ambiguity the mod had in fact resolved. Absorbent
        /// Soil and NACops were both published as blocked on that basis, and neither was.
        ///
        /// The array has to be a <c>Type[]</c> to count: a <c>HarmonyPatch</c> constructor also takes
        /// <c>ArgumentType[]</c> for by-ref and out variations, and an empty one of those says nothing
        /// about arity at all.
        /// </remarks>
        internal static bool IsTypeArray(TypeReference arrayType)
            => (arrayType as ArrayType)?.ElementType?.FullName == "System.Type";

        /// <summary>The by-ref markers that travel beside the types; see HarmonyTargets.Decorated.</summary>
        internal static bool IsVariationArray(TypeReference arrayType)
            => (arrayType as ArrayType)?.ElementType?.FullName == "HarmonyLib.ArgumentType";

        internal static List<TypeReference> TypesIn(CustomAttributeArgument[] array)
        {
            var types = new List<TypeReference>();
            foreach (var element in array)
                if (element.Value is TypeReference elementType) types.Add(elementType);
            return types;
        }

        internal static List<int> VariationsIn(CustomAttributeArgument[] array)
        {
            var variations = new List<int>();
            foreach (var element in array)
                variations.Add(element.Value is int value ? value : 0);
            return variations;
        }
    }
}
