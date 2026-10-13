using HarmonyLib;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// NPCInteract360 lifts the angle limit on the object an NPC is talked to through, under the name the
    /// game gives it now.
    /// </summary>
    /// <remarks>
    /// The mod does two things to an NPC, the first frame it sees the avatar (Core.cs:73-104):
    /// <code>
    /// if (isNPC &amp;&amp; lowerName.Contains("capsule")) obj.layer = Core.IgnoreNPCLayer;     // the body stops blocking the ray
    /// if (name != "interaction") continue;  interactable.LimitInteractionAngle = false;  // talk from any side
    /// </code>
    /// The first half needs nothing but its patch to bind, which is the Avatar.LateUpdate line in
    /// RenamedMethods. The second half finds nothing: an NPC's avatar carries three interactables, called
    /// "Ragdoll draggable", "Dialogue Interaction" and "Pickpocket Interaction", and none is called
    /// "Interaction" (0.4.6f12 export, Marco.prefab:2086 and :2123; the same three names on every NPC in a
    /// 0.4.7f12 save). So the capsule moves, the limit stays, and an NPC can still only be talked to from
    /// the front - with a mod installed whose whole point is the other 180 degrees.
    ///
    /// ONLY THE TALK OBJECT. The mod compares the whole name, so it never meant an object with a longer
    /// one, and "Pickpocket Interaction" keeps its limit. "Ragdoll draggable" has none to lift.
    ///
    /// BEHIND THE MOD'S OWN PATCH, not on Avatar.Update. The mod decides what an NPC is - not a player,
    /// and an NPC component somewhere under the root - and this runs only where that patch ran, so a
    /// build of the mod that stops patching avatars takes this with it.
    ///
    /// ONCE PER AVATAR, by instance id. The mod's patch runs every frame for every avatar and answers
    /// from its own set; this pays one call per avatar per frame for the same answer. An avatar that is
    /// switched off does not update and is reached the frame it comes back, which is before anybody can
    /// stand next to it.
    /// </remarks>
    internal sealed class NpcInteract360TalkAngle : Fix
    {
        internal override string Id => "npcinteract360-talk-angle";
        internal override string Mod => "NPCInteract360";
        internal override string ModVersions => "1.1.1";

        /// <summary>The two builds the object's name was read on. A prefab is free to rename it again.</summary>
        internal override string GameVersions => "0.4.6*,0.4.7*";

        internal override bool NeedsAScreen => true;

        internal override string What => "NPCs can be talked to from any side, not only from the front";

        internal override string StandsDownBecause
            => "NPCInteract360 lifts the angle limit on an object called Interaction, and the game calls the "
             + "one an NPC is talked to through Dialogue Interaction, so the limit stays on.";

        /// <summary>The interactable under an NPC's avatar that carries "Talk to".</summary>
        private const string TalkObject = "Dialogue Interaction";

        private static MelonLogger.Instance _log;
        private static readonly HashSet<int> Seen = new();
        private static int _lifted;
        private static bool _complained;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var patch = AccessTools.TypeByName("NPCInteract360.Patch_Avatar_LateUpdate");
            if (patch == null)
            {
                log.Warning("[fix] npcinteract360-talk-angle: NPCInteract360.Patch_Avatar_LateUpdate is not "
                          + "where it was, so there is nothing to run behind.");
                return false;
            }

            var postfix = AccessTools.Method(patch, "Postfix");
            if (postfix == null)
            {
                log.Warning("[fix] npcinteract360-talk-angle: the mod's avatar patch has no Postfix in this "
                          + "version, so it was left alone.");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            harmony.Patch(postfix, postfix: new HarmonyMethod(typeof(NpcInteract360TalkAngle), nameof(Finish)));
            return true;
        }

        /// <summary>Lift the limit on this avatar's talk object, the first time the avatar is seen.</summary>
        private static void Finish(Il2CppScheduleOne.AvatarFramework.Avatar __0)
        {
            try
            {
                if (__0 == null || !Seen.Add(__0.GetInstanceID())) return;

                // Players and display figures have an avatar and nobody to talk to. The mod skips them too.
                var npc = __0.GetComponentInParent<NPC>();
                if (npc == null) return;

                bool found = false;
                foreach (var interactable in __0.GetComponentsInChildren<InteractableObject>(true))
                {
                    if (interactable == null || interactable.name != TalkObject) continue;

                    found = true;
                    if (!interactable.LimitInteractionAngle) continue;

                    interactable.LimitInteractionAngle = false;
                    _lifted++;

                    // One line for the first, a count for the rest: a town is over a hundred NPCs.
                    if (_lifted == 1)
                        _log?.Msg($"[fix] npcinteract360-talk-angle: lifted the angle limit on '{TalkObject}' of "
                                + $"{npc.name}. The mod looks for an object called 'Interaction', which this "
                                + "build does not have; later NPCs are counted in `polyfillfixes`.");
                    Fixes.Record("npcinteract360-talk-angle", $"applied, angle limit lifted for {_lifted} NPC(s)");
                }

                if (!found) Complain($"{npc.name} has no object called '{TalkObject}' under its avatar");
            }
            catch (Exception e)
            {
                Complain("could not read an NPC's interactables: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static void Complain(string why)
        {
            if (_complained) return;
            _complained = true;
            _log?.Warning("[fix] npcinteract360-talk-angle: " + why + ", so that NPC keeps the limit the "
                        + "game gave it. Said once; later ones are not listed.");
        }
    }
}
