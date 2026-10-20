using HarmonyLib;
using Il2CppScheduleOne.Core.Weather;
using Il2CppScheduleOne.Weather;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Weather keeps running after a mod removes a building that had its own weather shelter.
    /// </summary>
    /// <remarks>
    /// A WorldEnclosure registers itself with the EnvironmentManager in Start, and nothing ever unregisters
    /// it: there is no UnregisterEnclosure call anywhere in 0.4.7f12. Vanilla never destroys one, so that
    /// never mattered. A mod that removes or replaces a building does, and the dead entry stays in
    /// <c>_weatherEnclosures</c>; one that removes only part of a building leaves a destroyed box in a live
    /// shelter's <c>_enclosures</c>. UpdateVolumes then asks the box whether the player is inside, which
    /// throws on its destroyed transform (EnvironmentManager.cs:541-548, BasicEnclosure.cs:49-58 on
    /// 0.4.7f12), and since that happens in Update before UpdateWeather, the wet surfaces and the wind, all
    /// of those stop for the rest of the session. Every frame also writes the NullReferenceException to the
    /// log once a mod has patched Update.
    ///
    /// A finalizer on Update takes the dead shelters and dead boxes out of both lists the first time the
    /// frame throws, and swallows that one exception when it removed something. A frame that throws for any
    /// other reason is left alone. Nothing is done on a frame that does not throw.
    /// </remarks>
    internal sealed class WeatherLostEnclosures : Fix
    {
        internal override string Id => "weather-lost-enclosures";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What => "weather keeps running after a mod removes a building with its own shelter";

        internal override string StandsDownBecause
            => "a building a mod removes leaves its weather shelter behind, so rain, wet surfaces and wind stop "
             + "and the log fills with a NullReferenceException every frame.";

        private static MelonLogger.Instance _log;
        private static int _removed;
        private static bool _saidOther;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var update = AccessTools.DeclaredMethod(typeof(EnvironmentManager), "Update");
            if (update == null)
            {
                log.Warning("[fix] weather-lost-enclosures: EnvironmentManager.Update is not where it was, so a "
                          + "removed weather shelter still stops the weather.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                update, finalizer: new HarmonyMethod(typeof(WeatherLostEnclosures), nameof(AfterUpdate)));
            return true;
        }

        private static Exception AfterUpdate(Exception __exception, EnvironmentManager __instance)
        {
            if (__exception == null) return null;
            try
            {
                var removed = Prune(__instance);
                if (removed == 0)
                {
                    if (!_saidOther)
                    {
                        _saidOther = true;
                        _log?.Warning("[fix] weather-lost-enclosures: the weather update threw with no removed "
                                    + "shelter in its lists, so the error is not one this repairs: "
                                    + __exception.Message);
                    }
                    return __exception;
                }

                _removed += removed;
                _log?.Warning($"[fix] weather-lost-enclosures: took {removed} removed part(s) of weather shelters "
                            + $"out of the weather update ({_removed} so far); the weather runs again.");
                return null;
            }
            catch (Exception e)
            {
                _log?.Error("[fix] weather-lost-enclosures: could not clear the removed weather shelters: " + e);
                return __exception;
            }
        }

        private static int Prune(EnvironmentManager manager)
        {
            if (manager == null) return 0;
            var removed = 0;

            var weather = manager._weatherEnclosures;
            if (weather != null)
                for (var i = weather.Count - 1; i >= 0; i--)
                {
                    var enclosure = weather[i];
                    if (enclosure == null) { weather.RemoveAt(i); removed++; }
                    else removed += PruneParts(enclosure);
                }

            var overrides = manager._overrideEnclosures;
            if (overrides != null)
                for (var i = overrides.Count - 1; i >= 0; i--)
                {
                    var enclosure = overrides[i];
                    if (enclosure == null) { overrides.RemoveAt(i); removed++; }
                    else removed += PruneParts(enclosure);
                }

            return removed;
        }

        /// <summary>
        /// The boxes a shelter is made of. A mod that removes one wall or room of a building leaves the
        /// shelter itself alive with a destroyed box in it, and that box is what throws.
        /// </summary>
        private static int PruneParts(WorldEnclosure enclosure)
        {
            var parts = enclosure._enclosures;
            if (parts == null) return 0;
            var removed = 0;
            for (var i = parts.Count - 1; i >= 0; i--)
                if (parts[i] == null) { parts.RemoveAt(i); removed++; }
            return removed;
        }
    }
}
