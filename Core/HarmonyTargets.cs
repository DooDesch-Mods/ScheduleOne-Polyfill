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
                    Verify(onClass, type.FullName, index, report, PatchMethods(type));

                if (!type.HasMethods) continue;
                foreach (var method in type.Methods)
                {
                    var onMethod = Read(method.CustomAttributes);
                    if (onMethod == null) continue;

                    var merged = Merge(onClass, onMethod);
                    if (merged.MethodName == null) continue;
                    // Already reported at class level, with the same target.
                    if (classHasPatch && onMethod.DeclaringType == null && onMethod.MethodName == null) continue;

                    Verify(merged, type.FullName + "." + method.Name, index, report,
                           new List<MethodDefinition> { method });
                }
            }
        }

        private static void Verify(Spec spec, string site, InteropIndex index, ModReport report,
                                   List<MethodDefinition> patches)
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
                string scope = spec.DeclaringType?.Scope?.Name ?? "";

                // A RENAMED TYPE IS PUT BACK AS A CLASS DERIVING FROM THE NEW ONE, and a patch aimed at it
                // then lands on the real method: Harmony resolves a name up the base chain, so
                // MixingStationCanvas::Open finds MixingStationInterface.Open(MixingStation) - measured, the
                // runtime reports that method as declared on MixingStationInterface. Saying the patch will
                // not apply was true before that repair existed and is false now, which is worse than
                // saying nothing.
                var renamed = Bridges.Registry.FindType(scope, wanted);
                var replacement = renamed == null ? null : index.FindType(scope, renamed.NewFullName);
                if (replacement != null)
                {
                    Verify(spec, site, index, report, replacement, wanted, patches);
                    return;
                }

                var elsewhere = index.BySimpleName(SimpleNameOf(wanted));
                report.Findings.Add(new Finding
                {
                    Kind = "harmony-target",
                    Scope = scope,
                    Symbol = wanted + "::" + spec.MethodName,
                    Reason = "the patched type does not exist here, so this patch will not apply",
                    Hint = elsewhere.Count == 1 ? elsewhere[0].FullName : "",
                    Site = site,
                });
                return;
            }

            Verify(spec, site, index, report, declaring, null, patches);
        }

        /// <summary>
        /// The methods of a patch class that Harmony will actually bind, when the target is on the class.
        /// </summary>
        /// <remarks>
        /// <c>[HarmonyPatch(typeof(X), "Y")]</c> on the class and <c>[HarmonyPostfix]</c> on the method is
        /// the common shape, and it is the shape that hid the parameter-rename break: the attribute the
        /// target comes from is on the class, so the method carrying the argument names was never looked
        /// at. Harmony also accepts the bare names, so those count too.
        /// </remarks>
        private static List<MethodDefinition> PatchMethods(TypeDefinition type)
        {
            var found = new List<MethodDefinition>();
            if (!type.HasMethods) return found;

            foreach (var method in type.Methods)
            {
                bool marked = method.Name is "Prefix" or "Postfix" or "Finalizer";
                if (!marked && method.HasCustomAttributes)
                    foreach (var attribute in method.CustomAttributes)
                    {
                        string name = attribute.AttributeType?.FullName;
                        if (name is "HarmonyLib.HarmonyPrefix" or "HarmonyLib.HarmonyPostfix"
                                 or "HarmonyLib.HarmonyFinalizer")
                        { marked = true; break; }
                    }
                if (marked) found.Add(method);
            }
            return found;
        }

        /// <summary>The method half of the check, once the type it is on has been settled.</summary>
        /// <param name="under">The name the mod used, when that is not the type being searched.</param>
        private static void Verify(Spec spec, string site, InteropIndex index, ModReport report,
                                   TypeDefinition declaring, string under, List<MethodDefinition> patches)
        {
            string name = Decorate(spec.MethodName, spec.MethodType);
            int argumentCount = spec.ArgumentTypes?.Count ?? -1;

            foreach (var method in declaring.Methods)
            {
                if (method.Name != name) continue;
                if (argumentCount >= 0 && method.Parameters.Count != argumentCount) continue;
                Names(method, patches, site, report);     // the target is there; is it still spelled right
                return;
            }

            // Inherited counts, because Harmony's own lookup walks the base chain. Without this, a patch
            // aimed at a renamed screen was reported as dead while the runtime was resolving it fine.
            for (var above = Base(declaring, index); above != null; above = Base(above, index))
                foreach (var method in above.Methods)
                    if (method.Name == name
                        && (argumentCount < 0 || method.Parameters.Count == argumentCount))
                    { Names(method, patches, site, report); return; }

            // A PATCH TARGET CAN ASK FOR A REPAIR TOO, and until this line it could not. The member check
            // collects a bridge whenever a mod CALLS something that is gone; a member that is only ever
            // PATCHED went through here instead, which reported it and asked for nothing. So a method the
            // game deleted outright - Player.Activate, a station screen's SetIsOpen - stayed missing even
            // where a bridge for it existed, and Harmony threw away the whole patch class it was in.
            string scope = declaring.Module?.Assembly?.Name?.Name ?? "";
            string owner = under ?? declaring.FullName;
            var bridge = Bridges.Registry.FindByName(scope, owner, name, argumentCount);

            if (bridge != null)
            {
                string key = Triage.Request(new InteropAugmentor.MemberForward
                {
                    InAssembly = bridge.Assembly,
                    DeclaringType = bridge.DeclaringType,
                    OldName = bridge.OldName,
                    NewName = null,
                    ParameterCount = bridge.ParameterCount,
                    ParameterTypes = bridge.ParameterTypes,
                    Rule = "curated",
                });

                report.Findings.Add(new Finding
                {
                    Kind = "harmony-target",
                    Scope = scope,
                    Symbol = owner + "::" + name,
                    Reason = "the patched method is gone",
                    Hint = "hand-written rule: " + bridge.Because,
                    RepairKey = key,
                    Site = site,
                });
                return;
            }

            // The old signature is one Polyfill puts back AND moves the patch off again, so calling this
            // dead was true before that pair existed. Said here because a report that names a working
            // patch as broken sends people to fix what is not wrong.
            if (GrownOverloads.Doubled(owner, name))
            {
                report.Findings.Add(new Finding
                {
                    Kind = "harmony-target",
                    Scope = scope,
                    Symbol = owner + "::" + name,
                    Reason = "the signature this patch names is gone; Polyfill puts it back and moves the "
                           + "patch onto the method the game calls (`polyfillfixes` lists it as "
                           + "patches-on-grown-overloads)",
                    Site = site,
                });
                return;
            }

            var candidates = NameHeuristics.ForMethod(declaring, name, null);
            report.Findings.Add(new Finding
            {
                Kind = "harmony-target",
                Scope = scope,
                Symbol = owner + "::" + name,
                Reason = candidates.Count > 1
                    ? $"the patched method is gone; {candidates.Count} members could be meant, so none is chosen"
                    : under != null
                        ? $"the type is put back as {declaring.Name}, which has no {name} to patch"
                        : "the patched method is gone, so this patch will not apply",
                Hint = candidates.Count == 1 ? candidates[0].NewName + "  [" + candidates[0].Rule + "]" : "",
                Site = site,
            });
        }

        /// <summary>
        /// The target is there and the patch may still not bind to it, because Harmony matches by name.
        /// </summary>
        /// <remarks>
        /// The only check here that runs on a member NOTHING is wrong with. A parameter rename changes no
        /// type, no signature and no name a compiler resolves - and kills the patch anyway:
        /// <c>Parameter "isOpen" not found in method void ShopInterface::SetIsOpen(bool open)</c>.
        ///
        /// What is collected is EVIDENCE, not a decision. Each installed mod's patch says which spelling it
        /// wants; the injector renames only where the old one is wanted and the new one is not. See
        /// <see cref="RenamedParameters"/> for why both cannot be right at once.
        ///
        /// Harmony's own injected names are skipped: they are its vocabulary, not the game's.
        /// </remarks>
        private static void Names(MethodDefinition target, List<MethodDefinition> patches, string site,
                                  ModReport report)
        {
            if (patches == null || patches.Count == 0 || target == null) return;

            string type = target.DeclaringType?.FullName;
            if (type == null) return;

            foreach (var entry in RenamedParameters.For(type, target.Name, target.Parameters.Count))
            {
                bool wantsOld = false, wantsNew = false;
                foreach (var patch in patches)
                foreach (var parameter in patch.Parameters)
                {
                    if (parameter.Name == null || parameter.Name.StartsWith("__", StringComparison.Ordinal))
                        continue;
                    if (parameter.Name == entry.OldName) wantsOld = true;
                    if (parameter.Name == entry.NewName) wantsNew = true;
                }
                if (!wantsOld) continue;

                if (wantsNew)
                {
                    report.Findings.Add(new Finding
                    {
                        Kind = "harmony-target",
                        Scope = target.Module?.Assembly?.Name?.Name ?? "",
                        Symbol = type + "::" + target.Name + "(" + entry.NewName + ")",
                        Reason = $"this patch asks for the argument under both names, so neither can be "
                               + $"put back; 0.4.6 renamed {entry.OldName} to {entry.NewName}",
                        Site = site,
                    });
                    continue;
                }

                string key = Triage.RequestRename(new InteropAugmentor.ParameterRename
                {
                    InAssembly = target.Module?.Assembly?.Name?.Name,
                    DeclaringType = type,
                    Method = target.Name,
                    ParameterCount = target.Parameters.Count,
                    Index = entry.Index,
                    Name = entry.OldName,
                    WasCalled = entry.NewName,
                    Because = entry.Because,
                });

                report.Findings.Add(new Finding
                {
                    Kind = "harmony-target",
                    Scope = target.Module?.Assembly?.Name?.Name ?? "",
                    Symbol = type + "::" + target.Name + "(" + entry.OldName + ")",
                    Reason = $"the method is here and its argument was renamed to {entry.NewName}, which "
                           + "Harmony matches on",
                    Hint = "hand-written rule: " + entry.Because,
                    RepairKey = key,
                    Site = site,
                });
            }
        }

        private static TypeDefinition Base(TypeDefinition type, InteropIndex index)
            => type?.BaseType == null ? null : Triage.Resolve(Triage.Root(type.BaseType), index);

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
