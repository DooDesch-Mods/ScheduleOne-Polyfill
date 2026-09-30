namespace Polyfill.Contract
{
    /// <summary>
    /// Methods a later build fanned out into one method per case, where the old name was a patch target.
    /// </summary>
    /// <remarks>
    /// 0.4.7 replaced <c>HandoverScreen.Open(contract, customer, mode, callback, ...)</c>, which every
    /// handover went through, with one method per mode: <c>Open_Contract</c>, <c>Open_Sample</c>,
    /// <c>Open_Offer</c> and the new <c>Open_SpecialCustomer</c> (HandoverScreen.cs on 0.4.7f6). Each opens
    /// the same screen; they differ in what they are handed.
    ///
    /// Unlike <see cref="ReplacedMethods"/> there is no stand-in to call. The old arguments are not constants
    /// - the contract, the customer and the callback are the call's own - so calling a stand-in would hand
    /// every patch nulls. Instead a patch is attached to each successor on which Harmony can bind every one
    /// of its parameters as they are: <c>__instance</c>, or a name the successor still takes. A patch that
    /// only needs the screen, which is what "after the handover screen opened" patches usually are, fits all
    /// of them. One that reads the old arguments fits only where they survive, or nowhere, and is reported.
    /// </remarks>
    internal static class FannedOutMethods
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string OldName;

            /// <summary>The methods that took its place, by name; every overload of each is a candidate.</summary>
            internal string[] NowCalled;

            /// <summary>Where this was read, so the next person can check it rather than trust it.</summary>
            internal string Because;
        }

        /// <summary>The entry for this type and old name, or null.</summary>
        internal static Entry For(string type, string oldName)
        {
            foreach (var entry in All)
                if (entry.Type == type && entry.OldName == oldName) return entry;
            return null;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.UI.Handover.HandoverScreen",
                OldName = "Open",
                NowCalled = new[] { "Open_Contract", "Open_Sample", "Open_Offer", "Open_SpecialCustomer" },
                Because = "0.4.6f13 opened every handover through HandoverScreen.Open(contract, customer, mode, "
                        + "callback, successChanceMethod); 0.4.7f6 has one Open_ method per mode, each opening "
                        + "the same screen",
            },
        };
    }
}
