using HarmonyLib;
using Il2CppScheduleOne.Property;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Let the game finish setting up Chemical Plant's property instead of throwing halfway through.
    /// </summary>
    /// <remarks>
    /// Chemical Plant builds its plant as a new <c>Property</c> and gives it a <c>ListingPoster</c> that is a
    /// bare GameObject (<c>MakePoint("ListingPoster", ...)</c>). <c>Property.Awake</c> writes the name, price,
    /// parking and employee count into the poster's <c>Title</c>, <c>Price</c>, <c>Parking/Text</c> and
    /// <c>Employee/Text</c> children (Property.cs:906-911 on 0.4.7f12). Those children do not exist, so
    /// <c>Find</c> returns null and the Awake ends in a NullReferenceException on every load. The code is the
    /// same on 0.4.6f13, so this is not an update break.
    ///
    /// What the exception skips: hiding the map marker, hooking the switch and toggle lists (both empty here)
    /// and registering the property with the save manager. Chemical Plant registers it again itself, and
    /// the marker it shows is the one players use to find the plant. So the prefix treats a poster without a
    /// <c>Title</c> as no poster, and the postfix puts the poster and the marker back as they were. The plant
    /// ends up the way it does today, minus the exception, and the registration the game does is a no-op
    /// for Chemical Plant's own later call (<c>SaveManager.RegisterSaveable</c> checks for it first).
    /// </remarks>
    internal sealed class ChemicalPlantListingPoster : Fix
    {
        internal override string Id => "chemicalplant-listing-poster";
        internal override string Mod => "ChemicalPlant";
        internal override string ModVersions => "1.4.0";
        internal override string GameVersions => "*";

        internal override string What
            => "Chemical Plant's property finishes its setup without an error on every load";

        internal override string StandsDownBecause
            => "Chemical Plant gives its property an empty listing poster, and the game throws a "
             + "NullReferenceException in Property.Awake on every load.";

        private static MelonLogger.Instance _log;
        private static bool _said;

        private sealed class Held
        {
            internal Transform Poster;
            internal bool MarkerShown;
        }

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var target = AccessTools.Method(typeof(Property), nameof(Property.Awake));
            if (target == null)
            {
                log.Warning("[fix] chemicalplant-listing-poster: Property.Awake was not found, so Chemical Plant's "
                          + "property keeps throwing on load.");
                return false;
            }
            new HarmonyLib.Harmony("doodesch.polyfill.chemicalplantposter").Patch(target,
                prefix: new HarmonyMethod(typeof(ChemicalPlantListingPoster), nameof(Before)),
                postfix: new HarmonyMethod(typeof(ChemicalPlantListingPoster), nameof(After)));
            return true;
        }

        private static void Before(Property __instance, ref Held __state)
        {
            try
            {
                var poster = __instance.ListingPoster;
                if (poster == null || poster.Find("Title") != null) return;

                var marker = __instance.PoI;
                __state = new Held { Poster = poster, MarkerShown = marker != null && marker.gameObject.activeSelf };
                __instance.ListingPoster = null;
            }
            catch (System.Exception e)
            {
                _log?.Error($"[fix] chemicalplant-listing-poster: could not check the listing poster of "
                          + $"{SafeName(__instance)}, the game sets it up as it is: {e.Message}");
            }
        }

        private static void After(Property __instance, Held __state)
        {
            if (__state == null) return;
            try
            {
                __instance.ListingPoster = __state.Poster;
                var marker = __instance.PoI;
                if (marker != null) marker.gameObject.SetActive(__state.MarkerShown);
                if (!_said)
                {
                    _said = true;
                    _log?.Msg($"[fix] chemicalplant-listing-poster: {SafeName(__instance)} has an empty listing poster; "
                            + "it was set up without it, so the load no longer throws.");
                }
            }
            catch (System.Exception e)
            {
                _log?.Error($"[fix] chemicalplant-listing-poster: could not put the poster and map marker of "
                          + $"{SafeName(__instance)} back: {e.Message}");
            }
        }

        private static string SafeName(Property property)
        {
            try { return property.propertyCode ?? "a property"; }
            catch { return "a property"; }
        }
    }
}
