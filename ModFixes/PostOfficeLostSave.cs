using System.IO;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Persistence.Loaders;
using Il2CppScheduleOne.Property;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A save that lost its post office gets the post office a new game starts with.
    /// </summary>
    /// <remarks>
    /// A business is one file, <c>Businesses/&lt;name&gt;.json</c>, holding whether it is owned and every
    /// object placed in it. Each save first deletes every file in <c>Businesses/</c> and then writes one per
    /// business that is alive at that moment (ISaveable.cs:110-143, BusinessManager.cs:48-63 on 0.4.7f12).
    /// So a session in which the post office object was gone - a building mod's site clearing destroys a
    /// whole LOD root when one of its renderers stands in the cleared area - writes a save without
    /// <c>Post Office.json</c>, and every later load has an unowned, empty post office.
    ///
    /// For a property that loses its file, the game has its own way back: <c>ApplyPropertyDefaultSave</c>
    /// loads the file from StreamingAssets/DefaultSave (BusinessesLoader.cs:36-49). The post office does not
    /// carry that component, because a new game always copies its file in (SetupScreen.cs:69-70). Its
    /// laundering station is not sold in any shop (ShopCategories is empty), so without the file the post
    /// office cannot launder again.
    ///
    /// After the businesses load, and only when no file in the save's <c>Businesses/</c> names the post
    /// office, this loads the game's own new-game file for it the way the default-save path does: laundering
    /// station and storage rack, not owned. A save that has its post office file is left alone.
    /// </remarks>
    internal sealed class PostOfficeLostSave : Fix
    {
        internal override string Id => "post-office-lost-save";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What => "a post office missing from the save comes back with the laundering station a new game starts with";

        internal override string StandsDownBecause
            => "a save that lost its post office file loads an empty post office with no laundering station, and no shop sells one.";

        private const string Code = "postoffice";

        private static MelonLogger.Instance _log;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var load = AccessTools.DeclaredMethod(typeof(BusinessesLoader), nameof(BusinessesLoader.Load), new[] { typeof(string) });
            if (load == null)
            {
                log.Warning("[fix] post-office-lost-save: BusinessesLoader.Load(string) is not where it was, so a "
                          + "lost post office stays empty.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                load, postfix: new HarmonyMethod(typeof(PostOfficeLostSave), nameof(AfterLoad)));
            return true;
        }

        private static void AfterLoad(string mainPath)
        {
            try
            {
                if (string.IsNullOrEmpty(mainPath) || !Directory.Exists(mainPath))
                {
                    _log?.Msg("[fix] post-office-lost-save: this save has no Businesses folder; left to the game.");
                    return;
                }

                // An older save keeps a business as a folder, not a file; the game reads those itself.
                var files = Directory.GetFiles(mainPath);
                if (files.Length == 0)
                {
                    _log?.Msg("[fix] post-office-lost-save: no business files in this save; left to the game.");
                    return;
                }
                foreach (var file in files)
                    if (File.ReadAllText(file).Contains("\"" + Code + "\"")) return;   // the save has it

                Business postOffice = null;
                foreach (var business in Business.Businesses)
                    if (business != null && business.PropertyCode == Code) { postOffice = business; break; }
                if (postOffice == null)
                {
                    _log?.Warning("[fix] post-office-lost-save: the save has no post office file and the map has no "
                                + "post office either; nothing to set up.");
                    return;
                }
                if (postOffice.GetComponent<ApplyPropertyDefaultSave>() != null)
                {
                    _log?.Msg("[fix] post-office-lost-save: the post office has the game's own default save now; left to the game.");
                    return;
                }

                var path = Path.Combine(Application.streamingAssetsPath, "DefaultSave", "Businesses", "Post Office.json");
                if (!File.Exists(path))
                {
                    _log?.Warning($"[fix] post-office-lost-save: the save lost its post office, and the new-game file "
                                + $"is not at {path}; it stays empty.");
                    return;
                }

                var contents = File.ReadAllText(path);
                var data = JsonUtility.FromJson(contents, Il2CppType.Of<BusinessData>())?.TryCast<BusinessData>();
                if (data == null || data.PropertyCode != Code)
                {
                    _log?.Warning($"[fix] post-office-lost-save: {path} does not read as the post office; it stays empty.");
                    return;
                }

                new BusinessLoader().Load(data, contents);
                _log?.Warning($"[fix] post-office-lost-save: this save had no post office file, so the post office "
                            + $"was set up as a new game has it ({data.Objects?.Length ?? 0} object(s), not owned).");
            }
            catch (System.Exception e)
            {
                _log?.Error("[fix] post-office-lost-save: could not set up the lost post office: " + e);
            }
        }
    }
}
