namespace Polyfill.Contract
{
    /// <summary>
    /// Parameters the game renamed, which breaks a patch without touching a single type or method.
    /// </summary>
    /// <remarks>
    /// HARMONY BINDS A PATCH'S ARGUMENTS BY NAME. So a rename that changes nothing a compiler can see
    /// still kills the patch outright:
    ///
    /// <code>
    /// 0.4.5f2   ShopInterface.SetIsOpen(bool isOpen)
    /// 0.4.6f12  ShopInterface.SetIsOpen(bool open)
    ///
    /// Exception: Parameter "isOpen" not found in method void ShopInterface::SetIsOpen(bool open)
    /// </code>
    ///
    /// The method is there, the signature is identical, and the mod is dead. Nothing in this project's
    /// other layers can see it: there is no missing member to bridge and no missing type to stand in for.
    ///
    /// The repair is to write the old name back into the interop assembly, where a parameter name is
    /// metadata and nothing else - no call site binds to it, and the CLR does not care.
    ///
    /// BOTH NAMES CANNOT BE RIGHT AT ONCE, and that is why this is not applied on sight. A mod built for
    /// 0.4.6 patching the same method would name its argument <c>open</c> and would break in exactly the
    /// way this exists to prevent. So the rename happens only on evidence read out of the installed mods:
    /// something asks for the old name, and nothing asks for the new one. Where both are wanted, the game
    /// is left alone and the report says who wanted what - a conflict is a thing to be told about, not a
    /// coin to flip.
    /// </remarks>
    internal static class RenamedParameters
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string Method;
            internal int ParameterCount;
            internal int Index;
            internal string OldName;
            internal string NewName;
            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.UI.Shop.ShopInterface",
                Method = "SetIsOpen",
                ParameterCount = 1,
                Index = 0,
                OldName = "isOpen",
                NewName = "open",
                Because = "the flag kept its type and its meaning and lost its name in 0.4.6",
            },
        };

        /// <summary>Every entry for this method, or nothing.</summary>
        internal static IEnumerable<Entry> For(string type, string method, int parameterCount)
        {
            foreach (var entry in All)
                if (entry.Method == method && entry.ParameterCount == parameterCount
                    && string.Equals(entry.Type, type, StringComparison.Ordinal))
                    yield return entry;
        }
    }
}
