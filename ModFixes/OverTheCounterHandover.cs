using HarmonyLib;
using Il2CppScheduleOne.Economy;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod's own customer can be handed goods again.
    /// </summary>
    /// <remarks>
    /// OverTheCounter builds a SYNTHETIC <c>Customer</c> component for Bella - enough for a contract, and
    /// no longer enough for the offer screen. 0.4.6's detail panel reads the customer's data straight out,
    /// with no check:
    /// <code>
    /// // ScheduleOne.UI.Handover/HandoverScreenDetailPanel.cs:47-60
    /// StandardsStar.color = ItemQuality.GetColor(customer.CustomerData.Standards.GetCorrespondingQuality());
    /// for (int i = 0; i &lt; customer.CustomerData.PreferredProperties.Count; i++)
    /// </code>
    /// A synthetic customer has no <c>CustomerData</c>, so building the panel throws and it never fills in.
    /// Reported as "no options appear as a choice, even though I obviously have a weed mix worth over $105"
    /// - the offer is there, the screen that should show it is not.
    ///
    /// So a customer that HAS data stands in, for the screen only. That is not this project's idea: the
    /// mod's own community repatch does exactly this and says why -
    /// <code>
    /// // Borrow a fully initialized vanilla customer only as the screen's presentation proxy;
    /// // our callback still performs Bella's actual drug/value validation and quest progression.
    /// </code>
    /// The trade is real and worth naming: the screen shows the proxy's preferences rather than Bella's,
    /// and the handover itself is judged by the mod exactly as before, because the callback the mod passed
    /// in is untouched. A panel showing the wrong likes beats a panel that never opens.
    ///
    /// Only ever fills in a customer that cannot work. One with data is left alone.
    /// </remarks>
    internal sealed class OverTheCounterHandover : Fix
    {
        internal override string Id => "otc-handover-customer";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";
        internal override string What => "handing goods to the mod's own customer opens the offer screen again";

        internal override string StandsDownBecause
            => "Bella's handover screen will open with no options in it, whatever you are carrying.";

        private static MelonLogger.Instance _log;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var open = AccessTools.Method(typeof(Il2CppScheduleOne.UI.Handover.HandoverScreen), "Open");
            if (open == null)
            { log.Warning("[fix] otc-handover-customer: HandoverScreen.Open is not where it was."); return false; }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                open, prefix: new HarmonyMethod(typeof(OverTheCounterHandover), nameof(OpenPrefix)));
            return true;
        }

        /// <summary>Swap in a customer the panel can read, and only then.</summary>
        private static void OpenPrefix(ref Customer customer)
        {
            try
            {
                if (customer != null && customer.CustomerData != null) return;

                var stand = WithData(customer);
                if (stand == null)
                {
                    if (!_said)
                    {
                        _said = true;
                        _log?.Warning("[fix] otc-handover-customer: this save has no customer with data to "
                                    + "show the offer screen with, so it stays empty.");
                    }
                    return;
                }

                customer = stand;
                if (_said) return;
                _said = true;
                _log?.Msg("[fix] otc-handover-customer: the offer screen was handed a customer with no data "
                        + "and could not draw itself. Showing another customer's preferences instead - what "
                        + "you hand over is still judged by the mod.");
            }
            catch (Exception e) { _log?.Warning("[fix] otc-handover-customer: " + e.Message); }
        }

        /// <summary>
        /// Any customer the panel can actually read.
        /// </summary>
        /// <remarks>
        /// Unlocked first, because one the player has met is the least surprising thing to see on that
        /// screen. Locked ones count too - the screen only reads their data, and never reaches them.
        /// </remarks>
        private static Customer WithData(Customer avoid)
        {
            foreach (var list in new[] { Customer.UnlockedCustomers, Customer.LockedCustomers })
            {
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var candidate = list[i];
                    try
                    {
                        if (candidate == null || candidate == avoid) continue;
                        if (candidate.CustomerData != null) return candidate;
                    }
                    catch { }
                }
            }
            return null;
        }
    }
}
