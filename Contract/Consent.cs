using MelonLoader;

namespace Polyfill.Contract
{
    /// <summary>
    /// Whether the player agreed to share findings, and how often they were asked.
    /// </summary>
    /// <remarks>
    /// One file so the plugin (which asks) and the mod (which would send) read the same answer, and so
    /// there is exactly one place that can say yes.
    ///
    /// THE DEFAULT IS NO, and every failure lands on it. An unreadable preference, a category that will
    /// not open, an exception: all of them return no rather than assuming. That asymmetry is the whole
    /// point - a wrong no costs a data point, a wrong yes sends something nobody agreed to.
    /// </remarks>
    internal static class Consent
    {
        internal sealed class State
        {
            internal bool Sharing;
            internal bool Answered;
            internal int Asked;
        }

        private const string Category = "Polyfill";
        private const string SharingKey = "ShareFindings";
        private const string AnsweredKey = "ShareFindingsAnswered";
        private const string AskedKey = "ShareFindingsAsked";

        /// <summary>May findings be sent right now? The only question the sender is allowed to ask.</summary>
        internal static bool Sharing => Read().Sharing;

        internal static State Read()
        {
            var state = new State();
            try
            {
                var category = MelonPreferences.GetCategory(Category);
                if (category == null) return state;

                state.Sharing = category.GetEntry<bool>(SharingKey)?.Value ?? false;
                state.Answered = category.GetEntry<bool>(AnsweredKey)?.Value ?? false;
                state.Asked = category.GetEntry<int>(AskedKey)?.Value ?? 0;
            }
            catch { }                                    // unreadable is the same as not agreed
            return state;
        }

        internal static void Write(bool sharing, bool answered)
        {
            try
            {
                var category = Ensure();
                if (category == null) return;

                Entry(category, SharingKey, sharing,
                      "Send anonymous findings - which mod, which symbol, repaired or not. Never your "
                    + "name, your paths or your save.");
                Entry(category, AnsweredKey, answered,
                      "Whether the question has been answered. Clear this to be asked again.");
                MelonPreferences.Save();
            }
            catch { }
        }

        /// <summary>One more launch on which the question was put, whether or not it was answered.</summary>
        internal static void CountOneAsk(State state)
        {
            try
            {
                var category = Ensure();
                if (category == null) return;

                Entry(category, AskedKey, state.Asked + 1,
                      "How many launches have asked. After three, the question stops.");
                MelonPreferences.Save();
            }
            catch { }
        }

        private static MelonPreferences_Category Ensure()
        {
            try { return MelonPreferences.GetCategory(Category) ?? MelonPreferences.CreateCategory(Category); }
            catch { return null; }
        }

        private static void Entry<T>(MelonPreferences_Category category, string key, T value, string description)
        {
            var entry = category.GetEntry<T>(key);
            if (entry == null) category.CreateEntry(key, value, key, description);
            else entry.Value = value;
        }
    }
}
