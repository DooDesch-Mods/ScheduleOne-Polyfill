namespace Polyfill.Contract
{
    /// <summary>
    /// Methods 0.4.6 replaced with two differently-named ones, where the old name was a patch target.
    /// </summary>
    /// <remarks>
    /// THE SAME REPAIR AS <see cref="SplitScreens"/> WITHOUT ITS SHAPE. That one describes five station
    /// canvases whose <c>SetIsOpen(station, open, removeUI)</c> became <c>Open(station)</c> and
    /// <c>Close()</c>, and its entry is written in those terms - a station type, the station parameter's
    /// name, whether there was a third argument. Useful exactly five times.
    ///
    /// The compatibility index then produced the same failure in a shape that data cannot express:
    /// <c>ObjectSelector.Close(bool, bool)</c> became <c>CloseAndSubmit()</c> and <c>CloseAndCancel()</c>,
    /// which share no name with the old method and take no arguments at all. So this says the general
    /// thing instead: here is the signature that went away, here are the methods that replaced it, and
    /// here is what each of them would have passed.
    ///
    /// WHY AN EMPTY BODY IS RIGHT HERE. Everything listed is a method a mod PATCHES. The plugin writes the
    /// old signature back so Harmony resolves it and the patch class registers - without that one gap
    /// takes the mod's other patches with it, because Harmony discards a whole class when one target is
    /// missing. A body would be dead code: the game is compiled against the new names and never calls the
    /// stand-in. What makes the stand-in worth having is the other half, in
    /// <c>ModFixes/PatchesOnReplacedMethods</c>, which postfixes the real methods so they call it.
    ///
    /// A method a mod also CALLS does not belong here - it needs a forwarding body and is a bridge of its
    /// own. The two are not interchangeable and getting it backwards is silent: an empty body under a
    /// caller returns without doing anything.
    ///
    /// Named entries only, and for the reason <see cref="GrownOverloads"/> gives at length: a rule like
    /// "any method whose name vanished next to two new ones" would pair things that merely look alike.
    /// Every entry below was read in both builds, in both branches, before it was written down.
    /// </remarks>
    internal static class ReplacedMethods
    {
        internal sealed class Replacement
        {
            /// <summary>The method the game calls now.</summary>
            internal string Name;

            /// <summary>Its parameter types, by full name. Empty for a parameterless method.</summary>
            internal string[] Parameters = new string[0];

            /// <summary>
            /// What this path would have passed to the old method, in its parameter order.
            /// </summary>
            /// <remarks>
            /// Constants, because that is all these cases need and anything richer invites a rule nobody
            /// checked. A value that cannot be a constant - the station argument the five canvases forward
            /// - is why <see cref="SplitScreens"/> still exists separately rather than being folded in.
            /// </remarks>
            internal object[] Arguments = new object[0];
        }

        internal sealed class Entry
        {
            internal string Type;
            internal string OldName;

            /// <summary>The old parameter types, by full name, in order.</summary>
            internal string[] Parameters;

            /// <summary>
            /// And their names, because Harmony binds a patch's arguments by name and not by position.
            /// </summary>
            internal string[] ParameterNames;

            /// <summary>Both halves, so the stand-in is called on either path.</summary>
            internal Replacement[] Replacements;

            /// <summary>Where this was read, so the next person can check it rather than trust it.</summary>
            internal string Because;
        }

        private const string Bool = "System.Boolean";
        private const string Management = "Il2CppScheduleOne.UI.Management.";

        /// <summary>
        /// The two management selectors, which took the same refactor in the same commit.
        /// </summary>
        /// <remarks>
        /// 0.4.5f2 has <c>public virtual void Close(bool returnToClipboard, bool pushChanges)</c> on both.
        /// 0.4.6f13 has neither, and instead a private <c>CloseAndSubmit()</c> that calls
        /// <c>callback(selectedObjects)</c> before <c>OnClose()</c>, and a private <c>CloseAndCancel()</c>
        /// that calls <c>OnClose()</c> alone (ObjectSelector.cs:120-130).
        ///
        /// The arguments are read off those bodies rather than guessed. <c>pushChanges</c> is what the
        /// callback did, so it is true for Submit and false for Cancel. <c>returnToClipboard</c> is true on
        /// both, because <c>OnClose()</c> now does <c>EquippedClipboard.EndOverride()</c> and
        /// <c>ManagementClipboard.Open(...)</c> unconditionally (ObjectSelector.cs:142-143) where 0.4.5f2
        /// did it only when the flag was set.
        /// </remarks>
        private static Entry Selector(string type) => new Entry
        {
            Type = Management + type,
            OldName = "Close",
            Parameters = new[] { Bool, Bool },
            ParameterNames = new[] { "returnToClipboard", "pushChanges" },
            Replacements = new[]
            {
                new Replacement { Name = "CloseAndSubmit", Arguments = new object[] { true, true } },
                new Replacement { Name = "CloseAndCancel", Arguments = new object[] { true, false } },
            },
            Because = "0.4.5f2 " + type + ".Close(bool returnToClipboard, bool pushChanges); 0.4.6f13 has "
                    + "CloseAndSubmit and CloseAndCancel over a shared OnClose that always returns to the "
                    + "clipboard (ObjectSelector.cs:120-143)",
        };

        internal static readonly Entry[] All =
        {
            Selector("ObjectSelector"),
            Selector("TransitEntitySelector"),
        };
    }
}
