using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Core
{
    /// <summary>
    /// Game references a mod only makes from code it runs on Mono.
    /// </summary>
    /// <remarks>
    /// SOME MODS SHIP ONE DLL FOR BOTH BRANCHES. They are built against the Mono assemblies and switch on
    /// <c>MelonUtils.IsGameIl2Cpp()</c> at runtime, so the file carries Mono-shaped references that the
    /// IL2CPP build of the game cannot answer. Counted like any other reference, each one is a missing
    /// name and the mod reads "blocked" - while it runs clean, because the code naming it never runs here.
    ///
    /// ModsApp 1.2.3 is the case this was written for. <c>MissingDepsPanelComponent.AddButtonListener</c>
    /// constructs <c>new UnityAction(listener.Invoke)</c>, which compiles to the Mono delegate constructor
    /// <c>UnityAction::.ctor(Object, IntPtr)</c>. Its three callers all sit behind <c>if (!IsIl2Cpp)</c> or
    /// after an <c>if (IsIl2Cpp) { ...; return; }</c>, where <c>IsIl2Cpp</c> is a static readonly field set
    /// from <c>MelonUtils.IsGameIl2Cpp()</c>. 512 of 514 reported sessions ran clean under a "blocked".
    ///
    /// THE UNIT IS THE METHOD, NOT THE LINE. A missing member throws when the method that names it is
    /// compiled, not when the line runs, so a reference in a dead branch of a method that does run still
    /// kills that method. Only a reference whose every use sits in a method that is never called on this
    /// branch is answered here. A branch on the flag only decides which CALLS are made.
    ///
    /// A method counts as never called only when nothing outside this assembly can reach it: private or
    /// internal, not virtual, not named in a string (reflection, Harmony by name), not a Unity message,
    /// and referenced at least once. A method nothing in the assembly references is called from outside
    /// by definition, so it is live.
    /// </remarks>
    internal static class MonoOnlyCode
    {
        private const string MelonUtils = "MelonLoader.MelonUtils";
        private const string IsGameIl2Cpp = "IsGameIl2Cpp";

        /// <summary>Called by Unity by name, so private does not mean unreachable.</summary>
        private static readonly HashSet<string> UnityMessages = new(StringComparer.Ordinal)
        {
            "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy",
            "OnGUI", "OnApplicationQuit", "OnApplicationFocus", "OnApplicationPause", "OnValidate", "Reset",
            "OnTriggerEnter", "OnTriggerExit", "OnTriggerStay", "OnCollisionEnter", "OnCollisionExit",
            "OnCollisionStay", "OnBecameVisible", "OnBecameInvisible", "OnRectTransformDimensionsChange",
        };

        /// <summary>
        /// Full names of the member references whose every use is in a method this mod only calls on Mono.
        /// Empty for a mod that never asks which branch it is on, which is nearly all of them.
        /// </summary>
        internal static HashSet<string> References(ModuleDefinition module)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (!AsksForTheBranch(module)) return result;

            var methods = new List<MethodDefinition>();
            foreach (var type in AllTypes(module))
                foreach (var method in type.Methods)
                    if (method.HasBody) methods.Add(method);

            var flagFields = new HashSet<FieldDefinition>();
            var flagGetters = new HashSet<MethodDefinition>();
            FindFlags(methods, flagFields, flagGetters);

            var named = StringsIn(methods);
            var referenced = new HashSet<MethodDefinition>();
            foreach (var method in methods)
                foreach (var instruction in method.Body.Instructions)
                {
                    var callee = Local(instruction.Operand as MethodReference, module);
                    if (callee != null) referenced.Add(callee);
                }

            // Everything that might be called from outside is a root. What a root reaches on THIS branch
            // is live; what nothing live reaches is never compiled here.
            var live = new HashSet<MethodDefinition>();
            var work = new Queue<MethodDefinition>();
            foreach (var method in methods)
                if (!Hidden(method, referenced, named)) { live.Add(method); work.Enqueue(method); }

            while (work.Count > 0)
            {
                var method = work.Dequeue();
                foreach (var instruction in Reachable(method, flagFields, flagGetters))
                {
                    var callee = Local(instruction.Operand as MethodReference, module);
                    if (callee != null && callee.HasBody && live.Add(callee)) work.Enqueue(callee);
                }
            }

            var usedLive = new HashSet<string>(StringComparer.Ordinal);
            var usedDead = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in methods)
            {
                var into = live.Contains(method) ? usedLive : usedDead;
                foreach (var instruction in method.Body.Instructions)
                {
                    string key = Key(instruction.Operand);
                    if (key != null) into.Add(key);
                }
            }

            foreach (string key in usedDead)
                if (!usedLive.Contains(key)) result.Add(key);
            return result;
        }

        /// <summary>The cheap gate: does the module reference MelonUtils.IsGameIl2Cpp at all?</summary>
        private static bool AsksForTheBranch(ModuleDefinition module)
        {
            foreach (var reference in module.GetMemberReferences())
                if (reference is MethodReference method && IsBranchQuery(method)) return true;
            return false;
        }

        private static bool IsBranchQuery(MethodReference method)
            => method.Name == IsGameIl2Cpp && method.Parameters.Count == 0
               && method.DeclaringType?.FullName == MelonUtils;

        /// <summary>
        /// Static fields that only ever hold the branch, and parameterless static getters that only return it.
        /// </summary>
        /// <remarks>
        /// A field counts when every store to it anywhere in the module is the value of a source right
        /// before it. Round until nothing changes, so a field copied from another flag field counts too.
        /// </remarks>
        private static void FindFlags(List<MethodDefinition> methods, HashSet<FieldDefinition> fields,
                                      HashSet<MethodDefinition> getters)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;

                var good = new Dictionary<FieldDefinition, bool>();
                foreach (var method in methods)
                    foreach (var instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode.Code != Code.Stsfld) continue;
                        if (!(instruction.Operand is FieldDefinition field) || field.FieldType.FullName != "System.Boolean")
                            continue;
                        bool fromSource = instruction.Previous != null
                                          && IsSource(instruction.Previous, fields, getters);
                        good[field] = (!good.TryGetValue(field, out bool sofar) || sofar) && fromSource;
                    }
                foreach (var pair in good)
                    if (pair.Value && fields.Add(pair.Key)) changed = true;

                foreach (var method in methods)
                {
                    if (!method.IsStatic || method.Parameters.Count != 0
                        || method.ReturnType.FullName != "System.Boolean" || getters.Contains(method)) continue;
                    var body = new List<Instruction>();
                    foreach (var instruction in method.Body.Instructions)
                        if (instruction.OpCode.Code != Code.Nop) body.Add(instruction);
                    if (body.Count == 2 && body[1].OpCode.Code == Code.Ret
                        && IsSource(body[0], fields, getters))
                    { getters.Add(method); changed = true; }
                }
            }
        }

        /// <summary>Does this instruction push the branch flag, true on IL2CPP?</summary>
        private static bool IsSource(Instruction instruction, HashSet<FieldDefinition> fields,
                                     HashSet<MethodDefinition> getters)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Call:
                    if (instruction.Operand is MethodDefinition own) return getters.Contains(own);
                    return instruction.Operand is MethodReference method && IsBranchQuery(method);
                case Code.Ldsfld:
                    return instruction.Operand is FieldDefinition field && fields.Contains(field);
                default:
                    return false;
            }
        }

        /// <summary>
        /// The value this instruction leaves on the stack when it is a known function of the flag, or null.
        /// </summary>
        /// <remarks>
        /// Covers what compilers emit for <c>if (flag)</c> and <c>if (!flag)</c>: the flag itself, the flag
        /// compared to zero (<c>ldc.i4.0; ceq</c>, a debug build's negation), and a local written once from
        /// such a value and read straight back.
        /// </remarks>
        private static bool? Value(Instruction instruction, MethodDefinition method,
                                   HashSet<FieldDefinition> fields, HashSet<MethodDefinition> getters,
                                   HashSet<Instruction> targets, int depth = 0)
        {
            if (instruction == null || depth > 6) return null;
            if (IsSource(instruction, fields, getters)) return true;

            if (instruction.OpCode.Code == Code.Ceq)
            {
                var zero = instruction.Previous;
                if (zero == null || zero.OpCode.Code != Code.Ldc_I4_0 || targets.Contains(zero)
                    || targets.Contains(instruction)) return null;
                var inner = Value(zero.Previous, method, fields, getters, targets, depth + 1);
                return inner.HasValue ? !inner.Value : null;
            }

            int local = LocalIndex(instruction, load: true);
            if (local >= 0)
            {
                Instruction store = null;
                foreach (var other in method.Body.Instructions)
                    if (LocalIndex(other, load: false) == local)
                    {
                        if (store != null) return null;          // written twice: not a known value
                        store = other;
                    }
                if (store == null || targets.Contains(store)) return null;
                return Value(store.Previous, method, fields, getters, targets, depth + 1);
            }
            return null;
        }

        /// <summary>The instructions of a method that can run on IL2CPP, given the flag is true.</summary>
        private static HashSet<Instruction> Reachable(MethodDefinition method, HashSet<FieldDefinition> fields,
                                                      HashSet<MethodDefinition> getters)
        {
            var body = method.Body;
            var seen = new HashSet<Instruction>();
            if (body.Instructions.Count == 0) return seen;

            // A branch target can be entered with a different stack, so a pattern that spans one is not read.
            var targets = new HashSet<Instruction>();
            foreach (var instruction in body.Instructions)
            {
                if (instruction.Operand is Instruction target) targets.Add(target);
                else if (instruction.Operand is Instruction[] many) foreach (var t in many) targets.Add(t);
            }
            foreach (var handler in body.ExceptionHandlers)
            {
                if (handler.HandlerStart != null) targets.Add(handler.HandlerStart);
                if (handler.FilterStart != null) targets.Add(handler.FilterStart);
            }

            var work = new Stack<Instruction>();
            work.Push(body.Instructions[0]);
            while (work.Count > 0)
            {
                var instruction = work.Pop();
                if (instruction == null || !seen.Add(instruction)) continue;

                // A handler runs whenever its protected block can.
                foreach (var handler in body.ExceptionHandlers)
                    if (handler.TryStart == instruction)
                    {
                        work.Push(handler.HandlerStart);
                        if (handler.FilterStart != null) work.Push(handler.FilterStart);
                    }

                switch (instruction.OpCode.FlowControl)
                {
                    case FlowControl.Return:
                    case FlowControl.Throw:
                        break;

                    case FlowControl.Branch:
                        if (instruction.Operand is Instruction jump) work.Push(jump);
                        break;

                    case FlowControl.Cond_Branch:
                        if (instruction.Operand is Instruction[] cases)
                        {
                            foreach (var t in cases) work.Push(t);
                            work.Push(instruction.Next);
                            break;
                        }

                        var target = instruction.Operand as Instruction;
                        bool? taken = null;
                        if (!targets.Contains(instruction))
                        {
                            var flag = Value(instruction.Previous, method, fields, getters, targets);
                            if (flag.HasValue)
                            {
                                switch (instruction.OpCode.Code)
                                {
                                    case Code.Brtrue: case Code.Brtrue_S: taken = flag.Value; break;
                                    case Code.Brfalse: case Code.Brfalse_S: taken = !flag.Value; break;
                                }
                            }
                        }
                        if (taken != false) work.Push(target);
                        if (taken != true) work.Push(instruction.Next);
                        break;

                    default:
                        work.Push(instruction.Next);
                        break;
                }
            }
            return seen;
        }

        /// <summary>Could only this assembly call it, and does it?</summary>
        private static bool Hidden(MethodDefinition method, HashSet<MethodDefinition> referenced,
                                   HashSet<string> named)
        {
            if (method.IsVirtual || method.IsAbstract || method.IsConstructor && method.IsStatic) return false;
            if (!(method.IsPrivate || method.IsAssembly || method.IsFamilyAndAssembly)) return false;
            if (!referenced.Contains(method)) return false;
            if (named.Contains(method.Name) || UnityMessages.Contains(method.Name)) return false;
            foreach (var attribute in method.CustomAttributes)
                if (attribute.AttributeType.FullName != "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                    return false;
            return true;
        }

        private static HashSet<string> StringsIn(List<MethodDefinition> methods)
        {
            var strings = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in methods)
                foreach (var instruction in method.Body.Instructions)
                    if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string text)
                        strings.Add(text);
            return strings;
        }

        /// <summary>The definition behind a call into this same module, or null.</summary>
        private static MethodDefinition Local(MethodReference reference, ModuleDefinition module)
        {
            if (reference == null) return null;
            if (reference is GenericInstanceMethod generic) reference = generic.ElementMethod;
            if (reference is MethodDefinition own) return own.Module == module ? own : null;
            if (reference.DeclaringType?.Scope != module) return null;
            try
            {
                var resolved = reference.Resolve();
                return resolved?.Module == module ? resolved : null;
            }
            catch (Exception)
            {
                return null;                              // a member ref into this module that does not resolve
            }
        }

        /// <summary>The key a member reference is reported under, matching GetMemberReferences.</summary>
        private static string Key(object operand)
        {
            switch (operand)
            {
                case MethodDefinition _:
                case FieldDefinition _:
                    return null;                          // this module's own, never a game reference
                case GenericInstanceMethod generic:
                    return generic.ElementMethod is MethodDefinition ? null : generic.ElementMethod.FullName;
                case MemberReference member when member is MethodReference || member is FieldReference:
                    return member.FullName;
                default:
                    return null;
            }
        }

        private static int LocalIndex(Instruction instruction, bool load)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc_0: return load ? 0 : -1;
                case Code.Ldloc_1: return load ? 1 : -1;
                case Code.Ldloc_2: return load ? 2 : -1;
                case Code.Ldloc_3: return load ? 3 : -1;
                case Code.Stloc_0: return load ? -1 : 0;
                case Code.Stloc_1: return load ? -1 : 1;
                case Code.Stloc_2: return load ? -1 : 2;
                case Code.Stloc_3: return load ? -1 : 3;
                case Code.Ldloc: case Code.Ldloc_S:
                    return load && instruction.Operand is VariableDefinition l ? l.Index : -1;
                case Code.Stloc: case Code.Stloc_S:
                    return !load && instruction.Operand is VariableDefinition s ? s.Index : -1;
                case Code.Ldloca: case Code.Ldloca_S:
                    // An address taken means it can be written through; treat it as a second store.
                    return !load && instruction.Operand is VariableDefinition a ? a.Index : -1;
                default:
                    return -1;
            }
        }

        private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
        {
            var stack = new Stack<TypeDefinition>(module.Types);
            while (stack.Count > 0)
            {
                var type = stack.Pop();
                yield return type;
                foreach (var nested in type.NestedTypes) stack.Push(nested);
            }
        }
    }
}
