using System.Reflection;

namespace Polyfill.Contract
{
    /// <summary>
    /// The installed game's build number, read the same way on both sides.
    /// </summary>
    /// <remarks>
    /// There used to be two readers: the plugin asked MelonLoader by reflection, the companion mod asked
    /// <c>UnityEngine.Application.version</c>. Two sources for one value that nothing compared, in a product
    /// whose whole job is deciding what applies to which build.
    ///
    /// MelonLoader's is the one that survives both places. The plugin runs before the support module is set
    /// up and reaching into Unity there would load interop metadata that must stay untouched; MelonLoader has
    /// already read the version out of the build by then and prints it at startup. By reflection, because the
    /// property has moved between MelonLoader versions and an unknown version is a caption, not a failure.
    ///
    /// The mod still has Unity and checks the two against each other once, so a disagreement is a line in the
    /// log rather than a mystery.
    /// </remarks>
    internal static class GameVersionSource
    {
        private static string _raw;

        /// <summary>The raw string, whatever it is. Empty-ish becomes "unknown" so a report never has a hole.</summary>
        internal static string Raw => _raw ??= Read();

        internal static GameVersion Current => GameVersion.Parse(Raw);

        private static string Read()
        {
            string[] candidates =
            {
                "MelonLoader.InternalUtils.UnityInformationHandler, MelonLoader",
                "MelonLoader.MelonUtils, MelonLoader",
            };
            foreach (string typeName in candidates)
            {
                try
                {
                    var type = Type.GetType(typeName, false);
                    var property = type?.GetProperty("GameVersion",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                    if (property?.GetValue(null) is string version && version.Length > 0) return version;
                }
                catch { }
            }
            return "unknown";
        }

        /// <summary>
        /// Compare this reading against another source's. Returns null when they agree.
        /// </summary>
        /// <remarks>
        /// Compared by PARTS, not by text: two readings that spell the same build differently are the same
        /// build, and only a real difference is worth a warning.
        /// </remarks>
        internal static string Disagreement(string other)
        {
            if (string.IsNullOrEmpty(other)) return null;
            if (GameVersion.Parse(other) == Current) return null;
            return $"MelonLoader says the game is '{Raw}' and Unity says '{other}'. Polyfill went with "
                 + "MelonLoader's, which is the one every version decision was made against.";
        }
    }
}
