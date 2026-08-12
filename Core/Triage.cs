using MelonLoader;
using Mono.Cecil;
using Polyfill.Boot;

namespace Polyfill.Core
{
    /// <summary>
    /// Ask every installed mod what it expects, and check it against what this installation has.
    /// </summary>
    /// <remarks>
    /// The whole of this phase, and the thing that decides what the next one is worth building.
    ///
    /// The key point is that a mod's metadata already names everything it wants. Cecil reads the type and
    /// member references straight out of the file, so there is no need to know which game version the mod
    /// was built against, and no archive has to exist for this to work. An unresolved name plus a live index
    /// is enough to say what is missing - and often enough to say what it became.
    ///
    /// Nothing here writes, loads or patches anything. Every mod is loaded afterwards exactly as MelonLoader
    /// would have loaded it.
    /// </remarks>
    internal static class Triage
    {
        internal static List<ModReport> Reports { get; private set; } = new();

        /// <summary>Repairs the analysis found, collected across every mod: one game, one set of forwarders.</summary>
        private static readonly List<InteropAugmentor.TypeForward> _forwards = new();
        private static readonly List<InteropAugmentor.MemberForward> _members = new();

        /// <summary>
        /// Put the found repairs into the interop assemblies.
        /// </summary>
        /// <remarks>
        /// Deduplicated first: two mods built against the same old version ask for the same missing name, and
        /// the forwarder belongs in the game once, not once per mod.
        /// </remarks>
        private static void Repair(string interopDirectory, MelonLogger.Instance log)
        {
            if (_forwards.Count == 0 && _members.Count == 0) return;
            if (Boot.Plugin.DryRun)
            {
                log.Msg($"[inject] DryRun: {_forwards.Count + _members.Count} repair(s) found and none applied. "
                      + "Turn DryRun off in MelonPreferences to let them through.");
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var uniqueTypes = new List<InteropAugmentor.TypeForward>();
            foreach (var forward in _forwards)
                if (seen.Add("T|" + forward.InAssembly + "!" + forward.Namespace + "." + forward.Name))
                    uniqueTypes.Add(forward);

            var uniqueMembers = new List<InteropAugmentor.MemberForward>();
            foreach (var member in _members)
                if (seen.Add("M|" + member.InAssembly + "!" + member.DeclaringType + "::" + member.OldName))
                    uniqueMembers.Add(member);

            var result = InteropAugmentor.Apply(interopDirectory, uniqueTypes, uniqueMembers, log);
            foreach (string applied in result.Applied) log.Msg("[inject]   " + applied);
            foreach (string refused in result.Refused) log.Warning("[inject]   refused: " + refused);
        }

        internal static void Run(List<ModCandidate> candidates, MelonLogger.Instance log)
        {
            string interopDirectory = InteropIndex.LocateDirectory();
            if (interopDirectory == null)
            {
                log.Warning("[triage] the generated interop assemblies could not be found; skipping analysis.");
                return;
            }

            _forwards.Clear();
            _members.Clear();
            var reports = new List<ModReport>(candidates.Count);
            int assemblyCount;

            // Scoped, and the scope matters: Cecil's assembly resolver opens every assembly it resolves
            // WITHOUT reading it into memory first, and keeps the handle until it is disposed. Repairing
            // inside this block fails with "the file is being used by another process" - and the process is
            // this one.
            using (var index = new InteropIndex(interopDirectory, LibraryDirectories(), SearchDirectories()))
            {
                foreach (var candidate in candidates)
                {
                    var report = Analyse(candidate, index, log);
                    if (report != null) reports.Add(report);
                }
                assemblyCount = index.AssemblyCount;
            }

            Reports = reports;
            Report.Write(reports, interopDirectory, assemblyCount, new List<string>());
            Summarise(reports, log);
            Repair(interopDirectory, log);
        }

        /// <summary>Installed libraries - checked as well as searched. S1API lives here.</summary>
        private static IEnumerable<string> LibraryDirectories()
        {
            yield return MelonLoader.Utils.MelonEnvironment.ModsDirectory;
            yield return MelonLoader.Utils.MelonEnvironment.UserLibsDirectory;
        }

        /// <summary>Searched so references resolve, but never checked: MelonLoader's own ABI is stable and
        /// is its business, not ours.</summary>
        private static IEnumerable<string> SearchDirectories()
        {
            yield return MelonLoader.Utils.MelonEnvironment.PluginsDirectory;
            yield return MelonLoader.Utils.MelonEnvironment.OurRuntimeDirectory;
        }

        private static ModReport Analyse(ModCandidate candidate, InteropIndex index, MelonLogger.Instance log)
        {
            var report = new ModReport
            {
                Path = candidate.Path,
                AssemblyName = candidate.AssemblyName,
                Name = candidate.MelonName,
                Version = candidate.MelonVersion,
                Author = candidate.MelonAuthor,
            };

            try
            {
                using var module = ModuleDefinition.ReadModule(candidate.Path, new ReaderParameters
                {
                    InMemory = true,                       // the player's file is never locked
                    ReadingMode = ReadingMode.Deferred,
                    AssemblyResolver = index.Resolver,
                });

                var missingTypes = CheckTypes(module, index, report);
                CheckMembers(module, index, report, missingTypes);
                HarmonyTargets.Check(module, index, report);
            }
            catch (Exception e)
            {
                report.Findings.Add(new Finding
                {
                    Kind = "read",
                    Symbol = Path.GetFileName(candidate.Path),
                    Reason = "could not be read: " + e.Message,
                });
                log.Warning($"[triage] {candidate.Display} could not be analysed: {e.Message}");
            }
            return report;
        }

        /// <summary>Every game type the mod names, and whether it is still there.</summary>
        private static HashSet<string> CheckTypes(ModuleDefinition module, InteropIndex index, ModReport report)
        {
            var missing = new HashSet<string>(StringComparer.Ordinal);

            foreach (var reference in module.GetTypeReferences())
            {
                string scope = reference.Scope?.Name;
                if (!index.IsTracked(scope)) continue;
                report.TypeRefs++;

                if (Resolve(reference, index) != null) continue;
                missing.Add(reference.FullName);

                var elsewhere = index.BySimpleName(reference.Name);
                string hint = "", reason = "type no longer exists in " + scope;

                if (elsewhere.Count == 1)
                {
                    var found = elsewhere[0];
                    hint = found.FullName + " in " + found.Module.Assembly.Name.Name;
                    reason = "type moved";
                }
                else if (elsewhere.Count > 1)
                {
                    reason = $"type no longer exists in {scope}; {elsewhere.Count} types share the name "
                           + $"{reference.Name}, so which one it became is not decidable here";
                }

                if (elsewhere.Count == 1)
                    _forwards.Add(new InteropAugmentor.TypeForward
                    {
                        InAssembly = scope,
                        Namespace = reference.Namespace,
                        Name = reference.Name,
                        TargetAssembly = elsewhere[0].Module.Assembly.Name.Name,
                        TargetFullName = elsewhere[0].FullName,
                    });

                report.Findings.Add(new Finding
                {
                    Kind = (index.Kind(scope) == "game" ? "" : "library-") + "type",
                    Scope = scope, Symbol = reference.FullName,
                    Reason = reason, Hint = hint,
                });
            }
            return missing;
        }

        /// <summary>Every game member the mod calls or reads, and whether it is still there.</summary>
        private static void CheckMembers(ModuleDefinition module, InteropIndex index, ModReport report,
                                         HashSet<string> missingTypes)
        {
            foreach (var reference in module.GetMemberReferences())
            {
                var declaringReference = Root(reference.DeclaringType);
                string scope = declaringReference?.Scope?.Name;
                if (!index.IsTracked(scope)) continue;
                report.MemberRefs++;

                // A member of a type that is already reported missing is not a second finding.
                if (declaringReference != null && missingTypes.Contains(declaringReference.FullName)) continue;

                var declaring = Resolve(declaringReference, index);
                if (declaring == null) continue;

                // A library break is a different repair from a game break - the game's missing names go back
                // into the interop assemblies, a library's cannot - so the two are never one finding kind.
                string prefix = index.Kind(scope) == "game" ? "" : "library-";
                if (reference is MethodReference method) CheckMethod(method, declaring, scope, prefix, report);
                else if (reference is FieldReference field) CheckField(field, declaring, scope, prefix, report);
            }
        }

        private static void CheckMethod(MethodReference wanted, TypeDefinition declaring, string scope,
                                        string kindPrefix, ModReport report)
        {
            bool nameExists = false;
            foreach (var method in declaring.Methods)
            {
                if (method.Name != wanted.Name) continue;
                nameExists = true;
                if (NameHeuristics.SameParameters(wanted, method)) return;   // still there, unchanged
            }

            var hits = NameHeuristics.ForMethod(declaring, wanted.Name, wanted);
            string hint = hits.Count == 1 ? hits[0].NewName + "  [" + hits[0].Rule + "]" : "";
            int parameters = wanted.Parameters?.Count ?? 0;

            // One candidate, on the game, and a method: that is a repair we can make without inferring
            // anything. Anything else is reported and left alone.
            if (hits.Count == 1 && kindPrefix.Length == 0 && hits[0].Member is MethodDefinition)
                _members.Add(new InteropAugmentor.MemberForward
                {
                    InAssembly = scope,
                    DeclaringType = declaring.FullName,
                    OldName = wanted.Name,
                    NewName = hits[0].NewName,
                    ParameterCount = parameters,
                    Rule = hits[0].Rule,
                });
            else if (hits.Count == 0 && kindPrefix.Length == 0)
            {
                // Nothing on the type to point at, but somebody may have read the game and written down
                // what this became.
                var rule = CuratedRules.Find(scope, declaring.FullName, wanted.Name, parameters);
                if (rule != null)
                {
                    hint = "hand-written rule: " + rule.Because;
                    _members.Add(new InteropAugmentor.MemberForward
                    {
                        InAssembly = scope,
                        DeclaringType = declaring.FullName,
                        OldName = wanted.Name,
                        NewName = null,
                        ParameterCount = parameters,
                        Rule = "curated",
                    });
                }
            }
            string reason = nameExists
                ? "the method still exists but its parameters changed"
                : hits.Count > 1
                    ? $"method missing; {hits.Count} members could be meant, so none is chosen"
                    : "method missing, with nothing on this type to point at";

            report.Findings.Add(new Finding
            {
                Kind = kindPrefix + "member", Scope = scope,
                Symbol = declaring.FullName + "::" + wanted.Name + Signature(wanted),
                Reason = reason, Hint = hint,
            });
        }

        private static void CheckField(FieldReference wanted, TypeDefinition declaring, string scope,
                                       string kindPrefix, ModReport report)
        {
            foreach (var field in declaring.Fields)
                if (field.Name == wanted.Name) return;

            var hits = NameHeuristics.ForField(declaring, wanted.Name);
            string hint = hits.Count == 1 ? hits[0].NewName + "  [" + hits[0].Rule + "]" : "";
            string reason = hits.Count > 1
                ? $"field missing; {hits.Count} members could be meant, so none is chosen"
                : hits.Count == 1 && hits[0].KindChanged
                    ? "the field became a property"
                    : "field missing, with nothing on this type to point at";

            report.Findings.Add(new Finding
            {
                Kind = kindPrefix + "field", Scope = scope,
                Symbol = declaring.FullName + "::" + wanted.Name,
                Reason = reason, Hint = hint,
            });
        }

        /// <summary>Unwrap arrays, by-ref and generic instances down to the type that carries the scope.</summary>
        internal static TypeReference Root(TypeReference reference)
        {
            while (reference is TypeSpecification specification) reference = specification.ElementType;
            return reference;
        }

        /// <summary>
        /// The index is asked FIRST, and that order is the point.
        /// </summary>
        /// <remarks>
        /// The index reads what MelonLoader generated. Cecil's own resolver reads whatever is in the folder
        /// right now - which, after a previous launch, is the file we already wrote. Asking Cecil first
        /// makes the analysis see its own repairs as if the game had always had them, so nothing is
        /// collected, so the next write rebuilds from the original WITHOUT them. Silent, and it undoes
        /// itself every second launch.
        /// </remarks>
        internal static TypeDefinition Resolve(TypeReference reference, InteropIndex index)
        {
            if (reference == null) return null;

            var fromIndex = index.FindType(reference.Scope?.Name, reference.FullName);
            if (fromIndex != null) return fromIndex;

            try { return reference.Resolve(); } catch { return null; }
        }

        private static string Signature(MethodReference method)
        {
            var parts = new List<string>();
            if (method.HasParameters)
                foreach (var parameter in method.Parameters)
                    parts.Add(parameter.ParameterType?.Name ?? "?");
            return "(" + string.Join(", ", parts) + ")";
        }

        private static void Summarise(List<ModReport> reports, MelonLogger.Instance log)
        {
            int clean = 0, adaptable = 0, blocked = 0;
            foreach (var report in reports)
                switch (report.Verdict) { case "clean": clean++; break; case "adaptable": adaptable++; break; default: blocked++; break; }

            int typeRefs = 0, memberRefs = 0, harmony = 0;
            foreach (var report in reports)
            { typeRefs += report.TypeRefs; memberRefs += report.MemberRefs; harmony += report.HarmonyTargetsChecked; }

            log.Msg($"[triage] Schedule I {Report.GameVersion()} - {reports.Count} mod(s): "
                  + $"{clean} need nothing, {adaptable} could be adapted, {blocked} ask for something that is gone.");
            // Printed even when everything is clean: "no findings" and "nothing was looked at" are not the
            // same answer, and only these numbers tell them apart.
            log.Msg($"[triage] checked {typeRefs} type references, {memberRefs} member references and "
                  + $"{harmony} Harmony targets.");

            foreach (var report in reports)
            {
                if (report.Verdict == "clean") continue;
                int withHint = 0;
                foreach (var finding in report.Findings) if (!string.IsNullOrEmpty(finding.Hint)) withHint++;
                log.Warning($"[triage]   {report.Name ?? report.AssemblyName} {report.Version} - "
                          + $"{report.Findings.Count} missing, {withHint} with a candidate on this machine");
            }

            if (reports.Count > 0)
                log.Msg("[triage] Type `polyfill` in the console for the full list, or read " + Report.LastRunPath);
        }
    }
}
