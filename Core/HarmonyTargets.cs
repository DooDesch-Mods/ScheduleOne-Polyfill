using Mono.Cecil;
using Polyfill.Contract;

namespace Polyfill.Core
{
    /// <summary>
    /// Check what every <c>[HarmonyPatch]</c> in a mod aims at, and whether it is still there.
    /// </summary>
    /// <remarks>
    /// This is the most useful thing the analysis produces, because it is the breakage that compiles
    /// cleanly forever. <c>[HarmonyPatch(typeof(Pot), "RpcLogic___PlantSeed_Client_4077118173")]</c> is a
    /// string; nothing checks it until the game is running, and by then the only symptom is a feature that
    /// quietly does not happen.
    ///
    /// It also does not kill the mod, which is easy to get wrong in the other direction: MelonLoader wraps
    /// each patch class in its own try/catch and logs the type that failed, so a dead target costs exactly
    /// one patch class. That is why nothing here proposes stripping the attribute - MelonLoader's own path
    /// already skips the class AND prints which one, and removing the attribute would only remove the
    /// diagnostic.
    /// </remarks>
    internal static class HarmonyTargets
    {
        private const string PatchAttribute = "HarmonyLib.HarmonyPatch";

        /// <summary>Harmony's MethodType enum, by value. Getter and Setter name a property, not a method.</summary>
        private static string Decorate(string name, int? methodType) => methodType switch
        {
            1 => "get_" + name,
            2 => "set_" + name,
            3 => ".ctor",
            4 => ".cctor",
            _ => name,
        };

        internal static void Check(ModuleDefinition module, InteropIndex index, ModReport report)
        {
            foreach (var type in module.GetTypes())
            {
                var onClass = Read(type.CustomAttributes);
                bool classHasPatch = onClass != null;

                // A class-level target with a name is a patch site in its own right.
                if (classHasPatch && onClass.MethodName != null)
                    Verify(onClass, type.FullName, index, report);

                if (!type.HasMethods) continue;
                foreach (var method in type.Methods)
                {
                    var onMethod = Read(method.CustomAttributes);
                    if (onMethod == null) continue;

                    var merged = Merge(onClass, onMethod);
                    if (merged.MethodName == null) continue;
                    // Already reported at class level, with the same target.
                    if (classHasPatch && onMethod.DeclaringType == null && onMethod.MethodName == null) continue;

                    Verify(merged, type.FullName + "." + method.Name, index, report);
                }
            }
        }

        private static void Verify(Spec spec, string site, InteropIndex index, ModReport report)
        {
            report.HarmonyTargetsChecked++;
            TypeDefinition declaring = null;

            if (spec.DeclaringType != null) declaring = Triage.Resolve(spec.DeclaringType, index);
            else if (spec.TypeName != null)
            {
                var hits = index.BySimpleName(SimpleNameOf(spec.TypeName));
                foreach (var hit in hits) if (hit.FullName == spec.TypeName) { declaring = hit; break; }
            }

            if (declaring == null)
            {
                string wanted = spec.DeclaringType?.FullName ?? spec.TypeName ?? "?";
                var elsewhere = index.BySimpleName(SimpleNameOf(wanted));
                report.Findings.Add(new Finding
                {
                    Kind = "harmony-target",
                    Scope = spec.DeclaringType?.Scope?.Name ?? "",
                    Symbol = wanted + "::" + spec.MethodName,
                    Reason = "the patched type does not exist here, so this patch will not apply",
                    Hint = elsewhere.Count == 1 ? elsewhere[0].FullName : "",
                    Site = site,
                });
                return;
            }

            string name = Decorate(spec.MethodName, spec.MethodType);
            int argumentCount = spec.ArgumentTypes?.Count ?? -1;

            foreach (var method in declaring.Methods)
            {
                if (method.Name != name) continue;
                if (argumentCount >= 0 && method.Parameters.Count != argumentCount) continue;
                return;                                   // the target is there
            }

            var candidates = NameHeuristics.ForMethod(declaring, name, null);
            report.Findings.Add(new Finding
            {
                Kind = "harmony-target",
                Scope = declaring.Module?.Assembly?.Name?.Name ?? "",
                Symbol = declaring.FullName + "::" + name,
                Reason = candidates.Count > 1
                    ? $"the patched method is gone; {candidates.Count} members could be meant, so none is chosen"
                    : "the patched method is gone, so this patch will not apply",
                Hint = candidates.Count == 1 ? candidates[0].NewName + "  [" + candidates[0].Rule + "]" : "",
                Site = site,
            });
        }

        private static string SimpleNameOf(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return fullName;
            int slash = fullName.LastIndexOfAny(new[] { '/', '+' });
            if (slash >= 0) return fullName.Substring(slash + 1);
            int dot = fullName.LastIndexOf('.');
            return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
        }

        private sealed class Spec
        {
            internal TypeReference DeclaringType;
            internal string TypeName;
            internal string MethodName;
            internal List<TypeReference> ArgumentTypes;
            internal int? MethodType;
        }

        private static Spec Merge(Spec outer, Spec inner)
        {
            if (outer == null) return inner;
            return new Spec
            {
                DeclaringType = inner.DeclaringType ?? outer.DeclaringType,
                TypeName = inner.TypeName ?? outer.TypeName,
                MethodName = inner.MethodName ?? outer.MethodName,
                ArgumentTypes = inner.ArgumentTypes ?? outer.ArgumentTypes,
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
        private static Spec Read(Mono.Collections.Generic.Collection<CustomAttribute> attributes)
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
                        case CustomAttributeArgument[] array:
                            var types = new List<TypeReference>();
                            foreach (var element in array)
                                if (element.Value is TypeReference elementType) types.Add(elementType);
                            if (types.Count > 0) spec.ArgumentTypes ??= types;
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

        private static void Apply(Spec spec, CustomAttributeNamedArgument named)
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
                    var types = new List<TypeReference>();
                    foreach (var element in array)
                        if (element.Value is TypeReference elementType) types.Add(elementType);
                    if (types.Count > 0) spec.ArgumentTypes ??= types;
                    break;
            }
        }
    }
}
