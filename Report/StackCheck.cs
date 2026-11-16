#if DEBUG
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;

namespace Polyfill.Report
{
    /// <summary>
    /// Logs an item's stack limit and every inventory slot that holds it, so a repair of a mod that changes
    /// stack sizes (CustomStackSizes) can be checked after the game's own <c>give</c> command.
    /// </summary>
    /// <remarks>
    /// <c>give</c> adds one item at a time and starts a new slot when one is full, so the slot quantities
    /// show the limit the game really applied, not only the number the definition holds.
    /// </remarks>
    internal static class StackCheck
    {
        internal static void Run(string argument)
        {
            string id = (argument ?? "").Trim();
            if (id.Length == 0)
            {
                Core.Log.Msg("polyfillstack <itemId> - logs the item's stack limit and the inventory slots that hold it.");
                return;
            }
            var definition = Registry.GetItem(id);
            if (definition == null)
            {
                Core.Log.Warning($"polyfillstack: the game has no item '{id}'.");
                return;
            }
            Core.Log.Msg($"polyfillstack: {definition.ID} stack limit {definition.StackLimit}.");

            var inventory = PlayerSingleton<PlayerInventory>.Instance;
            var slots = inventory == null ? null : inventory.GetAllInventorySlots();
            if (slots == null)
            {
                Core.Log.Warning("polyfillstack: no player inventory; load a save first.");
                return;
            }
            int found = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot?.ItemInstance == null || slot.ItemInstance.ID != definition.ID) continue;
                found++;
                Core.Log.Msg($"polyfillstack: inventory slot {i + 1} holds {definition.ID} x{slot.Quantity}.");
            }
            Core.Log.Msg($"polyfillstack: {found} inventory slot(s) hold {definition.ID}.");
            LogDryingRacks();
        }

        /// <summary>
        /// Drying rack capacity, grouped by value: a mod that patches rack setup changes ItemCapacity on
        /// every placed rack, and the game's own value is 20 (DryingRack.ItemCapacity).
        /// </summary>
        private static void LogDryingRacks()
        {
            var racks = UnityEngine.Object.FindObjectsOfType<DryingRack>();
            if (racks == null || racks.Length == 0)
            {
                Core.Log.Msg("polyfillstack: no drying racks placed in this save.");
                return;
            }
            var byCapacity = new System.Collections.Generic.SortedDictionary<int, int>();
            foreach (var rack in racks)
            {
                if (rack == null) continue;
                byCapacity.TryGetValue(rack.ItemCapacity, out int n);
                byCapacity[rack.ItemCapacity] = n + 1;
            }
            foreach (var pair in byCapacity)
                Core.Log.Msg($"polyfillstack: {pair.Value} drying rack(s) with capacity {pair.Key}.");
        }
    }
}
#endif
