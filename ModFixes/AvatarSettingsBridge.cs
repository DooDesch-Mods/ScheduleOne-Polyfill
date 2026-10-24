using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
    /// keeps no settings object at all. The plugin puts <c>CurrentSettings</c>, <c>LoadAvatarSettings</c>,
    /// <c>LoadNakedSettings</c>, the goon pool's <c>MaleBaseAppearances</c>, <c>IsMale</c>, <c>IsWhite</c>,
    /// <c>ApplyBodySettings</c>, <c>ApplyAccessorySettings</c> and <c>appliedAccessories</c> back as empty
    /// stand-ins; this gives them their behaviour.
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

        /// <summary>0.4.6 avatars kept this many accessory slots (Avatar.cs:674-725 on 0.4.6f13).</summary>
        private const int AccessorySlotCount = 9;

        /// <summary>Body layers above this order were clothes in 0.4.6 (Avatar.cs:356 on 0.4.6f13).</summary>
        private const int NakedMaxOrder = 19;

        private static MelonLogger.Instance _log;
        private static Type _naked, _nakedObject, _outfit, _serialized, _list;

        /// <summary>The settings each avatar last took, by instance id: a native address is reused.</summary>
        private static readonly Dictionary<int, AvatarSettings> Applied = new();
        private static readonly Dictionary<int, List<object>> Worn = new();

        /// <summary>Set while this fix applies a body, so the game's own applications can be told apart.</summary>
        private static bool _applying;

        /// <summary>The goon pool's male bodies as settings, and the pool they were built for.</summary>
        private static Il2CppReferenceArray<AvatarSettings> _goonBases;
        private static int _goonBasesFor;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var avatar = typeof(Il2CppScheduleOne.AvatarFramework.Avatar);
            var current = Declared(avatar, "get_CurrentSettings", 0);
            var load = Declared(avatar, "LoadAvatarSettings", 1);
            var dataLook = Declared(typeof(Il2CppScheduleOne.NPCs.Framework.Appearance), "set_AvatarSettings", 1);
            var loadNaked = Declared(avatar, "LoadNakedSettings", 3);
            var goonBases = Declared(typeof(Il2CppScheduleOne.Cartel.GoonPool), "get_MaleBaseAppearances", 0);
            var isMale = Declared(avatar, "IsMale", 0);
            var isWhite = Declared(avatar, "IsWhite", 0);
            var body = Declared(avatar, "ApplyBodySettings", 1);
            var accessories = Declared(avatar, "ApplyAccessorySettings", 1);
            var slots = Declared(avatar, "get_appliedAccessories", 0);
            if (current == null && load == null && dataLook == null && loadNaked == null && goonBases == null
                && isMale == null && isWhite == null && body == null && accessories == null && slots == null)
            {
                log.Msg($"[fix] {Id}: no mod uses CurrentSettings, LoadAvatarSettings, LoadNakedSettings, an NPC's "
                      + "AvatarSettings, the goon pool's base appearances, IsMale, IsWhite, ApplyBodySettings, "
                      + "ApplyAccessorySettings or appliedAccessories; nothing to translate.");
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
            if (loadNaked != null)
                harmony.Patch(loadNaked, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(LoadNaked)));
            if (goonBases != null)
                harmony.Patch(goonBases, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(GoonBases)));
            if (isMale != null)
                harmony.Patch(isMale, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(IsMale)));
            if (isWhite != null)
                harmony.Patch(isWhite, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(IsWhite)));
            if (body != null)
                harmony.Patch(body, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(ApplyBody)));
            if (accessories != null)
                harmony.Patch(accessories, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(ApplyAccessories)));
            if (slots != null)
                harmony.Patch(slots, prefix: new HarmonyMethod(typeof(AvatarSettingsBridge), nameof(AccessorySlots)));

            var bridged = new[] { current != null ? "CurrentSettings" : null, load != null ? "LoadAvatarSettings" : null,
                                  loadNaked != null ? "LoadNakedSettings" : null,
                                  dataLook != null ? "an NPC's AvatarSettings" : null,
                                  goonBases != null ? "the goon pool's base appearances" : null,
                                  isMale != null ? "IsMale" : null, isWhite != null ? "IsWhite" : null,
                                  body != null ? "ApplyBodySettings" : null,
                                  accessories != null ? "ApplyAccessorySettings" : null,
                                  slots != null ? "appliedAccessories" : null }.Where(x => x != null);
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

            var settings = DescribeBody(naked);
            var index = Legacy.Index;

            // Worn objects the 0.4.6 catalogue has no path for cannot become a layer or an accessory; they
            // ride along in EquivalentOutfit, and a load puts them back on from there.
            Worn.TryGetValue(Instance(appearance), out var worn);
            var extras = Activator.CreateInstance(_list);
            foreach (var entry in worn ?? new List<object>())
            {
                if (!index.ById.TryGetValue(ObjectId(entry), out var legacy)) { Call(extras, "Add", entry); continue; }
                if (legacy.Kind == Kind.Accessory) settings.AccessorySettings.Add(Accessory(legacy.Path, Tint(entry)));
                else settings.BodyLayerSettings.Add(Layer(legacy.Path, Tint(entry)));
            }

            var outfit = Cast(ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(_outfit)), _outfit);
            Set(outfit, "AvatarObjects", Call(extras, "ToArray"));
            Set(settings, "EquivalentOutfit", outfit);
            return PadAccessories(settings);
        }

        /// <summary>
        /// Fills the accessory list up to the nine slots a 0.4.6 avatar always had.
        /// </summary>
        /// <remarks>
        /// Every 0.4.6 NPC settings asset carried six to nine accessory entries, the unused ones with an empty
        /// path, and mods index into them: Cartel Enforcer's boss writes slots 0 to 2 of a copy. An empty path
        /// is skipped by <see cref="Translate"/>, so the padding puts nothing on.
        /// </remarks>
        private static AvatarSettings PadAccessories(AvatarSettings settings)
        {
            while (settings.AccessorySettings.Count < AccessorySlotCount)
                settings.AccessorySettings.Add(Accessory("", Color.white));
            return settings;
        }

        /// <summary>
        /// An AvatarSettings that says what a 0.4.7 body looks like, with a copy of that body parked in it.
        /// </summary>
        /// <remarks>
        /// Everything the lists cannot name rides along in <c>EquivalentNakedAppearance</c>, the field 0.4.7
        /// added for this, and a load takes it from there.
        /// </remarks>
        private static AvatarSettings DescribeBody(object naked)
        {
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

            var parked = ScriptableObject.CreateInstance(Il2CppInterop.Runtime.Il2CppType.From(_nakedObject));
            var parkedNaked = Cast(parked, _nakedObject);
            Set(parkedNaked, "Appearance", Call(naked, "Clone"));
            Set(settings, "EquivalentNakedAppearance", parkedNaked);
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

        // ------------------------------------------------------------------ LoadNakedSettings

        /// <summary>
        /// <c>Avatar.LoadNakedSettings(settings, keepOldLayers, maxLayerOrder)</c>: a new body under the
        /// accessories the avatar already wears.
        /// </summary>
        /// <remarks>
        /// 0.4.6 replaced body, hair and face, repainted the body layers from the settings - plus the layers
        /// already on, when asked to keep them - up to the given order, and did not touch the accessories
        /// (Avatar.cs:356-385, :600-648 on 0.4.6f13). Its cartel goons used it to put a pool body under the
        /// clothes loaded a line earlier (CartelGoon.cs:336-337).
        ///
        /// On 0.4.7 a clothing layer is a worn object like an accessory, so the worn list is split by what
        /// each entry was in 0.4.6: accessories stay, clothing layers stay only when the old layers are kept,
        /// and an object with no 0.4.6 counterpart stays, because nothing says which of the two it is.
        /// </remarks>
        private static bool LoadNaked(Il2CppScheduleOne.AvatarFramework.Avatar __instance, AvatarSettings settings,
                                      bool keepOldLayers, int maxLayerOrder)
        {
            if (settings == null)
            {
                // 0.4.6 did the same: warned and changed nothing (Avatar.cs:358 on 0.4.6f13).
                _log?.Warning($"[fix] avatar-settings-bridge: LoadNakedSettings(null) on {Who(__instance)}; ignored.");
                return false;
            }

            try { ApplyNaked(__instance, settings, keepOldLayers, maxLayerOrder); }
            catch (Exception e)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)} could not take its "
                            + "naked settings: " + e);
            }
            return false;
        }

        private static void ApplyNaked(Il2CppScheduleOne.AvatarFramework.Avatar avatar, AvatarSettings settings,
                                       bool keepOldLayers, int maxOrder)
        {
            var appearance = Get(avatar, "Appearance");
            if (appearance == null)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: {Who(avatar)} has no Appearance; naked settings not applied.");
                return;
            }

            var index = Legacy.Index;
            var look = Translate(settings, Who(avatar), maxOrder);
            bool Kept(Legacy layer) => keepOldLayers && Fits(layer.Order, maxOrder);

            // The layers 0.4.6 counted as body (tattoos, underwear) live in the applied body on 0.4.7.
            var before = Get(appearance, "AppliedNakedAppearance");
            if (before != null)
                foreach (var entry in Objects(Get(before, "AvatarObjects")))
                    if (index.ById.TryGetValue(ObjectId(entry), out var legacy) && legacy.Kind == Kind.Body && Kept(legacy))
                        Call(look.Naked, "AddAvatarObject", entry);

            Worn.TryGetValue(Instance(appearance), out var wornBefore);
            var worn = Activator.CreateInstance(_list);
            int dropped = 0;
            foreach (var entry in wornBefore ?? new List<object>())
            {
                bool layer = index.ById.TryGetValue(ObjectId(entry), out var legacy) && legacy.Kind == Kind.Body;
                if (layer && !Kept(legacy)) { dropped++; continue; }
                Call(worn, "Add", entry);
            }
            foreach (var layer in look.Clothes) Call(worn, "Add", layer);

            _applying = true;
            try
            {
                Call(appearance, "ApplyNakedAppearance", look.Naked);
                if (dropped > 0 || look.Clothes.Count > 0) Call(appearance, "ApplyOutfit", worn);
            }
            finally { _applying = false; }

            // 0.4.6 kept a copy with the old layers added as CurrentSettings; here it is read back off the avatar.
            Applied.Remove(avatar.GetInstanceID());
            _log?.Msg($"[fix] avatar-settings-bridge: {Who(avatar)} took the body '{settings.name}' "
                    + $"(clothing layers: {dropped} taken off, {look.Clothes.Count} put on).");
        }

        /// <summary>0.4.6's order test: no limit below zero (Avatar.cs:630 on 0.4.6f13).</summary>
        private static bool Fits(int order, int maxOrder) => maxOrder <= -1 || order <= maxOrder;

        // ------------------------------------------------------------------ IsMale, IsWhite

        /// <summary>
        /// <c>Avatar.IsMale()</c>: CurrentSettings.Gender below 0.5, and true without settings
        /// (Avatar.cs:185-192 on 0.4.6f13).
        /// </summary>
        /// <remarks>
        /// CurrentSettings is the settings last loaded, or else the applied body, so the gender is read from
        /// the same two places without building a settings object for every question.
        /// </remarks>
        private static bool IsMale(Il2CppScheduleOne.AvatarFramework.Avatar __instance, ref bool __result)
        {
            try
            {
                if (Applied.TryGetValue(__instance.GetInstanceID(), out var kept) && kept != null)
                    __result = kept.Gender < 0.5f;
                else
                {
                    var naked = AppliedBody(__instance);
                    __result = naked == null || (float)Get(naked, "Gender") < 0.5f;
                }
            }
            catch (Exception e)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: IsMale on {Who(__instance)} could not read the body; "
                            + "answering true: " + e.Message);
                __result = true;
            }
            return false;
        }

        /// <summary>
        /// <c>Avatar.IsWhite()</c>: r + g + b of CurrentSettings.SkinColor above 1.5, and true without settings
        /// (Avatar.cs:194-201 on 0.4.6f13).
        /// </summary>
        private static bool IsWhite(Il2CppScheduleOne.AvatarFramework.Avatar __instance, ref bool __result)
        {
            try
            {
                Color? skin = null;
                if (Applied.TryGetValue(__instance.GetInstanceID(), out var kept) && kept != null) skin = kept.SkinColor;
                else if (AppliedBody(__instance) is { } naked) skin = (Color)Get(naked, "SkinColor");
                __result = skin == null || skin.Value.r + skin.Value.g + skin.Value.b > 1.5f;
            }
            catch (Exception e)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: IsWhite on {Who(__instance)} could not read the body; "
                            + "answering true: " + e.Message);
                __result = true;
            }
            return false;
        }

        private static object AppliedBody(Il2CppScheduleOne.AvatarFramework.Avatar avatar)
        {
            var appearance = Get(avatar, "Appearance");
            return appearance == null ? null : Get(appearance, "AppliedNakedAppearance");
        }

        // ------------------------------------------------------------------ ApplyBodySettings

        /// <summary>
        /// <c>Avatar.ApplyBodySettings(settings)</c>: gender, weight, skin colour and height from the settings,
        /// the rest of the look untouched.
        /// </summary>
        /// <remarks>
        /// 0.4.6 set the four and copied the skin colour into CurrentSettings (Avatar.cs:387-398 on 0.4.6f13).
        /// On 0.4.7 the four live in the applied NakedAppearance, which the avatar re-reads every frame, so a
        /// copy of it takes the new values and is applied without its avatar objects
        /// (AvatarAppearance.cs:227-246 on 0.4.7f12). Setting them one by one through SetWeight or SetGender
        /// would last a frame.
        /// </remarks>
        private static bool ApplyBody(Il2CppScheduleOne.AvatarFramework.Avatar __instance, AvatarSettings settings)
        {
            if (settings == null)
            {
                // 0.4.6 threw here.
                _log?.Warning($"[fix] avatar-settings-bridge: ApplyBodySettings(null) on {Who(__instance)}; ignored.");
                return false;
            }

            try
            {
                var appearance = Get(__instance, "Appearance");
                var before = appearance == null ? null : Get(appearance, "AppliedNakedAppearance");
                if (before == null)
                {
                    _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)} has no body yet; "
                                + "ApplyBodySettings not applied.");
                    return false;
                }

                var naked = Call(before, "Clone");
                Set(naked, "Height", settings.Height * HeightScale);
                Set(naked, "Gender", settings.Gender);
                Set(naked, "Weight", settings.Weight);
                Set(naked, "SkinColor", settings.SkinColor);
                Call(appearance, "ApplyNakedAppearanceExcludingAvatarObjects", naked, true);

                if (Applied.TryGetValue(__instance.GetInstanceID(), out var kept) && kept != null)
                    kept.SkinColor = settings.SkinColor;
                _log?.Msg($"[fix] avatar-settings-bridge: {Who(__instance)} took height {settings.Height:0.##}, "
                        + $"weight {settings.Weight:0.##}, gender {settings.Gender:0.##} from '{settings.name}'.");
            }
            catch (Exception e)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)} could not take its body settings: " + e);
            }
            return false;
        }

        // ------------------------------------------------------------------ ApplyAccessorySettings

        /// <summary>
        /// <c>Avatar.ApplyAccessorySettings(settings)</c>: the accessories become the first nine in the settings,
        /// clothing stays.
        /// </summary>
        /// <remarks>
        /// 0.4.6 kept nine accessory slots apart from the body and clothing layers (Avatar.cs:674-725 on
        /// 0.4.6f13). On 0.4.7 accessories and clothing are one outfit, so the worn list is split by what each
        /// entry was in 0.4.6: accessories are replaced, everything else stays, including objects 0.4.6 had no
        /// counterpart for.
        /// </remarks>
        private static bool ApplyAccessories(Il2CppScheduleOne.AvatarFramework.Avatar __instance, AvatarSettings settings)
        {
            if (settings == null)
            {
                // 0.4.6 threw here.
                _log?.Warning($"[fix] avatar-settings-bridge: ApplyAccessorySettings(null) on {Who(__instance)}; ignored.");
                return false;
            }

            try
            {
                var appearance = Get(__instance, "Appearance");
                if (appearance == null)
                {
                    _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)} has no Appearance; "
                                + "accessories not applied.");
                    return false;
                }

                var index = Legacy.Index;
                var added = new List<object>();
                var missing = new List<string>();
                var list = settings.AccessorySettings;
                for (int i = 0; list != null && i < list.Count && i < AccessorySlotCount; i++)
                {
                    var accessory = list[i];
                    if (accessory == null || string.IsNullOrEmpty(accessory.path)) continue;
                    var equivalent = index.Equivalent(accessory.path);
                    if (equivalent == null) { missing.Add(accessory.path); continue; }
                    added.Add(Call(equivalent, "SerializeWithPrimaryColor", accessory.color));
                }
                if (missing.Count > 0)
                    _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)}: {missing.Count} accessory(s) have "
                                + "no 0.4.7 equivalent and were left off: " + string.Join(", ", missing.Distinct()));

                if (!Worn.TryGetValue(Instance(appearance), out var wornBefore))
                {
                    // Without the worn list the old accessories cannot be told from the clothes, and replacing
                    // the outfit would undress the character; the new ones go on top.
                    foreach (var entry in added) Call(appearance, "ApplyWornObject", entry, true);
                    _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)}'s outfit is not known; "
                                + $"{added.Count} accessory(s) put on over what it wears.");
                    return false;
                }

                var worn = Activator.CreateInstance(_list);
                int removed = 0;
                foreach (var entry in wornBefore)
                {
                    if (index.ById.TryGetValue(ObjectId(entry), out var legacy) && legacy.Kind == Kind.Accessory)
                    {
                        removed++;
                        continue;
                    }
                    Call(worn, "Add", entry);
                }
                foreach (var entry in added) Call(worn, "Add", entry);
                Call(appearance, "ApplyOutfit", worn);
                _log?.Msg($"[fix] avatar-settings-bridge: {Who(__instance)} took the accessories of "
                        + $"'{settings.name}' ({removed} taken off, {added.Count} put on).");
            }
            catch (Exception e)
            {
                _log?.Warning($"[fix] avatar-settings-bridge: {Who(__instance)} could not take its accessories: " + e);
            }
            return false;
        }

        // ------------------------------------------------------------------ appliedAccessories

        /// <summary>
        /// <c>Avatar.appliedAccessories</c>: nine empty slots.
        /// </summary>
        /// <remarks>
        /// 0.4.6 kept the Accessory components it had instantiated there, null in an empty slot
        /// (Avatar.cs:102 on 0.4.6f13). 0.4.7 has no such components on an avatar, so every slot is empty,
        /// as on a 0.4.6 avatar without accessories, and a caller that walks the nine slots finds nothing
        /// instead of throwing on a null array.
        /// </remarks>
        private static bool AccessorySlots(ref Il2CppReferenceArray<Accessory> __result)
        {
            __result = new Il2CppReferenceArray<Accessory>(AccessorySlotCount);
            return false;
        }

        // ------------------------------------------------------------------ the goon pool

        /// <summary>
        /// <c>GoonPool.MaleBaseAppearances</c>: the pool's male bodies, each described as the AvatarSettings
        /// 0.4.6 kept there.
        /// </summary>
        /// <remarks>
        /// Built once per pool and handed out again, as 0.4.6 handed out one array. Each entry carries a copy
        /// of its 0.4.7 body, so loading it - or a copy of it made with Instantiate - gives the avatar that
        /// body back in full. The entries are kept across a scene's asset sweep; nothing else holds them.
        /// </remarks>
        private static bool GoonBases(Il2CppScheduleOne.Cartel.GoonPool __instance,
                                      ref Il2CppReferenceArray<AvatarSettings> __result)
        {
            try
            {
                int pool = __instance.GetInstanceID();
                if (_goonBases != null && _goonBasesFor == pool && _goonBases.All(entry => entry != null))
                {
                    __result = _goonBases;
                    return false;
                }

                var bases = new List<AvatarSettings>();
                foreach (var entry in Objects(Get(__instance, "_maleAppearances")))
                {
                    var naked = entry == null ? null : Get(entry, "Appearance");
                    if (naked == null)
                    {
                        _log?.Warning("[fix] avatar-settings-bridge: a male goon appearance holds no body; left out.");
                        continue;
                    }
                    var settings = PadAccessories(DescribeBody(naked));
                    settings.name = ((UnityEngine.Object)entry).name;
                    settings.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    bases.Add(settings);
                }

                _goonBases = new Il2CppReferenceArray<AvatarSettings>(bases.ToArray());
                _goonBasesFor = pool;
                __result = _goonBases;
                _log?.Msg($"[fix] avatar-settings-bridge: the goon pool's {bases.Count} male bodies are described "
                        + "as base appearances.");
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] avatar-settings-bridge: the goon pool's base appearances could not be built: " + e);
                __result = null;
            }
            return false;
        }

        /// <summary>
        /// A body and an outfit in the 0.4.7 form, the clothing layers among the outfit, and whether the
        /// outfit replaces the one worn.
        /// </summary>
        private sealed class Look
        {
            internal object Naked;
            internal object Worn;
            internal List<object> Clothes;
            internal bool ReplacesOutfit;
        }

        /// <param name="maxOrder">Body layers above this order are left out; no limit below zero.</param>
        private static Look Translate(AvatarSettings settings, string who, int maxOrder = -1)
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
            var clothes = new List<object>();
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
                int order = index.Order(layer.layerPath);
                if (!Fits(order, maxOrder)) continue;
                var serialized = Call(equivalent, "SerializeWithPrimaryColor", layer.layerTint);
                if (order > NakedMaxOrder) { Call(worn, "Add", serialized); clothes.Add(serialized); fromLists++; }
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
                Clothes = clothes,
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
