using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Weather;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// GraphicsMOD's lighting toggle finds the sun again, after the scene moved it.
    /// </summary>
    /// <remarks>
    /// THE ONE REPAIR IN HERE THAT IS NOT ABOUT A SIGNATURE, and it is worth saying why it is allowed.
    /// GraphicsMOD is symbol-clean on 0.4.6f13: every type and member it names is still there, so the
    /// report says nothing about it at all. What it does instead is walk a hierarchy by hand:
    /// <code>
    /// GraphicsSettings.OptimitationLights(bool enabled)
    ///     GameObject.Find("Managers/@EnvironmentFX/SkySystemController").transform.Find("Sun")
    ///         .gameObject.SetActive(enabled);
    /// </code>
    /// That path is gone. 0.4.6 keeps the sun under EnvironmentManager and hands its Light to
    /// DayNightController, which holds it as <c>_sunLight</c> (DayNightController.cs:19). So the mod logs
    /// "SkySystemController not found" and its toggle does nothing.
    ///
    /// THE OLD EFFECT IS THE TARGET, not the old code. The mod switched one GameObject - the sun - on and
    /// off, so this switches the sun's GameObject on and off. Moon, ambient light, fog and Volumes are
    /// left alone, which is what the option's body always did whatever its label suggests.
    ///
    /// A HIERARCHY ALIAS WAS THE OBVIOUS ANSWER AND IS THE WRONG ONE. Putting an empty
    /// "Managers/@EnvironmentFX/SkySystemController/Sun" into the scene would keep the broken lookup
    /// alive, need a proxy or a reparented sun, and quietly promise that Polyfill keeps scene paths
    /// stable. It does not, and this repair does not need one: it asks the game's own controller which
    /// Light is the sun.
    ///
    /// It stands down rather than guess. Wrong mod version, wrong game build, a method that is not the
    /// one-argument void this knows, no DayNightController, or a null <c>_sunLight</c>, and nothing is
    /// patched. If the old path ever resolves again - a repacked mod, a future scene - the original runs
    /// untouched, because the prefix only steps in where the mod's own lookup comes back empty.
    /// </remarks>
    internal sealed class GraphicsModSunToggle : Fix
    {
        internal override string Id => "graphicsmod-sun-toggle";
        internal override string Mod => "GraphicsMOD";
        internal override string ModVersions => "2.0.0";
        internal override string GameVersions => "0.4.6f13";

        internal override string What
            => "GraphicsMOD's lighting toggle reaches the sun the game holds now";

        internal override string StandsDownBecause
            => "GraphicsMOD's lighting option does nothing at all - it looks for the sun under a path "
             + "0.4.6 no longer has, and says so in the log.";

        /// <summary>The path the mod walks. Its absence is what makes stepping in honest.</summary>
        private const string OldPath = "Managers/@EnvironmentFX/SkySystemController";

        private static MelonLogger.Instance _log;
        private static MethodInfo _sunLight;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var settings = AccessTools.TypeByName("GraphicsSettings");
            if (settings == null) return false;                       // not this mod's build

            var target = AccessTools.Method(settings, "OptimitationLights");
            var parameters = target?.GetParameters();
            if (target == null || target.ReturnType != typeof(void)
                || parameters.Length != 1 || parameters[0].ParameterType != typeof(bool))
            {
                log.Warning($"[fix] {Id}: GraphicsSettings.OptimitationLights is not the one-argument "
                          + "method this knows, so the lighting toggle stays as it is.");
                return false;
            }

            // Read off the controller rather than searched for in the scene: the controller is what OWNS
            // the sun now, so its own reference is the only one that cannot pick some other Light.
            //
            // Through the GETTER, not the field. Il2CppInterop turns a game field into a property on the
            // managed side, private ones included, so AccessTools.Field finds nothing and the first
            // version of this stood down on a build that has the sun right there.
            _sunLight = AccessTools.PropertyGetter(typeof(DayNightController), "_sunLight");
            if (_sunLight == null)
            {
                log.Warning($"[fix] {Id}: DayNightController has no _sunLight on this build, so there is "
                          + "no sun to point the toggle at.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.graphicsmodsun").Patch(target,
                prefix: new HarmonyMethod(typeof(GraphicsModSunToggle), nameof(ToggleTheSunTheGameHolds)));
            return true;
        }

        /// <summary>Switch the sun the way the mod switched it, where the game keeps it now.</summary>
        private static bool ToggleTheSunTheGameHolds(bool enabled)
        {
            // The mod's own path first: if it resolves, this build is not the one that needs repairing and
            // the mod's own code is the more faithful of the two.
            try { if (GameObject.Find(OldPath) != null) return true; }
            catch { return true; }

            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<DayNightController>();
                if (controller == null) return Complain("no DayNightController is loaded");

                if (_sunLight.Invoke(controller, null) is not Light sun || sun == null)
                    return Complain("the controller has no sun light");

                sun.gameObject.SetActive(enabled);
                if (!_said)
                {
                    _said = true;
                    _log?.Msg("[fix] graphicsmod-sun-toggle: the lighting option now switches the sun the "
                            + "game holds, which is where 0.4.6 moved it.");
                }
                return false;                                          // the original would find nothing
            }
            catch (Exception e) { return Complain(e.Message); }
        }

        /// <summary>Say it once, then let the mod's own message stand.</summary>
        private static bool Complain(string why)
        {
            if (!_said)
            {
                _said = true;
                _log?.Warning($"[fix] graphicsmod-sun-toggle: {why}, so the lighting option was left alone "
                            + "and changed nothing.");
            }
            return true;
        }
    }
}
