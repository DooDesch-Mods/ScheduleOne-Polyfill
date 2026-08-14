namespace Polyfill.Contract
{
    /// <summary>
    /// The station screens whose one open-or-close method 0.4.6 split into two.
    /// </summary>
    /// <remarks>
    /// A PATCH TARGET THAT WAS SPLIT IN TWO HAS NO SUCCESSOR, which is what the report said and what this
    /// is the answer to. <c>SetIsOpen(station, open)</c> became <c>Open(station)</c> plus <c>Close()</c>,
    /// so there is nothing to point a patch at - and because Harmony throws a whole patch CLASS away when
    /// one target is missing, that one gap also took Backpack's canvas handling with it, five times over.
    ///
    /// The repair is in two halves, and neither works alone. The plugin writes <c>SetIsOpen</c> back as an
    /// empty method, so the patch resolves and the class registers. The mod then patches something the
    /// game never calls - so the mod half hangs a postfix on the real <c>Open</c> and <c>Close</c> that
    /// calls the empty one, at the moment it used to be called and with the value it used to be given.
    ///
    /// Parameter names carry the repair as much as the types do: Harmony binds a patch's arguments by
    /// name, and every one of these took its flag as <c>open</c> (PackagingStationCanvas.cs:148 and its
    /// four siblings in 0.4.5f2). Backpack's postfix is declared <c>(canvas __instance, bool open)</c> and
    /// binds unchanged.
    /// </remarks>
    internal static class SplitScreens
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string Station;      // the parameter the old method took first, by full name
            internal string StationName;  // and what it was called, because Harmony binds by name
            internal bool HasRemoveUi;    // four of the five carried a third argument

            /// <summary>The station type at runtime, or null when this build does not have it.</summary>
            internal Type StationType() => HarmonyLib.AccessTools.TypeByName(Station);
        }

        private const string Stations = "Il2CppScheduleOne.UI.Stations.";
        private const string Objects = "Il2CppScheduleOne.ObjectScripts.";

        internal static readonly Entry[] All =
        {
            new Entry { Type = Stations + "PackagingStationCanvas", Station = Objects + "PackagingStation",
                        StationName = "station", HasRemoveUi = true },
            new Entry { Type = Stations + "BrickPressCanvas", Station = Objects + "BrickPress",
                        StationName = "press", HasRemoveUi = true },
            new Entry { Type = Stations + "LabOvenCanvas", Station = Objects + "LabOven",
                        StationName = "oven", HasRemoveUi = true },
            new Entry { Type = Stations + "CauldronCanvas", Station = Objects + "Cauldron",
                        StationName = "cauldron", HasRemoveUi = true },
            new Entry { Type = Stations + "DryingRackCanvas", Station = Objects + "DryingRack",
                        StationName = "rack", HasRemoveUi = false },
        };
    }

    /// <summary>
    /// Methods the game kept and gave a trailing argument to, named once for both halves of the project.
    /// </summary>
    /// <remarks>
    /// A method that grows an argument breaks a mod TWICE, and the two breaks need different repairs.
    ///
    /// The CALL is fixed by putting the old signature back as a method that supplies the new argument -
    /// that is a bridge, and it happens in the interop assembly before anything loads. The PATCH is not:
    /// Harmony resolved the old signature, found the method Polyfill just added, and patched that. The mod
    /// registers without error, and the hook never fires, because the game calls the real one. Silence is
    /// the worst of the three outcomes - louder than a crash, in the sense that nobody hears it.
    ///
    /// So this list exists in <c>Contract</c> rather than beside the bridges: the plugin uses it to know
    /// what it added, the mod uses it to move those patches onto the method the game actually calls, and a
    /// single edit keeps them from drifting. Anything named here must be a method whose old parameter list
    /// is a PREFIX of the new one - that is what makes a prefix written for the old form still bind by name
    /// on the new one.
    ///
    /// Deliberately a list rather than a rule. "Any method with a longer sibling of the same name" also
    /// describes <c>NPCMovement.SetDestination(Vector3)</c> next to its four-argument form - two methods
    /// the game has always had and a mod may well mean one and not the other. Moving a patch there would
    /// be a change nobody asked for.
    /// </remarks>
    internal static class GrownOverloads
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string Name;
            internal string[] OldParameters;   // by full name, in order
            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.UI.StorageMenu",
                Name = "Open",
                OldParameters = new[]
                {
                    "System.String", "System.String", "Il2CppScheduleOne.ItemFramework.IItemSlotOwner",
                },
                Because = "0.4.6 gave every StorageMenu.Open a closing callback",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.UI.StorageMenu",
                Name = "Open",
                OldParameters = new[]
                {
                    "Il2CppScheduleOne.ItemFramework.IItemSlotOwner", "System.String", "System.String",
                },
                Because = "0.4.6 gave every StorageMenu.Open a closing callback",
            },
            new Entry
            {
                Type = "Il2CppScheduleOne.Economy.CustomerData",
                Name = "GetOrderDays",
                OldParameters = new[] { "System.Single", "System.Single" },
                Because = "GetOrderDays stopped returning the list and started filling one it is handed",
            },
        };

        /// <summary>Has Polyfill put a second signature under this name?</summary>
        internal static bool Doubled(string type, string name)
        {
            foreach (var entry in All)
                if (entry.Name == name && string.Equals(entry.Type, type, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>Is a method of that arity one Polyfill added rather than one the game has?</summary>
        internal static bool IsStandIn(string type, string name, int parameterCount)
        {
            foreach (var entry in All)
                if (entry.Name == name && entry.OldParameters.Length == parameterCount
                    && string.Equals(entry.Type, type, StringComparison.Ordinal))
                    return true;
            return false;
        }
    }
}
