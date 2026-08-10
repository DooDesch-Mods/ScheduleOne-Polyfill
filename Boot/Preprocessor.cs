using MelonLoader;
using Mono.Cecil;

namespace Polyfill.Boot
{
    /// <summary>One mod file that MelonLoader would have loaded.</summary>
    internal sealed class ModCandidate
    {
        internal string Path;
        internal string AssemblyName;
        internal Version AssemblyVersion;
        internal string MelonName;
        internal string MelonVersion;
        internal string MelonAuthor;

        /// <summary>Without it MelonLoader does not treat the file as a melon at all.</summary>
        internal bool HasMelonInfo;

        internal string Display => MelonName ?? AssemblyName ?? System.IO.Path.GetFileName(Path);
    }

    /// <summary>
    /// A transcription of <c>MelonPreprocessor.PreprocessFolder</c>, kept deliberately literal.
    /// </summary>
    /// <remarks>
    /// Once the Mods folder is excluded from the scan, this list IS the set of mods that will run. Anything
    /// missing here does not load and does not appear anywhere - not in the log, not in the report, not in
    /// the mod manager's idea of what is installed. That is the single worst failure this project can have,
    /// and it has nothing to do with polyfilling.
    ///
    /// So the rules are copied one for one, in order, rather than approximated:
    ///   1. *.dll, top directory only, per directory in the mod-directory list
    ///   2. managed only - the extension is ".dll" and AssemblyName.GetAssemblyName does not throw
    ///   3. must carry [MelonInfo]; without it MelonLoader does not consider the file a melon at all
    ///   4. deduplicate by ASSEMBLY name, keeping the highest assembly VERSION, across all directories
    ///
    /// Rule 4 is the surprising one: two copies of the same mod in different folders are not both loaded,
    /// and the winner is decided by assembly version, not by folder order or file date.
    /// </remarks>
    internal static class Preprocessor
    {
        /// <summary>The mods that will run, in the order their assembly names were first seen.</summary>
        internal static List<ModCandidate> Collect(List<string> directories, MelonLogger.Instance log,
                                                   out List<string> dropped)
        {
            var byAssembly = new Dictionary<string, ModCandidate>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            dropped = new List<string>();

            foreach (string dir in directories)
            {
                string[] files;
                try { files = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly); }
                catch (Exception e) { log.Warning($"[pass] could not read {dir}: {e.Message}"); continue; }

                foreach (string file in files)
                {
                    if (!IsManagedDll(file)) { dropped.Add(file + " | not a managed assembly"); continue; }

                    var candidate = Read(file);
                    if (candidate == null) { dropped.Add(file + " | unreadable by Cecil"); continue; }
                    if (!candidate.HasMelonInfo) { dropped.Add(file + " | no [MelonInfo]"); continue; }

                    string key = candidate.AssemblyName ?? Path.GetFileNameWithoutExtension(file);
                    if (byAssembly.TryGetValue(key, out var existing)
                        && existing.AssemblyVersion >= candidate.AssemblyVersion)
                    {
                        dropped.Add(file + $" | superseded by {existing.Path} (version {existing.AssemblyVersion})");
                        continue;
                    }

                    if (existing != null) dropped.Add(existing.Path + $" | superseded by {file} (version {candidate.AssemblyVersion})");
                    else order.Add(key);
                    byAssembly[key] = candidate;
                }
            }

            var result = new List<ModCandidate>(order.Count);
            foreach (string key in order) result.Add(byAssembly[key]);
            return result;
        }

        /// <summary>
        /// The self-check. Every .dll under the mod directories that did NOT become a candidate, with the
        /// reason. A mod missing from both lists means the transcription above drifted from MelonLoader.
        /// </summary>
        internal static void ReportDropped(List<string> directories, List<ModCandidate> kept,
                                           List<string> dropped, MelonLogger.Instance log)
        {
            var keptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in kept) keptPaths.Add(c.Path);

            var unaccounted = new List<string>();
            var explained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in dropped)
            {
                int bar = line.IndexOf(" | ", StringComparison.Ordinal);
                explained.Add(bar > 0 ? line.Substring(0, bar) : line);
            }

            foreach (string dir in directories)
            {
                string[] files;
                try { files = Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly); }
                catch { continue; }
                foreach (string file in files)
                    if (!keptPaths.Contains(file) && !explained.Contains(file))
                        unaccounted.Add(file);
            }

            if (unaccounted.Count == 0) return;
            log.Error($"[pass] {unaccounted.Count} file(s) under Mods are neither loaded nor explained. "
                    + "This is a bug in Polyfill's folder rules, not in the mods:");
            foreach (string file in unaccounted) log.Error("[pass]   " + file);
        }

        /// <summary>
        /// MelonUtils.IsManagedDLL, transcribed: the extension is ".dll" and the file has a readable
        /// assembly name. Transcribed rather than called so a future signature change here is a compile
        /// error instead of a silent behaviour change.
        /// </summary>
        private static bool IsManagedDll(string path)
        {
            string extension = Path.GetExtension(path);
            if (extension == null || !extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)) return false;
            try { System.Reflection.AssemblyName.GetAssemblyName(path); return true; }
            catch { return false; }
        }

        private static ModCandidate Read(string file)
        {
            try
            {
                // InMemory: never hold a lock on the player's file. MelonLoader is about to open it too.
                using var def = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { InMemory = true });
                var candidate = new ModCandidate
                {
                    Path = file,
                    AssemblyName = def.Name?.Name,
                    AssemblyVersion = def.Name?.Version ?? new Version(0, 0, 0, 0),
                };

                foreach (var attribute in def.CustomAttributes)
                {
                    if (attribute.AttributeType?.FullName != "MelonLoader.MelonInfoAttribute") continue;
                    candidate.HasMelonInfo = true;
                    var args = attribute.ConstructorArguments;
                    if (args.Count > 1) candidate.MelonName = args[1].Value as string;
                    if (args.Count > 2) candidate.MelonVersion = args[2].Value as string;
                    if (args.Count > 3) candidate.MelonAuthor = args[3].Value as string;
                }
                return candidate;
            }
            catch { return null; }
        }
    }
}
