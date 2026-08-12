using System.Text.RegularExpressions;
using Mono.Cecil;

namespace Polyfill.Core
{
    /// <summary>A member on the live game that a mod's old name most likely meant.</summary>
    internal sealed class Candidate
    {
        internal string Rule;        // which rule found it, named in the report so a wrong hit is traceable
        internal string NewName;
        internal IMemberDefinition Member;
        internal bool KindChanged;   // the mod wanted a field and this is a property, or the reverse

        internal Candidate(string rule, IMemberDefinition member, bool kindChanged = false)
        {
            Rule = rule;
            Member = member;
            NewName = member.Name;
            KindChanged = kindChanged;
        }
    }

    /// <summary>
    /// The renames that can be read off the names themselves, with no version history at all.
    /// </summary>
    /// <remarks>
    /// Every rule here has the same shape: take the name the mod asks for, generate the small set of names
    /// it could have become, and keep the answer only if EXACTLY ONE of them exists on the live type with a
    /// matching signature. One match is a fact about this machine. Two is an ambiguity, and an ambiguity is
    /// reported, never resolved - a wrong member is worse than a missing one, because a missing one throws
    /// where it happens and a wrong one writes into the player's save.
    ///
    /// These cover the patterns that actually dominate real updates, measured on the 0.4.5f2 to 0.4.6f11
    /// diff: casing flips, underscore prefixes, FishNet accessor doubling, and RPC hashes that change with
    /// the signature they encode. None of them needs an archive, so they work across any version gap,
    /// including one we have no data for at all.
    /// </remarks>
    internal static class NameHeuristics
    {
        /// <summary>RpcLogic___Foo_123456 - the trailing number is a hash of the signature, so it moves
        /// whenever the signature does while the name in front of it stays put.</summary>
        private static readonly Regex RpcHash =
            new(@"^(?<prefix>Rpc(?:Logic|Writer|Reader)___)(?<base>.+)_(?<hash>\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// Every plausible successor to <paramref name="wanted"/> on <paramref name="type"/>.
        /// A null <paramref name="signature"/> means the caller has no parameter list to match against -
        /// a Harmony attribute target, where only the name is written down - and the check is skipped.
        /// </summary>
        internal static List<Candidate> ForMethod(TypeDefinition type, string wanted, MethodReference signature)
        {
            var hits = new List<Candidate>();
            if (type == null) return hits;

            var rpc = RpcHash.Match(wanted);
            if (rpc.Success)
            {
                string prefix = rpc.Groups["prefix"].Value, stem = rpc.Groups["base"].Value;
                foreach (var method in type.Methods)
                {
                    var other = RpcHash.Match(method.Name);
                    if (!other.Success) continue;
                    if (other.Groups["prefix"].Value != prefix || other.Groups["base"].Value != stem) continue;
                    if (signature != null && !SameParameters(signature, method)) continue;
                    hits.Add(new Candidate("rpc-hash", method));
                }
                if (hits.Count > 0) return hits;
            }

            foreach (var method in type.Methods)
            {
                if (method.Name == wanted) continue;                 // it resolved elsewhere or not at all
                if (!IsNameVariant(wanted, method.Name, out string rule)) continue;
                if (signature != null && !SameParameters(signature, method)) continue;
                hits.Add(new Candidate(rule, method));
            }
            return hits;
        }

        /// <summary>
        /// Successors to a field, including the case where it stopped being a field.
        /// </summary>
        /// <remarks>
        /// A field turning into a property is 39 members in a single update. Reading it back is a real
        /// repair; writing through it is too. Taking its ADDRESS is not - a property has none - so that case
        /// is reported rather than answered, and the caller decides.
        /// </remarks>
        internal static List<Candidate> ForField(TypeDefinition type, string wanted)
        {
            var hits = new List<Candidate>();
            if (type == null) return hits;

            foreach (var field in type.Fields)
            {
                if (field.Name == wanted) continue;
                if (IsNameVariant(wanted, field.Name, out string rule)) hits.Add(new Candidate(rule, field));
            }

            foreach (var property in type.Properties)
            {
                if (property.Name == wanted) { hits.Add(new Candidate("field-to-property", property, true)); continue; }
                if (IsNameVariant(wanted, property.Name, out string rule))
                    hits.Add(new Candidate(rule + "+field-to-property", property, true));
            }
            return hits;
        }

        /// <summary>
        /// Could <paramref name="have"/> be what <paramref name="wanted"/> was renamed to, judged on the
        /// names alone? Each branch is a pattern observed in a real Schedule I update.
        /// </summary>
        internal static bool IsNameVariant(string wanted, string have, out string rule)
        {
            rule = null;
            if (wanted == null || have == null || wanted == have) return false;

            // minsUntilDeaddropReady -> MinsUntilDeaddropReady, MarketValueLabel -> marketValueLabel
            if (string.Equals(wanted, have, StringComparison.OrdinalIgnoreCase)) { rule = "casing"; return true; }

            // runtimePitchMultiplier -> _runtimePitchMultiplier, and back
            if (Trim(wanted, '_') == Trim(have, '_') && Trim(wanted, '_').Length > 0) { rule = "underscore"; return true; }

            // Health -> <Health>k__BackingField, and back
            if (BackingFieldOf(have) == wanted || BackingFieldOf(wanted) == have) { rule = "backing-field"; return true; }

            // FishNet weaver output: SyncAccessor_debt -> SyncAccessor__debt, syncVar___debt -> syncVar____debt
            if (StripWeaverUnderscores(wanted) == StripWeaverUnderscores(have)) { rule = "syncvar-accessor"; return true; }

            // CustomerSlots -> _customerSlots. A member that went from public to private takes the underscore
            // and the lower case in one move, and neither rule above sees that once an accessor prefix sits in
            // front of it: get_CustomerSlots and get__customerSlots differ in the MIDDLE, so trimming the ends
            // finds nothing and the case comparison is thrown by the underscore.
            if (string.Equals(Normalize(wanted), Normalize(have), StringComparison.OrdinalIgnoreCase)
                && Normalize(wanted).Length > 0) { rule = "underscore+casing"; return true; }

            return false;
        }

        private static string Trim(string s, char c) => s.Trim(c);

        /// <summary>Every difference an underscore can make, taken out at once: runs collapsed, ends trimmed.</summary>
        private static string Normalize(string name) => Trim(StripWeaverUnderscores(name), '_');

        private static string BackingFieldOf(string name)
        {
            if (name == null || !name.StartsWith("<", StringComparison.Ordinal)) return null;
            int end = name.IndexOf(">k__BackingField", StringComparison.Ordinal);
            return end > 1 ? name.Substring(1, end - 1) : null;
        }

        /// <summary>Collapse any run of underscores to one, so the weaver's doubling is not a difference.</summary>
        private static string StripWeaverUnderscores(string name)
        {
            if (name == null) return null;
            var builder = new System.Text.StringBuilder(name.Length);
            bool previousWasUnderscore = false;
            foreach (char c in name)
            {
                if (c == '_') { if (!previousWasUnderscore) builder.Append(c); previousWasUnderscore = true; }
                else { builder.Append(c); previousWasUnderscore = false; }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Same parameter list, compared structurally.
        /// </summary>
        /// <remarks>
        /// Deliberately stricter than the arity check the workspace's own VerifyMods settles for. Arity is
        /// enough to DETECT that something broke; it is not enough to CHOOSE a member, because on an
        /// overloaded method arity picks one at random and a wrong call is silent.
        ///
        /// Comparing full names is what it cannot be. A reference to <c>List&lt;T&gt;.Add</c> spells its
        /// parameter <c>!0</c> - a positional placeholder - while the definition spells the same thing
        /// <c>T</c>. On names alone every generic call in every mod reads as a signature change: the first
        /// run of this reported 225 of them and not one was real.
        /// </remarks>
        internal static bool SameParameters(MethodReference wanted, MethodDefinition have)
        {
            if (wanted == null || have == null) return false;
            int count = wanted.Parameters?.Count ?? 0;
            if (count != (have.Parameters?.Count ?? 0)) return false;
            if (wanted.GenericParameters.Count != have.GenericParameters.Count) return false;
            for (int i = 0; i < count; i++)
                if (!SameType(wanted.Parameters[i].ParameterType, have.Parameters[i].ParameterType))
                    return false;
            return true;
        }

        /// <summary>Structural type equality, with generic parameters matched by position.</summary>
        private static bool SameType(TypeReference a, TypeReference b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;

            // Custom modifiers carry no identity for this purpose; look through them.
            if (a is IModifierType modifiedA) return SameType(modifiedA.ElementType, b);
            if (b is IModifierType modifiedB) return SameType(a, modifiedB.ElementType);

            // !0 (type parameter) and !!0 (method parameter) against T: same slot, same thing.
            if (a is GenericParameter parameterA)
                return b is GenericParameter parameterB
                    && parameterA.Position == parameterB.Position
                    && parameterA.Type == parameterB.Type;
            if (b is GenericParameter) return false;

            if (a is ArrayType arrayA)
                return b is ArrayType arrayB && arrayA.Rank == arrayB.Rank
                    && SameType(arrayA.ElementType, arrayB.ElementType);

            if (a is ByReferenceType refA)
                return b is ByReferenceType refB && SameType(refA.ElementType, refB.ElementType);

            if (a is PointerType pointerA)
                return b is PointerType pointerB && SameType(pointerA.ElementType, pointerB.ElementType);

            if (a is GenericInstanceType instanceA)
            {
                if (b is not GenericInstanceType instanceB) return false;
                if (!SameType(instanceA.ElementType, instanceB.ElementType)) return false;
                if (instanceA.GenericArguments.Count != instanceB.GenericArguments.Count) return false;
                for (int i = 0; i < instanceA.GenericArguments.Count; i++)
                    if (!SameType(instanceA.GenericArguments[i], instanceB.GenericArguments[i])) return false;
                return true;
            }
            if (b is GenericInstanceType) return false;

            return a.FullName == b.FullName;
        }
    }
}
