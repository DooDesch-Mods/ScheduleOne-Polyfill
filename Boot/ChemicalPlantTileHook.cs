using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Boot
{
    /// <summary>
    /// Chemical Plant's rule for its own tiles runs as a Harmony prefix on <c>Tile.CanBeBuiltOn</c> instead of a
    /// native hook on the same function.
    /// </summary>
    /// <remarks>
    /// Chemical Plant 1.4.0 hooks the machine code of <c>Tile.CanBeBuiltOn</c> with a MelonLoader NativeHook in its
    /// OnInitializeMelon, so its yard tiles always count as buildable. Over The Counter patches the same method with
    /// Harmony a moment later, and Il2CppInterop hooks the same address a second time. The second hook calls the
    /// first one back as the original, every call on a tile outside an OTC building re-enters, and the game dies of
    /// a stack overflow the first time anything asks a vanilla tile whether it can be built on.
    ///
    /// A Harmony prefix does the same thing as Chemical Plant's hook and joins Over The Counter's patch on one
    /// detour. Its yard tiles are read from the mod's own <c>_iyTilePtrs</c>, so the rule stays Chemical Plant's.
    /// This is installed here because the hook goes in during the mod's OnInitializeMelon, before any ModFix runs.
    /// </remarks>
    internal static class ChemicalPlantTileHook
    {
        private const string Id = "doodesch.polyfill.chemicalplant-tiles";
        private const string ModType = "IndustrialYard.IndustrialYardMod";
        private const string HookMethod = "ApplyCanBeBuiltOnHook";

        private static MelonLogger.Instance _log;
        private static HashSet<IntPtr> _tiles;
        private static Func<object, IntPtr> _pointer;
        private static bool _failed;

        internal static void Install(MelonLogger.Instance log)
        {
            _log = log;
            var mod = AccessTools.TypeByName(ModType);
            if (mod == null) return;   // Chemical Plant is not installed
            try
            {
                var hook = AccessTools.Method(mod, HookMethod);
                _tiles = AccessTools.Field(mod, "_iyTilePtrs")?.GetValue(null) as HashSet<IntPtr>;
                if (hook == null || _tiles == null)
                {
                    log.Warning($"[chemicalplant] {HookMethod} or its tile list is not where 1.4.0 has them, so its native "
                              + "hook on Tile.CanBeBuiltOn stays. With Over The Counter installed the game can crash.");
                    return;
                }
                new HarmonyLib.Harmony(Id).Patch(hook, prefix: new HarmonyMethod(typeof(ChemicalPlantTileHook), nameof(InsteadOfNativeHook)));
                log.Msg("[chemicalplant] its tile rule will run as a Harmony patch, so it can share Tile.CanBeBuiltOn "
                      + "with other mods.");
            }
            catch (Exception e)
            {
                log.Error("[chemicalplant] could not replace its native hook on Tile.CanBeBuiltOn; with Over The Counter "
                        + "installed the game can crash: " + e);
            }
        }

        /// <summary>Runs in place of Chemical Plant's hook installer and puts the same rule on as a Harmony prefix.</summary>
        private static bool InsteadOfNativeHook()
        {
            try
            {
                var tile = Type.GetType("Il2CppScheduleOne.Tiles.Tile, Assembly-CSharp");
                var canBeBuiltOn = tile == null ? null : AccessTools.Method(tile, "CanBeBuiltOn", Type.EmptyTypes);
                var pointer = tile == null ? null : AccessTools.Property(tile, "Pointer");
                if (canBeBuiltOn == null || pointer == null)
                {
                    _log?.Warning("[chemicalplant] Tile.CanBeBuiltOn was not found, so Chemical Plant's native hook goes in "
                                + "as it was written.");
                    return true;
                }

                var self = Expression.Parameter(typeof(object));
                _pointer = Expression.Lambda<Func<object, IntPtr>>(
                    Expression.Property(Expression.Convert(self, pointer.DeclaringType), pointer), self).Compile();

                new HarmonyLib.Harmony(Id).Patch(canBeBuiltOn,
                    prefix: new HarmonyMethod(typeof(ChemicalPlantTileHook), nameof(YardTileCanBeBuiltOn)));
                _log?.Msg("[chemicalplant] tile rule on Tile.CanBeBuiltOn is a Harmony patch; the native hook was left out.");
                return false;
            }
            catch (Exception e)
            {
                _log?.Error("[chemicalplant] could not patch Tile.CanBeBuiltOn, so Chemical Plant's native hook goes in as "
                          + "it was written: " + e);
                return true;
            }
        }

        /// <summary>Chemical Plant's yard tiles can always be built on; every other tile asks the game.</summary>
        private static bool YardTileCanBeBuiltOn(object __instance, ref bool __result)
        {
            if (_failed || __instance == null || _tiles.Count == 0) return true;
            try
            {
                if (!_tiles.Contains(_pointer(__instance))) return true;
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                _failed = true;
                _log?.Error("[chemicalplant] the tile rule failed and is off for this session; Chemical Plant's yard tiles "
                          + "ask the game like every other tile: " + e);
                return true;
            }
        }
    }
}
