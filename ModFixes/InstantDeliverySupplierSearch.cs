using System.Collections;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// InstantDelivery is handed its three suppliers by what they are, not by what they are called.
    /// </summary>
    /// <remarks>
    /// The mod has two switches, instant dead drops and no debt, and both work on three GameObjects it
    /// looks up once a second after the game scene loads:
    /// <code>
    /// GameObject check = GameObject.Find("Shirley");
    /// if (check.transform.parent == null) { RealShirley = GameObject.Find("Shirley"); ShirleyFound = true; }
    /// </code>
    /// It expects the supplier to be a root object and the only thing with that name. Neither holds any
    /// more. The suppliers sit under a <c>Suppliers</c> object, and each of them shares its name with a
    /// delivery van under <c>Delivery Vehicles</c>. So the parent is never null, the flag never goes up,
    /// and the switches change nothing. The Albert search is the same idea with one step more: it renames
    /// every "Albert" that has a parent to "FakeAlbert" until none is left, which renames the game's own
    /// supplier and van and then fails on the empty result once a second, forever.
    ///
    /// The four <c>Supplier</c> members the mod reads and writes are renamed too, and the bridges answer
    /// those. This is the half no bridge can reach: nothing is missing, a lookup by name just finds the
    /// wrong thing.
    ///
    /// SO THE SEARCH IS REPLACED, NOT THE SCENE. Each of the three finder methods returns this class's
    /// search instead, which asks for the one object carrying the <c>Albert</c>, <c>Shirley</c> or
    /// <c>Salvador</c> component and writes it into the fields the mod's own search would have filled.
    /// Everything after that is the mod's: its keys, its labels, its per-frame writes.
    ///
    /// EARLY, because the mod starts its searches from its own scene callback and a patch that arrives on
    /// the first frame after that finds them already running. Only the mod's type is read here; the game
    /// is asked nothing until the replaced search runs, and by then the scene is loaded.
    ///
    /// The Albert search also fills the mod's <c>PauseMenu</c> field, as the original did, and holds its
    /// flag back until that worked: the mod reads the field on every frame once the flag is up.
    /// </remarks>
    internal sealed class InstantDeliverySupplierSearch : Fix
    {
        internal override bool Early => true;

        internal override string Id => "instantdelivery-supplier-search";
        internal override string Mod => "InstantDelivery";
        internal override string ModVersions => "1.6.0";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "InstantDelivery finds Albert, Shirley and Salvador again, so its two switches reach them";

        internal override string StandsDownBecause
            => "InstantDelivery never finds its suppliers - it looks for a parentless object named after "
             + "each, and the game keeps them under one and has vans with the same names - so instant "
             + "deliveries and no debt change nothing.";

        private const string Albert = "Albert";
        private const string Shirley = "Shirley";
        private const string Salvador = "Salvador";

        private static MelonLogger.Instance _log;
        private static FieldInfo _pauseMenu;
        private static readonly Dictionary<string, FieldInfo> Held = new();
        private static readonly Dictionary<string, FieldInfo> Found = new();

        /// <summary>
        /// Which search for a supplier is the current one. The mod starts a new one on every load of the
        /// game scene, and one that never finished must not keep running beside its successor.
        /// </summary>
        private static readonly Dictionary<string, int> Round = new();

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            var main = AccessTools.TypeByName("InstantDelivery.Main");
            if (main == null)
            {
                log.Msg($"[fix] {Id}: InstantDelivery is not loaded, so there is nothing to repair.");
                return false;
            }

            // Everything is looked up before anything is patched: a mod whose three searches are answered
            // by two is worse than one that is left alone.
            var finders = new Dictionary<string, MethodInfo>();
            _pauseMenu = AccessTools.Field(main, "PauseMenu");
            bool known = _pauseMenu != null && _pauseMenu.FieldType == typeof(GameObject);
            foreach (string who in new[] { Albert, Shirley, Salvador })
            {
                var finder = AccessTools.Method(main, who + "Finder");
                var held = AccessTools.Field(main, "Real" + who);
                var found = AccessTools.Field(main, who + "Found");
                if (finder == null || finder.ReturnType != typeof(IEnumerator) || finder.GetParameters().Length != 0
                    || finder.IsStatic || held == null || held.FieldType != typeof(GameObject)
                    || found == null || found.FieldType != typeof(bool))
                {
                    known = false;
                    break;
                }
                finders[who] = finder;
                Held[who] = held;
                Found[who] = found;
            }

            if (!known)
            {
                log.Warning($"[fix] {Id}: InstantDelivery.Main is not the shape this knows - three finder "
                          + "coroutines with a GameObject and a flag each, and a PauseMenu field - so its "
                          + "supplier search was left alone.");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.instantdelivery");
            harmony.Patch(finders[Albert],
                prefix: new HarmonyMethod(typeof(InstantDeliverySupplierSearch), nameof(SearchForAlbert)));
            harmony.Patch(finders[Shirley],
                prefix: new HarmonyMethod(typeof(InstantDeliverySupplierSearch), nameof(SearchForShirley)));
            harmony.Patch(finders[Salvador],
                prefix: new HarmonyMethod(typeof(InstantDeliverySupplierSearch), nameof(SearchForSalvador)));
            return true;
        }

        private static bool SearchForAlbert(object __instance, ref IEnumerator __result)
        {
            __result = Search<Albert>(__instance, Albert);
            return false;
        }

        private static bool SearchForShirley(object __instance, ref IEnumerator __result)
        {
            __result = Search<Shirley>(__instance, Shirley);
            return false;
        }

        private static bool SearchForSalvador(object __instance, ref IEnumerator __result)
        {
            __result = Search<Salvador>(__instance, Salvador);
            return false;
        }

        /// <summary>Once a second, like the search it stands in for, until the supplier is handed over.</summary>
        private static IEnumerator Search<T>(object mod, string who) where T : Supplier
        {
            Round.TryGetValue(who, out int before);
            int mine = before + 1;
            Round[who] = mine;

            string lastWhy = null;
            while (true)
            {
                yield return new WaitForSeconds(1f);
                if (Round[who] != mine) yield break;

                if (HandOver<T>(mod, who, out string why))
                {
                    _log?.Msg($"[fix] instantdelivery-supplier-search: {who} was handed to InstantDelivery.");
                    yield break;
                }

                // Once per reason. A supplier that is not there yet is the normal first second of a load,
                // and the same line sixty times a minute is how the original filled the log.
                if (why != lastWhy)
                {
                    lastWhy = why;
                    _log?.Warning($"[fix] instantdelivery-supplier-search: {who} is not handed over yet, "
                                + $"because {why}. Still looking.");
                }
            }
        }

        /// <summary>Write the supplier's object into the mod's fields. False, with the reason, when it cannot yet.</summary>
        private static bool HandOver<T>(object mod, string who, out string why) where T : Supplier
        {
            why = null;
            try
            {
                T supplier = UnityEngine.Object.FindObjectOfType<T>();
                if (supplier == null)
                {
                    why = "the scene has no " + who + " supplier";
                    return false;
                }

                if (who == Albert)
                {
                    // The mod's own lookup, kept as it was: this name still resolves.
                    GameObject menu = GameObject.Find("PauseMenu");
                    if (menu == null || menu.transform.childCount == 0)
                    {
                        why = "there is no PauseMenu object with a child for the mod's labels";
                        return false;
                    }
                    _pauseMenu.SetValue(mod, menu.transform.GetChild(0).gameObject);
                }

                Held[who].SetValue(mod, supplier.gameObject);
                Found[who].SetValue(mod, true);
                return true;
            }
            catch (Exception e)
            {
                why = "the lookup failed: " + (e.InnerException ?? e).Message;
                return false;
            }
        }
    }
}
