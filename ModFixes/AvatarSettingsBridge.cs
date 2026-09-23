using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.AvatarFramework;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod that reads or sets a character's look through the 0.4.6 AvatarSettings gets it on 0.4.7.
    /// </summary>
    /// <remarks>
    /// 0.4.6 applied one AvatarSettings and kept it (Avatar.cs:328-354 on 0.4.6f13). 0.4.7 applies a
    /// NakedAppearance and an outfit through Avatar.Appearance (AvatarAppearance.cs:96-147 on 0.4.7f6), and
    /// keeps no settings object at all. The plugin puts <c>CurrentSettings</c> and <c>LoadAvatarSettings</c>
    /// back as empty stand-ins; this gives them their behaviour.
    ///
    /// THE TRANSLATION RESTS ON THE LEGACY ASSETS. Every layer, hair and accessory asset 0.4.6 named by
    /// path still loads on 0.4.7, knows its own path (<c>AssetPath</c>) and points at the 0.4.7 object that
    /// replaced it (<c>AvatarObjectEquivalent</c>, AvatarLayer.cs:26, Accessory.cs:32). That gives both
    /// directions: path to object for a load, object id back to path for CurrentSettings.
    ///
    /// Which list an entry lands in follows 0.4.6's own split (PlayerClothing.cs:67-90, Avatar.cs:356 on
    /// 0.4.6f13): face layers, hair and body layers up to order 19 are the body; body layers above 19 and
    /// every accessory are clothes.
    ///
    /// WHAT HAS NO LEGACY PATH SURVIVES THE ROUND TRIP through the two Equivalent fields 0.4.7 added to
    /// AvatarSettings (AvatarSettings.cs:79-84): CurrentSettings parks the applied appearance and outfit
    /// there, a copy made with Instantiate keeps them, and a load takes from them whatever the layer lists
    /// cannot describe. An object the lists CAN describe is taken from the lists, so a mod that removes a
    /// layer from CurrentSettings and loads it back sees it gone.
    ///
    /// The look is set on this machine only. 0.4.7 sends no NPC appearance over the network
    /// (NPC.cs:536-546 applies it from local data on every peer), and neither did the 0.4.6 method.
    /// </remarks>
    internal sealed class AvatarSettingsBridge : Fix
    {
        internal override string Id => "avatar-settings-bridge";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "a mod that sets a character's look the 0.4.6 way (custom NPCs, look editors) gets that look";

        internal override string StandsDownBecause
            => "a mod that sets a character's look through avatar settings leaves that character looking "
             + "the way the game made it.";

        /// <summary>0.4.6 wrote Height to the scale directly, 0.4.7 divides by this (AvatarAppearance.cs:240).</summary>
        private const float HeightScale = 1.875f;

        /// <summary>Body layers above this order were clothes in 0.4.6 (Avatar.cs:356 on 0.4.6f13).</summary>
        private const int NakedMaxOrder = 19;

        private static MelonLogger.Instance _log;
        private static Type _naked, _nakedObject, _outfit, _serialized, _list;

        /// <summary>The settings each avatar last took, by instance id: a native address is reused.</summary>
        private static readonly Dictionary<int, AvatarSettings> Applied = new();
        private static readonly Dictionary<int, List<object>> Worn = new();

        /// <summary>Set while this fix applies a body, so the game's own applications can be told apart.</summary>
        private static bool _applying;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var avatar = typeof(Il2CppScheduleOne.AvatarFramework.Avatar);
            var current = Declared(avatar, "get_CurrentSettings", 0);
            var load = Declared(avatar, "LoadAvatarSettings", 1);
            var dataLook = Declared(typeof(Il2CppScheduleOne.NPCs.Framework.Appearance), "set_AvatarSettings", 1);
            if (current == null && load == null && dataLook == null)
            {
                log.Msg($"[fix] {Id}: no mod uses CurrentSettings, LoadAvatarSettings or an NPC's AvatarSettings; "
                      + "nothing to translate.");
                return false;
            }

            _naked = AccessTools.TypeByName("Il2CppScheduleOne.Core.Avatar.NakedAppearance");
            _nakedObject = AccessTools.TypeByName("Il2CppScheduleOne.Core.Avatar.NakedAppearanceObject");
            _outfit = AccessTools.TypeByName("Il2CppScheduleOne.Core.Avatar.Outfit");
            _serialized = AccessTools.TypeByName("Il2CppScheduleOne.Core.Avatar.SerializedAvatarObject");
            var appearance = AccessTools.TypeByName("Il2CppScheduleOne.Avatar.AvatarAppearance");
            if (_naked == null || _nakedObject == null || _outfit == null || _serialized == null || appearance == null)
            {
                log.Warning($"[fix] {Id}: the 0.4.7 appearance types are not all here "
                          + $"(NakedAppearance {_naked != null}, NakedAppearanceObject {_nakedObject != null}, "
                          + $"Outfit {_outfit != null}, SerializedAvatarObject {_serialized != null}, "
                          + $"AvatarAppearance {appearance != null}).");
                return false;
            }
            _list = typeof(Il2CppSystem.Collections.Generic.List<>).MakeGenericType(_serialized);

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.avatarsettings");

            // What an avatar wears is not readable on 0.4.7 (AvatarAppearance.cs:37-41 keeps it private), so
            // it is written down on its way in.
            var listOutfit = appearance.GetMethods(AccessTools.all).FirstOrDefault(m => m.Name == "ApplyOutfit"
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == _list);
            var removeOutfit = Declared(appearance, "RemoveOutfit", 0);
            if (listOutfit == null || removeOutfit == null)
            {
                log.Warning($"[fix] {Id}: AvatarAppearance.ApplyOutfit(List) or RemoveOutfit is not here.");
                return false;
            }
            harmony.Patch(listOutfit, postfix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(Dressed)));
            harmony.Patch(removeOutfit, postfix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(Undressed)));

            var applyNaked = Declared(appearance, "ApplyNakedAppearance", 1);
            if (applyNaked == null)
            {
                log.Warning($"[fix] {Id}: AvatarAppearance.ApplyNakedAppearance is not here.");
                return false;
            }
            harmony.Patch(applyNaked, postfix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(BodyChanged)));

            if (current != null)
                harmony.Patch(current, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(Current)));
            if (load != null)
                harmony.Patch(load, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(Load)));
            if (dataLook != null)
                harmony.Patch(dataLook, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(SetDataLook)));

            var bridged = new[] { current != null ? "CurrentSettings" : null, load != null ? "LoadAvatarSettings" : null,
                                  dataLook != null ? "an NPC's AvatarSettings" : null }.Where(x => x != null);
            log.Msg($"[fix] {Id}: {string.Join(", ", bridged)} now translate to the 0.4.7 appearance.");
            return true;
        }

        private static MethodInfo Declared(Type type, string name, int arity)
            => type.GetMethods(AccessTools.all).FirstOrDefault(m => m.DeclaringType == type && m.Name == name
                                                                    && m.GetParameters().Length == arity);

        // ------------------------------------------------------------------ outfit tracking

        private static void Dressed(object __instance, object wornObjects)
        {
            try
            {
                var copy = new List<object>();
                if (wornObjects != null)
                {
                    int count = (int)Get(wornObjects, "Count");
                    var item = wornObjects.GetType().GetProperty("Item");
                    for (int i = 0; i < count; i++) copy.Add(item.GetValue(wornObjects, new object[] { i }));
                }
                Worn[Instance(__instance)] = copy;
            }
            catch (Exception e) { _log?.Warning("[fix] avatar-settings-bridge: could not note an outfit: " + e.Message); }
        }

        private static void Undressed(object __instance)
        {
            try { Worn.Remove(Instance(__instance)); }
            catch (Exception e) { _log?.Warning("[fix] avatar-settings-bridge: could not note an undress: " + e.Message); }
        }

        /// <summary>
        /// The game gave an avatar a body of its own, so the settings last loaded no longer describe it -
        /// in 0.4.6 every path that changed the look went through LoadAvatarSettings and replaced them.
        /// </summary>
        private static void BodyChanged(object __instance)
        {
            if (_applying) return;
            try
            {
                var avatar = Get(__instance, "gameObject") is GameObject go
                    ? go.GetComponentInParent<Il2CppScheduleOne.AvatarFramework.Avatar>() : null;
                if (avatar != null) Applied.Remove(avatar.GetInstanceID());
            }
            catch (Exception e) { _log?.Warning("[fix] avatar-settings-bridge: could not note a body change: " + e.Message); }
        }

        // ------------------------------------------------------------------ CurrentSettings

        private static bool Current(Il2CppScheduleOne.AvatarFramework.Avatar __instance, ref AvatarSettings __result)
        {
            try
            {
                var key = __instance.GetInstanceID();
                if (Applied.TryGetValue(key, out var kept) && kept != null)
                {
                    __result = kept;
                    return false;
                }

                __result = Describe(__instance);
                if (__result != null) Applied[key] = __result;
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] avatar-settings-bridge: CurrentSettings could not be built: " + e);
                __result = null;
            }
            return false;
        }

        /// <summary>An AvatarSettings that says what the avatar looks like now.</summary>
        private static AvatarSettings Describe(Il2CppScheduleOne.AvatarFramework.Avatar avatar)
        {
            var appearance = Get(avatar, "Appearance");
            var naked = appearance == null ? null : Get(appearance, "AppliedNakedAppearance");
            if (naked == null)
            {
                // 0.4.6 answered null before the first load as well.
                _log?.Msg($"[fix] avatar-settings-bridge: {Who(avatar)} has no appearance yet; "
                        + "CurrentSettings is null.");
                return null;
            }

            var settings = ScriptableObject.CreateInstance<AvatarSettings>();
            settings.Height = (float)Get(naked, "Height") / HeightScale;
            settings.Gender = (float)Get(naked, "Gender");
            settings.Weight = (float)Get(naked, "Weight");
            settings.SkinColor = (Color)Get(naked, "SkinColor");
            settings.HairColor = (Color)Get(naked, "HairColor");

            var brow = Get(naked, "LeftEyebrowSettings");
            settings.EyebrowScale = (float)Get(brow, "Scale");
            settings.EyebrowThickness = (float)Get(brow, "Thickness");
            settings.EyebrowRestingHeight = (float)Get(brow, "RestingHeight");
            settings.EyebrowRestingAngle = (float)Get(brow, "RestingAngle");

            var eye = Get(naked, "LeftEyeSettings");
            settings.EyeBallTint = (Color)Get(eye, "EyeballColor");
            settings.PupilDilation = (float)Get(eye, "PupilDilation");
            settings.LeftEyeRestingState = Lid(Get(naked, "LeftEyelidSettings"));
            settings.RightEyeRestingState = Lid(Get(naked, "RightEyelidSettings"));

            string mouth = "", facialHair = "";
            var faceRest = new List<AvatarSettings.LayerSetting>();
            var index = Legacy.Index;

            foreach (var entry in Objects(Get(naked, "AvatarObjects")))
            {
                if (!index.ById.TryGetValue(ObjectId(entry), out var legacy)) continue;
                switch (legacy.Kind)
                {
                    case Kind.Hair: settings.HairPath = legacy.Path; break;
                    case Kind.Face when legacy.IsMouth: mouth = legacy.Path; break;
                    case Kind.Face when legacy.IsFacialHair: facialHair = legacy.Path; break;
                    case Kind.Face: faceRest.Add(Layer(legacy.Path, Tint(entry))); break;
                    case Kind.Body: settings.BodyLayerSettings.Add(Layer(legacy.Path, Tint(entry))); break;
                    case Kind.Accessory: settings.AccessorySettings.Add(Accessory(legacy.Path, Tint(entry))); break;
                }
            }

            settings.FaceLayerSettings.Add(Layer(mouth, Color.white));
            settings.FaceLayerSettings.Add(Layer(facialHair, settings.HairColor));
            foreach (var layer in faceRest) settings.FaceLayerSettings.Add(layer);

            Worn.TryGetValue(Instance(appearance), out var worn);
            var extras = Activator.CreateInstance(_list);
            foreach (var entry in worn ?? new List<object>())
            {
                if (!index.ById.TryGetValue(ObjectId(entry), out var legacy)) { Call(extras, "Add", entry); continue; }
                if (legacy.Kind == Kind.Accessory) settings.AccessorySettings.Add(Accessory(legacy.Path, Tint(entry)));
                else settings.BodyLayerSettings.Add(Layer(legacy.Path, Tint(entry)));
            }

            // Everything the lists cannot name rides along in the two fields 0.4.7 added for this.
            var parked = ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(_nakedObject));
            var parkedNaked = Cast(parked, _nakedObject);
            Set(parkedNaked, "Appearance", Call(naked, "Clone"));
            Set(settings, "EquivalentNakedAppearance", parkedNaked);

            var outfit = Cast(ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(_outfit)), _outfit);
            Set(outfit, "AvatarObjects", Call(extras, "ToArray"));
            Set(settings, "EquivalentOutfit", outfit);
            return settings;
        }

        // ------------------------------------------------------------------ LoadAvatarSettings

        private static bool Load(Il2CppScheduleOne.AvatarFramework.Avatar __instance, AvatarSettings settings)
        {
            if (settings == null)
            {
                // 0.4.6 did the same: warned and changed nothing (Avatar.cs:330 on 0.4.6f13).
                _log?.Warning($"[fix] avatar-settings-bridge: LoadAvatarSettings(null) on {Who(__instance)}; ignored.");
                return false;
            }

            try { Apply(__instance, settings); }
            catch (Exception e)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)} could not take its "
                            + "settings: " + e);
            }
            return false;
        }

        private static void Apply(Il2CppScheduleOne.AvatarFramework.Avatar avatar, AvatarSettings settings)
        {
            var appearance = Get(avatar, "Appearance");
            if (appearance == null)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: {Who(avatar)} has no Appearance; settings not applied.");
                return;
            }

            var look = Translate(settings, Who(avatar));
            _applying = true;
            try
            {
                Call(appearance, "ApplyNakedAppearance", look.Naked);
                if (look.ReplacesOutfit) Call(appearance, "ApplyOutfit", look.Worn);
            }
            finally { _applying = false; }
            Applied[avatar.GetInstanceID()] = settings;

            if (settings.ImpostorTexture != null) Call(avatar, "SetImpostorTexture", settings.ImpostorTexture);
        }

        /// <summary>A body and an outfit in the 0.4.7 form, and whether the outfit replaces the one worn.</summary>
        private sealed class Look
        {
            internal object Naked;
            internal object Worn;
            internal bool ReplacesOutfit;
        }

        private static Look Translate(AvatarSettings settings, string who)
        {
            var index = Legacy.Index;

            var equivalentNaked = Get(settings, "EquivalentNakedAppearance");
            var equivalentOutfit = Get(settings, "EquivalentOutfit");
            var baseNaked = equivalentNaked == null ? null : Get(equivalentNaked, "Appearance");

            var naked = baseNaked != null ? Call(baseNaked, "Clone") : Activator.CreateInstance(_naked);
            foreach (var entry in Objects(Get(naked, "AvatarObjects")))
            {
                string id = ObjectId(entry);
                if (index.ById.ContainsKey(id)) Call(naked, "RemoveAvatarObject", id);
            }

            Set(naked, "Height", settings.Height * HeightScale);
            Set(naked, "Gender", settings.Gender);
            Set(naked, "Weight", settings.Weight);
            Set(naked, "SkinColor", settings.SkinColor);

            foreach (string side in new[] { "Left", "Right" })
            {
                var brow = Get(naked, side + "EyebrowSettings");
                Set(brow, "Scale", settings.EyebrowScale);
                Set(brow, "Thickness", settings.EyebrowThickness);
                Set(brow, "RestingHeight", settings.EyebrowRestingHeight);
                Set(brow, "RestingAngle", settings.EyebrowRestingAngle);
                Set(naked, side + "EyebrowSettings", brow);

                var eye = Get(naked, side + "EyeSettings");
                Set(eye, "EyeballColor", settings.EyeBallTint);
                Set(eye, "PupilDilation", settings.PupilDilation);
                Set(naked, side + "EyeSettings", eye);

                var rest = side == "Left" ? settings.LeftEyeRestingState : settings.RightEyeRestingState;
                var lid = Get(naked, side + "EyelidSettings");
                var position = Get(lid, "RestingState");
                Set(position, "TopLidOpenness", rest.topLidOpen);
                Set(position, "BottomLidOpenness", rest.bottomLidOpen);
                Set(lid, "RestingState", position);
                Set(naked, side + "EyelidSettings", lid);
            }
            // Eyebrows took the hair colour in 0.4.6 as well (Avatar.cs:476-485 on 0.4.6f13).
            Call(naked, "SetHairAndEyebrowColor", settings.HairColor);

            var worn = Activator.CreateInstance(_list);
            int fromLists = 0;
            var missing = new List<string>();

            void Naked(string path, Func<object, object> serialize)
            {
                var equivalent = index.Equivalent(path);
                if (equivalent == null) { missing.Add(path); return; }
                Call(naked, "AddAvatarObject", serialize(equivalent));
            }

            if (!string.IsNullOrEmpty(settings.HairPath))
                Naked(settings.HairPath, o => Call(o, "SerializeWithPrimaryColor", settings.HairColor));

            var faces = settings.FaceLayerSettings;
            for (int i = 0; faces != null && i < faces.Count; i++)
            {
                var layer = faces[i];
                if (string.IsNullOrEmpty(layer.layerPath)) continue;
                var tint = i == 1 ? settings.HairColor : layer.layerTint;
                Naked(layer.layerPath, o => i == 0 ? Call(o, "Serialize") : Call(o, "SerializeWithPrimaryColor", tint));
            }

            var bodies = settings.BodyLayerSettings;
            for (int i = 0; bodies != null && i < bodies.Count; i++)
            {
                var layer = bodies[i];
                if (string.IsNullOrEmpty(layer.layerPath)) continue;
                var equivalent = index.Equivalent(layer.layerPath);
                if (equivalent == null) { missing.Add(layer.layerPath); continue; }
                var serialized = Call(equivalent, "SerializeWithPrimaryColor", layer.layerTint);
                if (index.Order(layer.layerPath) > NakedMaxOrder) { Call(worn, "Add", serialized); fromLists++; }
                else Call(naked, "AddAvatarObject", serialized);
            }

            var accessories = settings.AccessorySettings;
            for (int i = 0; accessories != null && i < accessories.Count; i++)
            {
                var accessory = accessories[i];
                if (accessory == null || string.IsNullOrEmpty(accessory.path)) continue;
                var equivalent = index.Equivalent(accessory.path);
                if (equivalent == null) { missing.Add(accessory.path); continue; }
                Call(worn, "Add", Call(equivalent, "SerializeWithPrimaryColor", accessory.color));
                fromLists++;
            }

            if (equivalentOutfit != null)
                foreach (var entry in Objects(Get(equivalentOutfit, "AvatarObjects")))
                    if (!index.ById.ContainsKey(ObjectId(entry))) Call(worn, "Add", entry);

            if (missing.Count > 0)
                _log?.Warning($"[fix] avatar-settings-bridge: {who}: {missing.Count} part(s) have "
                            + "no 0.4.7 equivalent and were left off: " + string.Join(", ", missing.Distinct()));

            // 0.4.6 replaced the clothes with what the settings listed. A settings object that lists none
            // and came with a 0.4.7 body of its own is a game asset describing a body, and the clothes the
            // game put on stay.
            return new Look
            {
                Naked = naked,
                Worn = worn,
                ReplacesOutfit = equivalentOutfit != null || fromLists > 0 || equivalentNaked == null,
            };
        }

        // ------------------------------------------------------------------ NPC data

        /// <summary>
        /// <c>NPCs.Framework.Appearance.AvatarSettings = value</c>: the look an NPC is built with.
        /// </summary>
        /// <remarks>
        /// 0.4.6 NPC data carried one AvatarSettings, applied when the NPC was set up (NPC.cs:335-344 on
        /// 0.4.6f13). 0.4.7 carries a body and an outfit and applies those at the same point
        /// (Appearance.cs:10-16, NPC.cs:536-546 on 0.4.7f6), so the settings are translated into the two
        /// and the game applies them itself - which is how S1API gives a custom NPC its look.
        /// </remarks>
        private static bool SetDataLook(object __instance, AvatarSettings value)
        {
            if (value == null)
            {
                _log?.Warning("[fix] avatar-settings-bridge: an NPC's AvatarSettings was set to null; its look is left as it was.");
                return false;
            }

            try
            {
                var look = Translate(value, value.name);
                var body = Cast(ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(_nakedObject)), _nakedObject);
                Set(body, "Appearance", look.Naked);
                Set(__instance, "DefaultAppearance", body);

                if (look.ReplacesOutfit)
                {
                    var outfit = Cast(ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(_outfit)), _outfit);
                    Set(outfit, "AvatarObjects", Call(look.Worn, "ToArray"));
                    Set(__instance, "DefaultOutfit", outfit);
                }
                if (value.ImpostorTexture != null) Set(__instance, "Impostor", value.ImpostorTexture);
                _log?.Msg($"[fix] avatar-settings-bridge: NPC data took the look '{value.name}'.");
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] avatar-settings-bridge: an NPC's AvatarSettings could not be translated: " + e);
            }
            return false;
        }

        // ------------------------------------------------------------------ the legacy assets

        private enum Kind { Face, Body, Hair, Accessory }

        private sealed class Legacy
        {
            internal string Path;
            internal Kind Kind;
            internal int Order;
            internal object Equivalent;
            internal bool IsMouth => Name.StartsWith("Face_", StringComparison.OrdinalIgnoreCase);
            internal bool IsFacialHair => Name.StartsWith("FacialHair_", StringComparison.OrdinalIgnoreCase);
            private string Name => Path.Substring(Path.LastIndexOf('/') + 1);

            private static Catalogue _index;
            internal static Catalogue Index => _index ??= Catalogue.Build();
        }

        /// <summary>Every legacy asset, by the 0.4.7 object id it points at and by its own path.</summary>
        private sealed class Catalogue
        {
            internal readonly Dictionary<string, Legacy> ById = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, Legacy> _byPath = new(StringComparer.OrdinalIgnoreCase);

            internal static Catalogue Build()
            {
                var catalogue = new Catalogue();
                int layers = 0, prefabs = 0, without = 0;

                foreach (var asset in Resources.LoadAll<AvatarLayer>("Avatar/Layers"))
                {
                    if (asset == null) continue;
                    bool face = asset.TryCast<FaceLayer>() != null;
                    if (catalogue.Add(asset.AssetPath, face ? Kind.Face : Kind.Body, asset.Order, Get(asset, "AvatarObjectEquivalent")))
                        layers++;
                    else without++;
                }

                foreach (string folder in new[] { "Avatar/Hair", "Avatar/Accessories" })
                    foreach (var go in Resources.LoadAll<GameObject>(folder))
                    {
                        var accessory = go == null ? null : go.GetComponent<Accessory>();
                        if (accessory == null) continue;
                        var kind = accessory.TryCast<Hair>() != null ? Kind.Hair : Kind.Accessory;
                        if (catalogue.Add(accessory.AssetPath, kind, 0, Get(accessory, "AvatarObjectEquivalent")))
                            prefabs++;
                        else without++;
                    }

                _log?.Msg($"[fix] avatar-settings-bridge: {layers} legacy layer(s) and {prefabs} hair/accessory "
                        + $"prefab(s) map to 0.4.7 objects; {without} do not.");
                return catalogue;
            }

            private bool Add(string path, Kind kind, int order, object equivalent)
            {
                if (string.IsNullOrEmpty(path) || equivalent == null) return false;
                var entry = new Legacy { Path = path, Kind = kind, Order = order, Equivalent = equivalent };
                _byPath.TryAdd(path, entry);
                ById.TryAdd((string)Get(equivalent, "Id"), entry);
                return true;
            }

            /// <summary>The 0.4.7 object for a legacy path; a path outside the catalogue is loaded directly.</summary>
            internal object Equivalent(string path)
            {
                if (_byPath.TryGetValue(path, out var known)) return known.Equivalent;

                var loaded = Resources.Load(path);
                if (loaded == null) return null;
                var layer = loaded.TryCast<AvatarLayer>();
                if (layer != null) return Get(layer, "AvatarObjectEquivalent");
                var accessory = loaded.TryCast<GameObject>()?.GetComponent<Accessory>();
                return accessory == null ? null : Get(accessory, "AvatarObjectEquivalent");
            }

            internal int Order(string path)
            {
                if (_byPath.TryGetValue(path, out var known)) return known.Order;
                var layer = Resources.Load(path)?.TryCast<AvatarLayer>();
                return layer == null ? 0 : layer.Order;
            }
        }

        // ------------------------------------------------------------------ small pieces

        private static AvatarSettings.LayerSetting Layer(string path, Color tint)
            => new AvatarSettings.LayerSetting { layerPath = path, layerTint = tint };

        private static AvatarSettings.AccessorySetting Accessory(string path, Color color)
            => new AvatarSettings.AccessorySetting { path = path, color = color };

        private static Eye.EyeLidConfiguration Lid(object eyelid)
        {
            var position = Get(eyelid, "RestingState");
            return new Eye.EyeLidConfiguration
            {
                topLidOpen = (float)Get(position, "TopLidOpenness"),
                bottomLidOpen = (float)Get(position, "BottomLidOpenness"),
            };
        }

        /// <summary>The character an avatar belongs to: the avatar object itself is only ever called "Avatar".</summary>
        private static string Who(Il2CppScheduleOne.AvatarFramework.Avatar avatar) => avatar.transform.root.name;

        private static string ObjectId(object serialized) => (string)Get(serialized, "Id") ?? "";

        /// <summary>The first colour an avatar object was saved with, which is the one a layer tint set.</summary>
        private static Color Tint(object serialized)
        {
            var colors = Objects(Get(serialized, "Colors")).FirstOrDefault();
            return colors == null ? Color.white : (Color)Get(colors, "Value");
        }

        private static IEnumerable<object> Objects(object array)
        {
            if (array == null) yield break;
            var type = array.GetType();
            var length = type.GetProperty("Length") ?? type.GetProperty("Count");
            var item = type.GetProperty("Item");
            if (length == null || item == null) throw new MissingMemberException(type.FullName, "Length/Item");
            int count = (int)length.GetValue(array);
            for (int i = 0; i < count; i++) yield return item.GetValue(array, new object[] { i });
        }

        private static int Instance(object il2cpp) => ((Il2CppObjectBase)il2cpp).Cast<UnityEngine.Object>().GetInstanceID();

        private static object Cast(object il2cpp, Type type)
            => typeof(Il2CppObjectBase).GetMethod(nameof(Il2CppObjectBase.Cast)).MakeGenericMethod(type).Invoke(il2cpp, null);

        private static object Get(object target, string member)
        {
            var type = target.GetType();
            var property = type.GetProperty(member, AccessTools.all);
            if (property != null) return property.GetValue(target);
            var field = type.GetField(member, AccessTools.all);
            if (field != null) return field.GetValue(target);
            throw new MissingMemberException(type.FullName, member);
        }

        /// <summary>Writes a member. On a boxed struct the box is changed, so the caller writes the box back.</summary>
        private static void Set(object target, string member, object value)
        {
            var type = target.GetType();
            var property = type.GetProperty(member, AccessTools.all);
            if (property != null) { property.SetValue(target, value); return; }
            var field = type.GetField(member, AccessTools.all);
            if (field != null) { field.SetValue(target, value); return; }
            throw new MissingMemberException(type.FullName, member);
        }

        private static object Call(object target, string name, params object[] arguments)
        {
            var method = target.GetType().GetMethods(AccessTools.all).FirstOrDefault(m => m.Name == name
                && m.GetParameters().Length == arguments.Length
                && m.GetParameters().Select((p, i) => arguments[i] == null || p.ParameterType.IsInstanceOfType(arguments[i]))
                                    .All(fits => fits));
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, arguments);
        }
    }
}
