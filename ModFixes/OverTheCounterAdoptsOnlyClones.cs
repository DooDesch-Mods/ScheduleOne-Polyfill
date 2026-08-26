using System.Reflection;
using HarmonyLib;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.NPCs;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A manager is never a townsperson: an adoption that lands on a placed NPC is refused.
    /// </summary>
    /// <remarks>
    /// Reported twice, and it is the worst kind of bug because it cannot be undone: the OverTheCounter
    /// manager turns into Mick Lubbin - his face, his name, his meth in the manager's stock - and the
    /// manager marker follows Mick around the map.
    ///
    /// THE MECHANISM IS A REUSED NUMBER, not a wrong name. OverTheCounter stores a FishNet
    /// <c>ObjectId</c> in a Steam lobby value so a client can find the manager the host spawned. That
    /// number is a handle into one FishNet session, not an identity: <c>ServerObjects</c> holds a
    /// shuffled queue of 0..65533, hands them out to scene objects and spawns alike, takes them back on
    /// despawn, and rebuilds the queue whenever a server starts. Loading a save stops the server, loads
    /// the scene and starts a new one, so every number is dealt again - while the lobby value survives,
    /// because it lives in Steam and not in the game.
    ///
    /// Mick then gets the manager's old number, a client reads the stale slot before the host publishes
    /// the new one, and <c>ManagerInstance.FindNetworkNpc</c> - which compares nothing but the number
    /// (ManagerInstance.cs:1916-1954) - answers with Mick. <c>Adopt</c> overwrites his ID, name and
    /// appearance, and <c>SetupInventory</c> leaves the inventory it finds alone, which is where the meth
    /// comes from.
    ///
    /// WHAT THIS REFUSES is narrow and decides nothing else: a candidate whose NetworkObject is a SCENE
    /// object. A manager is instantiated and spawned at runtime; Mick and every other townsperson is
    /// placed in the scene and carries a SceneId. That one bit separates them, it is FishNet's own, and
    /// it needs no knowledge of what OverTheCounter intended.
    ///
    /// A REFUSED ADOPTION LEAVES THE MANAGER MISSING until the right slot arrives, and that is the
    /// trade being made deliberately: a missing manager is a rejoin away, an adopted Mick is a save
    /// away from being someone else forever.
    ///
    /// This does not fix the protocol. The number should never have travelled without a session marker
    /// beside it, and only the mod can add one.
    /// </remarks>
    internal sealed class OverTheCounterAdoptsOnlyClones : Fix
    {
        internal override string Id => "otc-adopts-only-clones";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a manager slot can no longer turn a townsperson into the manager";

        internal override string StandsDownBecause
            => "an OverTheCounter manager can become a named character - their face, their name and "
             + "their inventory - and a save later there is no way back.";

        private static MelonLogger.Instance _log;
        private static int _refused;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            int patched = 0;
            patched += Guard("OverTheCounter.Logic.ManagerInstance", "FindNetworkNpc", log);
            patched += Guard("OverTheCounter.Logic.DrifterManager", "FindNetworkDrifter", log);

            if (patched == 0)
            {
                log.Warning($"[fix] {Id}: neither lookup is where this build of OverTheCounter keeps it, "
                          + "so a stale slot can still adopt a townsperson.");
                return false;
            }
            return true;
        }

        /// <summary>Put the refusal behind one lookup, if this build has it.</summary>
        private static int Guard(string typeName, string methodName, MelonLogger.Instance log)
        {
            var type = AccessTools.TypeByName(typeName);
            var found = type == null ? null : AccessTools.Method(type, methodName);

            // The return type is what this reads, so a method of the same name that answers something
            // else is not the one meant and is left alone rather than patched hopefully.
            if (found == null || !typeof(NPC).IsAssignableFrom(found.ReturnType)) return 0;

            new HarmonyLib.Harmony("doodesch.polyfill.otcadoption").Patch(
                found, postfix: new HarmonyMethod(typeof(OverTheCounterAdoptsOnlyClones), nameof(NotAPlacedNpc)));

            log.Msg($"[fix] otc-adopts-only-clones: {typeName}.{methodName} now refuses a placed NPC.");
            return 1;
        }

        /// <summary>Drop a candidate that was placed in the scene rather than spawned into it.</summary>
        private static void NotAPlacedNpc(ref NPC __result)
        {
            if (__result == null) return;

            try
            {
                var network = __result.gameObject.GetComponent<NetworkObject>();

                // No NetworkObject at all is not this bug and not this fix's business: the number cannot
                // have matched, so the mod found the candidate some other way.
                if (network == null || !network.IsSceneObject) return;

                string who = null;
                try { who = __result.NPCData?.BasicInfo?.ID; } catch { }

                if (_refused++ == 0)
                {
                    _log?.Warning("[fix] otc-adopts-only-clones: a manager slot pointed at "
                                + (string.IsNullOrEmpty(who) ? "a character placed in the world" : who)
                                + ", who was placed in the world rather than spawned as a manager. The "
                                + "adoption was refused - the manager stays missing until the right slot "
                                + "arrives, which is the recoverable half of this.");
                }

                __result = null;
            }
            catch (Exception e)
            {
                if (_refused++ == 0)
                    _log?.Warning("[fix] otc-adopts-only-clones: could not tell a placed NPC from a "
                                + "spawned one: " + e.Message);
            }
        }
    }
}
