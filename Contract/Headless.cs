using System;

namespace Polyfill.Contract
{
    /// <summary>
    /// Is anybody in front of this game?
    /// </summary>
    /// <remarks>
    /// TWO THINGS CHANGE WHEN NOBODY IS: the consent question has no one to answer it, and the session
    /// stops being a player's session. Both used to assume a person. Asking anyway held three separate
    /// startups for the full 60-second deadline on a hosted box, and a server's uptime went into the
    /// index as though somebody had played 55 mods for a day and hit nothing.
    ///
    /// FROM THE COMMAND LINE, not from Unity. The earliest caller runs in the plugin's first hook,
    /// before the interop assemblies are usable, so Application.isBatchMode is not reachable yet - and
    /// these are the switches the server launcher passes, so they say the same thing.
    ///
    /// Any one of them is enough. A dedicated server passes all three; a headless test rig might pass
    /// one. Both dash spellings, because Unity accepts either and a server script may use either.
    /// </remarks>
    internal static class Headless
    {
        private static bool _asked;
        private static bool _answer;
        private static string _why;

        /// <summary>The answer, worked out once. <paramref name="why"/> is null when it is no.</summary>
        internal static bool Yes(out string why)
        {
            if (!_asked)
            {
                _asked = true;
                _answer = Look(out _why);
            }
            why = _why;
            return _answer;
        }

        internal static bool Yes() => Yes(out _);

        private static bool Look(out string why)
        {
            why = null;
            try
            {
                foreach (string argument in Environment.GetCommandLineArgs())
                {
                    switch (argument.TrimStart('-').ToLowerInvariant())
                    {
                        case "batchmode": why = "the game is running in batch mode"; return true;
                        case "nographics": why = "the game is running without graphics"; return true;
                        case "dedicated-server":
                        case "dedicatedserver": why = "this is a dedicated server"; return true;
                    }
                }
            }
            catch (Exception)
            {
                // Reading the command line must never be the reason a player is not asked, or the
                // reason a session is not counted. Saying no leaves both exactly as they were.
            }
            return false;
        }
    }
}
