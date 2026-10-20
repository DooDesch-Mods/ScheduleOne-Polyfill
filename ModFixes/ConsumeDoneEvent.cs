using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.Product;
using MelonLoader;
using UnityEngine.Events;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Give each NPC's product behaviour its <c>onConsumeDone</c> event back, and raise it when the NPC is done.
    /// </summary>
    /// <remarks>
    /// THE HALF OF THE BRIDGE THAT HOLDS THE VALUE. 0.4.6 had <c>public UnityEvent onConsumeDone</c> on every
    /// ConsumeProductBehaviour and invoked it when the smoking, snorting or eating animation ended
    /// (ConsumeProductBehaviour.cs:162-231 on 0.4.6f13). 0.4.7 has <c>event Action&lt;ProductItemInstance&gt;
    /// OnProductConsumed</c> at the same point (:319 on 0.4.7f12). The plugin puts the accessors back as a
    /// doorway (Bridges/Steps/S0_4_6f13_To_0_4_7f5/Held.cs); this keeps one event per behaviour, keyed by the
    /// native pointer, and subscribes it to OnProductConsumed the first time a mod asks for it.
    ///
    /// OverTheCounter is why: Static adds a listener in OnCreated and its drifters walk home from one, and
    /// without the member OnCreated stops at the first line that names it, so Static is left half set up.
    ///
    /// Each entry holds the behaviour's wrapper, and that is what keeps the key honest: a live wrapper holds
    /// the IL2CPP object, so its pointer cannot be handed to another object while the entry exists.
    /// </remarks>
    internal sealed class ConsumeDoneEvent : Fix
    {
        internal override string Id => "consume-done-event";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What => "an NPC that finishes using a product tells a mod again";

        internal override string StandsDownBecause
            => "a mod that waits for an NPC to finish smoking or snorting never hears it, so whatever it does "
             + "next - an NPC walking off, a reaction - does not happen.";

        private sealed class Kept
        {
            internal ConsumeProductBehaviour Owner;
            internal UnityEvent Event;
        }

        private static readonly Dictionary<IntPtr, Kept> Events = new();
        private static MelonLogger.Instance _log;
        private static bool _raisedOnce;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            // The slots exist only when a mod on this machine names the member; absent means nobody asks.
            var get = Slot("<polyfill>get_onConsumeDone");
            var set = Slot("<polyfill>set_onConsumeDone");
            if (get == null && set == null) return false;

            get?.SetValue(null, new Func<object, object>(Get));
            set?.SetValue(null, new Action<object, object>(Set));
            return true;
        }

        private static FieldInfo Slot(string name)
            => typeof(ConsumeProductBehaviour).GetField(name, BindingFlags.Public | BindingFlags.Static);

        private static object Get(object behaviour)
        {
            try
            {
                return Entry((ConsumeProductBehaviour)behaviour)?.Event;
            }
            catch (Exception e)
            {
                _log?.Error("[fix] consume-done-event: could not hand out an NPC's onConsumeDone: " + e);
                return null;
            }
        }

        private static void Set(object behaviour, object value)
        {
            try
            {
                var entry = Entry((ConsumeProductBehaviour)behaviour);
                if (entry != null) entry.Event = (UnityEvent)value;
            }
            catch (Exception e)
            {
                _log?.Error("[fix] consume-done-event: could not store an NPC's onConsumeDone: " + e);
            }
        }

        /// <summary>
        /// The entry for this behaviour, made the first time with an empty event, as the field was on a
        /// prefab-loaded NPC.
        /// </summary>
        private static Kept Entry(ConsumeProductBehaviour behaviour)
        {
            if (behaviour == null)
            {
                _log?.Warning("[fix] consume-done-event: a mod asked for onConsumeDone on a destroyed NPC.");
                return null;
            }

            var pointer = ((Il2CppObjectBase)behaviour).Pointer;
            if (Events.TryGetValue(pointer, out var kept)) return kept;

            Forget();
            kept = new Kept { Owner = behaviour, Event = new UnityEvent() };
            Events[pointer] = kept;
            behaviour.add_OnProductConsumed((Il2CppSystem.Action<ProductItemInstance>)
                new Action<ProductItemInstance>(_ => Raise(pointer)));
            return kept;
        }

        private static void Raise(IntPtr pointer)
        {
            try
            {
                if (!Events.TryGetValue(pointer, out var kept) || kept.Event == null) return;
                kept.Event.Invoke();

                if (_raisedOnce) return;
                _raisedOnce = true;
                _log?.Msg("[fix] consume-done-event: raised onConsumeDone for an NPC that finished using a "
                        + "product, which 0.4.7 stopped having and a mod is listening for.");
            }
            catch (Exception e)
            {
                _log?.Error("[fix] consume-done-event: a listener on onConsumeDone threw: " + e);
            }
        }

        /// <summary>Drop the entries of NPCs that no longer exist.</summary>
        private static void Forget()
        {
            List<IntPtr> gone = null;
            foreach (var pair in Events)
                if (pair.Value.Owner == null) (gone ??= new List<IntPtr>()).Add(pair.Key);
            if (gone == null) return;
            foreach (var pointer in gone) Events.Remove(pointer);
        }
    }
}
