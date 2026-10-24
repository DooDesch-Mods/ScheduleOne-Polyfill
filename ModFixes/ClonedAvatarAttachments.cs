using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A copy of a character that a mod makes with Instantiate starts without an error per clothing piece.
    /// </summary>
    /// <remarks>
    /// Mods build extra characters by cloning one that exists: Smugglers Flight its pilot and drop crew,
    /// NACops its extra officers. On 0.4.6 the copy was a plain set of renderers and needed nothing more.
    ///
    /// 0.4.7 colours clothes through <c>AttachmentColorApplicator</c>, whose <c>Start</c> reads the
    /// attachment's <c>Parent</c> (AttachmentColorApplicator.cs:40-52 in ScheduleOne.Core on 0.4.7f12).
    /// That parent is a private field set only by <c>AvatarAttachment.Initialize</c>
    /// (AvatarAttachment.cs:19-23), when the game itself puts the piece on. Instantiate does not copy it, so
    /// on a clone every coloured piece throws a NullReferenceException in Start.
    ///
    /// Skipping Start for such a piece is the 0.4.6 result: the clone was copied with the colours its source
    /// wore, and those stay on its materials. A piece the game puts on later is initialised and runs Start
    /// as usual.
    /// </remarks>
    internal sealed class ClonedAvatarAttachments : Fix
    {
        internal override string Id => "cloned-avatar-attachments";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "a character a mod copies from another one keeps its clothing colours without an error";

        internal override string StandsDownBecause
            => "a mod that copies a character logs an error for every coloured piece of clothing the copy wears.";

        private static MelonLogger.Instance _log;
        private static readonly HashSet<string> Reported = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var applicator = AccessTools.TypeByName("Il2CppScheduleOne.Core.Avatar.AttachmentColorApplicator");
            var start = applicator == null ? null : AccessTools.DeclaredMethod(applicator, "Start");
            if (start == null)
            {
                log.Warning($"[fix] {Id}: AttachmentColorApplicator ({applicator != null}) or its Start is not here.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                start, prefix: new HarmonyMethod(typeof(ClonedAvatarAttachments), nameof(Start)));
            return true;
        }

        /// <summary>Let Start run only when the piece knows which avatar object it belongs to.</summary>
        private static bool Start(object __instance)
        {
            try
            {
                var attachment = Traverse.Create(__instance).Property("_attachment").GetValue();
                var parent = attachment == null ? null : Traverse.Create(attachment).Property("Parent").GetValue();
                if (attachment != null && parent != null) return true;

                var component = (UnityEngine.Component)__instance;
                string avatar = component.transform.root.name;
                if (Reported.Add(avatar))
                    _log?.Msg($"[fix] cloned-avatar-attachments: {avatar} is a copy; its clothes keep the "
                            + "colours they were copied with.");
                return false;
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] cloned-avatar-attachments: could not check a clothing piece; the game "
                            + "colours it itself: " + e.Message);
                return true;
            }
        }
    }
}
