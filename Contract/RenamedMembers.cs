namespace Polyfill.Contract
{
    /// <summary>
    /// Private members a build renamed, so a lookup by name finds the one that took over.
    /// </summary>
    /// <remarks>
    /// A bridge cannot reach these. A mod that takes a private field by string never names it in its own
    /// metadata, so the check sees no missing member, and <c>AccessTools.Property</c> answers null. The mod
    /// then carries on with a lookup it reads later, usually as "game update?".
    ///
    /// Nothing is written for them. The lookup answers with the member the game has, so the caller reads and
    /// writes the value the game itself uses. One rename per line, and each one carries where it was read.
    /// </remarks>
    internal static class RenamedMembers
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string OldName;
            internal string NewName;
            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.NPCs.NPCMovement",
                OldName = "faceDirectionRoutine",
                NewName = "_faceDirectionRoutine",
                Because = "the coroutine turning an NPC towards a direction, started and cleared at the same points "
                        + "(NPCMovement.cs:138, :971-980 on 0.4.6f13; NPCMovement.cs:143, :973-982 on 0.4.7f12)",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.AvatarFramework.EyeController",
                OldName = "blinkRoutine",
                NewName = "_blinkRoutine",
                Because = "the coroutine that makes an avatar blink, started when blinking is on and cleared when it "
                        + "ends (EyeController.cs:64, :96-98, :255 on 0.4.6f13; EyeController.cs:36, :59-61, :290 on "
                        + "0.4.7f12)",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.AvatarFramework.AvatarEffects",
                OldName = "laxativeEnabled",
                NewName = "_laxativeEnabled",
                Because = "the flag EnableLaxative sets and DisableLaxative clears (AvatarEffects.cs:77, :391, :415 on "
                        + "0.4.6f13; AvatarEffects.cs:78, :410, :434 on 0.4.7f12)",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.NPCs.NPC",
                OldName = "_npcData",
                NewName = "_defaultNPCData",
                Because = "NPC.cs:93-95 on 0.4.7f12 carries [FormerlySerializedAs(\"_npcData\")] on _defaultNPCData",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.AvatarFramework.Avatar",
                OldName = "originalHipPos",
                NewName = "_savedHipPosition",
                Because = "both hold HipBone.localPosition read when the avatar awakes and restored when the ragdoll "
                        + "ends (Avatar.cs:113, :144, :151 on 0.4.6f13; Avatar.cs:127 on 0.4.7f12)",
            },
        };

        /// <summary>The member that took over, or null when this rename is not on record.</summary>
        internal static string Successor(string type, string oldName)
        {
            foreach (var entry in All)
                if (entry.Type == type && entry.OldName == oldName) return entry.NewName;
            return null;
        }
    }
}
