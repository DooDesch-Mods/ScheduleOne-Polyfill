using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Stop Cash Stack Limit from dropping the frame rate for a second and a half after every cash move.
    /// </summary>
    /// <remarks>
    /// After each cash move Cash Stack Limit 1.0.29 redraws the slots it touched, every frame for 1.5 s
    /// (<c>RequestSlotUIRefresh</c> and <c>EnsureForcedSlotRefresh</c>). Each redraw looks the slot's UI up
    /// with <c>Resources.FindObjectsOfTypeAll&lt;ItemSlotUI&gt;()</c>, which walks every loaded object: about
    /// 55 ms for 109 slot UIs on 0.4.7f12. The frame time goes from 14 ms to about 60 ms for that time.
    ///
    /// The game already knows which UI shows which slot: <c>ItemSlotUI.AssignSlot</c> and <c>ClearSlot</c>
    /// are the only places that change it. This fix keeps that link in a table and answers the mod's lookup
    /// from it, so the redraw itself stays and only the search goes. UIs assigned before the table existed
    /// are picked up by one full search, the first time the mod asks.
    /// </remarks>
    internal sealed class CashStackLimitSlotRefresh : Fix
    {
        internal override string Id => "cashstacklimit-slot-refresh";
        internal override string Mod => "Cash Stack Limit";
        internal override string ModVersions => "1.0.29";
        internal override string GameVersions => "*";

        internal override bool NeedsAScreen => true;

        internal override string What
            => "moving cash no longer drops the frame rate for a second and a half";

        internal override string StandsDownBecause
            => "Cash Stack Limit searches every loaded object for slot UIs on each frame for 1.5 s after a "
             + "cash move, which drops the frame rate to about a quarter.";

        private const string MainType = "Schedule1CashStackLimit.Main";

        private static MelonLogger.Instance _log;
        private static readonly Dictionary<IntPtr, List<ItemSlotUI>> Shown = new Dictionary<IntPtr, List<ItemSlotUI>>();
        private static bool _seeded;
        private static int _served;
        private static bool _warned;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            Type main = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                main = assembly.GetType(MainType, false);
                if (main != null) break;
            }
            var refresh = main == null ? null : AccessTools.Method(main, "RefreshSlotUIForSlot", new[] { typeof(ItemSlot) });
            if (refresh == null)
            {
                log.Warning("[fix] cashstacklimit-slot-refresh: Cash Stack Limit's slot redraw was not found, so "
                          + "moving cash keeps dropping the frame rate.");
                return false;
            }

            var assign = AccessTools.Method(typeof(ItemSlotUI), nameof(ItemSlotUI.AssignSlot));
            var clear = AccessTools.Method(typeof(ItemSlotUI), nameof(ItemSlotUI.ClearSlot));
            if (assign == null || clear == null)
            {
                log.Warning("[fix] cashstacklimit-slot-refresh: ItemSlotUI.AssignSlot or ClearSlot was not found, "
                          + "so moving cash keeps dropping the frame rate.");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.cashstackrefresh");
            harmony.Patch(assign, postfix: new HarmonyMethod(typeof(CashStackLimitSlotRefresh), nameof(Assigned)));
            harmony.Patch(clear, prefix: new HarmonyMethod(typeof(CashStackLimitSlotRefresh), nameof(Clearing)));
            harmony.Patch(refresh, prefix: new HarmonyMethod(typeof(CashStackLimitSlotRefresh), nameof(Refresh)));
            return true;
        }

        private static void Assigned(ItemSlotUI __instance)
        {
            try
            {
                var slot = __instance.assignedSlot;
                if (slot != null) Add(slot.Pointer, __instance);
            }
            catch (Exception e) { WarnOnce("could not note a slot UI: " + e.Message); }
        }

        private static void Clearing(ItemSlotUI __instance)
        {
            try
            {
                var slot = __instance.assignedSlot;
                if (slot != null && Shown.TryGetValue(slot.Pointer, out var list))
                    list.RemoveAll(ui => ui == null || ui.Pointer == __instance.Pointer);
            }
            catch (Exception e) { WarnOnce("could not forget a slot UI: " + e.Message); }
        }

        private static void Add(IntPtr slot, ItemSlotUI ui)
        {
            if (!Shown.TryGetValue(slot, out var list)) Shown[slot] = list = new List<ItemSlotUI>();
            foreach (var known in list)
                if (known != null && known.Pointer == ui.Pointer) return;
            list.Add(ui);
        }

        private static void Seed()
        {
            _seeded = true;
            int found = 0;
            foreach (var ui in Resources.FindObjectsOfTypeAll<ItemSlotUI>())
            {
                if (ui == null) continue;
                var slot = ui.assignedSlot;
                if (slot == null) continue;
                Add(slot.Pointer, ui);
                found++;
            }
            _log?.Msg($"[fix] cashstacklimit-slot-refresh: found {found} slot UI(s) once; later cash moves look "
                    + "them up without a search.");
        }

        /// <summary>The same redraw as Cash Stack Limit's RefreshSlotUIForSlot, without the search.</summary>
        private static bool Refresh(ItemSlot slot)
        {
            if (slot == null) return false;
            try
            {
                if (!_seeded) Seed();
                if (!Shown.TryGetValue(slot.Pointer, out var list)) return false;

                list.RemoveAll(ui => ui == null || ui.assignedSlot == null || ui.assignedSlot.Pointer != slot.Pointer);
                foreach (var ui in list)
                {
                    ui.SetVisible(true);
                    ui.UpdateUI();
                    var cash = slot.ItemInstance?.TryCast<CashInstance>();
                    var cashUi = ui.ItemUI == null ? null : ui.ItemUI.TryCast<ItemUI_Cash>();
                    if (cash != null && cashUi != null) cashUi.SetDisplayedBalance(cash.Balance);
                }

                _served++;
                if (_served == 1 || _served % 500 == 0)
                    _log?.Msg($"[fix] cashstacklimit-slot-refresh: redrew a cash slot without a search ({_served} so far).");
                return false;
            }
            catch (Exception e)
            {
                WarnOnce("the redraw failed, Cash Stack Limit searches for the slot itself: " + e.Message);
                return true;
            }
        }

        private static void WarnOnce(string why)
        {
            if (_warned) return;
            _warned = true;
            _log?.Warning("[fix] cashstacklimit-slot-refresh: " + why);
        }
    }
}
