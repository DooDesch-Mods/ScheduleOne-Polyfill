using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Customers added through S1API are written to the save again, so an unlock survives a reload.
    /// </summary>
    /// <remarks>
    /// S1API builds a mod customer from a game prefab whose NPC component is a <c>SpecialCustomer</c>, and
    /// <c>SpecialCustomer.ShouldSave</c> answers false (SpecialCustomer.cs:135-138 on 0.4.7f12). S1API
    /// replaces <c>NPCManager.GetSaveString</c> with its own loop over the registry, and that loop asks
    /// <c>npc.ShouldSave()</c> from managed code (NPCPatches.NPCManager_GetSaveString_Prefix).
    ///
    /// Without Polyfill that call goes through Il2CppInterop's managed shortcut on the patched base method
    /// (S1API patches <c>NPC.ShouldSave</c>), which runs the base NPC logic: unlocked, a changed
    /// relationship, messages, inventory or health (NPC.cs:1326-1345). <see cref="Boot.InteropPatchesStayNative"/>
    /// leaves that shortcut out, so the call now reaches the <c>SpecialCustomer</c> override, every mod
    /// customer answers false, and none of them is written to NPCs.json. The unlock, the relationship and
    /// the customer data are gone after the next load, with no error anywhere.
    ///
    /// The transpiler swaps that one call for <see cref="ShouldSave"/>, which keeps the game's answer and
    /// adds the base logic only for an NPC S1API created itself. Vanilla special customers keep their own
    /// answer; they are saved through SpecialCustomers.json.
    /// </remarks>
    internal sealed class S1ApiCustomCustomersSave : Fix
    {
        internal override string Id => "s1api-custom-customers-save";
        internal override string Mod => "S1API";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "customers added by mods keep their unlock and relationship after a reload";

        internal override string StandsDownBecause
            => "customers added through S1API are not written to the save, so an unlocked one is locked "
             + "again after a reload.";

        private const string PatchesType = "S1API.Internal.Patches.NPCPatches";
        private const string PrefixName = "NPCManager_GetSaveString_Prefix";
        private const string WrapperType = "S1API.Entities.NPC";

        private static MelonLogger.Instance _log;
        private static int _replaced;
        private static FieldInfo _all, _isCustom, _s1Npc;
        private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var patches = AccessTools.TypeByName(PatchesType);
            var target = patches == null ? null : AccessTools.DeclaredMethod(patches, PrefixName);
            if (target == null)
            {
                log.Msg($"[fix] {Id}: this S1API has no {PrefixName}, nothing to do.");
                return false;
            }

            var wrapper = AccessTools.TypeByName(WrapperType);
            _all = wrapper == null ? null : AccessTools.Field(wrapper, "All");
            _isCustom = wrapper == null ? null : AccessTools.Field(wrapper, "IsCustomNPC");
            _s1Npc = wrapper == null ? null : AccessTools.Field(wrapper, "S1NPC");
            if (_all == null || _isCustom == null || _s1Npc == null)
            {
                log.Warning($"[fix] {Id}: S1API's NPC has no All ({_all != null}), IsCustomNPC ({_isCustom != null}) "
                          + $"or S1NPC ({_s1Npc != null}), so customers added by mods are still not saved.");
                return false;
            }

            _replaced = 0;
            new HarmonyLib.Harmony("doodesch.polyfill.s1apicustomersave").Patch(target,
                transpiler: new HarmonyMethod(typeof(S1ApiCustomCustomersSave), nameof(Rewrite)));

            // Exactly one call site was read; none or two means the loop changed upstream.
            if (_replaced != 1)
            {
                log.Warning($"[fix] {Id}: {PrefixName} calls NPC.ShouldSave {_replaced} time(s) where this "
                          + "expected exactly one, so the call was not changed and customers added by mods may "
                          + "not be saved.");
                return false;
            }
            return true;
        }

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.DeclaredMethod(typeof(NPC), nameof(NPC.ShouldSave), Type.EmptyTypes);
            var ours = AccessTools.DeclaredMethod(typeof(S1ApiCustomCustomersSave), nameof(ShouldSave),
                                                  new[] { typeof(NPC) });
            var list = instructions.ToList();
            int found = list.Count(i => original != null && i.Calls(original));
            if (found != 1 || ours == null)
            {
                _replaced = found;
                return list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Calls(original)) continue;
                list[i] = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, ours)
                    .WithLabels(list[i].labels)
                    .WithBlocks(list[i].blocks);
            }
            _replaced = 1;
            return list;
        }

        /// <summary>The game's answer, plus the base NPC answer for an NPC S1API created.</summary>
        private static bool ShouldSave(NPC npc)
        {
            bool game = npc.ShouldSave();
            if (game) return true;

            try
            {
                if (!IsS1ApiCustom(npc)) return false;

                // NPC.ShouldSave on 0.4.7f12 (NPC.cs:1326-1345), without the SpecialCustomer override.
                bool base_ = npc.ShouldSaveRelationshipData() || npc.ShouldSaveMessages()
                          || npc.ShouldSaveInventory() || npc.ShouldSaveHealth() || npc.HasChanged;
                if (base_ && Reported.Add("saved:" + npc.ID))
                    _log?.Msg($"[fix] s1api-custom-customers-save: '{npc.ID}' is written to the save; its game "
                            + $"class ({npc.GetIl2CppType()?.Name}) would have left it out.");
                return base_;
            }
            catch (Exception e)
            {
                if (Reported.Add("error:" + npc?.ID))
                    _log?.Error($"[fix] s1api-custom-customers-save: could not tell whether '{npc?.ID}' should be "
                              + $"saved ({e.GetType().Name}: {e.Message}); it is left out of this save.");
                return false;
            }
        }

        /// <summary>Is this NPC one S1API created for a mod (S1API's NPC.All, IsCustomNPC)?</summary>
        private static bool IsS1ApiCustom(NPC npc)
        {
            if (_all.GetValue(null) is not System.Collections.IEnumerable all) return false;
            foreach (object wrapper in all)
            {
                if (wrapper == null) continue;
                if (_s1Npc.GetValue(wrapper) is not NPC s1 || s1.Pointer != npc.Pointer) continue;
                return _isCustom.GetValue(wrapper) is true;
            }
            return false;
        }
    }
}
