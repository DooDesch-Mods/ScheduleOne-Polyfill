namespace Polyfill.Core
{
    /// <summary>
    /// What the interop assemblies were generated FROM, as MelonLoader itself records it.
    /// </summary>
    /// <remarks>
    /// The interop assemblies are a cache. MelonLoader builds them out of the player's own GameAssembly.dll
    /// and rebuilds them whenever that file, Unity, or the dumper changes - and it writes down what it built
    /// them from, in Dependencies/Il2CppAssemblyGenerator/Config.cfg:
    /// <code>
    /// GameAssemblyHash = "7C845323BC9889CE..."   (SHA-512 of GameAssembly.dll)
    /// UnityVersion = "2022.3.62"
    /// DumperVersion = "2022.1.0-pre-release.21"
    /// </code>
    /// That hash is the value MelonLoader compares against to decide whether to regenerate, so it is by
    /// construction the answer to "are these assemblies from this game build". Reading it costs eight
    /// kilobytes and saves hashing a hundred megabytes at the most timing-sensitive point in startup.
    ///
    /// Missing or unreadable is not an error. It means every decision that depends on this falls to the
    /// careful branch, which is what a mod that rewrites files on someone's disk should do anyway.
    /// </remarks>
    internal sealed class GeneratorIdentity
    {
        internal string GameAssemblyHash;
        internal string UnityVersion;
        internal string DumperVersion;
        internal string DumperScrsVersion;

        /// <summary>MelonLoader's own version: an upgrade regenerates the assemblies as surely as a game
        /// update does, and the config file does not mention it.</summary>
        internal string Loader;

        /// <summary>Short enough for a log line and a stamp, complete enough that any regeneration
        /// changes it.</summary>
        internal string Digest()
        {
            string hash = GameAssemblyHash ?? "";
            if (hash.Length > 16) hash = hash.Substring(0, 16);
            return $"{hash}/{UnityVersion}/{DumperVersion}/{DumperScrsVersion}/{Loader}";
        }

        internal bool IsKnown => !string.IsNullOrEmpty(GameAssemblyHash);

        /// <summary>
        /// Read it off the installation. Never throws: an identity nobody could read reports itself as
        /// unknown rather than taking the run down.
        /// </summary>
        internal static GeneratorIdentity Read()
        {
            var identity = new GeneratorIdentity { Loader = MelonLoaderVersion() };

            string path = ConfigPath();
            if (path == null || !File.Exists(path)) return identity;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;

                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim().Trim('"');

                    switch (key)
                    {
                        case "GameAssemblyHash": identity.GameAssemblyHash = value; break;
                        case "UnityVersion": identity.UnityVersion = value; break;
                        case "DumperVersion": identity.DumperVersion = value; break;
                        case "DumperSCRSVersion": identity.DumperScrsVersion = value; break;
                    }
                }
            }
            catch { }
            return identity;
        }

        /// <summary>Where MelonLoader keeps it. Deleting this file is what makes it regenerate, which is
        /// what <c>polyfillregen</c> does.</summary>
        internal static string ConfigPath()
        {
            try
            {
                string root = MelonLoader.Utils.MelonEnvironment.MelonLoaderDirectory;
                if (string.IsNullOrEmpty(root)) return null;
                return Path.Combine(root, "Dependencies", "Il2CppAssemblyGenerator", "Config.cfg");
            }
            catch { return null; }
        }

        private static string MelonLoaderVersion()
        {
            try { return typeof(MelonLoader.MelonPlugin).Assembly.GetName().Version?.ToString() ?? "?"; }
            catch { return "?"; }
        }
    }
}
