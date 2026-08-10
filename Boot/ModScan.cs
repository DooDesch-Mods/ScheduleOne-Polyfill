using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// Find the installed mods and inspect them. Nothing more.
    /// </summary>
    /// <remarks>
    /// An earlier version of this took over the Mods pass entirely - excluded the folder from MelonLoader's
    /// scan and loaded every mod itself - which is what a design that rewrites mod DLLs would need. That is
    /// not this design, and taking the pass cost a working game on the first run: SideHustle's ModGate holds
    /// most mods back until a lobby exists, Polyfill runs first on [MelonPriority(int.MinValue)], and so it
    /// silently overrode the player's setting and loaded all twenty-nine at startup. Seven boots in a row
    /// died. With the plugin removed, the first boot came up.
    ///
    /// The repair is not to coordinate with ModGate. It is that Polyfill never needed the pass: it puts
    /// missing names back into the INTEROP assemblies, and a mod binds to those whenever it happens to load.
    /// Reading a mod is a file operation, and injecting is done before any mod loads because
    /// OnPreModsLoaded is early - not because we hold the door.
    ///
    /// So nothing here excludes, loads, registers or defers anything. Whoever owns the pass keeps owning it,
    /// and a mod that loads an hour later still finds the repairs waiting.
    /// </remarks>
    internal static class ModScan
    {
        internal static List<ModCandidate> Candidates { get; private set; } = new();

        internal static void Run(MelonLogger.Instance log)
        {
            var directories = FolderScan.ModDirectories(log);
            if (!FolderScan.Mirrored)
                log.Msg("[scan] mod directories came from the fallback scan, not from MelonLoader.");
            if (directories.Count == 0)
            {
                // Another plugin already excluded the folder. Its mods still load, just not by MelonLoader's
                // own scan, so the files are still there to read - ask the environment instead of the list.
                string root = MelonLoader.Utils.MelonEnvironment.ModsDirectory;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) directories.Add(root);
            }

            Candidates = Preprocessor.Collect(directories, log, out var dropped);
            Preprocessor.ReportDropped(directories, Candidates, dropped, log);

            if (Candidates.Count == 0) { log.Msg("[scan] no mods installed; nothing to do."); return; }

            Core.Triage.Run(Candidates, log);
        }
    }
}
