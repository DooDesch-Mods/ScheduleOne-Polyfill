using System;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A drifter, customer, budtender or hired manager keeps its own name, not the one it was cloned from.
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
    /// THE HIRED MANAGER IS THE SAME BUG WITH A DIFFERENT SHAPE, and it is repaired here too - at a
    /// different point, because two things rule out the obvious one. ManagerSpawner.Spawn returns a
    /// ValueTuple (otc-src:41059), so a postfix taking <c>NPC __result</c> cannot bind to it at all. And
    /// even if it could, it would be too late: Spawn builds the manager's phone conversation INSIDE itself
    /// at :41163, and MSGConversation copies the name into a plain field in its constructor
    /// (MSGConversation.cs:113) that nothing ever writes again. Repairing the NPC after Spawn returns
    /// would leave every message, notification and conversation header addressed to the template
    /// character.
    ///
    /// So the manager is repaired at <c>ApplyAppearance(NPC, int seed)</c> (otc-src:41209), which Spawn
    /// calls at :41162 - after <c>SetActive(true)</c> at :41132, so NPCData exists, and one line before
    /// the messaging is built. It carries the seed, and the mod's name generator is deterministic and
    /// side-effect free: DetermineGender saves and restores Random.state around its own draw
    /// (otc-src:40995-41002), and the mod itself already calls GetManagerName a second time from
    /// elsewhere (otc-src:39463). The id is not there, so it is captured from a prefix on Spawn, which
    /// takes it as its first argument.
    ///
    /// WHAT THAT COSTS IF IT IS WRONG: the manager half is NOT verified in a running game. Hiring a
    /// manager needs the phone, and nothing here can drive a mouse. Every step of it is guarded and logs
    /// what it could not do, so the failure mode is a line in the log and the identity staying as it is
    /// today - not a throw inside somebody else's spawn.
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

        /// <summary>The manager's own name generator, reached by seed. See the remarks.</summary>
        private static MethodInfo _gender, _firstName, _lastName;

        /// <summary>The id of the manager currently being spawned, and the seed it was asked for with.</summary>
        /// <remarks>
        /// Spawn calls ApplyAppearance itself, on the same thread, before it returns, so a single pending
        /// pair is enough - and the seed is checked rather than assumed, so a nested or reordered call
        /// leaves the identity alone instead of stamping the wrong one.
        /// </remarks>
        private static string _pendingId;
        private static int _pendingSeed;
        private static bool _pending;

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

            int managers = AttachManager(log);

            log.Msg("[fix] otc-spawned-identity: a spawned drifter or customer gets its own name, instead "
                  + "of the one belonging to the character it was cloned from."
                  + (managers == 2 ? " A hired manager does too." : ""));
            return true;
        }

        /// <summary>
        /// The manager half. Returns how many of its two targets bound; anything under two does nothing.
        /// </summary>
        /// <remarks>
        /// Kept separate and non-fatal on purpose: the civilian repair above is measured working, and a
        /// manager target that this build does not have must not take it down with it.
        /// </remarks>
        private static int AttachManager(MelonLogger.Instance log)
        {
            var spawner = AccessTools.TypeByName("OverTheCounter.Logic.ManagerSpawner");
            if (spawner == null) return 0;

            var spawn = AccessTools.Method(spawner, "Spawn");
            var appearance = AccessTools.Method(spawner, "ApplyAppearance");
            _gender = AccessTools.Method(spawner, "DetermineGender");
            _firstName = AccessTools.Method(spawner, "GetRandomFirstName");
            _lastName = AccessTools.Method(spawner, "GetRandomLastName");

            if (spawn == null || appearance == null || _gender == null || _firstName == null
                || _lastName == null)
            {
                log.Warning("[fix] otc-spawned-identity: ManagerSpawner is missing "
                          + (spawn == null ? "Spawn " : "") + (appearance == null ? "ApplyAppearance " : "")
                          + (_gender == null ? "DetermineGender " : "")
                          + (_firstName == null ? "GetRandomFirstName " : "")
                          + (_lastName == null ? "GetRandomLastName " : "")
                          + "on this build, so a hired manager keeps the name it is cloned from.");
                return 0;
            }

            try
            {
                var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
                harmony.Patch(spawn, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(OverTheCounterSpawnedIdentity), nameof(RememberManager))));
                harmony.Patch(appearance, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(OverTheCounterSpawnedIdentity), nameof(NameTheManager))));
                return 2;
            }
            catch (Exception e)
            {
                log.Warning("[fix] otc-spawned-identity: could not attach to ManagerSpawner ("
                          + e.Message + "), so a hired manager keeps the name it is cloned from.");
                return 0;
            }
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

        /// <summary>Spawn knows the id; ApplyAppearance, where the repair has to happen, does not.</summary>
        private static void RememberManager(string id, int seed)
        {
            _pendingId = id;
            _pendingSeed = seed;
            _pending = !string.IsNullOrEmpty(id);
        }

        /// <summary>
        /// After the clone is active and before its phone conversation is built.
        /// </summary>
        private static void NameTheManager(NPC npc, int seed)
        {
            if (!_pending || npc == null) return;
            // The pair belongs to THIS spawn or to nothing.
            if (seed != _pendingSeed) return;

            string id = _pendingId;
            _pending = false;

            string carried;
            try { carried = npc.ID; }
            catch (Exception e)
            {
                _log?.Warning("[fix] otc-spawned-identity: could not read the manager's id (" + e.Message
                            + "), so it was left as it is.");
                return;
            }
            if (carried == id) return;

            try
            {
                bool female = (float)_gender.Invoke(null, new object[] { seed }) >= 0.5f;
                var first = (string)_firstName.Invoke(null, new object[] { seed, female });
                var last = (string)_lastName.Invoke(null, new object[] { seed });

                _setId.Invoke(npc, new object[] { id });
                _setFirst.Invoke(npc, new object[] { first });
                _setLast.Invoke(npc, new object[] { last });

                _log?.Msg("[fix] otc-spawned-identity: manager '" + id + "' had been given the identity of '"
                        + carried + "', and is now " + first + " " + last + ".");
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] otc-spawned-identity: could not name the manager '" + id + "': "
                            + e.Message);
            }
        }
    }
}
