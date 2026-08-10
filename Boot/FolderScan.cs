using System.Reflection;
using MelonLoader;
using MelonLoader.Melons;
using MelonLoader.Utils;

namespace Polyfill.Boot
{
    /// <summary>
    /// Which directories MelonLoader would have read mods from.
    /// </summary>
    /// <remarks>
    /// This is asked by MIRRORING MelonLoader's own answer rather than recomputing it, and the difference
    /// matters more than it looks. <c>MelonFolderHandler.ScanForFolders</c> does two things a
    /// one-level-with-manifest walk does not:
    ///
    /// A subfolder carrying manifest.json gets its OWN subfolders added with <c>require_manifest: false</c>,
    /// so <c>Mods/Pack/Extra/x.dll</c> loads when <c>Mods/Pack/manifest.json</c> exists. And a folder NAMED
    /// UserLibs, Plugins or Mods switches which list it lands in, so <c>Mods/Pack/Plugins/</c> is a plugin
    /// directory, not a mod one.
    ///
    /// Getting either wrong means a mod that neither loads nor appears in any report - invisible to the
    /// player and to us. Reading the field MelonLoader already filled in cannot be wrong in that way. The
    /// reimplementation below exists only for the day the field is renamed, and it says so out loud when it
    /// is used.
    /// </remarks>
    internal static class FolderScan
    {
        /// <summary>True when the directory list came from MelonLoader itself rather than our fallback.</summary>
        internal static bool Mirrored { get; private set; }

        /// <summary>
        /// The mod directories, in MelonLoader's own order (base directory first).
        /// An EMPTY list from a successful mirror means somebody already excluded the folder.
        /// </summary>
        internal static List<string> ModDirectories(MelonLogger.Instance log)
        {
            var mirrored = TryMirror(log);
            if (mirrored != null) { Mirrored = true; return mirrored; }

            Mirrored = false;
            log.Warning("[scan] MelonLoader's own mod-directory list could not be read; falling back to a "
                      + "reimplementation of ScanForFolders. If a mod fails to load, this is the first suspect.");
            return Rebuild();
        }

        private static List<string> TryMirror(MelonLogger.Instance log)
        {
            try
            {
                var field = typeof(MelonFolderHandler).GetField("_modDirs",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (field?.GetValue(null) is not List<string> dirs) return null;
                // A copy, never the live list: another plugin may exclude the folder later in this same
                // event, and that mutates the original in place.
                return new List<string>(dirs);
            }
            catch (Exception e)
            {
                log.Warning("[scan] could not mirror MelonFolderHandler._modDirs: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Fallback only. A faithful transcription of ScanForFolders' Mods branch, including the recursion
        /// and the name-based scan-type switch. Never the primary path.
        /// </summary>
        private static List<string> Rebuild()
        {
            var dirs = new List<string>();
            string root = MelonEnvironment.ModsDirectory;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return dirs;

            dirs.Add(root);
            Walk(root, requireManifest: true, dirs);
            return dirs;
        }

        private static void Walk(string path, bool requireManifest, List<string> into)
        {
            if (DisableSubFolderLoad()) return;

            string[] children;
            try { children = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly); }
            catch { return; }

            foreach (string child in children)
            {
                if (!Directory.Exists(child)) continue;
                string name = Path.GetFileName(child);
                if (IsNameExcluded(name)) continue;

                // A folder named UserLibs or Plugins belongs to another list; stop descending into it.
                if (name == "UserLibs" || name == "Plugins") continue;

                if (requireManifest && !DisableSubFolderManifest()
                    && !File.Exists(Path.Combine(child, "manifest.json"))) continue;

                into.Add(child);
                // Note the false: children of an accepted folder do NOT need their own manifest.
                Walk(child, requireManifest: false, into);
            }
        }

        /// <summary>MelonLoader's built-in name exclusions, transcribed from MelonFolderHandler.</summary>
        private static bool IsNameExcluded(string name)
            => name.StartsWith("~") || name.StartsWith(".")
            || name == "Broken" || name == "Retired" || name == "Disabled";

        private static bool DisableSubFolderLoad() => LoaderFlag("DisableSubFolderLoad");
        private static bool DisableSubFolderManifest() => LoaderFlag("DisableSubFolderManifest");

        /// <summary>
        /// Read a LoaderConfig flag without a compile-time dependency on its shape. The config type has moved
        /// between MelonLoader versions and a missing flag must mean "default", not a crash before any mod loads.
        /// </summary>
        private static bool LoaderFlag(string name)
        {
            try
            {
                var configType = typeof(MelonFolderHandler).Assembly.GetType("MelonLoader.LoaderConfig");
                object current = configType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
                                            ?.GetValue(null);
                object loader = current?.GetType().GetProperty("Loader")?.GetValue(current);
                object value = loader?.GetType().GetProperty(name)?.GetValue(loader);
                return value is bool b && b;
            }
            catch { return false; }
        }
    }
}
