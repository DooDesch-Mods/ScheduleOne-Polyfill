namespace Polyfill.Contract
{
    /// <summary>
    /// Types Polyfill puts back as a class deriving from whatever the game renamed them to.
    /// </summary>
    /// <remarks>
    /// Named once, in Contract, because both halves need it and they must not drift: the plugin writes the
    /// stand-in classes into the interop assembly, and the plugin ALSO has to make Harmony look past them -
    /// see <c>Boot/DeclaredMethodFallback.cs</c> for why finding the type is only half the repair.
    ///
    /// This list is the old names only. Which type each one now derives from is decided where the stand-in
    /// is written, and read back off the class at runtime, so there is no second copy of that pairing to
    /// get wrong.
    /// </remarks>
    internal static class RenamedTypes
    {
        internal static readonly string[] StandIns =
        {
            "Il2CppScheduleOne.UI.Stations.MixingStationCanvas",
            "Il2CppScheduleOne.UI.Stations.ChemistryStationCanvas",
            "Il2CppScheduleOne.UI.Stations.CauldronCanvas",
            "Il2CppScheduleOne.UI.Stations.DryingRackCanvas",
            "Il2CppScheduleOne.UI.Handover.HandoverScreenPriceSelector",
            "Il2CppScheduleOne.Weather.WeatherConditions",
            "Il2CppScheduleOne.DevUtilities.ExitAction",
            "Il2CppScheduleOne.UI.ATM.ATMInterface",
            "Il2CppScheduleOne.UI.Stations.Drying_rack.DryingOperationUI",
        };

        internal static bool IsStandIn(string fullName)
        {
            if (fullName == null) return false;
            foreach (string name in StandIns)
                if (string.Equals(name, fullName, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
