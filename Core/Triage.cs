using MelonLoader;
using Mono.Cecil;
using Polyfill.Boot;
using Polyfill.Contract;

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
        private static readonly List<InteropAugmentor.ParameterRename> _renames = new();

        /// <summary>
        /// Put the found repairs into the interop assemblies.
        /// </summary>
        /// <remarks>
        /// Deduplicated first: two mods built against the same old version ask for the same missing name, and
        /// the forwarder belongs in the game once, not once per mod.
        /// </remarks>
        private static Dictionary<string, string> Repair(string interopDirectory, InteropOriginals originals,
                                                        MelonLogger.Instance log)
        {
            var nothing = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_forwards.Count == 0 && _members.Count == 0 && _renames.Count == 0) return nothing;
            if (Boot.Plugin.DryRun)
            {
                log.Msg($"[inject] DryRun: {_forwards.Count + _members.Count + _renames.Count} repair(s) found and none applied. "
                      + "Turn DryRun off in MelonPreferences to let them through.");
                return nothing;
            }

            // Keyed by the repair's own identity, which includes the parameter count. Without it Foo(int)
            // and Foo(int, int) are one key: the second is dropped as a duplicate, the mod that wanted it
            // stays broken, and its report says "adaptable" because a candidate was found for the name.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var uniqueTypes = new List<InteropAugmentor.TypeForward>();
            foreach (var forward in _forwards)
                if (seen.Add(forward.Key)) uniqueTypes.Add(forward);

            var uniqueMembers = new List<InteropAugmentor.MemberForward>();
            foreach (var member in _members)
                if (seen.Add(member.Key)) uniqueMembers.Add(member);

            var uniqueRenames = new List<InteropAugmentor.ParameterRename>();
            foreach (var rename in _renames)
                if (seen.Add(rename.Key)) uniqueRenames.Add(rename);

            var result = InteropAugmentor.Apply(interopDirectory, uniqueTypes, uniqueMembers, uniqueRenames,
                                                originals, log);
            foreach (string applied in result.Applied) log.Msg("[inject]   " + applied);
            foreach (string refused in result.Refused) log.Warning("[inject]   refused: " + refused);

            // Written even when nothing was: the stamp is the list of assemblies Polyfill has touched, and
            // an empty list is the correct answer after a launch that touched none.
            StampFile.Write(originals.Generator.Digest(), result.Stamped);
            return result.Outcomes;
        }

        /// <summary>
        /// Carry what the injector decided back onto the findings it decided about.
        /// </summary>
        /// <remarks>
        /// The report used to be written BEFORE the repair ran, so it could only say what was found. A
        /// refused repair reached the log and nothing else - which made "Polyfill had a candidate and did
        /// not trust it" invisible in the one file a player is ever asked to send.
        /// </remarks>
        private static void RecordOutcomes(List<ModReport> reports, Dictionary<string, string> outcomes)
        {
            if (outcomes.Count == 0) return;
            foreach (var report in reports)
                foreach (var finding in report.Findings)
                {
                    if (finding.RepairKey == null) continue;
                    if (!outcomes.TryGetValue(finding.RepairKey, out string outcome)) continue;

                    int colon = outcome.IndexOf(':');
                    finding.Outcome = colon < 0 ? outcome : outcome.Substring(0, colon);
                    finding.OutcomeDetail = colon < 0 ? "" : outcome.Substring(colon + 1);
                }
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
            _renames.Clear();
            var reports = new List<ModReport>(candidates.Count);
            int assemblyCount;

            // First, and before the index exists: which file is the untouched original of each assembly.
            // The analysis has to read that file or a repair applied last launch reads as "nothing was
            // missing"; the injector has to write from the same one or the two disagree. Under DryRun and
            // with the window already shut, the decisions are still made and nothing on disk is touched.
            bool mayAct = !Boot.Plugin.DryRun && !Boot.Diagnostics.InteropAlreadyLoaded;
            var originals = InteropOriginals.Take(interopDirectory, mayAct, log);

            // Said once, and not as a warning: nothing is wrong on a build nobody has read yet. Everything
            // that can be checked against the player's own game still runs.
            string horizon = Bridges.Registry.PastTheHorizon(GameVersionSource.Current);
            if (horizon != null) log.Msg("[bridge] " + horizon);

            // Scoped, and the scope matters: Cecil's assembly resolver opens every assembly it resolves
            // WITHOUT reading it into memory first, and keeps the handle until it is disposed. Repairing
            // inside this block fails with "the file is being used by another process" - and the process is
            // this one.
            using (var index = new InteropIndex(interopDirectory, originals, LibraryDirectories(),
                                               SearchDirectories()))
            {
                foreach (var candidate in candidates)
                {
                    var report = Analyse(candidate, index, log);
                    if (report != null) reports.Add(report);
                }
                assemblyCount = index.AssemblyCount;
            }

            Reports = reports;

            // Repair first, THEN write. The report is the answer to "what did Polyfill do about my mod",
            // and written the other way round it could only ever answer "what did it find".
            var outcomes = Repair(interopDirectory, originals, log);
            RecordOutcomes(reports, outcomes);
            Report.Write(reports, interopDirectory, assemblyCount, new List<string>());
            Summarise(reports, log);
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

                var repaired = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
                var missingTypes = CheckTypes(module, index, report, repaired);
                CheckMembers(module, index, report, missingTypes, repaired);
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

        /// <summary>The bridge's answer list, in the shape the emitter takes. Null when there is none.</summary>
        private static List<FacadeTypes.Member> Answers(Bridges.TypeRename renamed)
        {
            if (renamed?.Answers == null) return null;

            var members = new List<FacadeTypes.Member>();
            foreach (var answer in renamed.Answers)
                members.Add(new FacadeTypes.Member
                {
                    Name = answer.Name, Returns = answer.Returns, Takes = answer.Takes,
                });
            return members;
        }

        /// <summary>Every game type the mod names, and whether it is still there.</summary>
        private static HashSet<string> CheckTypes(ModuleDefinition module, InteropIndex index, ModReport report,
                                                  Dictionary<string, TypeDefinition> repaired)
        {
            var missing = new HashSet<string>(StringComparer.Ordinal);

            foreach (var reference in module.GetTypeReferences())
            {
                string scope = reference.Scope?.Name;
                if (!index.IsTracked(scope)) continue;
                report.TypeRefs++;

                if (Resolve(reference, index) != null) continue;
                missing.Add(reference.FullName);

                string hint = "", reason = "type no longer exists in " + scope;
                string repairKey = null;
                TypeDefinition became = null;

                // A person naming the pair outranks a name that merely matches, for the same reason a
                // bridge outranks a spelling rule on a member: one of the two was read out of both builds.
                var renamed = Bridges.Registry.FindType(scope, reference.FullName);
                if (renamed != null)
                {
                    became = index.FindType(scope, renamed.NewFullName);
                    if (became != null) hint = "hand-written rule: " + renamed.Because;
                    else reason = $"type no longer exists in {scope}, and {renamed.NewFullName} - what it "
                                + "became in 0.4.6 - is not on this build either";
                }

                if (became == null && renamed == null)
                {
                    var elsewhere = index.BySimpleName(reference.Name);
                    if (elsewhere.Count == 1)
                    {
                        became = elsewhere[0];
                        hint = became.FullName + " in " + became.Module.Assembly.Name.Name;
                        reason = "type moved";
                    }
                    else if (elsewhere.Count > 1)
                    {
                        reason = $"type no longer exists in {scope}; {elsewhere.Count} types share the name "
                               + $"{reference.Name}, so which one it became is not decidable here";
                    }
                }

                if (became != null)
                {
                    // The type is not missing after this - it is standing in for the new one. Remembering
                    // WHICH is the whole reason the members on it can be checked at all; see CheckMembers.
                    repaired[reference.FullName] = became;

                    var forward = new InteropAugmentor.TypeForward
                    {
                        InAssembly = scope,
                        Namespace = reference.Namespace,
                        Name = reference.Name,
                        NestedIn = Root(reference.DeclaringType)?.FullName,
                        TargetAssembly = became.Module.Assembly.Name.Name,
                        TargetFullName = became.FullName,
                        ByNativeClass = renamed?.ByNativeClass ?? false,
                        Answers = Answers(renamed),
                    };
                    _forwards.Add(forward);
                    repairKey = forward.Key;
                }

                report.Findings.Add(new Finding
                {
                    Kind = (index.Kind(scope) == "game" ? "" : "library-") + "type",
                    Scope = scope, Symbol = reference.FullName,
                    Reason = reason, Hint = hint, RepairKey = repairKey,
                });
            }
            return missing;
        }

        /// <summary>Every game member the mod calls or reads, and whether it is still there.</summary>
        private static void CheckMembers(ModuleDefinition module, InteropIndex index, ModReport report,
                                         HashSet<string> missingTypes,
                                         Dictionary<string, TypeDefinition> repaired)
        {
            foreach (var reference in module.GetMemberReferences())
            {
                var declaringReference = Root(reference.DeclaringType);
                string scope = declaringReference?.Scope?.Name;
                if (!index.IsTracked(scope)) continue;
                report.MemberRefs++;

                var declaring = Resolve(declaringReference, index);

                // A TYPE THAT WAS REPAIRED IS NOT MISSING, and treating it as missing skipped every member
                // on it. Measured: Lithium's ATM patch registered once the type came back, then threw
                // eighteen thousand times in one session on ATMInterface.isOpen - which 0.4.6 calls IsOpen
                // and which nothing had ever looked for, because the check that would have found it
                // stopped at "the type it is on was reported already".
                //
                // The members are checked against what the type BECAME. What is emitted then lands on that
                // type, and the stand-in inherits it.
                if (declaring == null && declaringReference != null)
                    repaired.TryGetValue(declaringReference.FullName, out declaring);

                // A member of a type that is genuinely gone is not a second finding.
                if (declaring == null && declaringReference != null
                    && missingTypes.Contains(declaringReference.FullName)) continue;
                if (declaring == null) continue;

                // A library break is a different repair from a game break - the game's missing names go back
                // into the interop assemblies, a library's cannot - so the two are never one finding kind.
                string prefix = index.Kind(scope) == "game" ? "" : "library-";
                if (reference is MethodReference method) CheckMethod(method, declaring, scope, prefix, report, index, repaired);
                else if (reference is FieldReference field) CheckField(field, declaring, scope, prefix, report, index);
            }
        }

        /// <summary>Is that name on the live type at all? The history says what a build called something,
        /// this says whether THIS build has it - and only both together are a repair.</summary>
        private static bool Has(TypeDefinition type, string name)
        {
            foreach (var method in type.Methods) if (method.Name == name) return true;
            return false;
        }

        /// <summary>
        /// The type a member could be declared on: itself, then everything it inherits from.
        /// </summary>
        /// <remarks>
        /// A MEMBER ON A BASE TYPE IS NOT MISSING, and not walking here made Polyfill invent breakage. 0.4.6
        /// moved <c>ID</c>, <c>Name</c>, <c>Icon</c> and five more off <c>ItemDefinition</c> onto
        /// <c>Core.Items.Framework.BaseItemDefinition</c> - a base class in ANOTHER assembly. The CLR
        /// resolves a member reference by walking the hierarchy, so every one of those calls still works;
        /// only this analysis, which looked at the named type and stopped, said otherwise. Four mods in a
        /// single reported run were marked "blocked" for members that were never gone.
        ///
        /// The same shape repeats where the game factors a base out: <c>StationInterface&lt;T&gt;</c> took
        /// <c>_canvas</c> from every station screen in 0.4.6.
        ///
        /// Generic bases are unwrapped to the open type, which is where the members are declared -
        /// <c>PackagingStationCanvas : StationInterface&lt;PackagingStationCanvas&gt;</c> answers about
        /// <c>StationInterface`1</c>. The visited set is not paranoia about real hierarchies but about
        /// hand-edited metadata, which this reads without trusting.
        /// </remarks>
        private static IEnumerable<TypeDefinition> Chain(TypeDefinition type, InteropIndex index)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var current = type; current != null; )
            {
                if (!seen.Add(current.FullName)) yield break;
                yield return current;

                if (current.BaseType == null) yield break;
                current = Resolve(Root(current.BaseType), index);
            }
        }

        /// <summary>The base type that carries this member under this exact signature, or null.</summary>
        private static TypeDefinition Inherits(TypeDefinition declaring, MethodReference wanted,
                                               InteropIndex index)
        {
            bool first = true;
            foreach (var type in Chain(declaring, index))
            {
                if (first) { first = false; continue; }          // the type itself was already asked
                foreach (var method in type.Methods)
                    if (method.Name == wanted.Name && NameHeuristics.SameParameters(wanted, method))
                        return type;
            }
            return null;
        }

        /// <summary>What a base type has that looks like the member, phrased for the report, or null.</summary>
        private static string Nearby(TypeDefinition declaring, string name, InteropIndex index)
        {
            bool first = true;
            foreach (var type in Chain(declaring, index))
            {
                if (first) { first = false; continue; }

                foreach (var method in type.Methods)
                    if (method.Name == name)
                        return $"{type.Name} has {name} under different parameters";

                var hits = NameHeuristics.ForMethod(type, name, null);
                if (hits.Count == 1)
                    return $"{type.Name} has {hits[0].NewName}";
            }
            return null;
        }

        private static void CheckMethod(MethodReference wanted, TypeDefinition declaring, string scope,
                                        string kindPrefix, ModReport report, InteropIndex index,
                                        Dictionary<string, TypeDefinition> repaired)
        {
            bool nameExists = false;
            foreach (var method in declaring.Methods)
            {
                if (method.Name != wanted.Name) continue;
                nameExists = true;
                if (!NameHeuristics.SameParameters(wanted, method)) continue;

                // A CALLER MATCHES ON THE RETURN TYPE TOO, and the same-name-same-parameters test does not.
                // ProductManagerApp still has FavouritesContainer, and it hands back the ProductTypeContainer
                // that moved OUT of it - a different type from the one the mod names, so the call does not
                // resolve and this check called it present. Only ever raised for a type Polyfill itself put
                // back, which is the one case where the two names are known to mean the same thing.
                string returns = wanted.ReturnType?.FullName;
                if (returns != null && repaired.ContainsKey(returns)
                    && !string.Equals(returns, method.ReturnType?.FullName, StringComparison.Ordinal))
                {
                    // AND IT IS REPAIRABLE, which it was not until the stand-in existed. The forward for a
                    // renamed member already knows how to hand its answer back under the old name - it
                    // declares the stand-in as the return type and rebuilds the shell around the same
                    // pointer. Here the NAME did not change at all, only what it hands back, so the same
                    // forward is asked for under the name it already has. Reported alone, this was the one
                    // finding that named a candidate and never used it: Deal Optimizer's street-deal
                    // postfix reads HandoverScreen.PriceSelector and lost its whole method to it.
                    string key = kindPrefix.Length == 0
                        ? Collect(new InteropAugmentor.MemberForward
                        {
                            InAssembly = scope,
                            DeclaringType = declaring.FullName,
                            OldName = wanted.Name,
                            NewName = method.Name,
                            ParameterCount = wanted.Parameters?.Count ?? 0,
                            ParameterTypes = ParameterTypes(wanted),
                            SameNameNewReturn = true,
                            Rule = "return type",
                        })
                        : null;

                    report.Findings.Add(new Finding
                    {
                        Kind = kindPrefix + "member", Scope = scope,
                        Symbol = declaring.FullName + "::" + wanted.Name + Signature(wanted),
                        Reason = "the method is here and hands back "
                               + (method.ReturnType?.Name ?? "something else") + " from where that type "
                               + "moved to, so a call naming the old one does not resolve",
                        Hint = key == null ? "" : "the same method, declared to hand back the name the mod knows",
                        RepairKey = key,
                    });
                }
                return;                                                      // still there, unchanged
            }

            // Inherited counts as present: the runtime finds it, so there is nothing to repair and nothing
            // to report. Asked before any candidate is looked for, because a base that HAS the member makes
            // every candidate on the derived type wrong by definition.
            if (Inherits(declaring, wanted, index) != null) return;

            // THE CANDIDATES MAY BE ONE LEVEL UP, and until this walk they were invisible. A type Polyfill
            // put back is an EMPTY class deriving from the renamed one, so a member reference against it
            // finds nothing on the type itself and every spelling rule came back with nothing to say.
            // Measured: Lithium asks for ATM.ATMInterface.get_isOpen, the game calls it IsOpen on the type
            // the stand-in derives from, and the patch that reads it threw eighteen thousand times in one
            // session - once per frame, because the repair that made the patch register did not also make
            // the member it reads resolvable.
            //
            // The forward is emitted on the type that CARRIES the candidate, not on the one the mod named:
            // the stand-in inherits it, and putting it on the empty class would hide the real member from
            // anything that resolves through the base.
            var host = declaring;
            var hits = NameHeuristics.ForMethod(declaring, wanted.Name, wanted);
            if (hits.Count == 0)
                foreach (var above in Chain(declaring, index))
                {
                    if (above == declaring) continue;
                    var higher = NameHeuristics.ForMethod(above, wanted.Name, wanted);
                    if (higher.Count == 0) continue;
                    hits = higher;
                    host = above;
                    break;
                }

            string hint = hits.Count == 1 ? hits[0].NewName + "  [" + hits[0].Rule + "]" : "";
            int parameters = wanted.Parameters?.Count ?? 0;

            // AUTHORITY RUNS DOWNHILL, and the order below is the whole of it.
            //
            // A bridge is the only source where a person compared the BODIES of both builds. The history
            // compares metadata shapes between two adjacent releases. A name rule compares spelling. So the
            // hand-written answer is asked first, the game's own history second, and English last.
            //
            // It used to be the other way round: the bridge was consulted only when nothing else had spoken
            // AND the spelling had produced no candidates at all. Two members that merely LOOKED alike were
            // therefore enough to silence the one source that actually knew, and the mod stayed broken while
            // the report said a candidate existed.
            string repairKey = null;
            var parameterTypes = ParameterTypes(wanted);
            var authored = kindPrefix.Length == 0
                ? Bridges.Registry.Find(scope, declaring.FullName, wanted.Name, parameters, parameterTypes)
                : null;

            if (authored != null)
            {
                hint = "hand-written rule: " + authored.Because;
                repairKey = Collect(new InteropAugmentor.MemberForward
                {
                    InAssembly = scope,
                    DeclaringType = declaring.FullName,
                    OldName = wanted.Name,
                    NewName = null,
                    ParameterCount = parameters,
                    ParameterTypes = parameterTypes,
                    Rule = "curated",
                });
            }
            else if (kindPrefix.Length == 0)
            {
                // The game's own history: a member that vanished between two adjacent builds and one of the
                // same shape that appeared on the same type is the same member, and those steps are chained
                // from 0.4.4 to here. This is where MAX_HEALTH -> MaxHealth and AssignedNPC_ID -> NPCId come
                // from, neither of which any rule on the installed game could have found.
                //
                // Confirmed against the LIVE type before it is believed: the history says what a build
                // CALLED something, only the installed game says whether it is there.
                string historical = AliasDb.Successor(declaring.FullName, wanted.Name, parameters,
                                                      Report.GameVersion());
                if (historical != null && Has(declaring, historical))
                {
                    hint = historical + "  [version history]";
                    repairKey = Collect(new InteropAugmentor.MemberForward
                    {
                        InAssembly = scope,
                        DeclaringType = declaring.FullName,
                        OldName = wanted.Name,
                        NewName = historical,
                        ParameterCount = parameters,
                        Rule = "version history",
                    });
                }
                else if (hits.Count == 1 && hits[0].Member is MethodDefinition)
                {
                    // One candidate on this type, reached by spelling alone. No inference, but the weakest
                    // of the three: it is a fact about English, not about the game.
                    repairKey = Collect(new InteropAugmentor.MemberForward
                    {
                        InAssembly = scope,
                        DeclaringType = host.FullName,
                        OldName = wanted.Name,
                        NewName = hits[0].NewName,
                        ParameterCount = parameters,
                        Rule = hits[0].Rule,
                    });
                }
            }

            string reason = nameExists
                ? "the method still exists but its parameters changed"
                : hits.Count > 1
                    ? $"method missing; {hits.Count} members could be meant, so none is chosen"
                    : "method missing, with nothing on this type to point at";

            // A candidate one level up is named even though it is not repaired here. The forward would have
            // to call a member of a generic base instantiation - PackagingStationCanvas inherits _canvas
            // from StationInterface<PackagingStationCanvas> - which this does not build. Saying WHERE the
            // value went is still most of the answer for whoever fixes the mod, and it is the difference
            // between "gone" and "on the base class under a different name".
            if (!nameExists && string.IsNullOrEmpty(hint))
            {
                string onBase = Nearby(declaring, wanted.Name, index);
                if (onBase != null) reason += "; the base type " + onBase;
            }

            report.Findings.Add(new Finding
            {
                Kind = kindPrefix + "member", Scope = scope,
                Symbol = declaring.FullName + "::" + wanted.Name + Signature(wanted),
                Reason = reason, Hint = hint, RepairKey = repairKey,
            });
        }

        private static void CheckField(FieldReference wanted, TypeDefinition declaring, string scope,
                                       string kindPrefix, ModReport report, InteropIndex index)
        {
            // Base types included, for the same reason as methods: the runtime resolves a field reference up
            // the hierarchy, so a field the game moved onto a base class was never missing.
            foreach (var type in Chain(declaring, index))
                foreach (var field in type.Fields)
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

        /// <summary>Remember a repair, and hand back the key the report uses to find out what became of it.</summary>
        private static string Collect(InteropAugmentor.MemberForward member)
        {
            _members.Add(member);
            return member.Key;
        }

        /// <summary>The same, for the Harmony pass - a patch target is a reason to repair too.</summary>
        internal static string Request(InteropAugmentor.MemberForward member) => Collect(member);

        /// <summary>A parameter the game renamed, which only the Harmony pass can see.</summary>
        internal static string RequestRename(InteropAugmentor.ParameterRename rename)
        {
            _renames.Add(rename);
            return rename.Key;
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

        /// <summary>The parameter types of a call, by full name, in order.</summary>
        private static string[] ParameterTypes(MethodReference method)
        {
            if (method?.Parameters == null) return Array.Empty<string>();
            var names = new string[method.Parameters.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = method.Parameters[i].ParameterType?.FullName ?? "?";
            return names;
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
