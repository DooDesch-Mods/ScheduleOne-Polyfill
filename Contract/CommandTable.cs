namespace Polyfill.Contract
{
    /// <summary>
    /// The console commands, in one place.
    /// </summary>
    /// <remarks>
    /// They were declared three times: a table for the help text and the autocomplete shim, a chain of
    /// string comparisons deciding whether a typed word belongs to Polyfill, and a switch dispatching it.
    /// Adding a command meant editing three lists, and forgetting the second one produced a command that
    /// exists, is documented, and silently does nothing because the game got the word instead.
    ///
    /// Now the table decides all three. Data only - the handlers live with the code that runs them, and a
    /// test asserts the two key sets are equal, which is the part a compiler cannot check. Data only, which
    /// is also what keeps this file free of everything the plugin may not name.
    /// </remarks>
    internal static class CommandTable
    {
        internal sealed class Command
        {
            internal string Name;
            internal string Help;
            internal string Example;
        }

        internal static readonly Command[] All =
        {
            C("polyfill",        "what Polyfill found in your mods at startup", "polyfill"),
            C("polyfilllist",    "every mod, with its verdict", "polyfilllist"),
            C("polyfillshow",    "everything one mod asks for that is missing", "polyfillshow hitman"),
            C("polyfillunfixed", "only what cannot be pointed at anything", "polyfillunfixed hitman"),
            C("polyfillexport",  "write one file with everything, ready to send", "polyfillexport"),
            C("polyfillprobe",   "can the runtime resolve this type by name?",
                                 "polyfillprobe Il2CppScheduleOne.Weather.WeatherConditions"),
            C("polyfillprefab",  "does the game still have this prefab, and what is near it",
                                 "polyfillprefab Basic Metal Glass Door"),
            C("polyfillfixes",   "the per-mod fixes, and switch one off", "polyfillfixes off s1mapi-prefabs"),
            C("polyfillrestore", "undo every repair, restart to take effect", "polyfillrestore"),
            C("polyfillregen",   "have MelonLoader rebuild the game's generated assemblies", "polyfillregen"),
            C("polyfillhelp",    "list the polyfill commands", "polyfillhelp"),
        };

        /// <summary>Is this word ours? Anything else belongs to the game and must be handed straight back.</summary>
        internal static bool Owns(string command)
        {
            if (string.IsNullOrEmpty(command)) return false;
            foreach (var one in All)
                if (string.Equals(one.Name, command, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Command C(string name, string help, string example)
            => new() { Name = name, Help = help, Example = example };
    }
}
