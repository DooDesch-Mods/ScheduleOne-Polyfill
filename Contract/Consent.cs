namespace Polyfill.Contract
{
    /// <summary>
    /// Where the answer to the sharing question is kept.
    /// </summary>
    /// <remarks>
    /// MelonPreferences, in every build that ships. It is behind an interface because everything under
    /// Contract/ has to compile against the BCL alone - the offline checker and the test suite link
    /// these files and neither has MelonLoader, and a build that reaches for it here fails in CI with
    /// a message about a using directive rather than about the rule it broke.
    ///
    /// Every method is allowed to fail. A store that cannot read is not an error to handle, it is a no.
    /// </remarks>
    internal interface IConsentStore
    {
        bool TryReadBool(string key, out bool value);
        bool TryReadInt(string key, out int value);
        void Write<T>(string key, T value, string description);
        void Flush();
    }

    /// <summary>
    /// Whether the player agreed to share findings, and how often they were asked.
    /// </summary>
    /// <remarks>
    /// One file so the plugin (which asks) and the mod (which would send) read the same answer, and so
    /// there is exactly one place that can say yes.
    ///
    /// THE DEFAULT IS NO, and every failure lands on it. An unreadable preference, a store that was
    /// never installed, an exception: all of them return no rather than assuming. That asymmetry is the
    /// whole point - a wrong no costs a data point, a wrong yes sends something nobody agreed to.
    /// </remarks>
    internal static class Consent
    {
        internal sealed class State
        {
            internal bool Sharing;
            internal bool Answered;
            internal int Asked;
        }

        internal const string SharingKey = "ShareFindings";
        internal const string AnsweredKey = "ShareFindingsAnswered";
        internal const string AskedKey = "ShareFindingsAsked";

        private static IConsentStore _store;

        /// <summary>
        /// Install the store. Called once, early, by whichever assembly loaded first.
        /// </summary>
        /// <remarks>
        /// Without it every read is a no and every write is dropped, which is the correct behaviour for
        /// a build that forgot to wire it up: nothing is sent, and nothing pretends to have been asked.
        /// </remarks>
        internal static void Use(IConsentStore store) => _store = store;

        /// <summary>May findings be sent right now? The only question the sender is allowed to ask.</summary>
        internal static bool Sharing => Read().Sharing;

        internal static State Read()
        {
            var state = new State();
            var store = _store;
            if (store == null) return state;

            try
            {
                if (store.TryReadBool(SharingKey, out bool sharing)) state.Sharing = sharing;
                if (store.TryReadBool(AnsweredKey, out bool answered)) state.Answered = answered;
                if (store.TryReadInt(AskedKey, out int asked)) state.Asked = asked;
            }
            catch { }                                    // unreadable is the same as not agreed
            return state;
        }

        internal static void Write(bool sharing, bool answered)
        {
            var store = _store;
            if (store == null) return;

            try
            {
                store.Write(SharingKey, sharing,
                      "Send anonymous findings - which mod, which symbol, repaired or not. Never your "
                    + "name, your paths or your save.");
                store.Write(AnsweredKey, answered,
                      "Whether the question has been answered. Clear this to be asked again.");
                store.Flush();
            }
            catch { }
        }

        /// <summary>One more launch on which the question was put, whether or not it was answered.</summary>
        internal static void CountOneAsk(State state)
        {
            var store = _store;
            if (store == null) return;

            try
            {
                store.Write(AskedKey, state.Asked + 1,
                      "How many launches have asked. After three, the question stops.");
                store.Flush();
            }
            catch { }
        }
    }
}
