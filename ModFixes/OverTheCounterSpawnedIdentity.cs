using System;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A drifter, customer or budtender keeps its own name instead of the one it was cloned from.
    /// </summary>
    /// <remarks>
    /// MEASURED, not reasoned. On a loaded save with OverTheCounter 2.0.10 the mod prints the NPC's own
    /// id when it opens a chat with it (otc-src:37904, <c>"Initialized messaging for " + npc.ID</c>), and
    /// it printed <c>bella_penthouse</c> and <c>bulk_deals_shady_guy</c> for two drifters that the mod had
    /// just created as <c>drifter_140_0</c> and <c>drifter_140_1</c>. Those are the ids of the S1API
    /// template characters, which is where the clones came from.
    ///
    /// THE CAUSE IS POLYFILL'S OWN. <see cref="OverTheCounterDrifterPrefab"/> hands the mod an S1API
    /// prefab because this build has no plain civilian, and S1API keeps its templates DEACTIVATED on
    /// purpose (NPCPrefabContainer.cs:91, and NPC.cs:1004 says why: "prevent Awake/Start side effects").
    /// A clone of an inactive object does not run Awake during Instantiate, so the game's
    /// <c>NPC.NPCData</c> is still null when the mod immediately writes the three names
    /// (otc-src:27510-27512). Polyfill's own bridge for those setters hops through NPCData, finds it null,
    /// and drops the write - <c>Pop; Ret</c>, no exception and no line
    /// (Bridges/Steps/S0_4_5f2_To_0_4_6f5/Set.cs:1223-1227). The mod then activates the clone
    /// (otc-src:27539), Awake finally builds NPCData - out of the TEMPLATE's asset - and the NPC carries
    /// the template character's identity for the rest of its life.
    ///
    /// So the repair is a matter of timing rather than of values: the same three writes, once the object
    /// they are meant for exists. The intended id, first name and last name are arguments of the spawn
    /// method itself, so nothing has to be reconstructed or guessed.
    ///
    /// It re-writes ONLY when the value actually came out wrong. On a build that still has a civilian
    /// prefab the clone is active, the mod's own writes land, and this stands down without touching
    /// anything.
    ///
    /// NOT COVERED: ManagerSpawner's own spawn. It returns a ValueTuple rather than an NPC, so a postfix
    /// taking <c>NPC __result</c> cannot bind to it, and Harmony would reject the whole patch rather than
    /// that one target. A hired manager may still carry a borrowed identity; that needs its own repair and
    /// its own measurement.
    /// </remarks>
    internal sealed class OverTheCounterSpawnedIdentity : Fix
    {
        private static MelonLogger.Instance _log;
        private static int _repaired;

        /// <summary>
        /// The three setters, resolved on the LIVE type rather than the one this was compiled against.
        /// </summary>
        /// <remarks>
        /// NPC.ID, FirstName and LastName are get-only in the interop assembly on disk. The setters exist
        /// at runtime because Polyfill itself puts them back - they are the very bridges that drop the
        /// mod's own writes while NPCData is null. Calling them here is deliberate: the value goes to the
        /// same place the mod meant it to go, through the same route, only later.
        /// </remarks>
        private static MethodInfo _setId, _setFirst, _setLast;

        internal override string Id => "otc-spawned-identity";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "Drifters and shop customers keep the name of the character they were built from, so every "
             + "one of them is somebody else.";

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var spawner = AccessTools.TypeByName("OverTheCounter.Logic.NpcSpawner");
            if (spawner == null)
            {
                // Said rather than silent: an installed-but-unloaded mod looks the same as an absent one.
                log.Msg("[fix] otc-spawned-identity: OverTheCounter is not loaded, so there is nothing to "
                      + "repair.");
                return false;
            }

            var target = AccessTools.Method(spawner, "SpawnCivilianNpc");
            if (target == null)
            {
                log.Warning("[fix] otc-spawned-identity: NpcSpawner has no SpawnCivilianNpc on this build, "
                          + "so it was left alone.");
                return false;
            }

            _setId = Setter("ID");
            _setFirst = Setter("FirstName");
            _setLast = Setter("LastName");
            if (_setId == null || _setFirst == null || _setLast == null)
            {
                log.Warning("[fix] otc-spawned-identity: NPC has no runtime setter for "
                          + (_setId == null ? "ID " : "") + (_setFirst == null ? "FirstName " : "")
                          + (_setLast == null ? "LastName " : "")
                          + "- the repair Polyfill normally injects is not there, so this stood down.");
                return false;
            }

            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target,
                    postfix: new HarmonyMethod(typeof(OverTheCounterSpawnedIdentity), nameof(Postfix)));
            }
            catch (Exception e)
            {
                log.Warning("[fix] otc-spawned-identity: could not attach to "
                          + "NpcSpawner.SpawnCivilianNpc: " + e.Message);
                return false;
            }

            log.Msg("[fix] otc-spawned-identity: a spawned drifter or customer gets its own name, instead "
                  + "of the one belonging to the character it was cloned from.");
            return true;
        }

        /// <summary>
        /// The injected setter, which is a bare method rather than half of a property.
        /// </summary>
        /// <remarks>
        /// Measured: AccessTools.PropertySetter(typeof(NPC), "ID") answers null on a running 0.4.6f13 even
        /// though the write works, because what Polyfill adds to the interop assembly is a method named
        /// set_ID with no property metadata around it. The property lookup is tried first anyway, so a
        /// build where the game itself carries a settable property is served by its own member.
        /// </remarks>
        private static MethodInfo Setter(string name)
            => AccessTools.PropertySetter(typeof(NPC), name)
            ?? AccessTools.Method(typeof(NPC), "set_" + name, new[] { typeof(string) });

        /// <summary>
        /// Put the three names back, once the object that holds them exists.
        /// </summary>
        private static void Postfix(NPC __result, string id, string firstName, string lastName)
        {
            if (__result == null || string.IsNullOrEmpty(id)) return;

            string carried;
            try { carried = __result.ID; }
            catch (Exception e)
            {
                _log?.Warning("[fix] otc-spawned-identity: could not read the spawned NPC's id (" +
                              e.Message + "), so it was left as it is.");
                return;
            }

            // The mod's own write landed - this build has a prefab that runs Awake on its own.
            if (carried == id) return;

            try
            {
                _setId.Invoke(__result, new object[] { id });
                _setFirst.Invoke(__result, new object[] { firstName });
                _setLast.Invoke(__result, new object[] { lastName });
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] otc-spawned-identity: could not write the name of '" + id + "': "
                            + e.Message);
                return;
            }

            // A HANDFUL BY NAME. "Every drifter is called Bella" and "the rotation works but the chat shows
            // the wrong one" look identical from the outside, and the first few lines separate them.
            if (_repaired < 6)
            {
                _repaired++;
                try
                {
                    _log?.Msg("[fix] otc-spawned-identity: '" + id + "' had been given the identity of '"
                            + carried + "'"
                            + (_repaired == 6 ? " (further repairs are not logged)" : ""));
                }
                catch { }
            }
        }
    }
}
