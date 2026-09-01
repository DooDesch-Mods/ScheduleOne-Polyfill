using System.Linq;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Asking the game for a free dead drop when there is none answers "none" instead of throwing.
    /// </summary>
    /// <remarks>
    /// A VANILLA BUG, not a version bridge - and the reason it belongs here anyway is that mods carry
    /// the cost of it while the game itself never does. <c>DeadDrop.GetRandomEmptyDrop</c> reads:
    ///
    /// <code>
    /// var source = DeadDrops.Where(drop => drop.Storage.ItemCount == 0).ToList();
    /// source = source.OrderBy(drop => Vector3.Distance(...)).ToList();
    /// source.RemoveAt(0);                              // throws when source is empty
    /// source.RemoveRange(source.Count / 2, source.Count / 2);
    /// if (source.Count == 0) return null;              // the guard, one line too late
    /// </code>
    ///
    /// So when every dead drop in the world already holds something, the method that exists to answer
    /// "is one free" throws ArgumentOutOfRangeException instead of answering. Identical in 0.4.5f2, so
    /// nothing about it is new - it simply needs every drop occupied to show, which happens to a player
    /// who has been playing a while and never to a fresh save.
    ///
    /// WHAT IT COSTS A MOD. Unicorn's Custom Seeds synthesises a seed in a coroutine: it registers the
    /// definition, creates the shop listing, and THEN asks for a dead drop to put one in. An exception
    /// inside a coroutine kills the coroutine where it stands, so the listing exists and the drop never
    /// happens - which is exactly what the player reported: "I still am not able to dead drop it, but I
    /// can buy it from him directly." No mod can guard this either, because the throw is inside the
    /// method they call.
    ///
    /// THE GUARD AND NOTHING ELSE. This does not choose a drop, does not change which one the game
    /// picks, and does not touch the case where the method works. It answers null for the one input the
    /// original cannot survive, which is the answer the original was already trying to give one line
    /// further down - and the answer the calling mod already handles.
    ///
    /// The "exactly one free drop" case is left alone on purpose. The original discards the nearest
    /// candidate before choosing, so one free drop yields null - that is a deliberate rule about not
    /// sending somebody next door, and quietly overriding it would change what the game does rather
    /// than stop it crashing.
    /// </remarks>
    internal sealed class EmptyDeadDropSearch : Fix
    {
        internal override string Id => "empty-dead-drop-search";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => "*";

        internal override string What
            => "asking for a free dead drop when every one is full answers 'none' instead of throwing, "
             + "so a mod that puts something in one carries on";

        internal override string StandsDownBecause
            => "DeadDrop.GetRandomEmptyDrop drops the nearest candidate before checking whether it had "
             + "any, so a world with no free dead drop throws out of the middle of whatever called it.";

        private static MelonLogger.Instance _log;
        private static bool _said;
#if DEBUG
        private static bool _counted;
#endif

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var type = AccessTools.TypeByName("Il2CppScheduleOne.Economy.DeadDrop");
            if (type == null)
            {
                log.Warning("[fix] empty-dead-drop-search: Il2CppScheduleOne.Economy.DeadDrop is not on "
                          + "this build, so there is nothing to guard.");
                return false;
            }

            var target = AccessTools.Method(type, "GetRandomEmptyDrop");
            if (target == null)
            {
                log.Warning("[fix] empty-dead-drop-search: DeadDrop.GetRandomEmptyDrop is not here. If "
                          + "the game renamed it, a mod calling the old name has a bigger problem than "
                          + "this guard.");
                return false;
            }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                target, prefix: new HarmonyMethod(typeof(EmptyDeadDropSearch), nameof(Before)));

#if DEBUG
            // Which method the guard is actually on, and whether Harmony kept it. "Applied" on its own
            // says a Patch call returned, not that anything will run.
            var info = HarmonyLib.Harmony.GetPatchInfo(target);
            log.Msg($"[fix] empty-dead-drop-search: on {target.DeclaringType?.Name}.{target.Name}"
                  + $"({string.Join(", ", target.GetParameters().Select(p => p.ParameterType.Name))})"
                  + $" -> {target.ReturnType.Name}; prefixes now: {info?.Prefixes?.Count ?? 0}");
#endif
            return true;
        }

        /// <summary>Answer null when nothing is free; otherwise stand aside and let the game choose.</summary>
        private static bool Before(ref object __result)
        {
            try
            {
                var type = AccessTools.TypeByName("Il2CppScheduleOne.Economy.DeadDrop");
                /*
                 * PROPERTY FIRST. Il2CppInterop projects every native field as a managed property over
                 * a field pointer, so AccessTools.Field finds nothing here - and it says so by
                 * returning null rather than throwing, which sent this straight down the stand-aside
                 * path. The prefix ran twice and looked exactly like a prefix that never ran; only
                 * Harmony's own "Could not find field ... DeadDrops" in the log gave it away.
                 *
                 * The loop below already did it in this order for Storage. This line did not.
                 */
                var drops = AccessTools.Property(type, "DeadDrops")?.GetValue(null)
                         ?? AccessTools.Field(type, "DeadDrops")?.GetValue(null);
                if (drops == null)
                {
                    Complain("neither a property nor a field called DeadDrops");
                    return true;                           // no list to read; let the original decide
                }

                int free = 0;
                foreach (var drop in Enumerate(drops))
                {
                    if (drop == null) continue;
                    var storage = AccessTools.Property(drop.GetType(), "Storage")?.GetValue(drop)
                               ?? AccessTools.Field(drop.GetType(), "Storage")?.GetValue(drop);
                    if (storage == null) continue;

                    var count = AccessTools.Property(storage.GetType(), "ItemCount")?.GetValue(storage);
                    if (count is int items && items == 0) free++;
                }

#if DEBUG
                // Standing aside and failing to read the list look identical from outside, so a debug
                // build says which happened. Gone from Release, where nobody is watching for it.
                if (!_counted)
                {
                    _counted = true;
                    _log?.Msg($"[fix] empty-dead-drop-search: read the list - {free} free drop(s).");
                }
#endif

                if (free > 0) return true;                 // the original is safe from here

                __result = null;
                if (!_said)
                {
                    _said = true;
                    _log?.Msg("[fix] empty-dead-drop-search: every dead drop in the world is full, so "
                            + "the game was asked for one and answered instead of throwing.");
                }
                return false;
            }
            catch (Exception e)
            {
                // Reading the list must never be the reason the call fails. Standing aside puts the
                // original back in charge, including its bug - which is where it was without this.
                Complain(e.GetType().Name + ": " + e.Message);
                return true;
            }
        }

        /// <summary>The interop list as something foreach can walk, without naming its generic type.</summary>
        private static IEnumerable<object> Enumerate(object list)
        {
            int count = AccessTools.Property(list.GetType(), "Count")?.GetValue(list) as int? ?? 0;
            var item = AccessTools.Property(list.GetType(), "Item");
            if (item == null) yield break;

            for (int i = 0; i < count; i++)
            {
                object value = null;
                try { value = item.GetValue(list, new object[] { i }); }
                catch { }
                if (value != null) yield return value;
            }
        }

        private static readonly HashSet<string> Complained = new();

        private static void Complain(string why)
        {
            if (!Complained.Add(why)) return;
            _log?.Warning("[fix] empty-dead-drop-search: could not read the dead drop list (" + why
                        + "), so the game answers this one itself.");
            Fixes.Record("empty-dead-drop-search", "stood aside: " + why);
        }
    }
}
