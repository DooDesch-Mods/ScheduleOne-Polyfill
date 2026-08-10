using System.Reflection;
using MelonLoader;
using MelonLoader.Utils;

namespace Polyfill.Boot
{
    /// <summary>
    /// The two startup facts the whole design rests on, written down every run instead of assumed once.
    /// </summary>
    /// <remarks>
    /// M1 - are the interop assemblies still UNLOADED at OnPreModsLoaded? Injecting into a file that the
    /// runtime has already mapped changes nothing this session. If this ever reports them loaded, the
    /// injection has to move or the player needs a restart, and that is not something to discover from a
    /// bug report.
    ///
    /// M2 asked whether AddFullPathExclusion empties the mod-directory list. It does - measured, once. The
    /// question is now moot and the check is gone with it: Polyfill does not exclude anything, because it
    /// never needed to own the Mods pass to put names back into the interop assemblies. What remains is a
    /// note of WHERE the mod list came from, since a fallback list is the first suspect if a mod is ever
    /// missing from a report.
    /// </remarks>
    internal static class Diagnostics
    {
        /// <summary>Interop assemblies whose absence at this point is what makes injection possible.</summary>
        private static readonly string[] Watched =
        {
            "Assembly-CSharp",
            "Assembly-CSharp-firstpass",
            "Il2CppScheduleOne.Core",
            "Il2CppFishNet.Runtime",
            "Il2Cppmscorlib",
        };

        /// <summary>M1, taken at the top of OnPreModsLoaded, before anything else runs.</summary>
        internal static void RecordInteropLoadState(MelonLogger.Instance log)
        {
            var loaded = new List<string>();
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name;
                    try { name = asm.GetName().Name; } catch { continue; }
                    foreach (string watched in Watched)
                        if (string.Equals(name, watched, StringComparison.OrdinalIgnoreCase))
                            loaded.Add(name);
                }
            }
            catch (Exception e) { log.Warning("[m1] could not enumerate loaded assemblies: " + e.Message); return; }

            string dir = Core.InteropIndex.LocateDirectory();
            int files = 0;
            try { if (dir != null && Directory.Exists(dir)) files = Directory.GetFiles(dir, "*.dll").Length; }
            catch { }

            if (loaded.Count == 0)
            {
                log.Msg($"[m1] no interop assembly is loaded yet - the window is open ({files} in {dir}).");
                return;
            }
            log.Warning($"[m1] {loaded.Count} interop assembly/assemblies are ALREADY loaded at this point: "
                      + string.Join(", ", loaded) + ". Changes to those files take effect on the next launch, "
                      + "not this one.");
        }
    }
}
