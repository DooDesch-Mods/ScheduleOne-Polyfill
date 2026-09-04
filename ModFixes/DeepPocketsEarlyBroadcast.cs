using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Deep Pockets waits for the game before it goes looking for players to talk to.
    /// </summary>
    /// <remarks>
    /// Deep Pockets overrides <c>OnPreferencesSaved</c>, and MelonLoader raises that on EVERY loaded melon
    /// whenever ANY of them calls <c>MelonPreferences.Save()</c> - once per preference FILE, so a startup
    /// with a handful of mods delivers it many times over. The handler ends in
    /// <c>BroadcastHostConfigLive()</c>, which reads FishNet's <c>InstanceFinder.IsServer</c> and calls
    /// <c>Object.FindObjectsOfType&lt;Player&gt;()</c> at a session that does not exist yet.
    ///
    /// On the main thread that is only wasted work. The danger is that it need not be on the main thread:
    /// MelonLoader dispatches the callback on whatever thread saved, and a mod that saves or reloads from
    /// a worker takes every other mod's handler there with it. Mules 0.2.1 does exactly that, which is
    /// what <see cref="MulesPrefsReloadOnAWorker"/> cuts - this one covers the branch that cut does not:
    /// a save that reaches Deep Pockets without going through Mules at all.
    ///
    /// The empty <c>catch { }</c> around it is why this reaches a player as the game silently
    /// disappearing. A native access violation is not a managed exception and never enters that catch.
    ///
    /// WHAT THE EVIDENCE DOES AND DOES NOT SAY. Every dead run ends on this handler's own line, and its
    /// next message never appears in any of 48 logs - but that message is printed only after a local
    /// player is found, so in the menu the method returns without it either way. The absence places the
    /// death in the handler, not inside this particular call. The measured 0 of 10 with Polyfill and 0 of
    /// 10 without is what rules Polyfill out.
    /// </remarks>
    internal sealed class DeepPocketsEarlyBroadcast : Fix
    {
        /// <summary>Kept for the prefix, which is static and has no logger of its own.</summary>
        private static MelonLogger.Instance _log;

        /// <summary>The call it guards happens while the other mods are still starting.</summary>
        internal override bool Early => true;

        internal override string Id => "deeppockets-early-broadcast";
        internal override string Mod => "Deep Pockets";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "Deep Pockets answers every other mod's settings save by looking for players who are not "
             + "there yet, before the game is loaded.";

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

            var target = AccessTools.Method(config, "BroadcastHostConfigLive");
            if (target == null || target.GetParameters().Length != 0)
            {
                log.Warning("[fix] deeppockets-early-broadcast: DeepPockets.Config has no no-argument "
                          + "BroadcastHostConfigLive on this build, so it was left alone.");
                return false;
            }

            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(DeepPocketsEarlyBroadcast),
                                                                 nameof(NotBeforeTheGameIsUp))));
            }
            catch (Exception e)
            {
                log.Warning("[fix] deeppockets-early-broadcast: could not guard "
                          + "DeepPockets.Config.BroadcastHostConfigLive: " + e.Message);
                return false;
            }

            log.Msg("[fix] deeppockets-early-broadcast: Deep Pockets waits for the game before it looks "
                  + "for players to send its settings to.");
            return true;
        }

        /// <summary>
        /// False skips the original. Reads a flag rather than asking Unity, because this can run on
        /// another mod's thread - which is the whole problem it is here for.
        /// </summary>
        private static bool NotBeforeTheGameIsUp() => MainSceneLatch.Reached;
    }
}
