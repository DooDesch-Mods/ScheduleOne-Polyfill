using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.AvatarFramework;
using Il2CppScheduleOne.NPCs;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Draw the contact pictures of OverTheCounter's drifters and managers with S1API's portrait camera.
    /// </summary>
    /// <remarks>
    /// OverTheCounter queues a picture for every drifter and hired manager and draws them one by one in
    /// <c>MugshotUtility.ProcessQueue</c>. That coroutine is built on the 0.4.6 rig: it dresses
    /// <c>MugshotGenerator.MugshotRig</c> with <c>AvatarSettings</c>, resets it with
    /// <c>MugshotGenerator.DefaultSettings</c> and reads it with <c>IconGenerator.GetTexture(Transform)</c>.
    /// 0.4.7 rebuilt the generator around a NakedAppearance and an Outfit; the rig, the default settings and
    /// that GetTexture overload are gone. The coroutine fails on its first step with a MissingMethodException,
    /// the queue never drains, and no drifter or manager gets a picture.
    ///
    /// S1API, which OverTheCounter requires, already draws portraits on 0.4.7
    /// (<c>AvatarCompatibility</c>): it turns an <c>AvatarSettings</c> into the two new inputs, takes the
    /// game's generator for itself, renders, and scales the picture to 512. This runs the same queue
    /// through those steps, so the pictures look like S1API's own characters (and get the same framing,
    /// see <see cref="S1ApiPortraitFraming"/>). The queue, the session counter and the callbacks stay
    /// OverTheCounter's; only the drawing is replaced.
    /// </remarks>
    internal sealed class OverTheCounterMugshots : Fix
    {
        internal override string Id => "otc-mugshots";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "2.0.10";
        internal override string GameVersions => ">=0.4.7";

        internal override bool NeedsAScreen => true;

        internal override string What
            => "drifters and managers get their contact pictures again";

        internal override string StandsDownBecause
            => "OverTheCounter draws the pictures of its drifters and managers with the mugshot rig of 0.4.6, "
             + "which 0.4.7 removed, so none of them gets a picture.";

        private const int FrameBudget = 600;

        private static MelonLogger.Instance _log;
        private static FieldInfo _queue, _isProcessing, _sessionId;
        private static FieldInfo _gameNpc, _label, _onComplete, _explicitSettings;
        private static MethodInfo _find, _acquire, _release, _inputs, _capture, _resize, _legacy;
        private static int _drawn;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var utility = AccessTools.TypeByName("OverTheCounter.Utilities.MugshotUtility");
            var request = utility == null ? null : AccessTools.Inner(utility, "MugshotRequest");
            var target = utility == null ? null : AccessTools.Method(utility, "ProcessQueue");
            if (request == null || target == null)
            {
                log.Warning("[fix] otc-mugshots: OverTheCounter's picture queue is not where 2.0.10 has it, so "
                          + "drifters and managers stay without pictures.");
                return false;
            }
            _queue = AccessTools.Field(utility, "_queue");
            _isProcessing = AccessTools.Field(utility, "_isProcessing");
            _sessionId = AccessTools.Field(utility, "_sessionId");
            _gameNpc = AccessTools.Field(request, "GameNpc");
            _label = AccessTools.Field(request, "Label");
            _onComplete = AccessTools.Field(request, "OnComplete");
            _explicitSettings = AccessTools.Field(request, "ExplicitSettings");

            var compat = AccessTools.TypeByName("S1API.Internal.Compatibility.AvatarCompatibility");
            _find = compat == null ? null : AccessTools.Method(compat, "FindMugshotGenerator");
            _acquire = compat == null ? null : AccessTools.Method(compat, "TryAcquireMugshotGenerator");
            _release = compat == null ? null : AccessTools.Method(compat, "ReleaseMugshotGenerator");
            _inputs = compat == null ? null : AccessTools.Method(compat, "CreateRenderInputs");
            _capture = compat == null ? null : AccessTools.Method(compat, "StartPortraitCapture");
            _resize = compat == null ? null : AccessTools.Method(compat, "ResizePortrait");
            _legacy = compat == null ? null : AccessTools.Method(compat, "CaptureLegacySettings");

            string missing = Missing();
            if (missing != null)
            {
                log.Warning($"[fix] otc-mugshots: {missing} was not found, so drifters and managers stay without "
                          + "pictures.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.otcmugshots").Patch(target,
                prefix: new HarmonyMethod(typeof(OverTheCounterMugshots), nameof(Replace)));
            return true;
        }

        private static string Missing()
        {
            if (_queue == null || _isProcessing == null || _sessionId == null) return "OverTheCounter's queue state";
            if (_gameNpc == null || _label == null || _onComplete == null || _explicitSettings == null)
                return "OverTheCounter's picture request";
            if (_find == null || _acquire == null || _release == null || _inputs == null || _capture == null
                || _resize == null || _legacy == null)
                return "S1API's portrait capture (AvatarCompatibility)";
            return null;
        }

        private static bool Replace(ref IEnumerator __result)
        {
            __result = Drain();
            return false;
        }

        private static IEnumerator Drain()
        {
            int session = Session();
            try
            {
                while (session == Session() && Count() > 0)
                {
                    var request = Dequeue();
                    if (request == null) yield break;
                    var npc = _gameNpc.GetValue(request) as NPC;
                    string label = _label.GetValue(request) as string ?? "a picture";
                    var done = _onComplete.GetValue(request) as Action<Sprite>;
                    var settings = _explicitSettings.GetValue(request) as AvatarSettings;

                    var job = new Job { Label = label };
                    if (npc != null)
                    {
                        // Stepped here rather than yielded as a nested coroutine, so it runs the same under
                        // every MelonLoader coroutine host.
                        var draw = Draw(job, npc, settings, session);
                        while (draw.MoveNext()) yield return draw.Current;
                    }
                    else _log?.Warning($"[fix] otc-mugshots: {label}: the NPC is gone, no picture.");

                    try { done?.Invoke(job.Sprite); }
                    catch (Exception e) { _log?.Error($"[fix] otc-mugshots: {label}: OverTheCounter failed to use its picture: {e}"); }

                    if (job.Sprite != null)
                    {
                        _drawn++;
                        if (_drawn == 1 || _drawn % 10 == 0)
                            _log?.Msg($"[fix] otc-mugshots: drew {_drawn} Over The Counter contact picture(s).");
                    }
                }
            }
            finally
            {
                _isProcessing.SetValue(null, false);
            }
        }

        private sealed class Job
        {
            internal string Label;
            internal Sprite Sprite;
            internal Texture2D Texture;
            internal bool Captured;
        }

        /// <summary>One picture, the way S1API's own queue draws it.</summary>
        private static IEnumerator Draw(Job job, NPC npc, AvatarSettings settings, int session)
        {
            var avatar = npc.Avatar;
            if (settings == null) settings = Call(job, "read the NPC's look", () => _legacy.Invoke(null, new object[] { avatar })) as AvatarSettings;
            if (settings == null)
            {
                _log?.Warning($"[fix] otc-mugshots: {job.Label}: no look to draw, no picture.");
                yield break;
            }

            object generator = null;
            for (int frame = 0; generator == null && frame < FrameBudget; frame++)
            {
                generator = Call(job, "find the mugshot generator", () => _find.Invoke(null, new object[] { avatar }));
                if (generator == null) yield return null;
                if (session != Session()) yield break;
            }
            if (generator == null)
            {
                _log?.Warning($"[fix] otc-mugshots: {job.Label}: the game has no mugshot generator yet, no picture.");
                yield break;
            }

            bool held = false;
            for (int frame = 0; !held && frame < FrameBudget; frame++)
            {
                held = Call(job, "take the mugshot generator", () => _acquire.Invoke(null, new[] { generator })) is true;
                if (!held) yield return null;
                if (session != Session()) { if (held) Release(job); yield break; }
            }
            if (!held)
            {
                _log?.Warning($"[fix] otc-mugshots: {job.Label}: the mugshot generator stayed busy, no picture.");
                yield break;
            }

            object outfit = null;
            try
            {
                var args = new object[] { settings, null, null };
                bool prepared = Call(job, "prepare the look", () => { _inputs.Invoke(null, args); return true; }) != null;
                outfit = args[2];
                if (prepared && args[1] != null && outfit != null)
                {
                    Action<Texture2D> took = texture => { job.Texture = texture; job.Captured = true; };
                    if (Call(job, "start the capture", () => { _capture.Invoke(null, new[] { generator, args[1], outfit, took }); return true; }) == null)
                        job.Captured = true;
                    for (int frame = 0; !job.Captured && frame < FrameBudget / 2; frame++) yield return null;
                }
                else
                {
                    _log?.Warning($"[fix] otc-mugshots: {job.Label}: S1API made no appearance from the look, no picture.");
                }
            }
            finally
            {
                Release(job);
                if (outfit is UnityEngine.Object o && o != null) UnityEngine.Object.Destroy(o);
            }

            job.Sprite = ToSprite(job);
        }

        private static Sprite ToSprite(Job job)
        {
            var source = job.Texture;
            if (source == null)
            {
                _log?.Warning($"[fix] otc-mugshots: {job.Label}: the capture returned nothing, no picture.");
                return null;
            }
            try
            {
                source.Apply();
                var sized = _resize.Invoke(null, new object[] { source, 512 }) as Texture2D;
                if (sized == null) return null;
                UnityEngine.Object.Destroy(source);
                return Sprite.Create(sized, new Rect(0f, 0f, sized.width, sized.height), Vector2.zero);
            }
            catch (Exception e)
            {
                _log?.Error($"[fix] otc-mugshots: {job.Label}: could not turn the capture into a picture: {e}");
                return null;
            }
        }

        private static object Call(Job job, string what, Func<object> call)
        {
            try { return call(); }
            catch (Exception e)
            {
                _log?.Error($"[fix] otc-mugshots: {job.Label}: could not {what}: {(e as TargetInvocationException)?.InnerException ?? e}");
                return null;
            }
        }

        private static void Release(Job job) => Call(job, "hand the mugshot generator back", () => _release.Invoke(null, null));

        private static int Session() => (int)_sessionId.GetValue(null);

        private static int Count() => (int)AccessTools.Property(_queue.FieldType, "Count").GetValue(_queue.GetValue(null));

        private static object Dequeue() => AccessTools.Method(_queue.FieldType, "Dequeue").Invoke(_queue.GetValue(null), null);
    }
}
