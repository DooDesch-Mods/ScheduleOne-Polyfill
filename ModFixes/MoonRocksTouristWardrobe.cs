using System.Reflection;
using Il2CppScheduleOne.Clothing;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Moon Rocks' tourists dress from the game's clothing again, hats and sunglasses included.
    /// </summary>
    /// <remarks>
    /// The mod builds its wardrobe by walking the item registry and reading each clothing item's
    /// <c>ClothingAssetPath</c> and <c>ApplicationType</c>. 0.4.7 removed both for
    /// <c>ClothingAvatarObject</c> (ClothingDefinition.cs:16 on 0.4.7f12), and the plugin puts them back
    /// answering nothing, so the wardrobe comes out empty.
    ///
    /// The legacy assets still point at the object that replaced them (see <see cref="AvatarSettingsBridge"/>), so
    /// the object's id gives the old path and the old kind back. The looks the mod builds from those paths go
    /// through the same translation as every other AvatarSettings.
    ///
    /// Only for this mod. Police Response Overhaul reads the same two names and paints a texture layer when it gets
    /// a path, which has no 0.4.7 counterpart; it keeps the empty answer it was checked against.
    /// </remarks>
    internal sealed class MoonRocksTouristWardrobe : Fix
    {
        internal override string Id => "moonrocks-tourist-wardrobe";
        internal override string Mod => "Moon Rocks";

        /// <summary>Written against 1.1.0. A later build may read ClothingAvatarObject itself.</summary>
        internal override string ModVersions => "<=1.1.0";

        /// <summary>0.4.7f5 is the first build without the clothing paths.</summary>
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "the moon's tourists wear their own clothes, hats and sunglasses";

        internal override string StandsDownBecause
            => "Moon Rocks' tourists wear what the person they were copied from wears, with no hats or sunglasses.";

        /// <summary>EClothingApplicationType on 0.4.6 (EClothingApplicationType.cs on 0.4.6f13).</summary>
        private const int BodyLayer = 0, Accessory = 2;

        private static MelonLogger.Instance _log;
        private static PropertyInfo _avatarObject;
        private static bool _reportedFailure;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var clothing = typeof(ClothingDefinition);
            var path = clothing.GetField("<polyfill>get_ClothingAssetPath", BindingFlags.Public | BindingFlags.Static);
            var kind = clothing.GetField("<polyfill>get_ApplicationType", BindingFlags.Public | BindingFlags.Static);
            _avatarObject = clothing.GetProperty("ClothingAvatarObject");
            if (path == null || kind == null || _avatarObject == null)
            {
                log.Warning($"[fix] {Id}: something it needs is not here (path slot {path != null}, kind slot "
                          + $"{kind != null}, ClothingAvatarObject {_avatarObject != null}).");
                return false;
            }

            path.SetValue(null, new Func<object, object>(item => Find(item)?.Path));
            kind.SetValue(null, new Func<object, object>(item => Find(item)?.Kind == AvatarSettingsBridge.Kind.Accessory ? Accessory : BodyLayer));
            return true;
        }

        private static AvatarSettingsBridge.Legacy Find(object clothing)
        {
            try
            {
                var worn = _avatarObject.GetValue(clothing);
                var id = worn?.GetType().GetProperty("Id")?.GetValue(worn) as string;
                return id != null && AvatarSettingsBridge.Legacy.Index.ById.TryGetValue(id, out var legacy) ? legacy : null;
            }
            catch (Exception e)
            {
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    _log?.Warning("[fix] moonrocks-tourist-wardrobe: a clothing item could not be read, it stays out of "
                                + "the wardrobe: " + e);
                }
                return null;
            }
        }
    }
}
