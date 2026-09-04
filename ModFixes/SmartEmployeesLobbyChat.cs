using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Smart Employees hears lobby chat again, from the service that receives it now.
    /// </summary>
    /// <remarks>
    /// HUB - Smart Employees syncs its employee assignments between players over the Steam lobby's own
    /// chat, and hears them by patching <c>Lobby.OnLobbyChatMessage</c>. 0.4.6 moved that callback to
    /// <c>SteamLobbyService.OnLobbyChatMessage</c>, name and signature unchanged (SteamLobbyService.cs:287,
    /// registered at :74), so the lookup finds nothing.
    ///
    /// UNLIKE ITS SIBLING, THIS MOD SAYS SO: it logs "Lobby chat method not found - multiplayer sync
    /// disabled" and carries on, which is how it was found. HUB - MeetPoints made the same call inside an
    /// empty try/catch and reported nothing at all; see <see cref="MeetPointsLobbyChat"/>, which this
    /// mirrors down to the parameter check.
    ///
    /// The mod's own postfix is attached to the method the game really calls. It takes only
    /// <c>LobbyChatMsg_t result</c> - no <c>__instance</c> - and the service's parameter carries the same
    /// type, so it binds exactly as it would have on Lobby, at the same moment, with the same message.
    ///
    /// Single-player and a host's own work never use this path, which is why the mod otherwise looks
    /// healthy. What is dead is the client-to-host sync: a client changes an assignment and the host never
    /// hears it.
    /// </remarks>
    internal sealed class SmartEmployeesLobbyChat : Fix
    {
        internal override string Id => "smartemployees-lobby-chat";
        internal override string Mod => "HUB - Smart Employees";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "Smart Employees listens for lobby chat on Lobby.OnLobbyChatMessage, which 0.4.6 moved to "
             + "SteamLobbyService - so a client's employee changes never reach the host.";

        internal override bool Apply(MelonLogger.Instance log)
        {
            var lobby = AccessTools.TypeByName("Il2CppScheduleOne.Networking.Lobby");
            var service = AccessTools.TypeByName("Il2CppScheduleOne.Networking.SteamLobbyService");
            if (service == null)
            {
                log.Warning("[fix] smartemployees-lobby-chat: Il2CppScheduleOne.Networking.SteamLobbyService "
                          + "is not on this build, so there is nothing to listen on.");
                return false;
            }

            // ONLY WHEN THE MOD'S OWN ATTEMPT CANNOT HAVE WORKED. If a build ever puts the callback back on
            // Lobby, the mod finds it by itself and a second postfix here would run its sync twice for
            // every message.
            if (lobby != null && AccessTools.Method(lobby, "OnLobbyChatMessage") != null) return false;

            var target = Only(service, "OnLobbyChatMessage");
            if (target == null)
            {
                log.Warning("[fix] smartemployees-lobby-chat: SteamLobbyService has no single "
                          + "OnLobbyChatMessage to attach to, so the mod's listener was left alone.");
                return false;
            }

            var patch = AccessTools.Method("HUB.SmartEmployees.Network.SmartEmployeesLobbyChatPatch:Postfix");
            if (patch == null)
            {
                // Said rather than silent: this looks the same whether the mod is absent or
                // present-but-unloaded, and the second happens - a gamemode gate can hold a mod back from
                // MelonLoader while every other tool still lists the file.
                log.Msg("[fix] smartemployees-lobby-chat: HUB - Smart Employees is not loaded, so there is "
                      + "no listener to attach. If the file is installed, something is keeping MelonLoader "
                      + "from loading it.");
                return false;
            }

            // Its one argument has to be the one the service hands out, or Harmony binds nothing and takes
            // the class with it.
            var wanted = patch.GetParameters();
            if (wanted.Length != 1 || target.GetParameters().Length != 1
                || wanted[0].ParameterType != target.GetParameters()[0].ParameterType)
            {
                log.Warning("[fix] smartemployees-lobby-chat: the mod's listener takes "
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
                log.Warning("[fix] smartemployees-lobby-chat: could not attach the mod's listener to "
                          + "SteamLobbyService.OnLobbyChatMessage: " + e.Message);
                return false;
            }

            // The finding names the OLD method, because that is what the mod asked for.
            Fixes.Repaired.Add("HUB.SmartEmployees|Il2CppScheduleOne.Networking.Lobby::OnLobbyChatMessage");

            log.Msg("[fix] smartemployees-lobby-chat: Smart Employees listens on "
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
