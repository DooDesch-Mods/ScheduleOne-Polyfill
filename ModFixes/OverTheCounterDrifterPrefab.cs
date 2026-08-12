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
    /// Stop OverTheCounter's drifters from being cloned out of an employee.
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
    /// </remarks>
    internal sealed class OverTheCounterDrifterPrefab : Fix
    {
        internal override string Id => "otc-drifter-prefab";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "2.0.10";
        internal override string GameVersions => "*";
        internal override string What => "the drifters stop being cloned out of an employee and throwing every tick";

        internal override string StandsDownBecause
            => "OverTheCounter's drifters may be cloned from an employee prefab, which throws a "
             + "NullReferenceException in Employee.UpdateBehaviour on every tick.";

        private static MelonLogger.Instance _log;
        private static NetworkObject _replacement;
        private static bool _searched;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var spawner = Find("OverTheCounter.Logic.NpcSpawner");
            if (spawner == null)
            { log.Warning("[fix] otc-drifter-prefab: NpcSpawner is not where it was."); return false; }

            var target = AccessTools.Method(spawner, "GetBasePrefab");
            if (target == null)
            { log.Warning("[fix] otc-drifter-prefab: GetBasePrefab is gone."); return false; }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, postfix: new HarmonyMethod(typeof(OverTheCounterDrifterPrefab), nameof(Postfix)));
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
              + $"have, and fell back to '{found.name}' - an employee. Handed it '{better.gameObject.name}' "
              + "instead, which is not one.");
            __result = better;
        }

        /// <summary>The first spawnable NPC prefab with no employee on it, or null.</summary>
        private static NetworkObject Replacement()
        {
            if (_searched) return _replacement;
            _searched = true;

            try
            {
                var manager = InstanceFinder.NetworkManager;
                var spawnable = manager?.SpawnablePrefabs;
                if (spawnable == null) return null;

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

                    if (npc != null && employee == null) { _replacement = candidate; return _replacement; }
                }
            }
            catch (Exception e) { _log?.Warning("[fix] otc-drifter-prefab: " + e.Message); }
            return null;
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
