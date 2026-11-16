#if DEBUG
using System.Collections;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using MelonLoader;
using UnityEngine;

namespace Polyfill.Report
{
    /// <summary>
    /// Lists, fills and empties storage-rack slots from the console, so a repair of a mod that reacts to a
    /// rack running empty (AutoRestock) can be watched without a person moving items by hand.
    /// </summary>
    /// <remarks>
    /// Emptying goes through ItemSlot.ChangeQuantity, the same door a player's click or an employee's
    /// pickup takes, so every mod patch on it runs as it does in play. Four seconds later the command logs
    /// the slot again with the online and cash balance, which is what a restock changes.
    ///
    /// DEBUG ONLY: it moves items the player did not move.
    /// </remarks>
    internal static class RackSlot
    {
        internal static void Run(string argument)
        {
            var words = (argument ?? "").Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            string verb = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            if (verb is not ("show" or "fill" or "empty"))
            {
                Core.Log.Msg("polyfillrack show | fill <itemId> [qty] [rackId] | empty [n] - lists the storage slots, puts items "
                           + "into the first empty one of a rack whose id contains rackId (default storagerack, 'any' for every storage), "
                           + "or empties the n-th filled one (default 1) and logs it again 4 s later.");
                return;
            }

            var racks = UnityEngine.Object.FindObjectsOfType<PlaceableStorageEntity>();
            if (racks == null || racks.Length == 0)
            {
                Core.Log.Warning("polyfillrack: no storage rack in this scene; load a save with one first.");
                return;
            }

            if (verb == "fill") { Fill(racks, words); return; }

            int wanted = 1;
            if (verb == "empty" && words.Length > 1 && (!int.TryParse(words[1], out wanted) || wanted < 1))
            {
                Core.Log.Warning($"polyfillrack: '{words[1]}' is not a number from 1 up.");
                return;
            }

            int filled = 0;
            foreach (var rack in racks)
            {
                var slots = rack.StorageEntity?.ItemSlots;
                if (slots == null) continue;
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot?.ItemInstance == null || slot.Quantity <= 0) continue;
                    filled++;
                    if (verb == "show")
                    {
                        Core.Log.Msg($"polyfillrack: {filled}. {Describe(rack, i, slot)}");
                        continue;
                    }
                    if (filled != wanted) continue;

                    int had = slot.Quantity;
                    Core.Log.Msg($"polyfillrack: emptying {Describe(rack, i, slot)}; {Balance()}.");
                    slot.ChangeQuantity(-had);
                    // Logged, because ChangeQuantity refuses a removal on a locked slot and says so only
                    // in the game's own console; a refill check must not pass on a slot that never emptied.
                    Core.Log.Msg($"polyfillrack: emptied, now {Describe(rack, i, slot)}.");
                    MelonCoroutines.Start(Recheck(rack, i, slot));
                    return;
                }
            }

            if (verb == "show")
                Core.Log.Msg($"polyfillrack: {filled} filled slot(s) on {racks.Length} rack(s).");
            else
                Core.Log.Warning($"polyfillrack: only {filled} filled slot(s) on {racks.Length} rack(s), no number {wanted}.");
        }

        private static void Fill(PlaceableStorageEntity[] racks, string[] words)
        {
            if (words.Length < 2)
            {
                Core.Log.Warning("polyfillrack fill: name an item id, for example `polyfillrack fill pseudo 20`.");
                return;
            }
            int quantity = 1;
            if (words.Length > 2 && (!int.TryParse(words[2], out quantity) || quantity < 1))
            {
                Core.Log.Warning($"polyfillrack fill: '{words[2]}' is not a number from 1 up.");
                return;
            }
            var definition = Registry.GetItem(words[1]);
            if (definition == null)
            {
                Core.Log.Warning($"polyfillrack fill: the game has no item '{words[1]}'.");
                return;
            }
            // Only racks whose id contains this text, e.g. "largestoragerack". FindObjectsOfType returns every
            // placeable storage, and a restock mod may watch some kinds and not others (AutoRestock skips
            // beds and lockers), so the default is storage racks and "any" lifts the filter.
            string kind = words.Length > 3 ? words[3].ToLowerInvariant() : "storagerack";
            if (kind == "any") kind = null;

            foreach (var rack in racks)
            {
                if (kind != null && !RackId(rack).Contains(kind)) continue;
                var slots = rack.StorageEntity?.ItemSlots;
                if (slots == null) continue;
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (slot == null || slot.ItemInstance != null) continue;
                    slot.InsertItem(definition.GetDefaultInstance(quantity));
                    Core.Log.Msg($"polyfillrack fill: put {Describe(rack, i, slot)}.");
                    return;
                }
            }
            Core.Log.Warning($"polyfillrack fill: no empty slot on {racks.Length} rack(s)"
                           + (kind == null ? "." : $" whose id contains '{kind}'."));
        }

        private static string RackId(PlaceableStorageEntity rack)
            => rack.ItemInstance?.ID?.ToLowerInvariant() ?? rack.gameObject.name.ToLowerInvariant();

        private static IEnumerator Recheck(PlaceableStorageEntity rack, int index, ItemSlot slot)
        {
            yield return new WaitForSeconds(4f);
            // Unity null: the rack can be picked up or the scene left within the 4 s.
            if (rack == null || slot == null)
            {
                Core.Log.Warning("polyfillrack: the rack is gone 4 s later.");
                yield break;
            }
            Core.Log.Msg($"polyfillrack: 4 s later {Describe(rack, index, slot)}; {Balance()}.");
        }

        private static string Describe(PlaceableStorageEntity rack, int index, ItemSlot slot)
        {
            string item = slot.ItemInstance == null ? "nothing" : slot.ItemInstance.ID;
            return $"{RackId(rack)} slot {index + 1}: {item} x{slot.Quantity}";
        }

        private static string Balance()
        {
            var money = NetworkSingleton<MoneyManager>.Instance;
            return money == null ? "no MoneyManager" : $"online ${money.onlineBalance:0}, cash ${money.cashBalance:0}";
        }
    }
}
#endif
