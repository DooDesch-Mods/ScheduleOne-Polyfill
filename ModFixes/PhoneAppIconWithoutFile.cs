using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A phone app that ships its icon as a sprite instead of a file still registers.
    /// </summary>
    /// <remarks>
    /// S1API offers an app two ways to set its icon: a file name it reads out of the Mods folder, or a
    /// <c>Sprite</c> the app supplies itself. Over The Counter's GreenTab uses the second and returns
    /// <c>null</c> for the first, which is what the API's own signature invites:
    /// <code>
    /// GreenTabApp.cs:568   protected override string IconFileName =&gt; null;
    /// GreenTabApp.cs:570   protected override Sprite IconSprite   =&gt; LoadIcon("GreenTabIcon");
    /// </code>
    /// The sprite path wins while the sprite is there. When it is not - and the reporter's repro is
    /// exactly that, go to the main menu and resume - S1API falls back to the file name and reaches
    /// <c>Path.Combine(ModsDirectory, null)</c>, which throws instead of failing the way a missing file
    /// does (PhoneApp.cs:352-366 and 648):
    /// <code>
    /// [PhoneApp] Failed to register OverTheCounter.Apps.GreenTabApp:
    ///            Value cannot be null. (Parameter 'path2')
    /// </code>
    /// The throw comes out of registration, so the whole app is gone rather than the icon.
    ///
    /// This makes the null behave like the missing file the very next line checks for: no icon, a line
    /// in the log, and an app the player can open. It is the same repair S1API wants upstream,
    /// applied where a player can have it today.
    ///
    /// NOT VERSION-SPECIFIC, and that is why the window is open: nothing about this changed with the
    /// game. Any app pairing a sprite with a null file name meets it on any build.
    /// </remarks>
    internal sealed class PhoneAppIconWithoutFile : Fix
    {
        internal override string Id => "phoneapp-icon-without-file";
        internal override string Mod => "S1API";
        internal override string ModVersions => "*";
        internal override string GameVersions => "*";

        internal override string What
            => "a phone app whose icon is a sprite rather than a file registers instead of throwing";

        internal override string StandsDownBecause
            => "a phone app that supplies its icon as a sprite and no file name does not register at "
             + "all once the sprite is unavailable - GreenTab is the reported one.";

        private static MelonLogger.Instance _log;
        private static bool _said;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var phoneApp = AccessTools.TypeByName("S1API.PhoneApp.PhoneApp");
            if (phoneApp == null) return false;                       // S1API is not installed

            // Shape AND parameter name, checked rather than assumed: this is a private method of somebody
            // else's library, Harmony binds a prefix argument BY NAME, and a rename would arrive as a patch
            // that will not compile rather than as a null here.
            var target = AccessTools.Method(phoneApp, "ChangeAppIconImage");
            var parameters = target?.GetParameters();
            if (target == null || target.ReturnType != typeof(bool)
                || parameters.Length != 2
                || parameters[1].ParameterType != typeof(string)
                || parameters[1].Name != "filename")
            {
                log.Warning("[fix] phoneapp-icon-without-file: S1API's icon loader is not the two-argument "
                          + "method this knows, so an app with no icon file still fails to register.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.phoneappicon").Patch(target,
                prefix: new HarmonyMethod(typeof(PhoneAppIconWithoutFile), nameof(NoFileNoThrow)));
            return true;
        }

        /// <summary>Answer for the icon that has no file, the way a missing file is answered.</summary>
        private static bool NoFileNoThrow(string filename, ref bool __result)
        {
            if (!string.IsNullOrEmpty(filename)) return true;

            __result = false;
            if (!_said)
            {
                _said = true;
                _log?.Msg("[fix] phoneapp-icon-without-file: an app asked for its icon by a file name it "
                        + "does not have. It keeps the icon the game gives it and registers.");
            }
            return false;
        }
    }
}
