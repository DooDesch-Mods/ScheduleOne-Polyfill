using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A patch on the counteroffer screen's price change runs again, from the box that took the job over.
    /// </summary>
    /// <remarks>
    /// 0.4.6 moved the price out of <c>CounterofferInterface</c> and into the <c>AmountSelector</c> the
    /// screen already pointed at. Polyfill puts <c>ChangePrice</c> back and forwards it, so a mod CALLING it
    /// works - but nothing calls it any more. The ramp on the box subscribes to
    /// <c>AmountSelector.ChangeAmount</c> directly (AmountSelector.cs:31), so a mod that PATCHED
    /// ChangePrice never fires, and its screen quietly stops updating.
    ///
    /// Moving the patch is not available here, the way it is for a method that only grew an argument. The
    /// patch takes <c>CounterofferInterface __instance</c> and the method that replaced it is on
    /// AmountSelector, which cannot supply one. So the patch stays where it is and is invoked from a
    /// postfix of Polyfill's own, with the counteroffer screen looked up and handed in.
    ///
    /// WHICH SCREEN, AND ONLY THEN. There is one AmountSelector per amount box in the game, and every one
    /// of them calls ChangeAmount. The counteroffer is reached through the phone
    /// (MessagesApp.cs:72 holds it, CounterofferInterface.cs:47 holds the selector), and the relay runs only
    /// when that screen's own PriceSelector IS the instance that just changed. Anything else is a different
    /// box and gets nothing - a mod that watched the counteroffer price must not be woken by a cash amount.
    ///
    /// AND ONLY ChangeAmount. Not SetAmount and not the change event: the old ChangePrice ran when somebody
    /// moved the price, not when it was typed in or set up, and a repair that fires more often than the
    /// original is a behaviour change wearing a fix's clothes.
    /// </remarks>
    internal sealed class PatchesOnMovedPriceBox : Fix
    {
        internal override string Id => "patches-on-moved-price-box";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a mod watching the counteroffer price sees it move again, from the box that now owns it";

        internal override string StandsDownBecause
            => "CounterofferInterface.ChangePrice is nothing the game calls any more, so a patch on it "
             + "never runs and whatever the mod showed beside the price stops following it.";

        private static readonly List<MethodInfo> Relayed = new();
        private static readonly List<string> _owners = new();
        private static MelonLogger.Instance _log;
        private static PropertyInfo _priceSelector;
        private static MethodInfo _counteroffer;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var standIn = AccessTools.TypeByName("Il2CppScheduleOne.UI.Phone.CounterofferInterface");
            var selector = AccessTools.TypeByName("Il2CppScheduleOne.UI.AmountSelector");
            if (standIn == null || selector == null)
            {
                log.Warning("[fix] patches-on-moved-price-box: the counteroffer screen or the amount box is "
                          + "not on this build, so nothing was relayed.");
                return false;
            }

            var oldMethod = AccessTools.Method(standIn, "ChangePrice", new[] { typeof(float) });
            if (oldMethod == null) return false;             // nothing put it back, so nobody patched it

            HarmonyLib.Patches info;
            try { info = HarmonyLib.Harmony.GetPatchInfo(oldMethod); }
            catch (Exception e)
            {
                log.Warning("[fix] patches-on-moved-price-box: could not read the patches on ChangePrice: "
                          + e.Message);
                return false;
            }

            if (info?.Postfixes == null) return false;

            foreach (var patch in info.Postfixes)
            {
                if (patch.owner != null
                    && patch.owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) continue;
                if (!Callable(patch.PatchMethod, log)) continue;
                Relayed.Add(patch.PatchMethod);
                _owners.Add(patch.PatchMethod?.DeclaringType?.Assembly?.GetName()?.Name);
            }

            if (Relayed.Count == 0) return false;

            _priceSelector = AccessTools.Property(standIn, "PriceSelector");
            _counteroffer = Owner();
            if (_priceSelector == null || _counteroffer == null)
            {
                Relayed.Clear();
                log.Warning("[fix] patches-on-moved-price-box: the way from the phone to the counteroffer "
                          + "screen is not on this build, so the patch was left where it is rather than "
                          + "run against the wrong box.");
                return false;
            }

            var target = AccessTools.Method(selector, "ChangeAmount", new[] { typeof(float) });
            if (target == null)
            {
                Relayed.Clear();
                log.Warning("[fix] patches-on-moved-price-box: AmountSelector.ChangeAmount(float) is not "
                          + "here, so there is nothing to relay from.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, postfix: new HarmonyMethod(typeof(PatchesOnMovedPriceBox), nameof(Moved)));

            // SAID TO THE REPORT, which was written before this ran and still calls the patch dead. The
            // OLD name, because that is the one the finding carries.
            foreach (string owner in _owners)
                if (!string.IsNullOrEmpty(owner))
                    Fixes.Repaired.Add(owner + "|Il2CppScheduleOne.UI.Phone.CounterofferInterface::ChangePrice");

            log.Msg($"[fix] patches-on-moved-price-box: {Relayed.Count} patch(es) on the counteroffer "
                  + "price will run when the box that owns it now changes.");
            return true;
        }

        /// <summary>The counteroffer screen the phone is holding, or null when it is not up.</summary>
        private static MethodInfo Owner()
        {
            var app = AccessTools.TypeByName("Il2CppScheduleOne.UI.Phone.Messages.MessagesApp");
            if (app == null) return null;

            var instance = AccessTools.PropertyGetter(app, "Instance")
                        ?? AccessTools.Method(app, "get_Instance");
            return instance != null && instance.IsStatic ? instance : null;
        }

        /// <summary>Run the relayed patches, but only for the box the counteroffer screen owns.</summary>
        private static void Moved(object __instance, float change)
        {
            if (Relayed.Count == 0 || __instance == null) return;

            try
            {
                object app = _counteroffer.Invoke(null, null);
                if (app == null) return;                     // the phone is not up, so no counteroffer is

                object screen = AccessTools.Property(app.GetType(), "CounterofferInterface")?.GetValue(app);
                if (screen == null) return;

                object box = _priceSelector.GetValue(screen);
                if (box == null || !Same(box, __instance)) return;

                foreach (var patch in Relayed) Run(patch, screen, change);
            }
            catch (Exception e)
            {
                // Reading the phone must never be the reason a price change throws. The mod misses one
                // update, which is where it was without this fix at all.
                Complain(e.GetType().Name + ": " + (e.InnerException ?? e).Message);
            }
        }

        /// <summary>
        /// The same object, compared the way interop objects have to be.
        /// </summary>
        /// <remarks>
        /// Two managed wrappers around one native pointer are different objects to <c>ReferenceEquals</c>
        /// and the same thing to the game, so identity is asked of the pointer. Falling back to reference
        /// equality keeps this working if the property is ever gone.
        /// </remarks>
        private static bool Same(object left, object right)
        {
            var pointer = AccessTools.Property(left.GetType(), "Pointer");
            if (pointer == null) return ReferenceEquals(left, right);

            var other = AccessTools.Property(right.GetType(), "Pointer");
            if (other == null) return ReferenceEquals(left, right);

            return Equals(pointer.GetValue(left), other.GetValue(right));
        }

        private static void Run(MethodInfo patch, object screen, float change)
        {
            try
            {
                var wanted = patch.GetParameters();
                var arguments = new object[wanted.Length];
                for (int i = 0; i < wanted.Length; i++)
                    // THE REAL DELTA WHERE THE OLD METHOD TOOK ONE. ChangePrice(float change) and
                    // ChangeAmount(float change) are the same number under the same name, and filling it
                    // with a default instead would run the mod's code against a price move of zero -
                    // which looks like a repair and is a lie about what the player did.
                    arguments[i] = wanted[i].Name == "__instance" ? screen
                        : wanted[i].ParameterType == typeof(float) && wanted[i].Name == "change" ? change
                        : Activator.CreateInstance(wanted[i].ParameterType);

                patch.Invoke(null, arguments);
            }
            catch (Exception e)
            {
                Complain($"{patch.DeclaringType?.Name}.{patch.Name} threw: "
                       + (e.InnerException ?? e).Message);
            }
        }

        /// <summary>
        /// Only parameters this can fill, and it says which one it could not.
        /// </summary>
        /// <remarks>
        /// A postfix asking for __result or a captured argument cannot be answered from here - the real
        /// method has different ones - and inventing a default would run the mod's code against a number
        /// nobody chose. Same rule as patches-on-split-methods, and the same reason.
        /// </remarks>
        private static bool Callable(MethodInfo patch, MelonLogger.Instance log)
        {
            if (patch == null || !patch.IsStatic) return false;

            foreach (var parameter in patch.GetParameters())
            {
                if (parameter.Name == "__instance") continue;
                if (parameter.ParameterType.IsValueType) continue;

                log.Warning($"[fix] patches-on-moved-price-box: {patch.DeclaringType?.Name}.{patch.Name} "
                          + $"takes '{parameter.Name}', which the box cannot fill. Left alone.");
                return false;
            }
            return true;
        }

        private static readonly HashSet<string> Complained = new(StringComparer.Ordinal);

        private static void Complain(string why)
        {
            if (!Complained.Add(why)) return;
            _log?.Warning("[fix] patches-on-moved-price-box: " + why);
            Fixes.Record("patches-on-moved-price-box", "stood aside: " + why);
        }
    }
}
