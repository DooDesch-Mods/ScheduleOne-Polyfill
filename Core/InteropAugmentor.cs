using MelonLoader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Core
{
    /// <summary>
    /// Put the missing names back, in MelonLoader's own generated interop assemblies.
    /// </summary>
    /// <remarks>
    /// This is the middleman. A mod compiled against an older game asks for a name that is gone; rather than
    /// editing the mod, the name is put back where the mod looks for it, pointing at wherever the thing
    /// lives today. One artifact serves every mod, present and future, and nobody's DLL is touched.
    ///
    /// Three properties make it safe to do at all:
    ///
    /// ADDITIVE ONLY. Nothing is removed, nothing existing is changed. A current mod therefore cannot be
    /// affected by construction - the surface it binds to is exactly what it was.
    ///
    /// THE ORIGINAL IS KEPT. The untouched assembly is copied to `.polyfill-orig` before the first write,
    /// and every later run reads from THAT, never from its own output. Otherwise a second run would inject
    /// into an already-injected file and the additions would pile up.
    ///
    /// WRITE LAST, WRITE ONCE. Cecil builds the whole image in memory and it goes to a temporary file
    /// first; the live assembly is replaced only once that has succeeded. A failure anywhere leaves the
    /// game exactly as it was.
    ///
    /// These files are MelonLoader's own cache, regenerated from the player's GameAssembly.dll whenever the
    /// game changes - which is why writing here is a different act from writing into somebody's Mods folder,
    /// and why a stamp records what the originals hashed to, so a regeneration is noticed rather than
    /// silently re-injected over.
    /// </remarks>
    internal static class InteropAugmentor
    {
        internal const string BackupSuffix = ".polyfill-orig";

        internal sealed class TypeForward
        {
            internal string InAssembly;      // where the mod looks for it
            internal string Namespace;       // the OLD namespace
            internal string Name;            // the OLD simple name
            internal string TargetAssembly;  // where it lives now
            internal string TargetFullName;  // and under what name, which a namespace change makes differ
        }

        /// <summary>An old member name, put back on the type that used to carry it.</summary>
        internal sealed class MemberForward
        {
            internal string InAssembly;
            internal string DeclaringType;   // full name, as the mod spells it
            internal string OldName;
            /// <summary>The member it became. Null means there is no single member it became, and a
            /// hand-written rule supplies the body instead.</summary>
            internal string NewName;
            internal int ParameterCount;
            internal string Rule;            // which heuristic or rule proposed it, for the log
        }

        internal sealed class Result
        {
            internal int Written;
            internal readonly List<string> Applied = new();
            internal readonly List<string> Refused = new();
        }

        /// <summary>Put every collected repair into the assemblies that need them.</summary>
        internal static Result Apply(string interopDirectory, List<TypeForward> types,
                                     List<MemberForward> members, MelonLogger.Instance log)
        {
            var result = new Result();
            if (types.Count == 0 && members.Count == 0) return result;

            var assemblies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var one in types) assemblies.Add(one.InAssembly);
            foreach (var one in members) assemblies.Add(one.InAssembly);

            foreach (string assembly in assemblies)
            {
                string live = Path.Combine(interopDirectory, assembly + ".dll");
                if (!File.Exists(live))
                {
                    result.Refused.Add($"{assembly}: not installed");
                    continue;
                }

                var typesHere = new List<TypeForward>();
                foreach (var one in types)
                    if (string.Equals(one.InAssembly, assembly, StringComparison.OrdinalIgnoreCase)) typesHere.Add(one);
                var membersHere = new List<MemberForward>();
                foreach (var one in members)
                    if (string.Equals(one.InAssembly, assembly, StringComparison.OrdinalIgnoreCase)) membersHere.Add(one);

                try { ApplyTo(live, typesHere, membersHere, result, log); }
                catch (Exception e)
                {
                    // The live file is only ever replaced by a completed temp file, so nothing is half-written.
                    result.Refused.Add($"{assembly}: {e.Message}");
                    log.Error($"[inject] {assembly} was left untouched: {e}");
                }
            }
            return result;
        }

        private static void ApplyTo(string livePath, List<TypeForward> forwards,
                                    List<MemberForward> members, Result result, MelonLogger.Instance log)
        {
            string interop = Path.GetDirectoryName(livePath);
            string backup = livePath + BackupSuffix;
            // The first run copies the untouched assembly aside; every run after reads from that copy, so
            // injections never stack on top of each other.
            if (!File.Exists(backup)) File.Copy(livePath, backup);
            string source = backup;

            string temporary = livePath + ".polyfill-tmp";
            int added = 0;

            // Writing resolves references, not just reading them - Cecil walks the whole reference graph on
            // Write and throws if it cannot find, say, UnityEngine.CoreModule. Every interop assembly sits in
            // one folder, so pointing the resolver there is the whole fix.
            //
            // The resolver is disposed BEFORE the live file is replaced, not after: it opens what it resolves
            // and holds the handle, and one of the things it can resolve is the file about to be overwritten.
            // What the verification is allowed to demand back out of the written image is what was actually
            // put in, never what was planned. A repair can be refused for good reasons - the name is taken,
            // the target is not on this build, a forwarder would point at its own assembly - and checking
            // against the plan turns any one of those into a failed verification, which throws away every
            // other repair with it. Refusing one repair must cost exactly that one repair.
            var emittedForwards = new List<TypeForward>();
            var emittedMembers = new List<MemberForward>();

            try
            {
                using (var resolver = new DefaultAssemblyResolver())
                using (var module = ReadWithResolver(source, interop, resolver))
                {
                    foreach (var forward in forwards)
                    {
                        if (module.GetType(Full(forward)) != null)
                        { result.Refused.Add($"{Full(forward)}: the name is taken here"); continue; }

                        // A forwarder is an entry saying "this name lives in ANOTHER assembly". Pointing one at
                        // the assembly it already sits in says the name is somewhere else and somewhere else is
                        // here, and the type loader has nowhere to go from there. Measured: the name stays
                        // unresolvable, and the process dies when a mod's compiled call reaches it rather than
                        // when reflection asks politely.
                        //
                        // A type that only changed namespace gets a class instead, deriving from where it
                        // lives now. See ShadowTypes.
                        if (string.Equals(forward.TargetAssembly, forward.InAssembly, StringComparison.OrdinalIgnoreCase))
                        {
                            var shadow = ShadowTypes.TryAdd(module, forward.Namespace, forward.Name,
                                                            forward.TargetFullName, out string why);
                            if (shadow == null)
                            {
                                result.Refused.Add($"{Full(forward)}: it only changed namespace, and {why}");
                                continue;
                            }
                            result.Applied.Add($"{forward.InAssembly}!{Full(forward)} -> a class deriving from "
                                             + forward.TargetFullName);
                            emittedForwards.Add(forward);
                            added++;
                            continue;
                        }

                        var scope = ScopeFor(module, forward.TargetAssembly);
                        if (scope == null)
                        { result.Refused.Add($"{Full(forward)}: {forward.TargetAssembly} is not installed"); continue; }

                        module.ExportedTypes.Add(new ExportedType(forward.Namespace, forward.Name, module, scope)
                        {
                            Attributes = TypeAttributes.Forwarder,
                        });
                        result.Applied.Add($"{forward.InAssembly}!{Full(forward)} -> {forward.TargetAssembly}");
                        emittedForwards.Add(forward);
                        added++;
                    }

                    foreach (var member in members)
                        if (AddMemberForward(module, member, result)) { emittedMembers.Add(member); added++; }

                    if (added == 0) return;
                    module.Write(temporary);
                }

                // Read back what was actually written before it is allowed anywhere near the game. Cecil
                // reporting no error is not the same as the member being in the file: the first version of
                // this reported two repairs applied and wrote one, and nothing noticed until a runtime
                // probe came up empty an hour later. A repair that cannot be found in the output is a bug
                // in here, and the game gets the original instead.
                string missing = Verify(temporary, emittedForwards, emittedMembers);
                if (missing != null)
                {
                    result.Refused.Add($"{Path.GetFileName(livePath)}: written image is missing {missing}; "
                                     + "the original was kept");
                    log.Error($"[inject] {Path.GetFileName(livePath)}: the written image does not contain "
                            + $"{missing}. Nothing was replaced.");
                    return;
                }

                File.Copy(temporary, livePath, true);
                result.Written++;
                log.Msg($"[inject] {Path.GetFileName(livePath)}: {added} repair(s) added.");
            }
            finally
            {
                // A half-written image must never be left lying next to the real one.
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        /// <summary>
        /// Put the old member name back on its type, as a method that calls the new one.
        /// </summary>
        /// <remarks>
        /// The whole repair for a rename, and it is deliberately the dumbest thing that can work: a new
        /// method with the SAME signature whose entire body is "call the one that replaced me". No opcode
        /// surgery in anyone's mod, no wrapper juggling, nothing to get subtly wrong.
        ///
        /// It works because these are interop assemblies. The real body resolves a native method by token
        /// and is generated code either way, so one more managed hop costs a call and changes nothing about
        /// what reaches the game.
        ///
        /// Refused rather than guessed: an ambiguous target, a signature that does not line up, or a name
        /// already present. Adding a second member under a name that exists would change what current mods
        /// bind to, and this is only ever allowed to ADD what is missing.
        /// </remarks>
        private static bool AddMemberForward(ModuleDefinition module, MemberForward member, Result result)
        {
            string label = $"{member.DeclaringType}::{member.OldName}";

            var type = module.GetType(member.DeclaringType);
            if (type == null) { result.Refused.Add($"{label}: the type is not in this assembly"); return false; }

            // No single successor - a hand-written rule builds the body instead.
            var rule = member.NewName == null
                ? CuratedRules.Find(member.InAssembly, member.DeclaringType,
                                    member.OldName, member.ParameterCount)
                : null;

            if (rule == null || !rule.AllowOverload)
                foreach (var existing in type.Methods)
                    if (existing.Name == member.OldName)
                    { result.Refused.Add($"{label}: the name is already taken"); return false; }

            if (member.NewName == null)
            {
                if (rule == null) { result.Refused.Add($"{label}: no rule for this"); return false; }

                var built = rule.Emit(module, type);
                if (built == null)
                { result.Refused.Add($"{label}: the rule needs members this build does not have"); return false; }

                // An overload rule is trusted to add a signature, never to replace one. If the exact shape
                // is already here the rule has misread the build, and adding it would mean two methods a
                // call cannot be told apart by.
                foreach (var existing in type.Methods)
                    if (existing.Name == built.Name && SameShape(existing, built))
                    { result.Refused.Add($"{label}: that exact signature is already here"); return false; }

                type.Methods.Add(built);
                result.Applied.Add($"{member.InAssembly}!{label}  [rule: {rule.Because}]");
                return true;
            }

            MethodDefinition target = null;
            foreach (var candidate in type.Methods)
            {
                if (candidate.Name != member.NewName) continue;
                if (target != null) { result.Refused.Add($"{label}: {member.NewName} is overloaded here"); return false; }
                target = candidate;
            }
            if (target == null) { result.Refused.Add($"{label}: {member.NewName} is not on this type"); return false; }

            if (target.HasGenericParameters)
            { result.Refused.Add($"{label}: {member.NewName} is generic"); return false; }

            var forward = new MethodDefinition(member.OldName,
                MethodAttributes.Public | MethodAttributes.HideBySig
                    | (target.IsStatic ? MethodAttributes.Static : 0),
                target.ReturnType);

            foreach (var parameter in target.Parameters)
                forward.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes,
                                                               parameter.ParameterType));

            var il = forward.Body.GetILProcessor();
            if (!target.IsStatic) il.Emit(OpCodes.Ldarg_0);
            for (int i = 0; i < target.Parameters.Count; i++)
                il.Emit(OpCodes.Ldarg, forward.Parameters[i]);
            // Call, not callvirt: the target is a concrete method on a concrete type and the null check has
            // already happened on the caller's side.
            il.Emit(OpCodes.Call, target);
            il.Emit(OpCodes.Ret);

            type.Methods.Add(forward);
            result.Applied.Add($"{member.InAssembly}!{label}() -> {member.NewName}()  [{member.Rule}]");
            return true;
        }

        /// <summary>The first repair that is not in the written image, or null when all of them are.</summary>
        private static string Verify(string writtenPath, List<TypeForward> forwards, List<MemberForward> members)
        {
            using var module = ModuleDefinition.ReadModule(writtenPath, new ReaderParameters { InMemory = true });

            foreach (var forward in forwards)
            {
                // Either kind of repair counts: a forwarder row for a type that left the assembly, a class
                // of its own for one that only changed namespace. What is being checked is that the name
                // resolves, not which of the two ways got it there.
                bool found = module.GetType(Full(forward)) != null;
                if (!found)
                    foreach (var exported in module.ExportedTypes)
                        if (exported.Namespace == forward.Namespace && exported.Name == forward.Name)
                        { found = true; break; }
                if (!found) return Full(forward);
            }

            foreach (var member in members)
            {
                var type = module.GetType(member.DeclaringType);
                bool found = false;
                if (type != null)
                    foreach (var method in type.Methods)
                        // By name AND arity: where a repair is an overload the name was there before it, so
                        // matching on the name alone would pass whether or not anything was written.
                        if (method.Name == member.OldName && method.Parameters.Count == member.ParameterCount)
                        { found = true; break; }
                if (!found) return $"{member.DeclaringType}::{member.OldName}";
            }
            return null;
        }

        /// <summary>Same parameter types in the same order - what makes two methods indistinguishable.</summary>
        private static bool SameShape(MethodDefinition a, MethodDefinition b)
        {
            if (a.Parameters.Count != b.Parameters.Count) return false;
            for (int i = 0; i < a.Parameters.Count; i++)
                if (a.Parameters[i].ParameterType.FullName != b.Parameters[i].ParameterType.FullName)
                    return false;
            return true;
        }

        private static ModuleDefinition ReadWithResolver(string path, string interop,
                                                         DefaultAssemblyResolver resolver)
        {
            resolver.AddSearchDirectory(interop);
            try
            {
                string core = Path.GetDirectoryName(typeof(object).Assembly.Location);
                if (!string.IsNullOrEmpty(core)) resolver.AddSearchDirectory(core);
            }
            catch { }
            try
            {
                // Where MelonLoader keeps Il2CppInterop. Every interop type derives from Il2CppObjectBase, so
                // without this the base chain stops at the assembly boundary and anything that has to walk it
                // - Pointer, for one - silently finds nothing.
                string loader = Path.GetDirectoryName(typeof(MelonLoader.MelonPlugin).Assembly.Location);
                if (!string.IsNullOrEmpty(loader)) resolver.AddSearchDirectory(loader);
            }
            catch { }

            return ModuleDefinition.ReadModule(path, new ReaderParameters
            {
                InMemory = true,                 // the backup is not held open either
                AssemblyResolver = resolver,
            });
        }

        private static string Full(TypeForward forward)
            => string.IsNullOrEmpty(forward.Namespace) ? forward.Name : forward.Namespace + "." + forward.Name;

        private static AssemblyNameReference ScopeFor(ModuleDefinition module, string assemblyName)
        {
            foreach (var reference in module.AssemblyReferences)
                if (string.Equals(reference.Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    return reference;

            // Interop assemblies are all 0.0.0.0 and unsigned, so a bare name reference is the whole identity.
            var added = new AssemblyNameReference(assemblyName, new Version(0, 0, 0, 0));
            module.AssemblyReferences.Add(added);
            return added;
        }

        /// <summary>
        /// The file a player leaves behind to say "undo it".
        /// </summary>
        /// <remarks>
        /// Restoring cannot happen while the game runs: the assemblies are mapped and Windows refuses to
        /// replace them, which is why the in-game command only asks. The undo itself belongs in the same
        /// window as the repair - before any of it is loaded.
        /// </remarks>
        internal static string PendingMarker(string userDataDirectory)
            => Path.Combine(userDataDirectory ?? ".", "Polyfill", "restore-pending");

        /// <summary>Put every augmented assembly back the way MelonLoader generated it.</summary>
        internal static int Restore(string interopDirectory, MelonLogger.Instance log)
        {
            int restored = 0;
            foreach (string backup in Directory.GetFiles(interopDirectory, "*" + BackupSuffix))
            {
                string live = backup.Substring(0, backup.Length - BackupSuffix.Length);
                try { File.Copy(backup, live, true); File.Delete(backup); restored++; }
                catch (Exception e) { log.Error($"[inject] could not restore {Path.GetFileName(live)}: {e.Message}"); }
            }
            return restored;
        }
    }
}
