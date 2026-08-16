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
                            + (_handed == 8 ? " (further handovers are not logged)" : ""));
                }
                catch { }
            }

            __result = better;
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
            return _pool[_next++ % _pool.Count];
        }

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
