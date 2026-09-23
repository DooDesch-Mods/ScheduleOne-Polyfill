namespace Polyfill.Contract
{
    /// <summary>
    /// Methods whose old form Polyfill puts back, and which a patch naming only the method has to reach.
    /// </summary>
    /// <remarks>
    /// THE OPPOSITE ANSWER TO <see cref="GrownOverloads"/>, and both are right. There, the game's method is
    /// the old one with more arguments, so a patch that names no parameters belongs on the game's. Here the
    /// game kept the name and rebuilt everything under it - 0.4.7's TryGetPlayerData takes whether the asker
    /// is the host and hands back one bundle instead of five values - so a patch written for the old form
    /// binds parameters (<c>PlayerData data</c>, <c>ref string inventoryString</c>) that only the stand-in
    /// has. It belongs on the stand-in, and the Report half calls it from the real method with the
    /// arguments translated.
    ///
    /// Only an arity is named: the plugin reads this list, and the plugin must not spell interop types.
    /// </remarks>
    internal static class ReshapedMethods
    {
        internal static readonly (string Type, string Name, int StandInArity, string Because)[] All =
        {
            ("Il2CppScheduleOne.PlayerScripts.PlayerManager", "TryGetPlayerData", 6,
             "0.4.7 bundles the five out values into one FullPlayerData and takes whether the asker is the host"),
        };

        /// <summary>The arity of the stand-in a name-only lookup should get, or -1.</summary>
        internal static int StandIn(string type, string name)
        {
            foreach (var entry in All)
                if (entry.Name == name && string.Equals(entry.Type, type, StringComparison.Ordinal))
                    return entry.StandInArity;
            return -1;
        }
    }
}
