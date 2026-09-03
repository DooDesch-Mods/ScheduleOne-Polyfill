using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Core
{
    /// <summary>
    /// The type whose successor cannot be inherited from - put back around the successor's native class.
    /// </summary>
    /// <remarks>
    /// <see cref="ShadowTypes"/> answers a rename by deriving from what the type became, which works
    /// because both managed classes are shells around one native pointer. That needs the two to be
    /// substitutable, and a self-referential base takes that away:
    /// <code>
    /// public class Singleton&lt;T&gt; : MonoBehaviour where T : Singleton&lt;T&gt;
    /// </code>
    /// A stand-in deriving from <c>InputPromptsManager</c> is a <c>Singleton&lt;InputPromptsManager&gt;</c>,
    /// so the mod's own <c>Singleton&lt;InputPromptsCanvas&gt;</c> breaks the constraint and the CLR refuses
    /// the constructed type - the same TypeLoadException, moved one step later.
    ///
    /// What this emits instead closes the SAME generic base over the stand-in itself, which satisfies the
    /// constraint, and then hands the stand-in the successor's native class:
    /// <code>
    /// class InputPromptsCanvas : Singleton&lt;InputPromptsCanvas&gt;
    /// {
    ///     static InputPromptsCanvas()
    ///     {
    ///         Il2CppClassPointerStore&lt;InputPromptsCanvas&gt;.NativeClassPtr =
    ///             Il2CppClassPointerStore&lt;InputPromptsManager&gt;.NativeClassPtr;
    ///     }
    ///     public InputPromptsCanvas(IntPtr pointer) : base(pointer) { }
    /// }
    /// </code>
    /// Il2CppInterop builds the native generic out of <c>Il2CppClassPointerStore&lt;T&gt;.NativeClassPtr</c>,
    /// so every native lookup lands on <c>Singleton&lt;InputPromptsManager&gt;</c>, a type IL2CPP has. An
    /// empty stand-in would leave that pointer at zero and the native construction would be handed
    /// <c>IntPtr.Zero</c>.
    ///
    /// BEHAVIOUR COMES BACK ONLY WHERE A RULE BRINGS IT. A member is emitted as an answer - a constant,
    /// no body - unless its <c>Answer.Emit</c> writes one, and a member without a rule gives nothing back.
    /// That makes a rule-less stand-in honest only where the caller already treats the whole thing as
    /// optional, which is why the shape is opted into per rename and never inferred.
    ///
    /// The stand-in carries the successor's native class, so <c>this</c> is the live object: a rule can
    /// build a wrapper of the successor around <c>this.Pointer</c> and call it. That is what the
    /// InputPrompts members do, and it is why a stand-in is not automatically a dead one.
    /// </remarks>
    internal static class FacadeTypes
    {
        internal sealed class Member
        {
            internal string Name;
            internal string Returns;      // full name, or null for void
            internal string[] Takes;      // full names, or null for none

            /// <summary>Builds the member instead of answering with a constant. See Bridges.Answer.Emit.</summary>
            internal Func<ModuleDefinition, TypeDefinition, TypeDefinition, MethodDefinition> Emit;
        }

        /// <summary>
        /// Puts the old name back as a class around <paramref name="targetFullName"/>'s native class,
        /// or says why it cannot.
        /// </summary>
        internal static TypeDefinition TryAdd(ModuleDefinition module, string oldNamespace, string oldName,
                                              string targetFullName, IEnumerable<Member> members,
                                              out string refusal)
        {
            refusal = null;

            var target = module.GetType(targetFullName);
            if (target == null)
            { refusal = "the type it stands in for is not in this assembly"; return null; }

            // The base has to be a generic closed over the target itself. Anything else is a case
            // ShadowTypes already handles better, and emitting this instead would throw away inheritance
            // for no reason.
            if (target.BaseType is not GenericInstanceType crtp || crtp.GenericArguments.Count != 1
                || !string.Equals(crtp.GenericArguments[0].FullName, target.FullName, StringComparison.Ordinal))
            { refusal = $"{target.FullName} does not derive from a generic closed over itself"; return null; }

            var store = PointerStore(module);
            if (store == null)
            { refusal = "Il2CppClassPointerStore is not referenced by this assembly"; return null; }

            var nativePointer = store.Fields.FirstOrDefault(f => f.Name == "NativeClassPtr");
            if (nativePointer == null)
            { refusal = "Il2CppClassPointerStore has no NativeClassPtr"; return null; }

            var basePointerConstructor = crtp.Resolve()?.Methods.FirstOrDefault(
                m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 1
                     && m.Parameters[0].ParameterType.MetadataType == MetadataType.IntPtr);
            if (basePointerConstructor == null)
            { refusal = $"{crtp.Name} has no pointer constructor to build on"; return null; }

            var facade = new TypeDefinition(oldNamespace, oldName,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);
            module.Types.Add(facade);

            var closedOverFacade = new GenericInstanceType(module.ImportReference(crtp.ElementType));
            closedOverFacade.GenericArguments.Add(facade);
            facade.BaseType = closedOverFacade;

            AddPointerConstructor(module, facade, closedOverFacade, basePointerConstructor);
            AddClassPointerAlias(module, facade, store, nativePointer, target);

            foreach (var member in members ?? Enumerable.Empty<Member>())
                if (!AddAnswer(module, facade, target, member, out refusal))
                { module.Types.Remove(facade); return null; }

            return facade;
        }

        private static void AddPointerConstructor(ModuleDefinition module, TypeDefinition facade,
                                                  GenericInstanceType closedBase, MethodDefinition baseConstructor)
        {
            var constructor = new MethodDefinition(".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            constructor.Parameters.Add(new ParameterDefinition("pointer", ParameterAttributes.None,
                                                               module.TypeSystem.IntPtr));

            var il = constructor.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, module.ImportReference(ShadowTypes.On(closedBase, baseConstructor)));
            il.Emit(OpCodes.Ret);

            facade.Methods.Add(constructor);
        }

        /// <summary>
        /// Hand the stand-in the native class of the type it stands in for.
        /// </summary>
        /// <remarks>
        /// This is the whole mechanism. Without it the stand-in has no native class, the generic base is
        /// constructed from <c>IntPtr.Zero</c>, and what comes back is a null call into IL2CPP rather than
        /// a type - which is worse than the TypeLoadException it was meant to replace, because it fails
        /// deeper and says less.
        /// </remarks>
        private static void AddClassPointerAlias(ModuleDefinition module, TypeDefinition facade,
                                                 TypeDefinition store, FieldDefinition nativePointer,
                                                 TypeDefinition target)
        {
            var initializer = new MethodDefinition(".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig
                    | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);

            var il = initializer.Body.GetILProcessor();
            il.Emit(OpCodes.Ldsfld, Closed(module, store, nativePointer, target));
            il.Emit(OpCodes.Stsfld, Closed(module, store, nativePointer, facade));
            il.Emit(OpCodes.Ret);

            facade.Methods.Add(initializer);
        }

        private static FieldReference Closed(ModuleDefinition module, TypeDefinition store,
                                             FieldDefinition field, TypeReference argument)
        {
            var closed = new GenericInstanceType(module.ImportReference(store));
            closed.GenericArguments.Add(module.ImportReference(argument));
            return new FieldReference(field.Name, module.ImportReference(field.FieldType), closed);
        }

        /// <summary>
        /// One member that answers and does nothing - or, where the rule brought one, a member that works.
        /// </summary>
        /// <remarks>
        /// Three shapes and no more when there is no rule: nothing, a null reference, and zero. A caller
        /// that reaches one of these has already been told by its own null check that there is nothing
        /// here, so the only job left is to return without throwing.
        ///
        /// A rule that brings its own body replaces all of that. It is handed the type the stand-in stands
        /// in for, because reaching the live object is the only reason such a body can exist, and it may
        /// refuse by returning null - which takes the whole stand-in with it rather than leaving a member
        /// that answers where the rest work.
        /// </remarks>
        private static bool AddAnswer(ModuleDefinition module, TypeDefinition facade, TypeDefinition target,
                                      Member member, out string refusal)
        {
            refusal = null;

            if (member.Emit != null)
            {
                MethodDefinition built;
                try { built = member.Emit(module, facade, target); }
                catch (Exception e)
                {
                    refusal = $"the rule for {member.Name} failed on this build: {e.Message}";
                    return false;
                }
                if (built == null)
                { refusal = $"the rule for {member.Name} needs members this build has not got"; return false; }

                // THE DECLARATION IS THE CONTRACT. Triage decides whether a mod's call is answered by
                // reading Takes and Returns, and the emitter writes the body - so the two drifting apart
                // would report a member as carried while the stand-in has a different signature, which is
                // the failure that looks like success. Cheaper to refuse here than to find it in a game.
                if (!Matches(built, member, out string mismatch))
                { refusal = $"the rule for {member.Name} built {mismatch}"; return false; }

                facade.Methods.Add(built);
                return true;
            }

            TypeReference returns = module.TypeSystem.Void;
            if (member.Returns != null)
            {
                var resolved = module.GetType(member.Returns) ?? Builtin(module, member.Returns);
                if (resolved == null)
                { refusal = $"{member.Returns}, which {member.Name} gives back, is not in this assembly"; return false; }
                returns = module.ImportReference(resolved);
            }

            var method = new MethodDefinition(member.Name,
                MethodAttributes.Public | MethodAttributes.HideBySig, returns);

            foreach (var takes in member.Takes ?? Array.Empty<string>())
            {
                var resolved = module.GetType(takes) ?? Builtin(module, takes);
                if (resolved == null)
                { refusal = $"{takes}, which {member.Name} takes, is not in this assembly"; return false; }
                method.Parameters.Add(new ParameterDefinition(module.ImportReference(resolved)));
            }

            var il = method.Body.GetILProcessor();
            if (returns.MetadataType == MetadataType.Void) { }
            else if (returns.IsValueType) il.Emit(OpCodes.Ldc_I4_0);
            else il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            facade.Methods.Add(method);
            return true;
        }

        /// <summary>Does the built method have the signature the member declared?</summary>
        private static bool Matches(MethodDefinition built, Member member, out string mismatch)
        {
            mismatch = null;

            if (built.Name != member.Name)
            { mismatch = $"a method called {built.Name}"; return false; }

            string returns = member.Returns ?? "System.Void";
            if (built.ReturnType.FullName != returns)
            { mismatch = $"one returning {built.ReturnType.FullName}, not {returns}"; return false; }

            var takes = member.Takes ?? Array.Empty<string>();
            if (built.Parameters.Count != takes.Length)
            { mismatch = $"one taking {built.Parameters.Count} argument(s), not {takes.Length}"; return false; }

            for (int i = 0; i < takes.Length; i++)
                if (built.Parameters[i].ParameterType.FullName != takes[i])
                {
                    mismatch = $"argument {i + 1} as {built.Parameters[i].ParameterType.FullName}, "
                             + $"not {takes[i]}";
                    return false;
                }

            return true;
        }

        private static TypeReference Builtin(ModuleDefinition module, string fullName) => fullName switch
        {
            "System.String" => module.TypeSystem.String,
            "System.Boolean" => module.TypeSystem.Boolean,
            "System.Int32" => module.TypeSystem.Int32,
            "System.Single" => module.TypeSystem.Single,
            _ => null,
        };

        /// <summary>
        /// <c>Il2CppClassPointerStore&lt;T&gt;</c>, found through the references this assembly already has.
        /// </summary>
        /// <remarks>
        /// BY SIMPLE NAME, AND THE NAMESPACE IS NEVER SPELLED. Polyfill.Boot is checked in CI for any
        /// mention of the interop runtime, because a plugin that names it loads it, and it runs before the
        /// runtime exists. Writing the namespace out here failed that check once; the type is unambiguous
        /// without it.
        /// </remarks>
        private static TypeDefinition PointerStore(ModuleDefinition module)
        {
            foreach (var reference in module.AssemblyReferences)
            {
                AssemblyDefinition assembly = null;
                try { assembly = module.AssemblyResolver?.Resolve(reference); } catch { }
                if (assembly == null) continue;

                foreach (var type in assembly.MainModule.Types)
                    if (type.Name == "Il2CppClassPointerStore`1" && type.HasGenericParameters)
                        return type;
            }
            return null;
        }
    }
}
