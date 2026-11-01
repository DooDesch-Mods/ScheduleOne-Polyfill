using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// The people Moon Rocks spawns get no phone conversation.
    /// </summary>
    /// <remarks>
    /// The mod stops the conversation with a prefix that asks its registries whether this NPC is one of its own.
    /// On 0.4.6 the conversation came after the NPC was registered. 0.4.7 builds it from Awake, inside
    /// Instantiate (NPC.cs:536-559, :603 on 0.4.7f12), when the registries do not know the clone yet and its
    /// NetworkObject has no id: every clone asks for "messageconversation_0", and every clone after the first
    /// logs "Conversation already registered" (MessagingManager.cs:72).
    ///
    /// The mod names every NPC data object it builds "MR_..." (NpcCloner.DataObjectFor, AlienSpawner.MakeDataObject,
    /// and both placeholders), and a clone carries that object from its first line of Awake. No game NPC has one.
    /// </remarks>
    internal sealed class MoonRocksClonesNoPhone : Fix
    {
        internal override string Id => "moonrocks-clones-no-phone";
        internal override string Mod => "Moon Rocks";

        /// <summary>Written against 1.1.0. A later build may stop the conversation itself.</summary>
        internal override string ModVersions => "<=1.1.0";

        /// <summary>0.4.7f5 is the first build with the conversation in Awake.</summary>
        internal override string GameVersions => ">=0.4.7f5";

        internal override string What => "the moon's people do not fill the log with phone conversation errors";

        internal override string StandsDownBecause
            => "Each person Moon Rocks spawns logs a phone conversation error.";

        private const string ModDataPrefix = "MR_";

        private static MelonLogger.Instance _log;
        private static bool _reportedSkip, _reportedFailure;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var create = AccessTools.DeclaredMethod(typeof(NPC), "CreateAndAssignDefaultMessageConversation");
            if (create == null)
            {
                log.Warning($"[fix] {Id}: NPC.CreateAndAssignDefaultMessageConversation is not here.");
                return false;
            }
            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(create,
                prefix: new HarmonyMethod(typeof(MoonRocksClonesNoPhone), nameof(Create)));
            return true;
        }

        private static bool Create(NPC __instance)
        {
            try
            {
                var data = __instance._defaultNPCData;
                if (data == null || !data.name.StartsWith(ModDataPrefix, StringComparison.Ordinal)) return true;

                if (!_reportedSkip)
                {
                    _reportedSkip = true;
                    _log?.Msg($"[fix] moonrocks-clones-no-phone: {data.name} gets no phone conversation.");
                }
                return false;
            }
            catch (Exception e)
            {
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    _log?.Warning("[fix] moonrocks-clones-no-phone: this NPC gets a conversation after all: " + e);
                }
                return true;
            }
        }
    }
}
