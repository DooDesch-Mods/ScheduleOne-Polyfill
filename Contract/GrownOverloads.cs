namespace Polyfill.Contract
{
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
        };
    }
}
