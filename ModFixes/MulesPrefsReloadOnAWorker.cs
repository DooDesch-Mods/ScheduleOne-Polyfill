using System;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Mules stops re-reading the settings file on a worker thread while the game is still starting.
    /// </summary>
    /// <remarks>
    /// Mules watches <c>MelonPreferences.cfg</c> and reloads it whenever it changes. All three watcher
    /// callbacks - Changed, Created, Renamed - go through <c>SchedulePrefsReload</c>, which does the
    /// reload in <c>Task.Run</c> after a 250 ms debounce (Mules 0.2.1, decompiled: the watcher at :913,
    /// the handlers at :920, :924 and :928, the task at :946 and <c>MelonPreferences.Load()</c> at :951).
    ///
    /// MelonLoader dispatches preference callbacks synchronously on whatever thread called it. So that
    /// reload runs <c>OnPreferencesLoaded</c> for EVERY installed mod on a worker, and any of them that
    /// touches Unity or FishNet there is calling the engine from a thread that may not. Deep Pockets does
    /// exactly that, and the game dies with a native access violation - no managed exception, no log line,
    /// because the call sits inside an empty <c>catch { }</c> that a native fault never reaches.
    ///
    /// It is also a loop, which is what makes it frequent rather than rare. Deep Pockets writes the
    /// settings file from inside the callback that runs when the file is LOADED
    /// (<c>OnPreferencesLoaded</c> to <c>LoadFromJsonFile</c> to <c>SyncPreferencesFromCurrent</c> to
    /// <c>ModCategory.SaveToFile(false)</c>), so a reload becomes a write and the write becomes another
    /// reload. Measured, that ran hundreds of turns per launch while the menu was still loading.
    ///
    /// MEASURED on 0.4.6f13 in a clean copy, the seven-mod set a player reported: 0 of 10 launches
    /// survived with Polyfill installed and 0 of 10 without - which is what rules Polyfill out - and the
    /// failure tracked how many mods save preferences during startup rather than which ones: Deep Pockets
    /// with three others booted 2/2, with four 0/2, with five 0/2.
    ///
    /// THIS IS THE CUT, and the reason it is here rather than on either of Deep Pockets' two entry points:
    /// blocking the schedule stops the task, so it stops the reload, so no mod's preference handler runs
    /// on a worker at all - whatever a mod does in one and however many mods are installed. Guarding Deep
    /// Pockets instead was tried twice and closed one door each time (0 of 5 and 5 of 6).
    ///
    /// THE TWO GUARDS ARE NOT INTERCHANGEABLE, and switching one off alone is worth knowing about.
    /// Measured on the same 27-mod install by disabling each through <c>DisabledFixes</c>: with both off
    /// the game died 3 of 3 with 0xC0000005. With ONLY this one off - so Deep Pockets was still kept away
    /// from Unity - the game reached the menu both times, and the loop ran wide open behind it: 41 turns
    /// in one launch and 2,386 in the other, against 19 with both guards on. That is a machine rewriting
    /// its settings file hundreds of times a second while it looks like nothing is wrong.
    ///
    /// The version range is closed on purpose. The claim above is only true while all three watcher
    /// callbacks still route through this one method.
    /// </remarks>
    internal sealed class MulesPrefsReloadOnAWorker : Fix
    {
        private static MelonLogger.Instance _log;

        /// <summary>The watcher is armed in the mod's own startup, so the guard has to be there first.</summary>
        internal override bool Early => true;

        internal override string Id => "mules-prefs-reload-on-a-worker";
        internal override string Mod => "Mules";
        internal override string ModVersions => "0.2.1";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "Mules re-reads the settings file on a background thread the moment anything writes it, "
             + "and during startup that runs other mods' settings code off the main thread, which takes "
             + "the game down with no error at all.";

        internal override string StandsDownBecause
            => "It guards one exact version of Mules, because the guard is only complete while every one "
             + "of its file-watcher callbacks still goes through the same method.";

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var plugin = AccessTools.TypeByName("Mules.Plugin");
            if (plugin == null)
            {
                // Said rather than silent: an installed-but-unloaded mod looks exactly like an absent one,
                // and a gamemode gate can hold a mod back while every file browser still shows the DLL.
                log.Msg("[fix] mules-prefs-reload-on-a-worker: Mules is not loaded, so there is nothing "
                      + "to guard.");
                return false;
            }

            var target = AccessTools.Method(plugin, "SchedulePrefsReload", Type.EmptyTypes);
            if (target == null)
            {
                log.Warning("[fix] mules-prefs-reload-on-a-worker: Mules.Plugin has no no-argument "
                          + "SchedulePrefsReload on this build, so it was left alone.");
                return false;
            }

            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(MulesPrefsReloadOnAWorker),
                                                                 nameof(NotBeforeTheGameIsUp))));
            }
            catch (Exception e)
            {
                log.Warning("[fix] mules-prefs-reload-on-a-worker: could not guard "
                          + "Mules.Plugin.SchedulePrefsReload: " + e.Message);
                return false;
            }

            log.Msg("[fix] mules-prefs-reload-on-a-worker: Mules waits for the game before it starts "
                  + "re-reading the settings file, which took the game down during startup.");
            return true;
        }

        /// <summary>
        /// False skips the original. Reads a flag rather than asking Unity, because this runs on the
        /// watcher's thread.
        /// </summary>
        private static bool NotBeforeTheGameIsUp() => MainSceneLatch.Reached;
    }
}
