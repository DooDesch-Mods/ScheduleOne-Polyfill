using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Smart Fill counts what it placed again, instead of stopping on bookkeeping the game removed.
    /// </summary>
    /// <remarks>
    /// OverTheCounter's Smart Fill puts a copy of a stack into a customer slot and then tells the handover
    /// screen the copy came from the player:
    ///
    /// <code>
    /// ItemInstance copy = itemInstance.GetCopy(amount);
    /// slot.SetStoredItem(copy, false);          // the item is in the slot
    /// handover.TrackItemAsPlayer(copy);         // throws
    /// placed += amount;                         // never runs
    /// </code>
    ///
    /// TrackItemAsPlayer reaches HandoverScreen.OriginalItemLocations, a dictionary 0.4.6 deleted along
    /// with the nested EItemSource enum it was keyed by. The call throws, an empty catch around the block
    /// swallows it, and the count is never raised - so the caller believes nothing was placed and takes
    /// nothing off the source stack. The item is in the handover AND still in the hotbar. A player sees
    /// Smart Fill fail, or "slots full", and ends up with more than they had.
    ///
    /// NOTHING IS LOST BY REMOVING IT. The dictionary was write-only even in 0.4.5f2: the game added to it
    /// (HandoverScreen.cs:463), guarded with ContainsKey (:457) and cleared it (:500), and never read a
    /// stored value to act on. 0.4.6 returns cancelled items to the player's inventory directly
    /// (HandoverScreen.cs:349), which is what the bookkeeping was for.
    ///
    /// WHY NO BRIDGE. The getter's type is Dictionary&lt;ItemInstance, HandoverScreen/EItemSource&gt;, a
    /// native closed generic over a nested enum that no longer exists. A copied enum has its own identity
    /// and cannot stand in inside a signature; null still throws at the indexer; and a managed dictionary
    /// is not the IL2CPP one the call expects. There is nothing honest to hand back, so the call goes
    /// instead of the value.
    ///
    /// THE CALL, NOT ITS ARGUMENTS. Replacing it with two pops consumes exactly what it would have
    /// consumed and leaves the stack as it was. Deleting the instructions that pushed them would mean
    /// deciding which those were, and the answer differs with every build of the mod.
    /// </remarks>
    internal sealed class OverTheCounterSmartFill : Fix
    {
        internal override string Id => "otc-smart-fill-tracking";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "Smart Fill counts the items it puts in a handover, so the same stack is not left behind "
             + "in your hotbar as well";

        internal override string StandsDownBecause
            => "Smart Fill tells the handover screen where a copied item came from, and 0.4.6 removed the "
             + "list it wrote to - so the placing throws half-way, the count stays at zero and the source "
             + "stack is never taken.";

        private static MelonLogger.Instance _log;
        private static MethodInfo _obsolete;
        private static int _removed;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            _obsolete = AccessTools.Method("OverTheCounter.PrivateAccess:TrackItemAsPlayer");
            var target = AccessTools.Method("OverTheCounter.UI.HandoverFillUI:TryPlaceInCustomerSlots");

            if (_obsolete == null || target == null)
            {
                log.Msg("[fix] otc-smart-fill-tracking: OverTheCounter's Smart Fill is not here in the "
                      + "shape this reads, so nothing was changed.");
                return false;
            }

            _removed = 0;
            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target,
                    transpiler: new HarmonyMethod(typeof(OverTheCounterSmartFill), nameof(Without)));
            }
            catch (Exception e)
            {
                log.Warning("[fix] otc-smart-fill-tracking: could not rewrite Smart Fill, so it still "
                          + "stops half-way: " + e.Message);
                return false;
            }

            if (_removed != 1)
            {
                // ONE CALL, NOT "AT LEAST ONE". None means the rewrite matched nothing and Smart Fill is
                // unchanged while this reports a repair. More than one means the method is not the one
                // this was read against, and removing every match would be a guess.
                log.Warning($"[fix] otc-smart-fill-tracking: TryPlaceInCustomerSlots calls "
                          + $"TrackItemAsPlayer {_removed} time(s) where this expected exactly one, so "
                          + "Smart Fill was left as it was.");
                return false;
            }

            log.Msg("[fix] otc-smart-fill-tracking: Smart Fill no longer writes to the handover list 0.4.6 "
                  + "removed, so it counts what it placed and takes it off your stack.");
            return true;
        }

        /// <summary>Drop the call, keeping the stack exactly as it was.</summary>
        private static IEnumerable<CodeInstruction> Without(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                if (_obsolete == null || !instruction.Calls(_obsolete)) { yield return instruction; continue; }

                _removed++;

                // Its two arguments are already on the stack and it returns nothing, so two pops leave
                // exactly what the next instruction expects. Labels and exception blocks move to the
                // first of them, or a branch or a try that pointed at the call would point at nothing.
                var first = new CodeInstruction(OpCodes.Pop)
                    .WithLabels(instruction.labels)
                    .WithBlocks(instruction.blocks);
                yield return first;
                yield return new CodeInstruction(OpCodes.Pop);
            }
        }
    }
}
