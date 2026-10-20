using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>The deal completion popup shows again, and still hides its title and payment during a robbery.</summary>
    /// <remarks>
    /// Section 97's prefix on <c>DealCompletionPopup.PlayPopup</c> reaches the title and the payment line through
    /// the path <c>Container/Title</c> and <c>Container/Payment</c> under the popup. 0.4.7 moved the standard deal
    /// view under its own animated object (<c>DealCompletionPopup.StandardDealAnimation</c>), so the first
    /// <c>Find</c> returns null, the prefix throws on every sale, and the popup never plays.
    ///
    /// The mod's prefix is taken off and replaced by one that reaches the same two objects through the popup's
    /// own <c>Title</c> and <c>PaymentLabel</c> fields, which do not depend on where they sit in the hierarchy.
    /// </remarks>
    internal sealed class Section97DealPopup : Fix
    {
        internal override string Id => "section97-deal-popup";
        internal override string Mod => "Section97";
        internal override string ModVersions => "*";
        internal override string GameVersions => "0.4.7*";
        internal override string What => "the deal completion popup shows again, and robberies still hide its title and payment";
        internal override string StandsDownBecause => "Section 97 finds the popup's title and payment by a path 0.4.7 moved";

        private static FieldInfo _robbing;
        private static MelonLogger.Instance _log;
        private static int _failures;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            var patch = AccessTools.TypeByName("Section97.Sec97+RobberySatisfactionPatch");
            var old = patch == null ? null : AccessTools.Method(patch, "Prefix");
            var target = AccessTools.DeclaredMethod(typeof(DealCompletionPopup), nameof(DealCompletionPopup.PlayPopup));
            _robbing = AccessTools.Field(AccessTools.TypeByName("Section97.Sec97"), "Robbing");
            if (old == null || _robbing == null)
            {
                log.Warning("[fix] section97-deal-popup: Section 97's popup prefix or its Robbing flag is not where it was; "
                          + "the popup was left as the mod patches it.");
                return false;
            }
            if (target == null)
            {
                log.Warning("[fix] section97-deal-popup: DealCompletionPopup.PlayPopup is missing; the popup was left as the "
                          + "mod patches it.");
                return false;
            }
            if (HarmonyLib.Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.PatchMethod == old) != true)
            {
                log.Warning("[fix] section97-deal-popup: Section 97's popup prefix is not registered; nothing to replace.");
                return false;
            }

            _log = log;
            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Unpatch(target, old);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(Section97DealPopup), nameof(Before)));
            if (HarmonyLib.Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.PatchMethod == old) == true)
                throw new InvalidOperationException("Section 97's popup prefix is still registered");
            return true;
        }

        private static void Before(DealCompletionPopup __instance)
        {
            try
            {
                var popup = __instance ?? Singleton<DealCompletionPopup>.Instance;
                if (popup == null)
                {
                    _log.Warning("[fix] section97-deal-popup: no deal popup to adjust; it plays as the game builds it.");
                    return;
                }
                bool show = !(bool)_robbing.GetValue(null);
                if (popup.Title != null) popup.Title.gameObject.SetActive(show);
                else _log.Warning("[fix] section97-deal-popup: the popup has no title; only the payment was adjusted.");
                if (popup.PaymentLabel != null)
                {
                    popup.PaymentLabel.gameObject.SetActive(show);
                    // The mod hid the whole payment row, which holds the label and its caption.
                    var row = popup.PaymentLabel.transform.parent;
                    if (row != null && row.name == "Payment") row.gameObject.SetActive(show);
                }
                else _log.Warning("[fix] section97-deal-popup: the popup has no payment label; only the title was adjusted.");

                if (!_said)
                {
                    _said = true;
                    _log.Msg($"[fix] section97-deal-popup: deal popup {(show ? "shows its title and payment" : "hides its title and payment for a robbery")}.");
                }
            }
            catch (Exception e)
            {
                _failures++;
                if (_failures == 1 || _failures % 20 == 0)
                    _log.Error($"[fix] section97-deal-popup: adjusting the popup failed ({_failures}): {e}; it plays unchanged.");
                Fixes.Record("section97-deal-popup", "popup adjust failed: " + e.GetBaseException().Message);
            }
        }
    }
}
