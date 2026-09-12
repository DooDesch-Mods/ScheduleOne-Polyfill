using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A fix that took the game down with it does not get a second turn.
    /// </summary>
    /// <remarks>
    /// NOT EVERY FAILURE IS AN EXCEPTION. Harmony pins the method it is about to patch, which makes the
    /// JIT compile it, and when that compile dies it dies as
    /// `Fatal error. Internal CLR error. (0x80131506)` - the process is gone mid-line, no catch anywhere
    /// runs, and the log stops in the middle of a sentence. A player sees the game close on startup and
    /// the last name in the log is Polyfill's.
    ///
    /// One did, on a ninety-mod install on 0.4.6f13, patching OverTheCounter's Smart Fill. It could not
    /// be reproduced here: the same mod, the same version, the same missing dependency and even the same
    /// wrong-branch DLL all patch cleanly on this machine, and every other mod's Harmony patches applied
    /// on theirs. Whatever the trigger is, it lives in that install.
    ///
    /// So this stops guessing and remembers instead. The id of the fix about to run is written to disk
    /// before it runs and removed after. A file still sitting there on the next start means the game did
    /// not survive that fix, and it is skipped - named in the log and in the report, so the player knows
    /// which mod to take up with whom and gets a game that starts in the meantime.
    ///
    /// ONE RUN, NOT FOR EVER. The note is cleared as soon as it has been acted on, so a fix blamed for
    /// one crash is tried again next time. A genuine repeat writes the note again and is skipped again;
    /// a one-off - a driver, a disk, the flaky boot MelonLoader has anyway - costs a single run.
    /// </remarks>
    internal static class CrashGuard
    {
        private const string FileName = "applying";

        private static string _blamed;
        private static bool _read;

        /// <summary>The fix that was mid-flight when the game last died, or null.</summary>
        internal static string Blamed(MelonLogger.Instance log)
        {
            if (_read) return _blamed;
            _read = true;

            try
            {
                string path = Path();
                if (!File.Exists(path)) return null;

                _blamed = File.ReadAllText(path).Trim();
                File.Delete(path);

                if (_blamed.Length == 0) { _blamed = null; return null; }

                log.Warning($"[fix] the game did not survive {_blamed} last time, so it is skipped this "
                          + "run. If this keeps happening, that repair and the mod it is for do not agree "
                          + "with something on this install - `polyfill` in the console names it, and "
                          + $"DisabledFixes can switch it off for good: {_blamed}");
            }
            catch (Exception e)
            {
                // Said rather than swallowed: a guard that cannot read its own note is a guard that is not
                // guarding, and the next crash would look like the first one again.
                log.Warning("[fix] could not read whether the last run survived, so nothing was skipped: "
                          + e.Message);
                _blamed = null;
            }

            return _blamed;
        }

        /// <summary>Note that this fix is running, so a death during it has a name.</summary>
        internal static void Entering(string id, MelonLogger.Instance log)
        {
            try
            {
                string path = Path();
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, id);
            }
            catch (Exception e)
            {
                log.Warning($"[fix] could not note that {id} is running, so a crash inside it would not "
                          + "be attributed: " + e.Message);
            }
        }

        /// <summary>It came back. Nothing to blame.</summary>
        internal static void Left(MelonLogger.Instance log)
        {
            try
            {
                string path = Path();
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                log.Warning("[fix] could not clear the running note, so the next start may skip a fix "
                          + "that did nothing wrong: " + e.Message);
            }
        }

        private static string Path()
            => System.IO.Path.Combine(
                Contract.PolyfillPaths.Folder(MelonEnvironment.UserDataDirectory), FileName);
    }
}
