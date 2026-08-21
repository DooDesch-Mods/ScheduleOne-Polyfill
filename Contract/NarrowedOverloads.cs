namespace Polyfill.Contract
{
    /// <summary>
    /// Methods 0.4.6 kept under the same name while changing what their argument IS.
    /// </summary>
    /// <remarks>
    /// The third shape in this family, and the only one where neither of the other two repairs reaches.
    /// <see cref="GrownOverloads"/> covers a method that gained a trailing argument, so the old parameter
    /// list is still a prefix and a patch binds unchanged. <see cref="SplitMethods"/> covers one method
    /// becoming two. This is the case where the argument itself was replaced:
    /// <code>
    /// 0.4.5f2   CounterofferInterface.ChangeQuantity(int change)
    /// 0.4.6     CounterofferInterface.ChangeQuantity(float change)     the +/- control's own step
    ///           CounterofferInterface.ChangeQuantity(string value)     what is typed into the box
    /// </code>
    /// A mod patching the old name meets BOTH failures at once, which is why the milder repairs do not
    /// help. Harmony resolves <c>[HarmonyPatch(typeof(X), "ChangeQuantity")]</c> through a lookup that
    /// throws the moment two methods share the name, so the patch class never binds; and even given the
    /// right one, a postfix declared <c>(int change)</c> cannot bind to a parameter that is now a float -
    /// Harmony refuses to compile a patch whose declared type does not match, and says so as "IL Compile
    /// Error (unknown location)" with nothing else to go on.
    ///
    /// So the patch is CALLED rather than bound, the way <see cref="SplitMethods"/> calls its own, and the
    /// value is converted on the way in. Deal Optimizer's postfix re-runs its counteroffer evaluation
    /// after the player nudges the quantity; the number it is handed is the same number, in the type its
    /// author wrote against.
    ///
    /// Only the named list, for the reason <see cref="GrownOverloads"/> gives at length: "any method whose
    /// argument type changed" would also describe pairs the game has always had, and moving a patch there
    /// is a change nobody asked for.
    /// </remarks>
    internal static class NarrowedOverloads
    {
        internal sealed class Entry
        {
            internal string Type;
            internal string Name;
            internal string[] RealParameters;   // what the game calls now, by full name
            internal string ParameterName;      // shared by both forms, which is how the value is matched
            internal string Because;
        }

        internal static readonly Entry[] All =
        {
            new Entry
            {
                Type = "Il2CppScheduleOne.UI.Phone.CounterofferInterface",
                Name = "ChangeQuantity",
                RealParameters = new[] { "System.Single" },
                ParameterName = "change",
                Because = "the quantity step took an int until 0.4.5f2 and takes a float now, and a second "
                        + "ChangeQuantity(string) arrived beside it for the text box "
                        + "(CounterofferInterface.cs:191-207)",
            },
        };
    }
}
