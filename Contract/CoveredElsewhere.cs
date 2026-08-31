namespace Polyfill.Contract
{
    /// <summary>
    /// Members that cannot be put back, whose PURPOSE a named fix restores anyway.
    /// </summary>
    /// <remarks>
    /// A finding says a member is gone. Sometimes that is the whole story and the mod is broken.
    /// Sometimes the member is genuinely unrestorable and what the mod used it FOR has been given back
    /// another way - and then the report says "broken" about something that works, which is the mirror
    /// image of the failure this project keeps guarding against.
    ///
    /// <c>AmountSelector.onPriceChanged</c> is the case. 0.4.5f2 had a UnityEvent field, so the interop
    /// assembly exposed <c>get_onPriceChanged()</c>; 0.4.6 has an event raised inside SetAmount. Handing
    /// a UnityEvent back is not possible here - it would have to be the same object every time it is
    /// read on a selector, and interop wrappers are pooled by weak reference, so a managed field on one
    /// is not on the next; and UnityEvent.Invoke is not virtual on this build, so a derived event cannot
    /// intercept a call a mod already compiled. Returning null would silence the notification, which is
    /// refused outright. What the mod needed - somebody hearing that the amount changed - is restored by
    /// <c>amount-changed-after-override</c> instead.
    ///
    /// A HINT, NOT AN OUTCOME, and the distinction is the point. This says a fix for it EXISTS; it does
    /// not say the fix ran. The plugin writes the report before the game exists and cannot know whether
    /// a mod-side fix stood down on this machine, so claiming a repair here would be a guess dressed as
    /// a fact. The finding stays visible and explains itself, and the fix reports its own fate through
    /// Fixes.Record like every other one.
    /// </remarks>
    internal static class CoveredElsewhere
    {
        internal sealed class Entry
        {
            /// <summary>The type as the mod spells it, interop-side.</summary>
            internal string Type;

            /// <summary>The member, accessor spelling included: get_X for a field or property.</summary>
            internal string Member;

            /// <summary>The fix that restores what the member was for.</summary>
            internal string FixId;

            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.UI.AmountSelector",
                Member = "get_onPriceChanged",
                FixId = "amount-changed-after-override",
                Because = "the UnityEvent this returned cannot be handed back - interop wrappers are "
                        + "pooled by weak reference, so it could not be the same object twice - but the "
                        + "change it announced is raised again when a mod replaces the setter that used "
                        + "to raise it",
            },
        };

        /// <summary>The fix covering this member, or null when nothing does.</summary>
        internal static Entry For(string type, string member)
        {
            if (type == null || member == null) return null;
            foreach (var entry in All)
                if (string.Equals(entry.Type, type, StringComparison.Ordinal)
                    && string.Equals(entry.Member, member, StringComparison.Ordinal))
                    return entry;
            return null;
        }
    }
}
