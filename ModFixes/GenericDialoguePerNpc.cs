using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.NPCs;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Let a mod give one NPC its own small-talk dialogue again, as <c>GenericDialogue</c> used to.
    /// </summary>
    /// <remarks>
    /// THE HALF OF THE BRIDGE THAT HOLDS THE VALUE. In 0.4.6 every DialogueController had its own
    /// <c>GenericDialogue</c> field, so a mod could hand one NPC a copy with extra choices. 0.4.7 replaced the
    /// field with <c>_genericConversation =&gt; Singleton&lt;DialogueManager&gt;.Instance.GenericConversation</c>
    /// (DialogueController.cs:112 on 0.4.7f12): one conversation for every NPC, and no setter. The plugin puts
    /// the accessors back as a doorway (Bridges/Steps/S0_4_6f13_To_0_4_7f5/Held.cs, and the getter in
    /// Forwards.cs); this keeps what a mod set, per controller, keyed by the native pointer.
    ///
    /// Keeping it is not enough, because the game never asks the controller for it. Interacted starts
    /// <c>_genericConversation</c>, and ModifyDialogueText, ModifyChoiceList, ChoiceCallback and CheckChoice
    /// each compare the active dialogue against it before they add the NPC's own choices (:149-375). So for
    /// the length of each of those calls on a controller that has its own, the shared conversation in the
    /// DialogueManager is swapped for it and put back afterwards. Nothing outside DialogueController reads
    /// GenericConversation, so no other NPC sees the swap.
    ///
    /// The value is also added to the NPC's <c>dialogueContainers</c>, which is where S1API looks up a
    /// conversation by name to inject a choice into it.
    ///
    /// Casino Direct Deposit is why: it gives Philip a copy with its payout choice, and without the setter its
    /// registration stops at the first line, so the choice never appears.
    /// </remarks>
    internal sealed class GenericDialoguePerNpc : Fix
    {
        internal override string Id => "generic-dialogue-per-npc";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What => "an NPC can have its own small-talk choices from a mod again";

        internal override string StandsDownBecause
            => "a mod that gives one NPC its own small-talk dialogue has it stored and never shown, so the "
             + "choices it adds - a casino payout option, for one - do not appear.";

        private sealed class Kept
        {
            internal DialogueController Owner;
            internal Conversation Value;
        }

        private static readonly Dictionary<IntPtr, Kept> Dialogues = new();
        private static readonly string[] Reads =
            { "Interacted", "StartGenericDialogue", "ModifyDialogueText", "ModifyChoiceList", "ChoiceCallback",
              "CheckChoice" };

        private static MelonLogger.Instance _log;
        private static bool _swappedOnce;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            // The slots exist only when a mod on this machine sets GenericDialogue; absent means nobody does.
            var set = Slot("<polyfill>set_GenericDialogue");
            if (set == null) return false;

            var get = Slot("<polyfill>get_GenericDialogue");
            if (get == null)
                log.Warning("[fix] generic-dialogue-per-npc: the getter's slot is missing, so a mod reading "
                          + "GenericDialogue gets the shared conversation instead of the one it set.");

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.fixes");
            var patched = 0;
            foreach (var name in Reads)
            {
                var method = AccessTools.DeclaredMethod(typeof(DialogueController), name);
                if (method == null)
                {
                    log.Warning($"[fix] generic-dialogue-per-npc: DialogueController.{name} is not where it was, "
                              + "so an NPC's own dialogue is not used there.");
                    continue;
                }
                harmony.Patch(method,
                              prefix: new HarmonyMethod(typeof(GenericDialoguePerNpc), nameof(SwapIn)),
                              finalizer: new HarmonyMethod(typeof(GenericDialoguePerNpc), nameof(SwapBack)));
                patched++;
            }
            if (patched == 0) return false;

            get?.SetValue(null, new Func<object, object>(Get));
            set.SetValue(null, new Action<object, object>(Set));
            return true;
        }

        private static FieldInfo Slot(string name)
            => typeof(DialogueController).GetField(name, BindingFlags.Public | BindingFlags.Static);

        private static object Get(object controller)
        {
            try
            {
                return Find((DialogueController)controller)?.Value;
            }
            catch (Exception e)
            {
                _log?.Error("[fix] generic-dialogue-per-npc: could not hand out an NPC's GenericDialogue: " + e);
                return null;
            }
        }

        private static void Set(object target, object value)
        {
            try
            {
                var controller = (DialogueController)target;
                if (controller == null)
                {
                    _log?.Warning("[fix] generic-dialogue-per-npc: a mod set GenericDialogue on a destroyed NPC.");
                    return;
                }

                var conversation = (Conversation)value;
                var pointer = ((Il2CppObjectBase)controller).Pointer;
                Forget();
                Dialogues.TryGetValue(pointer, out var previous);

                var containers = controller.handler?.dialogueContainers;
                if (containers != null)
                {
                    if (previous?.Value != null) containers.Remove(previous.Value);
                    if (conversation != null && !containers.Contains(conversation)) containers.Add(conversation);
                }
                else
                {
                    _log?.Warning($"[fix] generic-dialogue-per-npc: {Who(controller)} has no dialogue handler yet, "
                                + "so a mod cannot find its dialogue by name.");
                }

                if (conversation == null)
                {
                    Dialogues.Remove(pointer);
                    _log?.Msg($"[fix] generic-dialogue-per-npc: {Who(controller)} uses the shared small talk again.");
                    return;
                }

                Dialogues[pointer] = new Kept { Owner = controller, Value = conversation };
                _log?.Msg($"[fix] generic-dialogue-per-npc: {Who(controller)} now has its own small talk "
                        + $"({conversation.name}), which a mod set.");
            }
            catch (Exception e)
            {
                _log?.Error("[fix] generic-dialogue-per-npc: could not store an NPC's GenericDialogue: " + e);
            }
        }

        /// <summary>The NPC's id for the log; the controller's own GameObject is called "Dialogue" on all of them.</summary>
        private static string Who(DialogueController controller)
        {
            var npc = controller.GetComponentInParent<NPC>();
            return npc != null ? npc.ID : controller.name;
        }

        private static Kept Find(DialogueController controller)
        {
            if (controller == null) return null;
            return Dialogues.TryGetValue(((Il2CppObjectBase)controller).Pointer, out var kept) ? kept : null;
        }

        /// <summary>
        /// Before the controller reads the shared conversation, make it this NPC's own.
        /// </summary>
        /// <param name="__state">The conversation that was there, or null when nothing was swapped.</param>
        private static void SwapIn(DialogueController __instance, out Conversation __state)
        {
            __state = null;
            try
            {
                if (Dialogues.Count == 0) return;
                var kept = Find(__instance);
                if (kept == null) return;

                if (!Singleton<DialogueManager>.InstanceExists)
                {
                    _log?.Warning("[fix] generic-dialogue-per-npc: there is no DialogueManager, so "
                                + $"{Who(__instance)} talks with the shared small talk.");
                    return;
                }

                var manager = Singleton<DialogueManager>.Instance;
                var shared = manager._genericConversation;
                if (shared == null || ((Il2CppObjectBase)shared).Pointer == ((Il2CppObjectBase)kept.Value).Pointer)
                    return;

                manager._genericConversation = kept.Value;
                __state = shared;

                if (_swappedOnce) return;
                _swappedOnce = true;
                _log?.Msg($"[fix] generic-dialogue-per-npc: {Who(__instance)} talks with its own small talk "
                        + $"({kept.Value.name}).");
            }
            catch (Exception e)
            {
                _log?.Error("[fix] generic-dialogue-per-npc: could not give an NPC its own small talk: " + e);
            }
        }

        /// <summary>Put the shared conversation back, also when the call threw.</summary>
        private static Exception SwapBack(Exception __exception, Conversation __state)
        {
            if (__state == null) return __exception;
            try
            {
                if (Singleton<DialogueManager>.InstanceExists)
                    Singleton<DialogueManager>.Instance._genericConversation = __state;
                else
                    _log?.Warning("[fix] generic-dialogue-per-npc: the DialogueManager is gone, so the shared "
                                + "small talk could not be put back.");
            }
            catch (Exception e)
            {
                _log?.Error("[fix] generic-dialogue-per-npc: could not put the shared small talk back: " + e);
            }
            return __exception;
        }

        /// <summary>Drop the entries of NPCs that no longer exist.</summary>
        private static void Forget()
        {
            List<IntPtr> gone = null;
            foreach (var pair in Dialogues)
                if (pair.Value.Owner == null) (gone ??= new List<IntPtr>()).Add(pair.Key);
            if (gone == null) return;
            foreach (var pointer in gone) Dialogues.Remove(pointer);
        }
    }
}
