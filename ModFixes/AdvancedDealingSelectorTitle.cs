using System.Reflection.Emit;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>AdvancedDealing's dead-drop pickers in the Dealers app build again.</summary>
    /// <remarks>
    /// AdvancedDealing copies the game's customer picker (DealerManagementApp.CustomerSelector) for its
    /// cash and product dead-drop pickers and looks up the title as "Shade/Content/Title". The game keeps
    /// the title one level deeper, under "Shade/Content/Top" next to the Cancel button, on 0.4.6f12 and
    /// 0.4.7f12 alike (Player.prefab, DealerManagement/Container/CustomerSelector). Transform.Find answers
    /// null, the next line throws, and DeadDropSelector.BuildUI stops before it adds the picker's button
    /// to the dealer page, so neither dead drop can be chosen.
    ///
    /// The transpiler swaps that one path for the one the game has. Everything else in BuildUI is the
    /// mod's own code and runs as written.
    /// </remarks>
    internal sealed class AdvancedDealingSelectorTitle : Fix
    {
        private const string FixId = "advanceddealing-selector-title";
        private const string OldPath = "Shade/Content/Title";
        private const string NewPath = "Shade/Content/Top/Title";

        internal override string Id => FixId;
        internal override string Mod => "AdvancedDealing";
        internal override string ModVersions => "<=1.5.1";
        internal override string GameVersions => ">=0.4.6";
        internal override string What => "AdvancedDealing's cash and product dead-drop pickers appear in the Dealers app again";

        private static MelonLogger.Instance _log;
        private static int _swapped;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var selector = AccessTools.TypeByName("AdvancedDealing.UI.DeadDropSelector");
            var build = selector == null ? null : AccessTools.DeclaredMethod(selector, "BuildUI", Type.EmptyTypes);
            if (build == null)
            {
                log.Warning($"[fix] {FixId}: AdvancedDealing.UI.DeadDropSelector.BuildUI is not in this AdvancedDealing (type {selector != null}); nothing was changed.");
                return false;
            }
            new HarmonyLib.Harmony("doodesch.polyfill.fixes")
                .Patch(build, transpiler: new HarmonyMethod(typeof(AdvancedDealingSelectorTitle), nameof(SwapTitlePath)));
            if (_swapped == 0)
            {
                log.Warning($"[fix] {FixId}: BuildUI no longer looks up \"{OldPath}\"; the transpiler changed nothing.");
                return false;
            }
            log.Msg($"[fix] {FixId}: the dead-drop pickers look up \"{NewPath}\" ({_swapped} place(s)).");
            return true;
        }

        private static IEnumerable<CodeInstruction> SwapTitlePath(IEnumerable<CodeInstruction> code)
        {
            foreach (var op in code)
            {
                if (op.opcode == OpCodes.Ldstr && op.operand as string == OldPath)
                {
                    op.operand = NewPath;
                    _swapped++;
                }
                yield return op;
            }
        }
    }
}
