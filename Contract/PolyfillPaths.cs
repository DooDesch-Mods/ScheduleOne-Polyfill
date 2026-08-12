namespace Polyfill.Contract
{
    /// <summary>
    /// Every name the two halves of Polyfill have to agree on, written down once.
    /// </summary>
    /// <remarks>
    /// The plugin and the companion mod deliberately share no types: the plugin runs before the game exists
    /// and the mod cannot. What they do share is a handful of file names, and those were spelled out
    /// separately on both sides - <c>".polyfill-orig"</c> in two files, the restore marker in two more, the
    /// report name in a fifth. Nothing enforced that they matched, and a rename on one side would have been
    /// found by a player, not by a compiler.
    ///
    /// This file is linked into both projects, which keeps the no-shared-types rule (they each compile their
    /// own copy of an internal class) while making one spelling the only spelling.
    ///
    /// It takes UserData as an argument rather than asking MelonLoader for it, so it names no type outside
    /// the base library and can be compiled into a test that has no game.
    /// </remarks>
    internal static class PolyfillPaths
    {
        /// <summary>What an untouched assembly is called once Polyfill has kept a copy of it.</summary>
        internal const string BackupSuffix = ".polyfill-orig";

        /// <summary>The half-written image, never left lying next to the real one.</summary>
        internal const string TempSuffix = ".polyfill-tmp";

        /// <summary>Polyfill's own folder under UserData.</summary>
        internal const string FolderName = "Polyfill";

        internal const string LastRunFile = "last-run.txt";
        internal const string ReportFile = "polyfill-report.txt";
        internal const string StampFileName = "interop.stamp";
        internal const string RestorePendingFile = "restore-pending";

        internal static string Folder(string userDataDirectory)
            => Path.Combine(userDataDirectory ?? ".", FolderName);

        internal static string LastRun(string userDataDirectory)
            => Path.Combine(Folder(userDataDirectory), LastRunFile);

        internal static string Report(string userDataDirectory)
            => Path.Combine(Folder(userDataDirectory), ReportFile);

        internal static string Stamp(string userDataDirectory)
            => Path.Combine(Folder(userDataDirectory), StampFileName);

        internal static string RestorePending(string userDataDirectory)
            => Path.Combine(Folder(userDataDirectory), RestorePendingFile);

        /// <summary>The kept copy beside an assembly.</summary>
        internal static string Backup(string assemblyPath) => assemblyPath + BackupSuffix;
    }
}
