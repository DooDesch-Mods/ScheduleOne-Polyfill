using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// The amount box tells its listeners again when a mod replaces the method that used to.
    /// </summary>
    /// <remarks>
    /// THE LAST THING TWEAKABLES LOST, and the one that could not be given back the way it was asked for.
    ///
    /// 0.4.5f2's HandoverScreenPriceSelector carried <c>public UnityEvent onPriceChanged</c> - a field,
    /// so the interop assembly exposed <c>get_onPriceChanged()</c>. 0.4.6's AmountSelector has
    /// <c>public event Action&lt;float&gt; OnAmountChanged</c> instead, raised at the end of SetAmount.
    /// Tweakables' prefix replaces SetAmount when the deal cap is raised, and ends with
    /// <c>onPriceChanged.Invoke()</c> to say the value moved.
    ///
    /// HANDING BACK A UnityEvent IS NOT POSSIBLE HERE, and not for lack of trying:
    ///
    ///  - It would have to be the SAME object every time it is asked for on a given selector, or a mod
    ///    that adds a listener loses it on the next call. Interop wrappers are not that: the pool holds
    ///    weak references and rebuilds a wrapper on a miss, so a managed field on one wrapper is not
    ///    there on the next - which this project already learned once (Set.cs:2462).
    ///  - <c>UnityEvent.Invoke()</c> is not virtual on this build, so a derived event cannot intercept
    ///    the call a mod already compiled.
    ///  - Returning null would make the mod's null check skip the notification: no crash, no message, and
    ///    nobody able to tell. That is the one outcome this project refuses outright.
    ///
    /// So the notification is restored where it is lost rather than where it was asked for. Harmony runs
    /// postfixes even when a prefix returns false, and <c>__runOriginal</c> says which happened - so this
    /// raises the game's own event exactly when something replaced the body that would have raised it,
    /// and stays out of the way when the game did its own work.
    ///
    /// It raises OnAmountChanged directly rather than calling SetAmount again: the interop assembly
    /// exposes the event's backing field as a plain property (Il2Cpp AmountSelector.cs:132), and calling
    /// SetAmount would re-apply MinValue and MaxValue - which is the very clamp the mod is overriding.
    ///
    /// NAMED, NOT GENERAL. A mod that deliberately suppresses the notification would get one it did not
    /// want, so this is gated to the mod whose loss was measured.
    /// </remarks>
    internal sealed class AmountChangedAfterOverride : Fix
    {
        internal override string Id => "amount-changed-after-override";
        internal override string Mod => "Tweakables";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "raising the deal cap tells the rest of the screen the price moved, so it does not show a "
             + "stale number";

        internal override string StandsDownBecause
            => "the field this mod used to announce a price change with does not exist in 0.4.6, and the "
             + "event that replaced it is only raised by the method the mod replaces.";

        private static MelonLogger.Instance _log;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var type = AccessTools.TypeByName("Il2CppScheduleOne.UI.AmountSelector");
            if (type == null)
            {
                log.Warning("[fix] amount-changed-after-override: Il2CppScheduleOne.UI.AmountSelector is "
                          + "not on this build, so there is nothing to listen to.");
                return false;
            }

            var target = AccessTools.Method(type, "SetAmount", new[] { typeof(float) });
            if (target == null)
            {
                log.Warning("[fix] amount-changed-after-override: AmountSelector.SetAmount(float) is not "
                          + "here, so the moment the notification goes missing cannot be found.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, postfix: new HarmonyMethod(typeof(AmountChangedAfterOverride), nameof(After)));
            return true;
        }

        /// <summary>Only when somebody replaced the body that would have raised it.</summary>
        private static void After(object __instance, bool __runOriginal)
        {
            if (__runOriginal || __instance == null) return;      // the game announced it itself

            try
            {
                var type = __instance.GetType();
                var changed = AccessTools.PropertyGetter(type, "OnAmountChanged")?.Invoke(__instance, null);
                if (changed == null) return;                      // nobody is listening; nothing to say

                var amount = AccessTools.PropertyGetter(type, "SelectedAmount")?.Invoke(__instance, null);
                if (amount == null)
                {
                    Complain("the selector has no SelectedAmount to announce");
                    return;
                }

                var invoke = AccessTools.Method(changed.GetType(), "Invoke", new[] { typeof(float) });
                if (invoke == null)
                {
                    Complain("the event on this build takes something other than a single float, so what "
                           + "to pass it cannot be worked out without guessing");
                    return;
                }

                invoke.Invoke(changed, new[] { amount });

                if (!_said)
                {
                    _said = true;
                    _log?.Msg("[fix] amount-changed-after-override: a mod replaced the amount box's own "
                            + "setter, so Polyfill raises the change event it would have raised.");
                }
            }
            catch (Exception e)
            {
                Complain("could not raise the change event: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static readonly HashSet<string> Complained = new();

        private static void Complain(string why)
        {
            if (!Complained.Add(why)) return;
            _log?.Warning("[fix] amount-changed-after-override: " + why + ". Whatever listens for a price "
                        + "change will not hear this one.");
            Fixes.Record("amount-changed-after-override", "did nothing: " + why);
        }
    }
}
