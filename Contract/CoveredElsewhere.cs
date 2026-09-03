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
    /// intercept a call a mod already compiled. What the mod needed - somebody hearing that the amount
    /// changed - is restored by <c>amount-changed-after-override</c> instead.
    ///
    /// THE MEMBER IS EMITTED ANYWAY, returning null, and this note used to refuse that outright on the
    /// grounds that null "would silence the notification". It does not, and leaving the member absent was
    /// the worse of the two: the only caller guards it - Tweakables reads
    /// <c>if (onPriceChanged != null) onPriceChanged.Invoke()</c> - so null skips a call whose effect the
    /// fix above has already had, while ABSENCE throws MissingMethodException out of a native-to-managed
    /// trampoline three times every time a counteroffer opens, with no stack that names the mod.
    /// Measured on 0.4.6f13, and the bridge is in the 0.4.5f2 step's set.
    ///
    /// The pair is load-bearing: if amount-changed-after-override is ever removed, null stops being an
    /// honest answer and becomes a swallowed notification.
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

            /// <summary>
            /// The member, accessor spelling included: get_X for a field or property. Null for the type
            /// itself, which is what a fix that deletes the only use of a deleted type covers.
            /// </summary>
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

            new Entry
            {
                Type = "Il2CppScheduleOne.UI.Handover.HandoverScreen",
                Member = "get_OriginalItemLocations",
                FixId = "otc-smart-fill-tracking",
                Because = "the dictionary and the nested enum it was keyed by are both gone, and nothing "
                        + "of that shape can be handed back - but it was write-only bookkeeping even in "
                        + "0.4.5f2, so the fix drops the call instead of answering it",
            },

            new Entry
            {
                Type = "Il2CppScheduleOne.UI.Handover.HandoverScreen/EItemSource",
                Member = null,                              // the type itself
                FixId = "otc-smart-fill-tracking",
                Because = "the enum only ever typed HandoverScreen.OriginalItemLocations, and 0.4.6 deleted "
                        + "both - a copy would have its own identity and could not satisfy the signature. "
                        + "The one method that names it is OverTheCounter's TrackItemAsPlayer, and the fix "
                        + "takes the call to it out, so nothing reaches the name",
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

        /// <summary>
        /// The fix that answers for the whole type, or null.
        /// </summary>
        /// <remarks>
        /// A type with no successor cannot be stood in for, and a method that names one will not compile.
        /// A fix that takes the naming line out answers the finding as completely as a stand-in would -
        /// and without this the report kept the mod at "blocked" over a name nothing reaches any more.
        /// </remarks>
        internal static Entry ForType(string type)
        {
            if (type == null) return null;
            foreach (var entry in All)
                if (entry.Member == null && string.Equals(entry.Type, type, StringComparison.Ordinal))
                    return entry;
            return null;
        }
    }
}
