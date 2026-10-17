using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Curfew+'s night officers come out whole on 0.4.7, instead of as copies that throw on every frame.
    /// </summary>
    /// <remarks>
    /// Curfew+ staffs its night shift by copying officers that stand in the scene:
    ///
    /// <code>
    /// var go = Object.Instantiate(template.gameObject, spawnPosition, spawnPoint.rotation);   // CreateOfficerClone
    /// </code>
    ///
    /// Instantiate copies every child's active flag, and the template is often switched off in parts. An
    /// officer waiting in the station or sitting in a car has its whole Avatar object inactive
    /// (NPC.SetVisible, NPC.cs:742, called from EnterBuilding and EnterVehicle), and a culled avatar has
    /// its BodyContainer inactive (Avatar.cs:240). The copy's NPC.Awake then looks its parts up with
    /// GetComponentInChildren, which skips inactive objects (NPC.cs:575-580): no Avatar, no voice emitter,
    /// and Awake ends in a NullReferenceException before the officer is set up. Measured on 0.4.7f12: 20
    /// of 20 copies broke in GetAndValidateReferences or ApplyNPCData, and NPCMovement.SetDestination
    /// threw on the broken ones more than a thousand times in 90 seconds.
    ///
    /// The call is replaced by one that copies under an inactive parent, so Awake waits; switches the
    /// Avatar and its BodyContainer on; gives the copy its own GUID; and only then lets it wake up. The
    /// GUID matters because NPC.Start registers <c>BakedGUID</c> when the NPC has none yet (NPC.cs:425-433),
    /// and GUIDManager replaces whatever held that GUID before (GUIDManager.cs:14-21) - every copy would
    /// otherwise take the original officer's place in the lookup.
    /// </remarks>
    internal sealed class CurfewPlusOfficerClones : Fix
    {
        internal override string Id => "curfewplus-officer-clones";
        internal override string Mod => "Curfew+";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";
        internal override string What => "Curfew+'s night officers walk their routes";

        private static MelonLogger.Instance _log;
        private static int _replaced;
        private static int _cloned;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var manager = AccessTools.TypeByName("CurfewPlus.NightPatrolManager");
            if (manager == null)
            {
                log.Warning("[fix] curfewplus-officer-clones: CurfewPlus.NightPatrolManager is not in this Curfew+; nothing was changed.");
                return false;
            }

            var create = AccessTools.Method(manager, "CreateOfficerClone");
            if (create == null)
            {
                log.Warning("[fix] curfewplus-officer-clones: Curfew+ has no CreateOfficerClone; nothing was changed.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes")
                .Patch(create, transpiler: new HarmonyMethod(typeof(CurfewPlusOfficerClones), nameof(UseWholeCopy)));
            if (_replaced != 1)
            {
                log.Warning($"[fix] curfewplus-officer-clones: expected one Instantiate in CreateOfficerClone, replaced {_replaced}; officers are copied as before.");
                return false;
            }
            return true;
        }

        private static IEnumerable<CodeInstruction> UseWholeCopy(IEnumerable<CodeInstruction> instructions)
        {
            var copy = AccessTools.Method(typeof(CurfewPlusOfficerClones), nameof(Copy));
            var list = instructions.ToList();
            var found = list.Where(i => i.opcode == System.Reflection.Emit.OpCodes.Call
                                     && i.operand is MethodInfo m
                                     && m.Name == "Instantiate"
                                     && m.IsGenericMethod
                                     && m.GetGenericArguments()[0] == typeof(GameObject)
                                     && m.GetParameters().Length == 3
                                     && m.GetParameters()[1].ParameterType == typeof(Vector3)).ToList();
            _replaced = found.Count;
            if (found.Count == 1)
                found[0].operand = copy;
            return list;
        }

        /// <summary>
        /// <c>Object.Instantiate(template, position, rotation)</c>, with the copy's avatar switched on and a
        /// GUID of its own before its Awake runs.
        /// </summary>
        private static GameObject Copy(GameObject template, Vector3 position, Quaternion rotation)
        {
            var holder = new GameObject("[Polyfill] Curfew+ officer copy");
            holder.SetActive(false);
            GameObject copy = null;
            try
            {
                copy = UnityEngine.Object.Instantiate(template, holder.transform);
                copy.transform.SetPositionAndRotation(position, rotation);

                var avatar = copy.GetComponentInChildren<Il2CppScheduleOne.AvatarFramework.Avatar>(true);
                if (avatar == null)
                    _log.Warning("[fix] curfewplus-officer-clones: the officer copy has no avatar at all; it is woken up as it is.");
                else
                {
                    avatar.gameObject.SetActive(true);
                    if (avatar.BodyContainer != null)
                        avatar.BodyContainer.gameObject.SetActive(true);
                }

                var npc = copy.GetComponent<NPC>();
                if (npc == null)
                    _log.Warning("[fix] curfewplus-officer-clones: the officer copy has no NPC component; its GUID is left as copied.");
                else
                    npc.BakedGUID = Il2Cpp.GUIDManager.GenerateUniqueGUID().ToString();

                copy.transform.SetParent(null, true);
                _cloned++;
                if (_cloned == 1)
                    _log.Msg("[fix] curfewplus-officer-clones: copied the first night officer with its avatar switched on and a GUID of its own.");
                Fixes.Record("curfewplus-officer-clones", "copied a night officer whole");
                return copy;
            }
            catch (Exception e)
            {
                _log.Error("[fix] curfewplus-officer-clones: copying an officer failed: " + e.Message);
                if (copy != null) UnityEngine.Object.Destroy(copy);
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(holder);
            }
        }
    }
}
