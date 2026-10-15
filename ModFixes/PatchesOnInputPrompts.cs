using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.UI.Input;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A patch on the old key-hint <c>LoadModule</c> runs again when the game shows the hint it was for.
    /// </summary>
    /// <remarks>
    /// The plugin puts <c>InputPromptsCanvas</c> back as a stand-in around 0.4.6's InputPromptsManager, so
    /// a mod patching <c>LoadModule(string)</c> resolves and registers. The game never calls it: it loads
    /// its hints through the manager, under new ids. A mod that used that call as its "a pour task just
    /// started" signal - AutomatedTasks drives the soil, seed, water, fertilizer and harvest tasks off it -
    /// loses the feature with no error anywhere.
    ///
    /// So this postfixes the manager's string overloads and, when the game loads one of the ids below,
    /// calls the stand-in with the key 0.4.5f2 used at the same call site. The stand-in's own body is
    /// skipped for that call, because the module is already up and loading it again under the old key
    /// would only log "No input data found". For the length of the call <c>currentModuleLabel</c> answers
    /// with the old key, which is what a patch reading it compares against.
    ///
    /// ONLY IDS WITH ONE KNOWN MEANING ARE MAPPED. Each pair is read off the same call site in both
    /// builds. "Station" and "GenericTask" are left out: the code loads them where 0.4.5f2 loaded
    /// "packaging" and "generictask", but a State can also load them from prefab data, and a mod would
    /// then be told a packaging task started when something else did. <c>UnloadModule</c> is not relayed;
    /// nothing known patches it.
    ///
    /// NOTHING IS WIRED FOR NOBODY. Without a patch from another mod on the stand-in, no postfix goes on
    /// the manager.
    /// </remarks>
    internal sealed class PatchesOnInputPrompts : Fix
    {
        internal override string Id => "patches-on-input-prompts";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "a patch on the old key-hint LoadModule runs again when the game shows the hint it was "
             + "written for";

        internal override string StandsDownBecause
            => "a mod patching InputPromptsCanvas.LoadModule registers and never fires, because the game "
             + "loads its hints through InputPromptsManager under new ids.";

        /// <summary>0.4.6 id, as the manager lower-cases it, to the 0.4.5f2 key at the same call site.</summary>
        private static readonly Dictionary<string, string> OldKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["pourtask"] = "pourable",                              // GrowContainerPourTask, SowSeedTask
            ["harvestplant"] = "harvestplant",                      // HarvestPlant
            ["harvestshroom"] = "harvestshrooms",                   // HarvestMushroomBedTask
            ["spraymist"] = "mistingtask",                          // MistMushroomBedTask
            ["applyshroomspawn"] = "applyshroomspawn_breakchunks",  // ApplyShroomSpawnTask
            ["mixshroomspawn"] = "applyshroomspawn_mix",            // ApplyShroomSpawnTask
            ["packagingstationmk2"] = "packagingmk2",               // PackageProductTaskMk2
            ["trashgrabber"] = "trashgrabber",                      // Equippable_TrashGrabber
            ["skateboard"] = "skateboard",                          // Player
            ["heldskateboard"] = "heldskateboard",                  // Skateboard_Equippable
        };

        private static MelonLogger.Instance _log;
        private static MethodInfo _standIn;
        private static ConstructorInfo _wrap;
        private static FieldInfo _slot, _label;
        private static int _lastFrame = -1;
        private static string _lastId;
        private static bool _relaying;
        private static readonly HashSet<string> Said = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var type = AccessTools.TypeByName("Il2CppScheduleOne.UI.InputPromptsCanvas");
            if (type == null)
            {
                log.Msg("[fix] patches-on-input-prompts: no InputPromptsCanvas stand-in on this build, nothing "
                      + "to relay.");
                return false;
            }

            _standIn = AccessTools.DeclaredMethod(type, "LoadModule", new[] { typeof(string) });
            if (_standIn == null)
            {
                log.Warning("[fix] patches-on-input-prompts: the stand-in has no LoadModule(string), so a mod "
                          + "patching it will not load.");
                return false;
            }
            if (!IsPatchedByAnybodyElse(_standIn)) return false;

            _wrap = AccessTools.Constructor(type, new[] { typeof(IntPtr) });
            _slot = AccessTools.Field(type, "polyfillLoadedModule");
            _label = AccessTools.Field(type, "polyfillModuleLabel");
            if (_wrap == null || _slot == null || _label == null)
            {
                log.Warning("[fix] patches-on-input-prompts: the stand-in lacks "
                          + (_wrap == null ? "its pointer constructor" : _slot == null
                                 ? "polyfillLoadedModule" : "polyfillModuleLabel")
                          + " - the plugin is older than this mod. A patch on InputPromptsCanvas.LoadModule "
                          + "will not fire.");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.inputprompts");
            harmony.Patch(_standIn, prefix: new HarmonyMethod(typeof(PatchesOnInputPrompts), nameof(SkipWhileRelaying))
            {
                priority = Priority.Last,
            });

            int wired = 0;
            foreach (var method in typeof(InputPromptsManager).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != "LoadModule") continue;
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string)) continue;
                try
                {
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(PatchesOnInputPrompts), nameof(Leave)));
                    wired++;
                }
                catch (Exception e)
                {
                    log.Warning($"[fix] patches-on-input-prompts: LoadModule with {parameters.Length} "
                              + $"argument(s) could not be patched ({e.Message}); hints loaded that way are "
                              + "not relayed.");
                }
            }

            if (wired == 0)
            {
                log.Warning("[fix] patches-on-input-prompts: no string LoadModule on InputPromptsManager could "
                          + "be patched. A patch on InputPromptsCanvas.LoadModule will not fire.");
                return false;
            }
            log.Msg($"[fix] patches-on-input-prompts: {wired} LoadModule overload(s) relay {OldKeys.Count} "
                  + "hint ids to the old LoadModule, so a patch on it runs when the game shows that hint.");
            return true;
        }

        private static bool IsPatchedByAnybodyElse(MethodBase method)
        {
            try
            {
                var info = HarmonyLib.Harmony.GetPatchInfo(method);
                if (info == null) return false;
                foreach (var owner in info.Owners)
                    if (!owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) return true;
                return false;
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] patches-on-input-prompts: could not read who patched LoadModule ("
                            + e.Message + "), so it was wired anyway.");
                return true;
            }
        }

        /// <summary>The stand-in's own body would load the old key into the manager again.</summary>
        private static bool SkipWhileRelaying() => !_relaying;

        /// <summary>Run the old LoadModule, under the old key, for a hint the game just loaded.</summary>
        /// <remarks>
        /// One game load can pass through two of these overloads - <c>LoadModule("PourTask")</c> hands on
        /// "pourtask" to the three-argument one - so a second call with the same id in the same frame is
        /// the same load and is not relayed twice.
        /// </remarks>
        private static void Leave(InputPromptsManager __instance, string __0)
        {
            if (__instance == null || __0 == null || _relaying) return;
            if (!OldKeys.TryGetValue(__0, out string oldKey)) return;
            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastFrame && string.Equals(__0, _lastId, StringComparison.OrdinalIgnoreCase)) return;
            _lastFrame = frame;
            _lastId = __0;

            object priorSlot = null, priorLabel = null;
            try
            {
                priorSlot = _slot.GetValue(null);
                priorLabel = _label.GetValue(null);
                _slot.SetValue(null, __0.ToLowerInvariant());
                _label.SetValue(null, oldKey);
                _relaying = true;

                _standIn.Invoke(_wrap.Invoke(new object[] { __instance.Pointer }), new object[] { oldKey });
                if (Said.Add(oldKey))
                    _log.Msg($"[fix] patches-on-input-prompts: the game loaded '{__0}', relayed as '{oldKey}'.");
            }
            catch (Exception e)
            {
                var cause = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
                if (Said.Add("fail/" + oldKey))
                    _log.Error($"[fix] patches-on-input-prompts: relaying '{__0}' as '{oldKey}' threw "
                             + $"{cause.GetType().Name}: {cause.Message}. A patch on it did not run.");
                Fixes.Record("patches-on-input-prompts", "relay threw: " + cause.Message);
            }
            finally
            {
                _relaying = false;
                _slot.SetValue(null, priorSlot);
                _label.SetValue(null, priorLabel);
            }
        }
    }
}
