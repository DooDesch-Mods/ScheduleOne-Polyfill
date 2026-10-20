using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Frame the contact portraits S1API draws for its characters as tight as the game's own.
    /// </summary>
    /// <remarks>
    /// The game ships its mugshots as finished pictures: the face fills the frame. On 0.4.7 S1API draws the
    /// portrait of every character it adds (Static, Vic, Bella and the like) with a camera of its own,
    /// <c>AvatarCompatibility.CapturePortrait</c>: orthographic, half height 0.34 m, aimed 1.7 m above the
    /// avatar. That frame is 0.68 m tall, and a head is about 0.3 m, so the face fills half of it and the
    /// contact icon looks taken from across the room.
    ///
    /// <c>StartPortraitCapture</c> starts that coroutine, and its first step runs before the call
    /// returns: the camera exists and is set up, and it renders one frame later. So the postfix only has
    /// to tighten that camera. It touches it only when it still has the values this was measured against;
    /// anything else is a newer S1API with framing of its own, and that is left alone with a warning.
    /// </remarks>
    internal sealed class S1ApiPortraitFraming : Fix
    {
        internal override string Id => "s1api-portrait-framing";
        internal override string Mod => "S1API";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override bool NeedsAScreen => true;

        internal override string What
            => "contact pictures of characters added by mods are framed on the face, like the game's own";

        internal override string StandsDownBecause
            => "S1API draws the contact picture of every character it adds with the face at half the size "
             + "of the game's own, so the icon looks far away.";

        private const string CameraName = "S1API Portrait Camera";

        // What S1API 3.2.x sets, and what this changes it to. The tighter values come from the baked
        // vanilla mugshot (Benji): the head spans about 80 % of the frame height and sits a little above
        // its middle.
        private const float TheirHalfHeight = 0.34f;
        private const float OurHalfHeight = 0.19f;
        private const float Lift = 0.03f;

        private static MelonLogger.Instance _log;
        private static int _framed;
        private static bool _warned;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var compat = AccessTools.TypeByName("S1API.Internal.Compatibility.AvatarCompatibility");
            if (compat == null)
            {
                log.Msg("[fix] s1api-portrait-framing: this S1API draws no portraits of its own, nothing to do.");
                return false;
            }

            var target = AccessTools.Method(compat, "StartPortraitCapture");
            if (target == null)
            {
                log.Warning("[fix] s1api-portrait-framing: S1API's portrait capture is not where it was, so mod "
                          + "characters keep their far-away contact pictures.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.portraitframing").Patch(target,
                postfix: new HarmonyMethod(typeof(S1ApiPortraitFraming), nameof(Tighten)));
            return true;
        }

        private static void Tighten()
        {
            try
            {
                var go = GameObject.Find(CameraName);
                if (go == null)
                {
                    WarnOnce($"no '{CameraName}' right after the capture started");
                    return;
                }

                var camera = go.GetComponent<Camera>();
                if (camera == null)
                {
                    WarnOnce($"'{CameraName}' has no camera");
                    return;
                }

                if (!camera.orthographic || Mathf.Abs(camera.orthographicSize - TheirHalfHeight) > 0.001f)
                {
                    WarnOnce($"the portrait camera is not the one this was measured against (orthographic="
                           + $"{camera.orthographic}, half height {camera.orthographicSize:F3})");
                    return;
                }

                camera.orthographicSize = OurHalfHeight;
                go.transform.position += Vector3.up * Lift;

                _framed++;
                if (_framed == 1 || _framed % 10 == 0)
                    _log?.Msg($"[fix] s1api-portrait-framing: framed {_framed} contact picture(s) on the face.");
            }
            catch (System.Exception e)
            {
                _log?.Error("[fix] s1api-portrait-framing: could not frame this contact picture, it stays far "
                          + "away: " + e.Message);
            }
        }

        private static void WarnOnce(string why)
        {
            if (_warned) return;
            _warned = true;
            _log?.Warning($"[fix] s1api-portrait-framing: {why}, so the contact pictures stay as S1API frames "
                        + "them.");
        }
    }
}
