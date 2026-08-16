namespace Polyfill.Contract
{
    /// <summary>
    /// Methods 0.4.6 split, where Polyfill's stand-in is a patch target the game never calls.
    /// </summary>
    /// <remarks>
    /// THE SAME TRAP AS <see cref="GrownOverloads"/> WITH A DIFFERENT SHAPE, and it took a bug report to
    /// see it twice. A bridge puts the old signature back so a mod's CALL keeps working. A mod that also
    /// PATCHES that method resolves the old signature, lands on the stand-in, and registers cleanly - and
    /// the game, compiled against the new methods, never goes through it. The patch never runs.
    ///
    /// Over The Counter is the case this was written for. It patches
    /// <c>ManagementClipboard.Close(bool)</c> and closes its own manager panel from the postfix when the
    /// flag is false (ManagerClipboardPatch.cs:244-260). On 0.4.5f2 that covered every close. On 0.4.6 the
    /// player's own exit goes to the parameterless <c>Close()</c> instead (ManagementClipboard.cs:72-78),
    /// so the postfix never fires, the panel is never taken down, and the next employee's panel opens on
    /// top of it. Reported as two UIs overlapping.
    ///
    /// THE PATCH IS CALLED, NOT MOVED, and the first attempt at this got it wrong in a way worth keeping.
    /// Re-aiming the patch at the successor looks right - Harmony binds by name, the successor has no
    /// <c>preserveState</c>, so surely the postfix just gets <c>default(bool)</c>. It does not. Harmony
    /// refuses to compile a patch whose declared parameter cannot be bound, and says so as "IL Compile
    /// Error (unknown location)" with nothing else to go on. The relay therefore invokes the patch itself
    /// and fills the missing value.
    ///
    /// False is the right value to fill it with, and not by default: this path IS the ordinary close. The
    /// one path that must preserve the panel is the mod's own <c>Close(true)</c>, which still goes through
    /// the stand-in where its patch is untouched.
    ///
    /// Only the named list, for the reason <see cref="GrownOverloads"/> gives: "any method with a shorter
    /// sibling" would relay patches onto methods that mean something else.
    /// </remarks>
    internal static class SplitMethods
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string Name;                 // both halves carry it, which is what makes them a pair
            internal string[] StandInParameters;  // what the bridge put back
            internal string[] RealParameters;     // what the game calls instead
            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.Tools.ManagementClipboard",
                Name = "Close",
                StandInParameters = new[] { "System.Boolean" },
                RealParameters = new string[0],
                Because = "the player's own exit calls Close() and never the flagged one, so a mod's "
                        + "postfix on the old signature stopped seeing ordinary closes",
            },
        };
    }
}
