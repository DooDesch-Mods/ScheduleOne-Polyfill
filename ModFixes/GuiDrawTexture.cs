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
    /// are all still real - <c>GUI.CalculateScaledTextureRects</c> is the game's own scaling maths, and
    /// <c>GUI.DrawTextureWithTexCoords</c> ends in <c>Graphics.Internal_DrawTexture</c>, a native call
    /// that survived. Put together they are what <c>DrawTexture</c> did.
    ///
    /// BORDERS ARE NOT DRAWN. The border colours and radii of the wider overloads have no equivalent in
    /// what is left, so a call carrying them gets the texture without its frame. That is a visible
    /// difference and it is said out loud in the log rather than passed off as a repair; a crosshair
    /// with no border still aims, and the mods reporting this pass none.
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

        internal override string What
            => "mods that draw an image over the screen with GUI.DrawTexture draw it instead of throwing";

        internal override string StandsDownBecause
            => "GUI.DrawTexture is a stub that throws in this build, and a mod calling it from OnGUI "
             + "throws once per frame until the game dies.";

        private const string StubMessage = "Method unstripping failed";

        private static MelonLogger.Instance _log;
        private static bool _saidBorders;
        private static bool _saidFailed;

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

        /// <summary>Draw what the original would have drawn, out of the parts that still work.</summary>
        private static bool Draw(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend,
                                 float imageAspect, Color leftColor, Vector4 borderWidths)
        {
            try
            {
                if (image == null) return false;

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

                // The tint is the argument, and for every overload that does not take one the argument IS
                // GUI.color - so restoring it afterwards makes those calls a no-op rather than a change.
                var was = GUI.color;
                GUI.color = leftColor;
                try { GUI.DrawTextureWithTexCoords(screenRect, image, sourceRect, alphaBlend); }
                finally { GUI.color = was; }
            }
            catch (Exception e)
            {
                if (!_saidFailed)
                {
                    _saidFailed = true;
                    _log?.Warning("[fix] gui-drawtexture: drawing failed and the image is missing rather "
                                + "than the game dying: " + e.Message);
                }
            }

            // Never the original. It exists only to throw, and letting it run is the crash this repairs.
            return false;
        }
    }
}
