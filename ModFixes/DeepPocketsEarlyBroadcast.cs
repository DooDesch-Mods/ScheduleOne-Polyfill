using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Deep Pockets and Mules stop rewriting the settings file at each other while the game starts.
    /// </summary>
    /// <remarks>
    /// Two mods hand each other the same settings file until the game dies, and neither is doing anything
    /// obviously wrong on its own.
    ///
    /// Deep Pockets writes <c>MelonPreferences.cfg</c> from inside the callback that runs when that file is
    /// LOADED: <c>OnPreferencesLoaded</c> calls <c>LoadFromJsonFile</c>, which ends in
    /// <c>SyncPreferencesFromCurrent</c>, which ends in <c>ModCategory.SaveToFile(false)</c>. Mules
    /// watches the same file and live-reloads it whenever it changes. So a single write becomes a reload,
    /// the reload becomes a write, and the pair rewrites the file hundreds of times a second while the
    /// menu is still loading. The other half of it comes in through <c>OnPreferencesSaved</c>, which
    /// MelonLoader raises on EVERY melon whenever ANY of them saves.
    ///
    /// It ends in a native access violation, and the mod holds an empty <c>catch { }</c> around the call
    /// that dies, so nothing is written and the game is simply gone.
    ///
    /// MEASURED on 0.4.6f13 in a clean copy, the seven-mod set a player reported: 0 of 10 launches
    /// survived with Polyfill and 0 of 10 without, same exit code, same last line - which is what rules
    /// Polyfill out. Below that it tracked the CALL COUNT and not any particular second mod: Deep Pockets
    /// alone booted 2/2, with two others 2/2, with three 2/2, with four 0/2, with five 0/2. That is why
    /// the reporter's own bisection contradicted itself.
    ///
    /// TWO EARLIER TARGETS WERE WRONG AND BOTH ARE WORTH KEEPING HERE, because each one moved the crash
    /// rather than removing it. Guarding this as an ordinary fix did nothing at all: those run on the
    /// first frame, and the first guarded call lands 1.7 seconds before it. Guarding
    /// <c>BroadcastHostConfigLive</c> - the line the process actually dies on - booted 3 of 5 and made the
    /// loop VISIBLE: about 1,680 turns per launch instead of twelve. Guarding <c>OnPreferencesSaved</c>
    /// booted 0 of 5, because it closes only one of the two doors into the loop.
    ///
    /// So the guard sits on the write-back, which is the edge both doors lead to. The first call still
    /// happens, so the settings window still shows the mod's own values; the repeats during startup do
    /// not, and in the game nothing is guarded at all.
    /// </remarks>
    internal sealed class DeepPocketsEarlyBroadcast : Fix
    {
        /// <summary>The scene the game runs in. Anything else is the menu or a load screen.</summary>
        private const string GameScene = "Main";

        /// <summary>Kept for the prefix, which is static and has no logger of its own.</summary>
        private static MelonLogger.Instance _log;

        /// <summary>
        /// It has to be installed while the other mods are still starting. Measured: the first guarded
        /// call lands 1.7 seconds before the first frame, and with enough mods the game is gone by then.
        /// </summary>
        internal override bool Early => true;

        internal override string Id => "deeppockets-early-broadcast";
        internal override string Mod => "Deep Pockets";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "Deep Pockets answers every other mod's settings save by syncing and broadcasting to "
             + "players who are not there yet, over and over, and with enough mods installed that takes "
             + "the game down during startup.";

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var config = AccessTools.TypeByName("DeepPockets.Config");
            if (config == null)
            {
                // Said rather than silent: an installed-but-unloaded mod looks exactly like an absent one,
                // and a gamemode gate can hold a mod back while every file browser still shows the DLL.
                log.Msg("[fix] deeppockets-early-broadcast: Deep Pockets is not loaded, so there is "
                      + "nothing to guard.");
                return false;
            }

            var target = AccessTools.Method(config, "SyncPreferencesFromCurrent");
            if (target == null || target.GetParameters().Length != 0)
            {
                log.Warning("[fix] deeppockets-early-broadcast: DeepPockets.Config has no no-argument "
                          + "SyncPreferencesFromCurrent on this build, so it was left alone.");
                return false;
            }

            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(DeepPocketsEarlyBroadcast),
                                                                 nameof(OnceUntilTheGameIsUp))));
            }
            catch (Exception e)
            {
                log.Warning("[fix] deeppockets-early-broadcast: could not guard "
                          + "DeepPockets.Config.SyncPreferencesFromCurrent: " + e.Message);
                return false;
            }

            log.Msg("[fix] deeppockets-early-broadcast: Deep Pockets writes the settings file once during "
                  + "startup instead of once per reload, which is what took the game down.");
            return true;
        }

        /// <summary>How many times the write-back has been allowed since the game scene was last away.</summary>
        private static int _writtenDuringStartup;

        /// <summary>
        /// False skips the original. The first call does the real work; the rest are the loop.
        /// </summary>
        /// <remarks>
        /// The first write-back is what puts the mod's JSON values in front of the player in the settings
        /// window, so refusing it outright would change what they see. Every later one during startup is
        /// the same values written again because somebody re-read the file - which is the edge that has to
        /// be cut. In the game the count is reset and the mod behaves exactly as it always did.
        /// </remarks>
        private static bool OnceUntilTheGameIsUp()
        {
            bool inGame;
            try
            {
                inGame = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == GameScene;
            }
            catch (Exception e)
            {
                // A prefix that throws takes the original with it, so this answers rather than propagates -
                // and it answers "let it run", because refusing on a scene we could not read would switch
                // the mod's own feature off for everyone.
                _log?.Warning("[fix] deeppockets-early-broadcast: could not read the active scene ("
                            + e.Message + "), so the write-back was allowed through.");
                return true;
            }

            if (inGame) { _writtenDuringStartup = 0; return true; }
            if (_writtenDuringStartup == 0) { _writtenDuringStartup = 1; return true; }
            return false;
        }
    }
}
