using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.NPCs;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Stop OverTheCounter's drifters and managers from being cloned out of an employee.
    /// </summary>
    /// <remarks>
    /// OverTheCounter spawns its own NPCs to deal to. It looks for the spawnable prefab named
    /// <c>CivilianNPC</c> and, when that is not there, falls back to the first spawnable prefab that has
    /// ANY NPC component on it (NpcSpawner.GetBasePrefab, second loop).
    ///
    /// Measured on 0.4.6f12: there is no <c>CivilianNPC</c> any more - not among the 108 spawnable prefabs,
    /// nothing near it by name, and nothing loaded anywhere in the game under that name. So the fallback is
    /// what runs, and on this build it lands on an EMPLOYEE. That is why the drifters turn up dressed as
    /// cleaners.
    ///
    /// And an employee that nobody assigned a property to throws:
    /// <code>
    /// public override EmployeeHome GetHome() =&gt; configuration.assignedHome;   // Cleaner.cs:307
    /// configuration = new CleanerConfiguration(...);                          // only in AssignProperty()
    /// </code>
    /// OverTheCounter clones the prefab and sets ID, first and last name; it never assigns a property,
    /// because it is not making an employee. So <c>configuration</c> stays null and every tick that reaches
    /// <c>UpdateBehaviour</c> or <c>CanWork</c> raises a NullReferenceException.
    ///
    /// Worth saying plainly, because it looks like a game update broke it: <c>GetHome</c> and <c>CanWork</c>
    /// are identical in 0.4.5f2 and 0.4.6f11. The crash path did not change. What changed is that the prefab
    /// the mod asks for is gone, so it takes the wrong one.
    ///
    /// This picks a better one: the first spawnable NPC prefab that is NOT an employee. If there is no such
    /// prefab it changes nothing and says so - a drifter cloned from the wrong thing is a bug, and one
    /// cloned from a guess is a worse bug.
    ///
    /// THE SAME SEARCH EXISTS TWICE. <c>ManagerSpawner</c> has its own private <c>GetBasePrefab</c> with the
    /// same body, so patching only <c>NpcSpawner</c> left every hired manager cloned from a cleaner. Nobody
    /// noticed while hiring was failing earlier for a different reason; the moment that was repaired, the
    /// identical NullReferenceException came back as "the clipboard freezes on the manager". Both are
    /// patched now, and the id still says drifter because turning a fix off by name has to keep working.
    /// </remarks>
    internal sealed class OverTheCounterDrifterPrefab : Fix
    {
        internal override string Id => "otc-drifter-prefab";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "2.0.10";
        internal override string GameVersions => "*";
        internal override string What
            => "the drifters and the managers stop being cloned out of an employee and throwing every tick";

        internal override string StandsDownBecause
            => "OverTheCounter's drifters and hired managers may be cloned from an employee prefab, which "
             + "throws a NullReferenceException in Employee.UpdateBehaviour on every tick.";

        private static MelonLogger.Instance _log;
        private static readonly List<NetworkObject> _pool = new();
        private static readonly List<NetworkObject> _baked = new();
        private static int _next;
        private static bool _searched;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            // TWO CLASSES, THE SAME SEARCH, AND ONLY ONE WAS PATCHED. ManagerSpawner carries its own private
            // GetBasePrefab with the same body as NpcSpawner's, so a manager was still being built out of an
            // employee long after the drifters stopped being. It only became visible once hiring a manager
            // worked at all, which took a separate repair - and then the same NullReferenceException came
            // back under a different name.
            int patched = 0;
            foreach (string owner in new[] { "OverTheCounter.Logic.NpcSpawner",
                                             "OverTheCounter.Logic.ManagerSpawner" })
            {
                var spawner = Find(owner);
                if (spawner == null) continue;

                var target = AccessTools.Method(spawner, "GetBasePrefab");
                if (target == null) continue;

                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target, postfix: new HarmonyMethod(typeof(OverTheCounterDrifterPrefab), nameof(Postfix)));
                patched++;
            }

            if (patched == 0)
            { log.Warning("[fix] otc-drifter-prefab: neither spawner has a GetBasePrefab here."); return false; }

            // The count is printed because one of the two is only reached when a manager is hired, which is
            // not something a startup log can otherwise show.
            log.Msg($"[fix] otc-drifter-prefab: watching {patched} prefab search(es) - the drifters and, "
                  + "when you hire one, the manager.");
            return true;
        }

        /// <summary>
        /// Hand back a prefab that is not an employee, when the one found is.
        /// </summary>
        /// <remarks>
        /// A postfix rather than a replacement: what the mod's own search finds is left alone whenever it is
        /// already fine, which is what happens on any build that still has a plain civilian prefab. This only
        /// ever fires on the fallback's bad answer.
        /// </remarks>
        private static void Postfix(ref NetworkObject __result)
        {
            if (__result == null) return;

            GameObject found = null;
            try { found = __result.gameObject; } catch { }
            if (found == null) return;

            // An employee carries a configuration that only AssignProperty creates. Nothing else about the
            // prefab matters here.
            Employee employee = null;
            try { employee = found.GetComponent<Employee>(); } catch { }
            if (employee == null) return;

            var better = Replacement();
            if (better == null)
            {
                Say("[fix] otc-drifter-prefab: OverTheCounter fell back to the employee prefab "
                  + $"'{found.name}', and this build has no spawnable NPC that is not an employee. Left "
                  + "alone; the drifters will keep throwing in Employee.UpdateBehaviour.");
                return;
            }

            Say($"[fix] otc-drifter-prefab: OverTheCounter asked for 'CivilianNPC', which this build does not "
              + $"have, and fell back to '{found.name}' - an employee. Handed it one of {_pool.Count} "
              + $"spawnable NPC prefab(s) instead, a different one each time: {Names()}.");

            // THE NEXT FEW BY NAME, and this is not chatter. "The customers are all the same two people"
            // cannot be told apart from "the rotation works and something later overrides the look" by any
            // amount of reading, and the difference decides whose bug it is. Eight lines in a log settle
            // it; nothing available here does.
            if (_handed < 8)
            {
                _handed++;
                try
                {
                    _log?.Msg($"[fix] otc-drifter-prefab: handover {_handed} -> {better.gameObject.name}"
                            + $", asked for by {Caller()}, prefab active={Active(better.gameObject)}"
                            + (_handed == 8 ? " (further handovers are not logged)" : ""));
                }
                catch { }
            }

            __result = better;
        }


        /// <summary>
        /// Which of the mod's three spawners asked. Best effort, and worth the effort.
        /// </summary>
        /// <remarks>
        /// The patched method has three callers inside OverTheCounter - the shop customers, the budtenders
        /// and the drifters - and the handover line named none of them. A player reporting "the drifters
        /// are broken" therefore produced a log in which nothing said whether a single one of these
        /// handovers was a drifter at all, and a five-way investigation stalled on exactly that: the
        /// leading explanation could not be tied to the reported symptom, and could not be ruled out
        /// either. One name in this line is the difference.
        ///
        /// A managed stack walk is the only thing available here. Harmony can hand a patch its own target
        /// but not its caller, and the clone is renamed to Drifter_/Customer_/Budtender_ only AFTER this
        /// runs. It is bounded to the first eight handovers by the caller, so the cost is eight walks a
        /// session, and it answers "unknown" rather than throwing when the frames are not there - an
        /// IL2CPP build inlines aggressively and may simply not have them.
        /// </remarks>
        private static string Caller()
        {
            try
            {
                var trace = new System.Diagnostics.StackTrace(false);
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    var method = trace.GetFrame(i)?.GetMethod();
                    var type = method?.DeclaringType;
                    if (type == null) continue;

                    string ns = type.Namespace ?? "";
                    if (!ns.StartsWith("OverTheCounter")) continue;
                    // Skip the method this patch is attached to; the interesting frame is above it.
                    if (method.Name == "GetBasePrefab" || method.Name == "SpawnCivilianNpc") continue;
                    return type.Name + "." + method.Name;
                }
            }
            catch { }
            return "unknown";
        }

        /// <summary>
        /// Whether the prefab being handed over is active, which decides whether the clone runs Awake.
        /// </summary>
        /// <remarks>
        /// S1API keeps its templates deactivated on purpose (NPCPrefabContainer.cs:91). A clone of an
        /// inactive source does not run Awake during Instantiate, so anything the mod writes before it
        /// activates the clone lands on an object whose runtime data is not built yet. Whether that
        /// actually costs the drifter its identity is unmeasured - which is the point of printing it.
        /// </remarks>
        private static string Active(GameObject go)
        {
            try { return go.activeSelf ? "yes" : "no"; }
            catch { return "unknown"; }
        }

        private static int _handed;

        /// <summary>
        /// A spawnable NPC prefab that is not an employee - and a different one on each call.
        /// </summary>
        /// <remarks>
        /// A DIFFERENT ONE EACH TIME, and that is the whole change. Handing back the first match meant every
        /// customer in the shop was cloned from the same prefab, so a player with a dispensary full of
        /// people reported them as all being the same character. OverTheCounter does randomise hair, clothes
        /// and face layers on top (NpcSpawner.GenerateRandomAppearance), but the body it randomises is still
        /// the one it was cloned from.
        ///
        /// AND THERE IS NO VANILLA CANDIDATE AT ALL. Measured on 0.4.6f13, the whole set of spawnable NPC
        /// prefabs that are not employees is <c>S1API_MysteriousMan, S1API_BellaNPC, S1API_StaticNPC,
        /// S1API_VicNPC</c> - four prefabs, every one of them registered by S1API. The reporter who saw a
        /// shop full of the same character was looking at <c>S1API_BellaNPC</c>. So this repair works
        /// because another mod happens to be installed, which is why the log names what it found rather
        /// than saying it handed over "a prefab": without S1API there is nothing here to hand over, and the
        /// fix says that instead of pretending.
        /// </remarks>
        private static NetworkObject Replacement()
        {
            if (!_searched)
            {
                _searched = true;
                Gather();
            }

            if (_pool.Count == 0) return null;
            return Neutral(_pool[_next++ % _pool.Count]);
        }

        /// <summary>The private, unowned copy of one donor prefab - built once, then reused.</summary>
        private static readonly Dictionary<int, NetworkObject> _neutral = new();

        /// <summary>Where the private copies live, so nothing in the world can find them.</summary>
        private static Transform _shelf;

        /// <summary>
        /// A body that belongs to nobody, made from one that belongs to somebody.
        /// </summary>
        /// <remarks>
        /// THE DONOR IS ANOTHER MOD'S CHARACTER, AND THAT WAS THE BUG. Handing the donor straight back
        /// made every drifter a copy of a registered NPC, and the mod that registered it went on driving
        /// its copy: the reporter's drifter chat was headed "Shady Guy", the buttons in it said
        /// "Accept for today" - a string from BulkDeals, not from OverTheCounter - and the drifter itself
        /// stayed at OfferSent for ever because its own accept was never among the answers. Talking to it
        /// gave the default greeting, because the dialogue belonged to a character the donor mod thinks is
        /// somewhere else.
        ///
        /// Renaming the clone afterwards does not fix that, and this used to try: the identity is read
        /// during Awake (NPC.cs:3072, NPCData = _npcData.GetRuntimeData()), so by the time anything can
        /// rename it, the dialogue, the voice, the registrations and the phone conversation have all been
        /// built from the donor.
        ///
        /// So the identity is taken off the TEMPLATE instead, before any clone exists. The data object is
        /// a ScriptableObject shared with the donor, so it is copied first - editing it in place would
        /// rename that mod's own character across the whole game. What is left is a nameless townsperson
        /// wearing a body OverTheCounter randomises anyway, and no mod can recognise it, because there is
        /// nothing left to recognise.
        /// </remarks>
        private static NetworkObject Neutral(NetworkObject donor)
        {
            if (donor == null) return null;

            int key;
            try { key = donor.GetInstanceID(); } catch { return donor; }
            if (_neutral.TryGetValue(key, out var made) && made != null) return made;

            try
            {
                if (_shelf == null)
                {
                    var holder = new GameObject("Polyfill.NeutralBodies");
                    holder.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(holder);
                    _shelf = holder.transform;
                }

                // The donor prefab is inactive, so the copy is too and its Awake does not run. That is what
                // keeps this off every registry until OverTheCounter activates a clone of it.
                var copy = UnityEngine.Object.Instantiate(donor, _shelf, false);
                copy.gameObject.SetActive(false);
                copy.gameObject.name = "Polyfill_Civilian";

                var npc = copy.gameObject.GetComponent<NPC>();
                if (npc == null)
                {
                    UnityEngine.Object.Destroy(copy.gameObject);
                    _log?.Warning("[fix] otc-drifter-prefab: the copy of '" + donor.gameObject.name
                                + "' has no NPC component, so the donor was handed over unchanged.");
                    return donor;
                }

                if (!Anonymise(npc, donor.gameObject.name))
                {
                    UnityEngine.Object.Destroy(copy.gameObject);
                    return donor;
                }

                _neutral[key] = copy;
                _log?.Msg("[fix] otc-drifter-prefab: built a body of its own from '" + donor.gameObject.name
                        + "', with that character's name and id taken off it, so no mod can adopt what "
                        + "OverTheCounter spawns from it.");
                return copy;
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] otc-drifter-prefab: could not build an unowned body from '"
                            + donor.gameObject.name + "', so the donor was handed over unchanged: "
                            + e.Message);
                return donor;
            }
        }

        /// <summary>
        /// Take the character off a template: its own copy of the data object, and a name nobody owns.
        /// </summary>
        /// <remarks>
        /// Refuses rather than half-does it. A template that still carries the donor's id is the bug this
        /// exists to end, and handing one over while reporting success would be worse than handing over the
        /// donor and saying so.
        /// </remarks>
        private static bool Anonymise(NPC npc, string donorName)
        {
            // NOT AccessTools.Field. Il2CppInterop projects a native field as a PROPERTY over native
            // memory, so the reflection lookup answers null and the first version of this reported "NPC
            // has no _npcData on this build" about a member that is right there. Named directly instead,
            // which is also the only spelling that can be checked at compile time.
            var shared = npc._npcData;
            if (shared == null)
            {
                _log?.Warning("[fix] otc-drifter-prefab: '" + donorName + "' carries no NPC data object.");
                return false;
            }

            // A COPY FIRST. This object is the donor mod's asset and every NPC built from it reads the
            // same instance, so writing an id into it renames that mod's character everywhere.
            var mine = UnityEngine.Object.Instantiate(shared);
            var data = mine.GetOriginalData();
            var basics = data?.BasicInfo;
            if (basics == null)
            {
                UnityEngine.Object.Destroy(mine);
                _log?.Warning("[fix] otc-drifter-prefab: the copied data object for '" + donorName
                            + "' has no BasicInfo, so the character could not be taken off it.");
                return false;
            }

            basics.ID = NeutralId;
            basics.FirstName = "Passerby";
            basics.LastName = string.Empty;
            basics.HasLastName = false;

            npc._npcData = mine;

            // A baked GUID is the other half of the same problem: every copy claims that id and displaces
            // whoever held it. Cleared here rather than warned about, because this template is ours.
            try { npc.BakedGUID = string.Empty; } catch { }
            return true;
        }

        /// <summary>
        /// The id every unowned body carries.
        /// </summary>
        /// <remarks>
        /// One id for all of them on purpose. OverTheCounter writes its own id onto each clone the moment
        /// it is awake, so this is only ever what the NPC is called between Awake and that write - and a
        /// name that is obviously ours is what a reader needs if one ever escapes into a save.
        /// </remarks>
        private const string NeutralId = "polyfill_unowned_body";

        private static void Gather()
        {
            try
            {
                var manager = InstanceFinder.NetworkManager;
                var spawnable = manager?.SpawnablePrefabs;
                if (spawnable == null) return;

                int count = spawnable.GetObjectCount();
                for (int i = 0; i < count; i++)
                {
                    var candidate = spawnable.GetObject(true, i);
                    GameObject go = null;
                    try { go = candidate?.gameObject; } catch { }
                    if (go == null) continue;

                    NPC npc = null;
                    Employee employee = null;
                    try { npc = go.GetComponent<NPC>(); employee = go.GetComponent<Employee>(); } catch { }
                    if (npc == null || employee != null) continue;

                    // A DUPLICATE GUID DISPLACES ITS OWNER IN THE REGISTRY, SILENTLY. A clone inherits
                    // the prefab's BakedGUID, NPC.Awake registers it, and GUIDManager.RegisterObject
                    // logs one warning and then hands the id to the newcomer - so anything that looks
                    // an NPC up by GUID afterwards finds the clone.
                    //
                    // WHAT IT DOES NOT EXPLAIN is a manager turning into a named character. That was
                    // said here and it was too strong: NPCLoader finds a save record by NPC ID
                    // (NPCLoader.cs:18), not by GUID, and OverTheCounter adopts an NPC by FishNet
                    // ObjectId with no ID or prefab check at all (ManagerInstance.cs:1916-1954). A
                    // clashing GUID is a real hazard and a separate one.
                    try
                    {
                        string baked = npc.BakedGUID;
                        if (!string.IsNullOrEmpty(baked))
                            _log?.Warning($"[fix] otc-drifter-prefab: '{go.name}' carries a baked GUID "
                                        + $"({baked}); every copy of it claims that id and displaces "
                                        + "whoever held it.");
                    }
                    catch { }

                    if (Baked(npc)) _baked.Add(candidate);
                    else _pool.Add(candidate);
                }

                // A baked one is used only when there is nothing else. It is still an NPC and still not an
                // employee, which is what the caller crashed without.
                if (_pool.Count == 0) _pool.AddRange(_baked);
                else if (_baked.Count > 0)
                    _log?.Msg($"[fix] otc-drifter-prefab: skipped {_baked.Count} prefab(s) whose body is a "
                            + "single baked layer - a customer cloned from one keeps that body whatever "
                            + "OverTheCounter randomises on top.");
            }
            catch (Exception e) { _log?.Warning("[fix] otc-drifter-prefab: " + e.Message); }
        }

        /// <summary>
        /// Is this prefab's body one baked layer rather than the layers an outfit is made of?
        /// </summary>
        /// <remarks>
        /// A GUARD AND A DIAGNOSTIC, NOT A CONFIRMED REPAIR - and the difference is worth writing down,
        /// because it was written expecting to be one.
        ///
        /// OverTheCounter randomises an appearance over whatever it cloned - gender, skin, height, hair,
        /// face - by editing the settings object it finds on the clone and handing it back
        /// (NpcSpawner.cs:543-634). What it never touches is <c>UseCombinedLayer</c>, and
        /// <c>Avatar.ApplyBodyLayerSettings</c> checks that before anything else:
        /// <code>
        /// if (UseCombinedLayer &amp;&amp; settings.UseCombinedLayer &amp;&amp; settings.CombinedLayer != null)
        /// {
        ///     ... bodyMeshes[j].material = avatarLayer.CombinedMaterial;
        ///     return;                                  // Avatar.cs:607-620
        /// }
        /// </code>
        /// A base with a baked body would therefore keep it while the head changed, which is what "every
        /// customer is the same two people" looks like.
        ///
        /// MEASURED, AND IT IS NOT THAT - at least not here. All four candidates on the machine this was
        /// written on report <c>UseCombinedLayer</c> false, so this skips nothing and explains nothing
        /// about the report that prompted it. It stays because a base that IS baked would defeat the
        /// randomisation silently, and because the line it logs turns that into something visible instead
        /// of something to guess at.
        /// </remarks>
        private static bool Baked(NPC npc)
        {
            try
            {
                var settings = npc.Avatar?.CurrentSettings;
                return settings != null && settings.UseCombinedLayer && settings.CombinedLayer != null;
            }
            catch { return false; }
        }

        private static string Names()
        {
            var names = new List<string>();
            foreach (var one in _pool)
            {
                try { names.Add(one.gameObject.name); } catch { }
                if (names.Count == 8) { names.Add("..."); break; }
            }
            return string.Join(", ", names);
        }

        /// <summary>Once per launch. The mod asks for the prefab on every spawn and the answer never
        /// changes, so saying it every time would bury the log the player is asked to send.</summary>
        private static void Say(string line)
        {
            if (_said) return;
            _said = true;
            _log?.Msg(line);
        }

        private static Type Find(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = null;
                try { found = assembly.GetType(fullName, false); } catch { }
                if (found != null) return found;
            }
            return null;
        }
    }
}
