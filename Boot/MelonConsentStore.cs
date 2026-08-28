using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Boot
{
    /// <summary>MelonPreferences, behind the interface the consent flags speak.</summary>
    /// <remarks>
    /// The whole of the MelonLoader dependency the sharing flags used to carry, in one file, and the
    /// reason it is here rather than beside them: everything under <c>Contract/</c> is linked by the
    /// offline checker and by the test suite, neither of which has MelonLoader. A single `using` in
    /// that folder fails CI with a message about a missing directive rather than about the rule it
    /// broke. The mod links this one file the same way it links Core/GeneratorIdentity.cs.
    ///
    /// Nothing here throws. A preference file that will not open is a no, not a crash on somebody's
    /// first launch after an update.
    /// </remarks>
    internal sealed class MelonConsentStore : IConsentStore
    {
        private const string Category = "Polyfill";

        /// <summary>Install it, once, as early as the assembly runs anything at all.</summary>
        internal static void Install() => Consent.Use(new MelonConsentStore());

        public bool TryReadBool(string key, out bool value) => Read(key, false, out value);

        public bool TryReadInt(string key, out int value) => Read(key, 0, out value);

        /// <summary>
        /// Read one preference, registering it first so a saved value is actually applied.
        /// </summary>
        /// <remarks>
        /// THE ENTRY HAS TO BE CREATED, not just looked up. MelonPreferences loads the file before
        /// plugins run, but it holds a saved value aside until something registers an entry for it -
        /// so <c>GetEntry</c> on its own returns null on the very launch where the answer is in the
        /// file, and the reader concludes nobody has answered.
        ///
        /// That is not hypothetical. 0.10.0 asked at OnPreInitialization and only ever looked up, so
        /// the answer never took: the dialog came back on every single launch, and no button could
        /// stop it. Plugin.ReadPreferences had the pattern right the whole time, one file away.
        /// </remarks>
        private static bool Read<T>(string key, T fallback, out T value)
        {
            value = fallback;
            try
            {
                var category = MelonPreferences.GetCategory(Category)
                               ?? MelonPreferences.CreateCategory(Category);
                if (category == null) return false;

                var entry = category.GetEntry<T>(key) ?? category.CreateEntry(key, fallback, key, Describe(key));
                value = entry.Value;
                return true;
            }
            catch { return false; }
        }

        /// <summary>The description a key gets when this is the first thing to register it.</summary>
        private static string Describe(string key)
        {
            if (key == Consent.SharingKey)
                return "Send anonymous findings - which mod, which symbol, repaired or not. Never your "
                     + "name, your paths or your save.";
            if (key == Consent.AnsweredKey)
                return "Whether the question has been answered. Clear this to be asked again.";
            if (key == Consent.AskedKey)
                return "How many launches have asked. After three, the question stops.";
            return key;
        }

        public void Write<T>(string key, T value, string description)
        {
            try
            {
                var category = MelonPreferences.GetCategory(Category)
                               ?? MelonPreferences.CreateCategory(Category);
                if (category == null) return;

                var entry = category.GetEntry<T>(key);
                if (entry == null) category.CreateEntry(key, value, key, description);
                else entry.Value = value;
            }
            catch { }
        }

        public void Flush()
        {
            try { MelonPreferences.Save(); }
            catch { }
        }
    }
}
