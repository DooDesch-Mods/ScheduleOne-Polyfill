using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// The sleeping app gets its two fonts, so it stops waiting for them.
    /// </summary>
    /// <remarks>
    /// More Realistic Sleeping borrows its fonts off two vanilla phone apps, by reading a Text out of a
    /// screen:
    ///
    /// <code>
    /// GetAppCanvasByName("DeliveryApp/Container/Scroll View/Viewport/Content/Albert Hoover/Header")
    ///     .GetComponentInChildren&lt;Text&gt;().font
    /// </code>
    ///
    /// That entry is not there any more. The shop rows sit under <c>Order</c> on 0.4.6 - measured on
    /// 0.4.6f13, where "Albert Hoover" exists with <c>Order</c> as its parent - and it is inactive until
    /// the player knows the supplier, so even reaching it would give no Text to read a font off.
    /// <c>GetAppCanvasByName</c> casts the result of <c>transform.Find</c> without a check, so a sub-path
    /// that misses throws rather than returning null.
    ///
    /// WHAT IT COSTS IS THE WHOLE MOD, not a font. InitializeFonts asks for the bold one first and the
    /// semibold one second, so the second call throws, openSansSemiBoldIsInitialized is never set, and
    /// InitializeLaunderApp waits on exactly that flag - forever, one "Waiting for Fonts to be loaded..."
    /// every two seconds for the rest of the session.
    ///
    /// THE SCREEN WAS NEVER THE POINT. The method is called FindFontFromOtherApp and its two arguments name
    /// what it wants: openSansBold and openSansSemiBold. The game ships those as OpenSans-Bold and
    /// OpenSans-SemiBold, and they are in memory - so this asks for the font by name instead of walking a
    /// UI to a Text that happens to use it. It is the same font the old path led to, found in a way no
    /// rearranged screen can break.
    ///
    /// NOT FOUND IS SAID, NOT GUESSED. A name this fix does not know is handed back to the mod. A name it
    /// knows but cannot find gives null and no flag, which is what the mod itself does when the Text is
    /// missing - the app keeps waiting, and the log says which font was not loaded rather than throwing a
    /// NullReferenceException with nothing on it.
    ///
    /// WHAT THIS DOES NOT REPAIR, and it is the next thing the mod hits: CreateApp clones DeliveryApp and
    /// ProductManagerApp and then renames the SECOND home-screen icon whose label starts with "Deliveries"
    /// - and there is no second one. An icon is made in <c>App&lt;T&gt;.OnStartClient</c>
    /// (ScheduleOne.UI/App.cs:66), a FishNet callback that never fires for an object Instantiate made, so
    /// the clone never gets one. That code is identical in 0.4.5f2, so it is the mod's own technique and
    /// not something this update took away; the mod logs "Index 1 is out of range" and stops there.
    /// </remarks>
    internal sealed class MoreRealisticSleepingPhoneFonts : Fix
    {
        internal override string Id => "mrs-phone-fonts";
        internal override string Mod => "MoreRealisticSleeping";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override string What
            => "the sleeping app gets the two phone fonts it waits for, instead of waiting for the rest "
             + "of the session";

        internal override string StandsDownBecause
            => "More Realistic Sleeping reads a font off a delivery-shop row that 0.4.6 moved and leaves "
             + "switched off - so its font loader throws and the app waits for a font that never loads.";

        /// <summary>What the mod asks for, and what the game calls it.</summary>
        private static readonly Dictionary<string, string> Fonts = new(StringComparer.Ordinal)
        {
            ["openSansBold"] = "OpenSans-Bold",
            ["openSansSemiBold"] = "OpenSans-SemiBold",
        };

        private static MelonLogger.Instance _log;
        private static System.Type _fontType;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var target = AccessTools.Method("MoreRealisticSleeping.Util.FontLoader:FindFontFromOtherApp");
            if (target == null)
            {
                log.Msg("[fix] mrs-phone-fonts: More Realistic Sleeping is not loaded, or does not have "
                      + "FontLoader.FindFontFromOtherApp - nothing was changed.");
                return false;
            }

            _fontType = AccessTools.TypeByName("UnityEngine.Font");
            if (_fontType == null)
            {
                log.Warning("[fix] mrs-phone-fonts: UnityEngine.Font is not resolvable here, so the mod's "
                          + "fonts were left alone.");
                return false;
            }

            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                    target, prefix: new HarmonyMethod(typeof(MoreRealisticSleepingPhoneFonts),
                                                      nameof(Instead)));
            }
            catch (Exception e)
            {
                log.Warning("[fix] mrs-phone-fonts: could not replace FontLoader.FindFontFromOtherApp, so "
                          + "the app still waits for its fonts: " + e.Message);
                return false;
            }

            log.Msg("[fix] mrs-phone-fonts: More Realistic Sleeping asks the game for OpenSans-Bold and "
                  + "OpenSans-SemiBold by name, not by walking a screen that has been rearranged.");

            // WHETHER THE MOD HAS ALREADY TRIED CANNOT BE READ OFF A FLAG - false means "not yet" and
            // "died" alike. The fonts are the tell: they are in memory once the phone has loaded, which is
            // after the mod's loader would have run. No fonts means no phone means the mod's own first
            // look is still ahead of it, and the patch above is the whole repair.
            if (Named("OpenSans-Bold") == null) return true;
            Prime(log);
            return true;
        }

        /// <summary>
        /// Load the two fonts now, because the mod will not try again.
        /// </summary>
        /// <remarks>
        /// InitializeFonts is started once, from InitializeLaunderApp, and nothing restarts it - so on a
        /// session where it has already thrown, a repaired FindFontFromOtherApp is never called again and
        /// the app waits on its flag for the rest of the session. Measured: the loader threw at 06:17:23
        /// and the fixes ran at 06:17:33.
        ///
        /// Both fields are public and static, and so is the method, so this does exactly what the mod's own
        /// coroutine does at a moment when it works.
        /// </remarks>
        private static void Prime(MelonLogger.Instance log)
        {
            var load = AccessTools.Method("MoreRealisticSleeping.Util.FontLoader:FindFontFromOtherApp");
            var bold = AccessTools.Field("MoreRealisticSleeping.Util.FontLoader:openSansBold");
            var semi = AccessTools.Field("MoreRealisticSleeping.Util.FontLoader:openSansSemiBold");
            var boldReady = AccessTools.Field("MoreRealisticSleeping.Util.FontLoader:openSansBoldIsInitialized");
            var semiReady = AccessTools.Field("MoreRealisticSleeping.Util.FontLoader:openSansSemiBoldIsInitialized");
            if (load == null || bold == null || semi == null || boldReady == null || semiReady == null)
            {
                log.Warning("[fix] mrs-phone-fonts: FontLoader is not the shape this reads, so the fonts "
                          + "were not loaded again - the app will only open after a restart.");
                return;
            }

            try
            {
                bold.SetValue(null, load.Invoke(null, new object[] { "openSansBold" }));
                semi.SetValue(null, load.Invoke(null, new object[] { "openSansSemiBold" }));
            }
            catch (Exception e)
            {
                log.Warning("[fix] mrs-phone-fonts: loading the fonts again failed, so the app keeps "
                          + "waiting: " + (e.InnerException ?? e).Message);
                return;
            }

            if (boldReady.GetValue(null) is true && semiReady.GetValue(null) is true)
                log.Msg("[fix] mrs-phone-fonts: both phone fonts are loaded; the app is no longer waiting.");
            else
                log.Warning("[fix] mrs-phone-fonts: the phone fonts did not load, so the app keeps waiting "
                          + "for them.");
        }

        /// <summary>The font the mod asked for, by the name the game gives it.</summary>
        private static bool Instead(string fontName, ref Font __result)
        {
            if (fontName == null || !Fonts.TryGetValue(fontName, out string asset))
                return true;                                  // not one of the two; leave the mod to it

            __result = Named(asset);
            if (__result == null)
            {
                // SAID, NOT SWALLOWED, and not thrown either. The mod's own miss branch returns a fallback
                // and leaves the flag alone, which is what this does - the difference is that the log now
                // says which font, instead of a NullReferenceException from inside a dead coroutine.
                _log.Warning("[fix] mrs-phone-fonts: " + asset + " is not loaded, so " + fontName
                           + " stays unset and the app keeps waiting.");
                return false;
            }

            var ready = AccessTools.Field("MoreRealisticSleeping.Util.FontLoader:" + fontName
                                        + "IsInitialized");
            if (ready == null)
            {
                _log.Warning("[fix] mrs-phone-fonts: FontLoader has no " + fontName + "IsInitialized, so "
                           + "the font was handed back but the app will keep waiting.");
                return false;
            }

            ready.SetValue(null, true);
            return false;
        }

        /// <summary>A loaded font of that name, or null.</summary>
        /// <remarks>
        /// FindObjectsOfTypeAll and not FindObjectsOfType: a font is an asset, not something in the scene,
        /// and the scene search would never see it.
        /// </remarks>
        private static Font Named(string name)
        {
            var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(_fontType));
            if (all == null) return null;

            foreach (var one in all)
            {
                if (one == null || one.name != name) continue;
                var font = one.TryCast<Font>();
                if (font != null) return font;
            }
            return null;
        }
    }
}
