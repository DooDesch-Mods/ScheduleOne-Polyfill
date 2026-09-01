using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// An overlay drawn with <c>GUI.DrawTexture</c> appears instead of throwing every frame.
    /// </summary>
    /// <remarks>
    /// <c>UnityEngine.GUI.DrawTexture</c> is not in this build. Eleven overloads survive as managed
    /// delegation and every one of them funnels into the twelfth, which is
    /// <code>
    /// // UnityEngine.IMGUIModule, GUI.DrawTexture(Rect, Texture, ScaleMode, bool, float,
    /// //                                          Color, Color, Color, Color, Vector4, Vector4, bool)
    /// throw new System.NotSupportedException("Method unstripping failed");
    /// </code>
    /// so a mod calling the two-argument form gets the same exception as one calling the full one, and
    /// it gets it from <c>OnGUI</c> - once per frame, forever. Measured on 2026-08-03: 3205 error lines
    /// in a few seconds, and the process gone. The index has it from two mods at once, ClockOverlay and
    /// EZCrosshair, both drawing a plain image at a plain rect.
    ///
    /// NOTHING IS MISSING FROM THE GAME HERE, which is why the load check reads clean and no bridge
    /// applies: the method is present, it is a stub, and only calling it says so. The pieces it needs
    /// are all still real: <c>GUI.CalculateScaledTextureRects</c> is the game's own scaling maths, and
    /// <c>GUI.Box</c> draws a style whose background is a texture.
    ///
    /// NOT <c>GUI.DrawTextureWithTexCoords</c>, which is the obvious replacement and was the first one
    /// shipped. It does not throw, and it does not draw either - measured with a probe mod on
    /// 2026-08-29 against a <c>GUI.Box</c> control in the same <c>OnGUI</c>, which rendered fine. That
    /// left every overlay mod silent instead of crashing, which is the worse of the two.
    ///
    /// BORDERS ARE NOT DRAWN. The border colours and radii of the wider overloads have no equivalent in
    /// what is left, so a call carrying them gets the texture without its frame. That is a visible
    /// difference and it is said out loud in the log rather than passed off as a repair; a crosshair
    /// with no border still aims, and the mods reporting this pass none.
    ///
    /// AND IF IT CANNOT DRAW, IT GIVES THE EXCEPTION BACK. A fix that swallows the error without
    /// restoring the function is worse than no fix: the mod loads, nothing throws, and the overlay is
    /// simply not there - so the author is debugging an invisible feature instead of reading a stack
    /// trace. On the first failure this says why, marks itself failed in <c>polyfillfixes</c> and in
    /// the report, and steps aside so the original throws again as it did before Polyfill.
    ///
    /// The probe is the safety, not the version range. On a build where Unity ships the real method this
    /// fix finds no stub and does nothing, so it cannot replace a working implementation with a narrower
    /// one - which is the only way it could do harm.
    /// </remarks>
    internal sealed class GuiDrawTexture : Fix
    {
        internal override string Id => "gui-drawtexture";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";

        internal override bool NeedsAScreen => true;

        internal override string What
            => "mods that draw an image over the screen with GUI.DrawTexture draw it instead of throwing";

        internal override string StandsDownBecause
            => "GUI.DrawTexture is a stub that throws in this build, and a mod calling it from OnGUI "
             + "throws once per frame until the game dies.";

        private const string StubMessage = "Method unstripping failed";

        private static MelonLogger.Instance _log;
        private static bool _saidBorders;

        /// <summary>Why this stopped standing in, or null while it is working.</summary>
        /// <remarks>
        /// Once set the prefix steps aside and the original throws again. Deliberate: the exception is
        /// information, and a mod that draws nothing while reporting no error is a longer afternoon for
        /// its author than a stack trace with a name in it.
        /// </remarks>
        private static string _gaveUp;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var target = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture), new[]
            {
                typeof(Rect), typeof(Texture), typeof(ScaleMode), typeof(bool), typeof(float),
                typeof(Color), typeof(Color), typeof(Color), typeof(Color),
                typeof(Vector4), typeof(Vector4), typeof(bool),
            });

            if (target == null)
            {
                log.Warning("[fix] gui-drawtexture: the overload every other one calls is not where it "
                          + "was, so the family cannot be repaired in one place.");
                return false;
            }

            if (!IsStub(target))
            {
                log.Msg("[fix] gui-drawtexture: GUI.DrawTexture works in this build. Nothing to repair.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, prefix: new HarmonyMethod(typeof(GuiDrawTexture), nameof(Draw)));
            return true;
        }

        /// <summary>
        /// Is this method the interop stub, rather than an implementation?
        /// </summary>
        /// <remarks>
        /// Asked of the IL rather than assumed from the game version, because the alternative is a fix
        /// that quietly narrows a working method the day Unity ships it. The stub is three instructions -
        /// load the string, construct the exception, throw - so reading the string back out of the
        /// method's own module identifies it exactly and says nothing about anything else.
        ///
        /// A method with no readable body is NOT treated as a stub. Refusing there costs the repair on a
        /// runtime that hides IL; guessing there costs a working overload on every build.
        /// </remarks>
        private static bool IsStub(MethodBase method)
        {
            try
            {
                byte[] il = method.GetMethodBody()?.GetILAsByteArray();
                if (il == null || il.Length < 5) return false;

                var module = method.Module;
                for (int i = 0; i + 4 < il.Length; i++)
                {
                    if (il[i] != 0x72) continue;   // ldstr
                    int token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                    if (module.ResolveString(token) == StubMessage) return true;
                }
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] gui-drawtexture: could not read GUI.DrawTexture to see whether it is "
                            + "a stub, so it was left alone: " + e.Message);
            }
            return false;
        }

        /// <summary>Draw what the original would have drawn, or hand the exception back.</summary>
        private static bool Draw(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend,
                                 float imageAspect, Color leftColor, Vector4 borderWidths)
        {
            // Already established that this build cannot be stood in for. Let the original throw, which
            // is what it did before Polyfill and is at least true.
            if (_gaveUp != null) return true;

            if (image == null) return false;

            try
            {
                // GUIStyle backgrounds are Texture2D. TryCast rather than `as`, which returns null for a
                // live object across the interop boundary and would read as "no texture".
                var texture = image.TryCast<Texture2D>();
                if (texture == null)
                {
                    GiveUp("a mod drew a " + image.GetIl2CppType().Name + " rather than a Texture2D, and "
                         + "the only drawing call left in this build takes a Texture2D");
                    return true;
                }

                if (borderWidths != Vector4.zero && !_saidBorders)
                {
                    _saidBorders = true;
                    _log?.Msg("[fix] gui-drawtexture: a mod asked for a border around its texture. The "
                            + "border is not drawn - nothing left in this build can draw one - but the "
                            + "texture is.");
                }

                // Zero means "the texture's own shape", which is what every overload short of the widest
                // passes. CalculateScaledTextureRects divides by it, so it cannot be handed on as zero.
                float aspect = imageAspect;
                if (aspect <= 0f)
                {
                    int height = image.height;
                    aspect = height > 0 ? (float)image.width / height : 1f;
                }

                var screenRect = new Rect();
                var sourceRect = new Rect();
                if (!GUI.CalculateScaledTextureRects(position, scaleMode, aspect,
                                                     ref screenRect, ref sourceRect))
                    return false;

                // Built per call rather than cached. A GUIStyle held across frames is an interop wrapper
                // with nothing native behind it, and the next frame fails on a collected object - which
                // is exactly how the first version of this fix went quiet.
                var style = new GUIStyle();
                style.normal.background = texture;
                style.border = new RectOffset(0, 0, 0, 0);
                style.padding = new RectOffset(0, 0, 0, 0);
                style.margin = new RectOffset(0, 0, 0, 0);
                style.overflow = new RectOffset(0, 0, 0, 0);

                // The tint is the argument, and for every overload that does not take one the argument IS
                // GUI.color - so restoring it afterwards makes those calls a no-op rather than a change.
                var was = GUI.color;
                GUI.color = leftColor;
                try { GUI.Box(screenRect, GUIContent.none, style); }
                finally { GUI.color = was; }
            }
            catch (Exception e)
            {
                GiveUp("drawing through GUI.Box failed: " + e.GetType().Name + ": " + e.Message);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Stop standing in, say why, and make sure the report says so too.
        /// </summary>
        /// <remarks>
        /// Three places have to agree, because a player reads one of them and an author reads another:
        /// the log line, `polyfillfixes`, and the exported report. Leaving the state at "applied" while
        /// nothing is drawn is the lie this whole method exists to prevent.
        /// </remarks>
        private static void GiveUp(string reason)
        {
            if (_gaveUp != null) return;
            _gaveUp = reason;

            _log?.Error("[fix] gui-drawtexture: standing down - " + reason + ". GUI.DrawTexture will "
                      + "throw again from here on, which is what it did before Polyfill. An overlay that "
                      + "is simply missing is harder to report than one that crashes, so this says it "
                      + "rather than hiding it.");
            Fixes.Record("gui-drawtexture", "failed: " + reason);
        }
    }
}
