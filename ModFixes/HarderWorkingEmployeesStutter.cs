using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Growing;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.ObjectScripts;
using MelonLoader;
using UnityEngine;
using Property = Il2CppScheduleOne.Property.Property;
using Business = Il2CppScheduleOne.Property.Business;

namespace Polyfill.ModFixes
{
    /// <summary>Harder Working Employees stops freezing the game for most of a second once a minute.</summary>
    /// <remarks>
    /// Once a minute the mod's stuck check sweeps the stations, pots and storage for locks an employee left
    /// behind (StationLockUtility.ReleaseOrphanedStationLocks, ReleaseOrphanedTransitSlotLocks and
    /// ReleaseOrphanedGrowContainerLocks). Each sweep asks Object.FindObjectsOfType once per station type, sixteen
    /// times in all, and on 0.4.7 every one of those scans costs about 47 ms whatever it finds - 0 chemistry
    /// stations took as long as 155 storages. Together that is a 770-840 ms frame. Every 30 s its botanist
    /// diagnostics add one more scan for Botanist, about 50 ms.
    ///
    /// The game already keeps both lists: every placed item registers with its property
    /// (BuildableItem.InitializeBuildableItem, BuildableItem.cs:198-222 on 0.4.7f12) and every employee with
    /// EmployeeManager.AllEmployees (Employee.cs:752). The three sweeps are run here over the properties' items,
    /// with the mod's own per-slot and per-container checks (SweepSlotList, OwnerIsWorkingThisContainer), and the
    /// diagnostics read the employee list. Like FindObjectsOfType, only items whose object is active count.
    ///
    /// The mod's StorageEntity pass is left out: StorageEntity is not an ITransitEntity (StorageEntity.cs:23 on
    /// 0.4.7f12), so its cast fails for every storage and that pass never released anything.
    /// </remarks>
    internal sealed class HarderWorkingEmployeesStutter : Fix
    {
        private const string FixId = "harderworkingemployees-stutter";

        internal override string Id => FixId;
        internal override string Mod => "Harder Working Employees";

        /// <summary>Written against 2.2.3.</summary>
        internal override string ModVersions => "<=2.2.3";

        /// <summary>The scans got this slow on 0.4.7; StorageEntity is not a transit entity there either.</summary>
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "employees check for stuck shelves and stations without freezing the game once a minute";

        internal override string StandsDownBecause
            => "The game freezes for most of a second once a minute while the mod looks for stuck locks.";

        private static MethodInfo _sweepSlots, _ownerWorking, _telemetry;
        private static FieldInfo _unloading;
        private static MelonLogger.Instance _log;
        private static bool _saidSweep, _saidBotanists;
        private static int _failures, _botanistsReplaced;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var util = AccessTools.TypeByName("HarderWorkingEmployees.Utilities.StationLockUtility");
            var probe = AccessTools.TypeByName("HarderWorkingEmployees.Diagnostics.BotanistProbe");
            var telemetry = AccessTools.TypeByName("HarderWorkingEmployees.Recovery.RecoveryTelemetry");
            if (util == null)
            {
                log.Warning($"[fix] {FixId}: the mod has no StationLockUtility; nothing to change.");
                return false;
            }

            var stations = AccessTools.DeclaredMethod(util, "ReleaseOrphanedStationLocks", Type.EmptyTypes);
            var transit = AccessTools.DeclaredMethod(util, "ReleaseOrphanedTransitSlotLocks", Type.EmptyTypes);
            var grow = AccessTools.DeclaredMethod(util, "ReleaseOrphanedGrowContainerLocks", Type.EmptyTypes);
            _sweepSlots = AccessTools.DeclaredMethod(util, "SweepSlotList",
                new[] { typeof(Il2CppSystem.Collections.Generic.List<ItemSlot>), typeof(int), typeof(string), typeof(string) });
            _ownerWorking = AccessTools.DeclaredMethod(util, "OwnerIsWorkingThisContainer", new[] { typeof(NPC), typeof(GrowContainer) });
            _unloading = AccessTools.DeclaredField(util, "SceneIsUnloading");
            _telemetry = telemetry == null ? null : AccessTools.DeclaredMethod(telemetry, "Increment", new[] { typeof(string) });

            if (stations == null || transit == null || grow == null || _sweepSlots == null || _ownerWorking == null
                || _unloading == null || _telemetry == null)
            {
                log.Warning($"[fix] {FixId}: the sweep looks different from 2.2.3 (stations {stations != null}, transit "
                          + $"{transit != null}, grow {grow != null}, SweepSlotList {_sweepSlots != null}, "
                          + $"OwnerIsWorkingThisContainer {_ownerWorking != null}, SceneIsUnloading {_unloading != null}, "
                          + $"telemetry {_telemetry != null}); it is left as it is.");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Patch(stations, prefix: new HarmonyMethod(typeof(HarderWorkingEmployeesStutter), nameof(Stations)));
            harmony.Patch(transit, prefix: new HarmonyMethod(typeof(HarderWorkingEmployeesStutter), nameof(Transit)));
            harmony.Patch(grow, prefix: new HarmonyMethod(typeof(HarderWorkingEmployeesStutter), nameof(Grow)));

            var probeMethod = probe == null ? null : AccessTools.DeclaredMethod(probe, "Probe", Type.EmptyTypes);
            if (probeMethod == null)
                log.Warning($"[fix] {FixId}: the mod has no BotanistProbe.Probe; its botanist diagnostics are left as they are.");
            else
            {
                harmony.Patch(probeMethod, transpiler: new HarmonyMethod(typeof(HarderWorkingEmployeesStutter), nameof(UseEmployeeList)));
                if (_botanistsReplaced != 1)
                    log.Warning($"[fix] {FixId}: expected one scan for botanists in BotanistProbe.Probe, replaced "
                              + $"{_botanistsReplaced}; its diagnostics still scan the scene every 30 s.");
            }
            return true;
        }

        // ------------------------------------------------------------------------------------------- sweeps --

        private static bool Stations(ref int __result)
        {
            __result = 0;
            if (!ShouldSweep()) return false;
            try
            {
                foreach (var item in PlacedItems())
                {
                    if (item.TryCast<LabOven>() == null && item.TryCast<ChemistryStation>() == null
                        && item.TryCast<MixingStation>() == null && item.TryCast<Cauldron>() == null
                        && item.TryCast<PackagingStation>() == null && item.TryCast<BrickPress>() == null)
                        continue;
                    var usable = item.TryCast<IUsable>();
                    var owner = usable?.NPCUserObject;
                    if (owner == null) continue;
                    var npc = owner.GetComponent<NPC>();
                    if (npc != null && (npc.Behaviour == null || npc.Behaviour.activeBehaviour != null)) continue;
                    usable.SetNPCUser(null);
                    __result++;
                }
                if (__result > 0)
                    _log.Warning($"[fix] {FixId}: released {__result} station lock(s) left by an employee no longer working there.");
                Said("swept the stations");
            }
            catch (Exception e)
            {
                Fail("sweeping the station locks", e);
            }
            return false;
        }

        private static bool Transit(ref int __result)
        {
            __result = 0;
            if (!ShouldSweep()) return false;
            try
            {
                foreach (var item in PlacedItems())
                {
                    if (item.TryCast<DryingRack>() == null && item.TryCast<Cauldron>() == null
                        && item.TryCast<LabOven>() == null && item.TryCast<MixingStation>() == null
                        && item.TryCast<PackagingStation>() == null && item.TryCast<BrickPress>() == null
                        && item.TryCast<Pot>() == null)
                        continue;
                    var entity = item.TryCast<ITransitEntity>();
                    if (entity == null) continue;
                    int id = item.gameObject.GetInstanceID();
                    string name = item.gameObject.name;
                    __result += (int)_sweepSlots.Invoke(null, new object[] { entity.InputSlots, id, name, "input" });
                    __result += (int)_sweepSlots.Invoke(null, new object[] { entity.OutputSlots, id, name, "output" });
                }
                Said("swept the item slots");
            }
            catch (Exception e)
            {
                Fail("sweeping the item-slot locks", e);
            }
            return false;
        }

        private static bool Grow(ref int __result)
        {
            __result = 0;
            if (!ShouldSweep()) return false;
            try
            {
                foreach (var item in PlacedItems())
                {
                    if (item.TryCast<Pot>() == null && item.TryCast<MushroomBed>() == null) continue;
                    var usable = item.TryCast<IUsable>();
                    var owner = usable?.NPCUserObject;
                    var container = item.TryCast<GrowContainer>();
                    if (owner == null || container == null) continue;

                    var npc = owner.GetComponent<NPC>();
                    string why = npc == null ? "its owner no longer exists"
                               : (bool)_ownerWorking.Invoke(null, new object[] { npc, container }) ? null
                               : npc.FullName + " is no longer working it";
                    if (why == null) continue;

                    usable.SetNPCUser(null);
                    __result++;
                    _telemetry.Invoke(null, new object[] { "GrowContainerLockReleases" });
                    _log.Warning($"[fix] {FixId}: released the lock on '{item.gameObject.name}' - {why}.");
                }
                Said("swept the grow containers");
            }
            catch (Exception e)
            {
                Fail("sweeping the grow-container locks", e);
            }
            return false;
        }

        /// <summary>The mod's own two conditions: the scene is not going away, and this is the host.</summary>
        private static bool ShouldSweep()
        {
            if ((bool)_unloading.GetValue(null)) return false;
            try
            {
                return InstanceFinder.IsServer;
            }
            catch (Exception e)
            {
                _log.Warning($"[fix] {FixId}: could not ask whether this is the host ({e.Message}); sweeping as the mod does.");
                return true;
            }
        }

        /// <summary>Every active item placed on a property or business, each once.</summary>
        private static List<BuildableItem> PlacedItems()
        {
            var seen = new HashSet<IntPtr>();
            var items = new List<BuildableItem>();
            void From(Il2CppSystem.Collections.Generic.List<BuildableItem> list)
            {
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    if (item == null || !item.gameObject.activeInHierarchy || !seen.Add(item.Pointer)) continue;
                    items.Add(item);
                }
            }
            var properties = Property.Properties;
            if (properties != null)
                for (int i = 0; i < properties.Count; i++) From(properties[i]?.BuildableItems);
            var businesses = Business.Businesses;
            if (businesses != null)
                for (int i = 0; i < businesses.Count; i++) From(businesses[i]?.BuildableItems);
            return items;
        }

        // ------------------------------------------------------------------------------------- diagnostics --

        private static IEnumerable<CodeInstruction> UseEmployeeList(IEnumerable<CodeInstruction> instructions)
        {
            var replacement = AccessTools.Method(typeof(HarderWorkingEmployeesStutter), nameof(Botanists));
            var list = instructions.ToList();
            var found = list.Where(i => i.opcode == System.Reflection.Emit.OpCodes.Call
                                     && i.operand is MethodInfo m
                                     && m.Name == nameof(UnityEngine.Object.FindObjectsOfType)
                                     && m.IsGenericMethod
                                     && m.GetParameters().Length == 0
                                     && m.GetGenericArguments()[0] == typeof(Botanist)).ToList();
            _botanistsReplaced = found.Count;
            if (found.Count == 1) found[0].operand = replacement;
            return list;
        }

        /// <summary>The botanists among the game's employees, active ones only, as FindObjectsOfType returned them.</summary>
        private static Il2CppArrayBase<Botanist> Botanists()
        {
            var botanists = new List<Botanist>();
            try
            {
                var manager = NetworkSingleton<EmployeeManager>.Instance;
                var all = manager?.AllEmployees;
                if (all != null)
                    for (int i = 0; i < all.Count; i++)
                    {
                        var botanist = all[i]?.TryCast<Botanist>();
                        if (botanist != null && botanist.gameObject.activeInHierarchy) botanists.Add(botanist);
                    }
                if (!_saidBotanists)
                {
                    _saidBotanists = true;
                    _log.Msg($"[fix] {FixId}: botanist diagnostics read {botanists.Count} botanist(s) from the employee list.");
                }
            }
            catch (Exception e)
            {
                Fail("listing the botanists", e);
            }
            return new Il2CppReferenceArray<Botanist>(botanists.ToArray());
        }

        // ---------------------------------------------------------------------------------------- logging --

        private static void Said(string what)
        {
            if (_saidSweep) return;
            _saidSweep = true;
            _log.Msg($"[fix] {FixId}: {what} from the property lists, without scanning the scene.");
        }

        private static void Fail(string what, Exception e)
        {
            _failures++;
            if (_failures == 1 || _failures % 20 == 0)
                _log.Error($"[fix] {FixId}: {what} failed ({_failures}): {e}; no locks were released this minute.");
            Fixes.Record(FixId, what + " failed: " + e.GetBaseException().Message);
        }
    }
}
