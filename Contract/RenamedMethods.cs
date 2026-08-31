namespace Polyfill.Contract
{
    /// <summary>
    /// Methods 0.4.6 renamed, so a patch aimed at the old name binds to the new one.
    /// </summary>
    /// <remarks>
    /// NOT THE SAME REPAIR AS A BRIDGE, and the difference is what a mod does with the name. A bridge
    /// writes the old method back so a CALL works; that is right for a call and wrong for a patch,
    /// because the game never calls the name Polyfill wrote - so a patch on it binds and never fires.
    ///
    /// Writing a hook and relaying from the replacement fixes the firing but not the meaning. Tweakables'
    /// prefix on <c>PackagingStation.Open</c> returns false to suppress the vanilla open; nothing that
    /// runs after the real method can suppress it. A prefix has to be a prefix ON THE REAL METHOD.
    ///
    /// So this table does not write anything. It redirects Harmony's own lookup: when a patch asks a type
    /// for a name the build no longer has, and the rename is on record here, the lookup answers with the
    /// method that took over. The patch then binds to what the game actually calls, and a prefix
    /// returning false suppresses it exactly as it always did.
    ///
    /// One rename per line, and each one carries where it was read. A guess here is a patch pointed at
    /// the wrong method, which is worse than a patch that does not bind: the mod stays silent and the
    /// behaviour changes.
    /// </remarks>
    internal static class RenamedMethods
    {
        internal sealed class Entry
        {
            /// <summary>The type as the MOD spells it - a stand-in name where the type was renamed too.</summary>
            internal string Type;
            internal string OldName;
            internal string NewName;
            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.ObjectScripts.PackagingStation",
                OldName = "Open",
                NewName = "Use",
                Because = "0.4.5f2 PackagingStation.Open() set the camera up and opened the canvas; "
                        + "0.4.6f13 Use() pushes a state and opens the canvas, on the same type "
                        + "(PackagingStation.cs:405)",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.UI.Handover.HandoverScreenPriceSelector",
                OldName = "SetPrice",
                NewName = "SetAmount",
                Because = "the price control became the game's general amount box in 0.4.6, and SetPrice"
                        + "(float) became SetAmount(float) on it (AmountSelector.cs:61)",
            },
        };

        /// <summary>What this name became on the type, or null when nothing is on record.</summary>
        internal static string Successor(string type, string oldName)
        {
            if (type == null || oldName == null) return null;
            foreach (var entry in All)
                if (string.Equals(entry.Type, type, StringComparison.Ordinal)
                    && string.Equals(entry.OldName, oldName, StringComparison.Ordinal))
                    return entry.NewName;
            return null;
        }

        /// <summary>The rule behind a redirect, for the report.</summary>
        internal static string Because(string type, string oldName)
        {
            if (type == null || oldName == null) return null;
            foreach (var entry in All)
                if (string.Equals(entry.Type, type, StringComparison.Ordinal)
                    && string.Equals(entry.OldName, oldName, StringComparison.Ordinal))
                    return entry.Because;
            return null;
        }
    }
}
