using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// The meet-point protocol hears lobby chat again, from the service that receives it now.
    /// </summary>
    /// <remarks>
    /// HUB - MeetPoints coordinates where a customer meets a client over the Steam lobby's own chat: the
    /// client sends `MEET:pref_update`, `pre_deal`, `deal_accepted` and `deal_removed`, and the host acts
    /// on them. It hears those by patching <c>Lobby.OnLobbyChatMessage</c>.
    ///
    /// 0.4.6 moved that callback to <c>SteamLobbyService.OnLobbyChatMessage</c>, name and signature
    /// unchanged (SteamLobbyService.cs:287, registered at :74). The mod looks for it through ModHub's
    /// GameCompat, which resolves only the exact type names Il2CppScheduleOne.Lobby and
    /// Il2CppScheduleOne.Networking.Lobby and then enumerates their methods - so it never considers the
    /// service, finds nothing, and the whole call sits inside an empty try/catch. Nothing is logged and
    /// nothing binds.
    ///
    /// THAT IS WHY THE INDEX CALLS IT FINE. MeetPoints is the most played mod on polyfill.doomods.com
    /// that is not fully repaired - 52,520 minutes over 748 sessions - and reports `play: works`, because
    /// single-player and a host's own deals never use this path. What is dead is the client-to-host
    /// protocol: a client picks a meeting place, the host never hears it, and the customer goes to the
    /// vanilla one.
    ///
    /// A HARMONY REDIRECT CANNOT REACH IT. GameCompat enumerates <c>Lobby.GetMethods()</c> itself rather
    /// than asking AccessTools, so nothing Polyfill does to the lookup is seen. Putting the old method
    /// back on Lobby would be worse: the mod would patch a method the game never calls, and Polyfill would
    /// report a repair for a feature still switched off.
    ///
    /// So the mod's own postfix is attached to the method the game really calls. It takes only
    /// <c>LobbyChatMsg_t result</c> - no <c>__instance</c> - and the service's parameter carries the same
    /// name and type, so it binds exactly as it would have on Lobby, at the same moment, with the same
    /// message.
    ///
    /// It needs no other scaffolding, and that is worth saying because it needed some until today: the
    /// mod rereads each message with <c>GetLobbyChatEntry(lobby.LobbySteamID, ...)</c>, and Polyfill's
    /// bridge for that answered CSteamID(0) until it was pointed at SteamLobbyService._lobbyID. Without
    /// that fix this one would attach a postfix that runs and reads nothing.
    /// </remarks>
    internal sealed class MeetPointsLobbyChat : Fix
    {
        internal override string Id => "meetpoints-lobby-chat";
        internal override string Mod => "HUB - MeetPoints";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a client's chosen meeting place reaches the host again, over the lobby chat the game "
             + "moved to another type";

        internal override string StandsDownBecause
            => "MeetPoints listens for lobby chat on Lobby.OnLobbyChatMessage, which 0.4.6 moved to "
             + "SteamLobbyService - so the host never hears a client's meeting place and the customer "
             + "goes to the vanilla one.";

        internal override bool Apply(MelonLogger.Instance log)
        {
            var lobby = AccessTools.TypeByName("Il2CppScheduleOne.Networking.Lobby");
            var service = AccessTools.TypeByName("Il2CppScheduleOne.Networking.SteamLobbyService");
            if (service == null)
            {
                log.Warning("[fix] meetpoints-lobby-chat: Il2CppScheduleOne.Networking.SteamLobbyService "
                          + "is not on this build, so there is nothing to listen on.");
                return false;
            }

            // ONLY WHEN THE MOD'S OWN ATTEMPT CANNOT HAVE WORKED. If a build ever puts the callback back
            // on Lobby, the mod finds it by itself and a second postfix here would run its protocol twice
            // for every message.
            if (lobby != null && AccessTools.Method(lobby, "OnLobbyChatMessage") != null) return false;

            var target = Only(service, "OnLobbyChatMessage");
            if (target == null)
            {
                log.Warning("[fix] meetpoints-lobby-chat: SteamLobbyService has no single "
                          + "OnLobbyChatMessage to attach to, so the mod's listener was left alone.");
                return false;
            }

            var patch = AccessTools.Method("HUB.MeetPoints.Network.MeetPointsLobbyChatPatch:Postfix");
            if (patch == null) return false;                  // the mod is not installed

            // Its one argument has to be the one the service hands out, or Harmony binds nothing and
            // takes the class with it.
            var wanted = patch.GetParameters();
            if (wanted.Length != 1 || target.GetParameters().Length != 1
                || wanted[0].ParameterType != target.GetParameters()[0].ParameterType)
            {
                log.Warning("[fix] meetpoints-lobby-chat: the mod's listener takes "
                          + Describe(wanted) + " and the callback hands out "
                          + Describe(target.GetParameters()) + ", so it was left alone.");
                return false;
            }

            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target, postfix: new HarmonyMethod(patch));
            }
            catch (Exception e)
            {
                log.Warning("[fix] meetpoints-lobby-chat: could not attach the mod's listener to "
                          + "SteamLobbyService.OnLobbyChatMessage: " + e.Message);
                return false;
            }

            // The finding names the OLD method, because that is what the mod asked for.
            Fixes.Repaired.Add("HUB.MeetPoints|Il2CppScheduleOne.Networking.Lobby::OnLobbyChatMessage");

            log.Msg("[fix] meetpoints-lobby-chat: MeetPoints listens on "
                  + "SteamLobbyService.OnLobbyChatMessage, where 0.4.6 moved the lobby chat callback.");
            return true;
        }

        /// <summary>The one method of that name, or null when there is none or several.</summary>
        private static MethodInfo Only(Type type, string name)
        {
            MethodInfo found = null;
            foreach (var method in type.GetMethods(AccessTools.all))
            {
                if (method.Name != name || method.DeclaringType != type) continue;
                if (found != null) return null;
                found = method;
            }
            return found;
        }

        private static string Describe(ParameterInfo[] parameters)
        {
            if (parameters.Length == 0) return "nothing";
            var names = new List<string>(parameters.Length);
            foreach (var one in parameters) names.Add(one.ParameterType.Name);
            return string.Join(", ", names);
        }
    }
}
