using System.Reflection;

namespace Polyfill.Contract
{
    /// <summary>
    /// Names with two methods on this build, and the one a patch that names no parameters means.
    /// </summary>
    /// <remarks>
    /// <c>[HarmonyPatch(typeof(X), "Name")]</c> is resolved by a lookup that throws the moment two methods
    /// share the name, and the throw comes out of <c>PatchAll</c>: the mod loses that patch class and every
    /// one after it. A name gets its second method in two ways, and neither is the mod's doing.
    ///
    /// The game adds an overload beside the method the patch was written for. 0.4.6 had one
    /// <c>Contract.GetProductListMatch</c>; 0.4.7 put a second in front of it that converts its list and
    /// calls the first.
    ///
    /// Or Polyfill does, by putting an old signature back beside the method the game kept under the same
    /// name. <see cref="GrownOverloads"/> covers that where the old parameter list is a prefix of the new
    /// one; here the game's method is the shorter of the two.
    ///
    /// In both cases the patch means one particular method, and this list says which, by its parameters.
    /// Only the named list: two methods of one name are usually two methods the game has always had, and a
    /// mod that named neither never bound.
    ///
    /// Parameters are spelled the short way <see cref="Spell"/> writes them, because the same line is read
    /// against reflection at runtime and against metadata by the report.
    /// </remarks>
    internal static class NameOnlyTargets
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string Name;
            internal string[] Parameters;
            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.Quests.Contract",
                Name = "GetProductListMatch",
                Parameters = new[] { "List<ItemInstance>", "Int32&" },
                Because = "0.4.6 had one GetProductListMatch(List<ItemInstance>, out int) (Contract.cs:313 on "
                        + "0.4.6f13); 0.4.7 added GetProductListMatch(List<ProductItemInstance>, out int), which "
                        + "copies its list and calls the first (Contract.cs:312-322 on 0.4.7f6)",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.AvatarFramework.Avatar",
                Name = "EnableRagdoll",
                Parameters = new string[0],
                Because = "0.4.7's EnableRagdoll() takes no force; the two-argument form beside it is the one "
                        + "Polyfill puts back for callers",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.Combat.CombatBehaviour",
                Name = "SetMovementSpeed",
                Parameters = new[] { "Single" },
                Because = "0.4.7's SetMovementSpeed(normalizedSpeed) is the game's; the three-argument form "
                        + "beside it is the one Polyfill puts back for callers",
            },
        };

        /// <summary>The entry for this name on this type, or null.</summary>
        internal static Entry For(string type, string name)
        {
            if (type == null || name == null) return null;
            foreach (var entry in All)
                if (entry.Name == name && string.Equals(entry.Type, type, StringComparison.Ordinal))
                    return entry;
            return null;
        }

        /// <summary>The method the entry means, declared on this type, or null when the build has none.</summary>
        internal static MethodInfo Find(Type type, Entry entry)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static;

            foreach (var method in type.GetMethods(all))
            {
                if (method.Name != entry.Name || method.DeclaringType != type) continue;

                var actual = method.GetParameters();
                if (actual.Length != entry.Parameters.Length) continue;

                bool same = true;
                for (int i = 0; i < actual.Length && same; i++)
                    same = Spell(actual[i].ParameterType) == entry.Parameters[i];
                if (same) return method;
            }
            return null;
        }

        /// <summary>
        /// A type by its own name without namespace, its generic arguments in angle brackets and a trailing
        /// ampersand for by-reference: <c>List&lt;ItemInstance&gt;</c>, <c>Int32&amp;</c>.
        /// </summary>
        internal static string Spell(Type type)
        {
            if (type.IsByRef) return Spell(type.GetElementType()) + "&";
            if (!type.IsGenericType) return type.Name;

            var arguments = type.GetGenericArguments();
            var spelled = new string[arguments.Length];
            for (int i = 0; i < arguments.Length; i++) spelled[i] = Spell(arguments[i]);
            return Bare(type.Name) + "<" + string.Join(",", spelled) + ">";
        }

        /// <summary>A generic type's name without its arity suffix.</summary>
        internal static string Bare(string name)
        {
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }
    }
}
