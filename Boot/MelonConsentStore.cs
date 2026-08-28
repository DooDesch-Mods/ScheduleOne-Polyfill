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

        public bool TryReadBool(string key, out bool value)
        {
            value = false;
            try
            {
                var entry = MelonPreferences.GetCategory(Category)?.GetEntry<bool>(key);
                if (entry == null) return false;
                value = entry.Value;
                return true;
            }
            catch { return false; }
        }

        public bool TryReadInt(string key, out int value)
        {
            value = 0;
            try
            {
                var entry = MelonPreferences.GetCategory(Category)?.GetEntry<int>(key);
                if (entry == null) return false;
                value = entry.Value;
                return true;
            }
            catch { return false; }
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
