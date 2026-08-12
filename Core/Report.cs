using MelonLoader.Utils;
using Polyfill.Contract;

namespace Polyfill.Core
{
    /// <summary>
    /// Where the run's findings go, and what the companion mod reads back.
    /// </summary>
    /// <remarks>
    /// The shape of the file and the reading of it live in <see cref="RunReport"/>, compiled into both
    /// halves from one source. What is left here is where it goes on this machine, which is the one part
    /// the plugin can answer and a test cannot.
    /// </remarks>
    internal static class Report
    {
        internal static string Directory => PolyfillPaths.Folder(MelonEnvironment.UserDataDirectory);

        internal static string LastRunPath => PolyfillPaths.LastRun(MelonEnvironment.UserDataDirectory);

        /// <summary>The installed build, as MelonLoader read it out of the game.</summary>
        internal static string GameVersion() => GameVersionSource.Raw;

        internal static void Write(List<ModReport> reports, string interopDirectory, int assemblyCount,
                                   List<string> dropped)
        {
            var report = new RunReport
            {
                Generated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Game = GameVersion(),
                Interop = interopDirectory ?? "?",
                AssemblyCount = assemblyCount,
            };
            report.Mods.AddRange(reports);
            if (dropped != null) report.Dropped.AddRange(dropped);

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(LastRunPath, report.Text());
            }
            catch (Exception e)
            {
                Boot.Plugin.Log?.Warning("[report] could not be written: " + e.Message);
            }
        }
    }
}
