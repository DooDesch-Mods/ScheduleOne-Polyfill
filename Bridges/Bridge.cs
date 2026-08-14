using Mono.Cecil;
using Polyfill.Contract;

namespace Polyfill.Bridges
{
    /// <summary>
    /// One repair a person had to read the game to write.
    /// </summary>
    /// <remarks>
    /// Renames are mechanical: the old name is put back and it calls the new one. These are not. The member
    /// did not move, it was DISSOLVED - the value it held is now computed somewhere else out of two other
    /// members, or the write it performed is now an entry on a stack. Nothing in the metadata says so; it
    /// took reading the decompiled bodies of both versions to know.
    ///
    /// So each one is written out by hand, cites where it came from, and is emitted only when the analysis
    /// actually finds that member missing and a mod actually asks for it. If any piece a bridge needs is not
    /// on this build of the game, it refuses rather than emitting something approximate.
    ///
    /// This is also the honest boundary of the whole project. A bridge is a person deciding that two
    /// different pieces of code mean the same thing. No amount of diffing produces that, and a wrong one is
    /// worse than a missing one, because it runs.
    /// </remarks>
    internal sealed class Bridge
    {
        internal string Assembly;
        internal string DeclaringType;
        internal string OldName;
        internal int ParameterCount;
        internal string Because;
        internal Func<ModuleDefinition, TypeDefinition, MethodDefinition> Emit;

        /// <summary>
        /// The old parameter types, by full name, when the count alone picks the wrong overload.
        /// </summary>
        /// <remarks>
        /// A COUNT IS NOT A SIGNATURE, and treating it as one cost a mod. 0.4.6 added a trailing callback to
        /// all THREE of StorageMenu's Opens, two of which took three arguments in different orders:
        /// <c>Open(IItemSlotOwner, string, string)</c> and <c>Open(string, string, IItemSlotOwner)</c>. One
        /// bridge was written, matched on the name and the count, and answered for both - so a mod calling
        /// the second one got a method with the first one's parameters, which is not the method it asked
        /// for. The repair was reported as applied and the mod stayed broken, which is the worst of the
        /// three possible outcomes.
        ///
        /// Left null where the name carries one shape of that arity, which is nearly always.
        /// </remarks>
        internal string[] ParameterTypes;

        /// <summary>Does this bridge answer for that exact call?</summary>
        internal bool Fits(IReadOnlyList<string> parameterTypes)
        {
            if (ParameterTypes == null) return true;
            if (parameterTypes == null || parameterTypes.Count != ParameterTypes.Length) return false;
            for (int i = 0; i < ParameterTypes.Length; i++)
                if (!string.Equals(ParameterTypes[i], parameterTypes[i], StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>Which step wrote it. Filled in by the set it belongs to; never by hand.</summary>
        internal BridgeSet Set;

        /// <summary>
        /// The name a person types to talk about this repair, derived rather than invented.
        /// </summary>
        /// <remarks>
        /// Derived so that thirty of them cannot drift from what they repair, and so adding one is adding a
        /// line rather than a line plus a name nobody checks. A test asserts they are unique.
        /// </remarks>
        internal string Id
        {
            get
            {
                string type = DeclaringType ?? "";
                int dot = type.LastIndexOfAny(new[] { '.', '/' });
                if (dot >= 0) type = type.Substring(dot + 1);
                string name = (OldName ?? "").Replace("get_", "get-").Replace("set_", "set-");
                string id = (type + "-" + name).ToLowerInvariant();

                // Two bridges for the same name need two names. The first parameter is what tells the
                // StorageMenu Opens apart, and it is also what a person would say out loud.
                if (ParameterTypes is { Length: > 0 })
                {
                    string first = ParameterTypes[0];
                    int mark = first.LastIndexOfAny(new[] { '.', '/' });
                    id += "-" + (mark >= 0 ? first.Substring(mark + 1) : first).ToLowerInvariant();
                }
                return id;
            }
        }

        /// <summary>
        /// The name is still on the type and the OLD signature is what went missing.
        /// </summary>
        /// <remarks>
        /// Normally a name that already exists is left alone, because putting a second member under a name
        /// current mods bind to is the one thing this must never do. An overload is the exception and only
        /// ever by hand: <c>FreeMouse()</c> gained a parameter, so the no-argument form a mod calls is
        /// genuinely absent while the name is not. Nothing existing changes - a compiled call names its full
        /// signature, so it keeps resolving to exactly the method it resolved to before.
        ///
        /// The cost is real and worth naming: <c>AccessTools.Method(type, name)</c> with no parameter list
        /// becomes ambiguous on that one type and name. That is why this is opt-in per bridge rather than
        /// something a heuristic can reach.
        /// </remarks>
        internal bool AllowOverload;

        /// <summary>Was this bridge read against the build that is running?</summary>
        internal bool Verified(GameVersion game)
            => Set == null || Set.VerifiedRange.Allows(game);
    }

    /// <summary>
    /// A type the game renamed to something no rule could match it to.
    /// </summary>
    /// <remarks>
    /// The automatic search matches a missing type against every type with the SAME SIMPLE NAME, which is
    /// how a namespace move is found. It cannot find a rename: <c>MixingStationCanvas</c> and
    /// <c>MixingStationInterface</c> share no name at all, and 0.4.6 renamed four station screens that way
    /// in one pass while factoring their common parts into a base class.
    ///
    /// Naming the pair by hand is the whole repair, and it buys more than a resolvable type. The two
    /// classes have the same members under the same names, so a Harmony patch aimed at
    /// <c>MixingStationCanvas::Open</c> walks the shadow's base chain and lands on the real
    /// <c>MixingStationInterface.Open(MixingStation)</c> - the mod's patch applies to the method the game
    /// actually calls.
    ///
    /// What it does NOT buy is a member the rename dropped on the way. <c>Close(bool)</c> became
    /// <c>Close()</c>, so a patch that names the old parameter list still finds nothing. That is a true
    /// answer rather than a repair, and it is the reason this only ever states the pair and never claims
    /// the members line up.
    /// </remarks>
    internal sealed class TypeRename
    {
        internal string Assembly;
        internal string OldFullName;
        internal string NewFullName;
        internal string Because;

        /// <summary>Which step wrote it. Filled in by the set it belongs to; never by hand.</summary>
        internal BridgeSet Set;
    }

    /// <summary>
    /// Every bridge written for one step of the game, and the gate they share.
    /// </summary>
    /// <remarks>
    /// The unit is the STEP, not the version: 60 of the 79 renames in the game's history fell in one step
    /// and three steps changed nothing at all, so a folder per version would be two thirds empty and the
    /// empty ones are an invitation to file the next bridge in the wrong place. The folder name is the
    /// fact - S0_4_5f2_To_0_4_6f5 says "this is what that update took away".
    ///
    /// THE GATE IS THE PROBE, NOT THE VERSION. A bridge runs when its target is on the installed build and
    /// refuses when it is not, whatever the version says. That is what makes an obsolete bridge disqualify
    /// itself: if 0.4.7 moves NPC.ID again, the bridge written for 0.4.6 stops finding what it needs and
    /// drops out on its own, while the new one in the new folder is the only survivor. No Until field, no
    /// retirement list, nothing to maintain.
    ///
    /// The version window is a CONFIDENCE LABEL. Below From the original member is still there and the
    /// bridge is not needed; above VerifiedTo it still runs and is reported as unverified. Refusing to run
    /// on an unfamiliar build would invent a failure - the thing the whole layer exists to prevent.
    /// </remarks>
    internal abstract class BridgeSet
    {
        /// <summary>Which step this is, for the report and the folder name.</summary>
        internal abstract string Step { get; }

        /// <summary>The first build that NEEDED these - the one the old names went away in.</summary>
        internal abstract string From { get; }

        /// <summary>The newest build they were read against. Never a stop; only a label.</summary>
        internal abstract string VerifiedTo { get; }

        internal abstract IEnumerable<Bridge> Declare();

        /// <summary>The types this step renamed beyond what a name match can follow. Usually none.</summary>
        internal virtual IEnumerable<TypeRename> DeclareRenames() => Array.Empty<TypeRename>();

        private List<Bridge> _bridges;
        private List<TypeRename> _renames;
        private VersionRange _verified;

        internal IReadOnlyList<TypeRename> Renames
        {
            get
            {
                if (_renames != null) return _renames;
                _renames = new List<TypeRename>();
                foreach (var rename in DeclareRenames())
                {
                    if (rename == null) continue;
                    rename.Set = this;
                    _renames.Add(rename);
                }
                return _renames;
            }
        }

        internal IReadOnlyList<Bridge> Bridges
        {
            get
            {
                if (_bridges != null) return _bridges;
                _bridges = new List<Bridge>();
                foreach (var bridge in Declare())
                {
                    if (bridge == null) continue;
                    bridge.Set = this;
                    _bridges.Add(bridge);
                }
                return _bridges;
            }
        }

        internal VersionRange VerifiedRange => _verified ??= VersionRange.Parse(From + ".." + VerifiedTo);
    }
}
