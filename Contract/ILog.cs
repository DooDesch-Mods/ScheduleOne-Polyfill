namespace Polyfill.Contract
{
    /// <summary>
    /// Somewhere to say what happened, without deciding where that is.
    /// </summary>
    /// <remarks>
    /// The analysis was written against MelonLoader's logger and used it in four lines, which was four
    /// lines too many: everything else in <c>Core/</c> already reads assemblies with Cecil and needs no
    /// game, no MelonLoader and no Unity. Those four lines were the only reason the same code could not
    /// answer the same question from a command line.
    ///
    /// That matters because of who is asking. A player finds out a mod is broken when it breaks; the
    /// author finds out when the player posts. The check that tells them apart is the one in here, and
    /// it can run for either of them.
    /// </remarks>
    internal interface ILog
    {
        void Msg(string message);
        void Warning(string message);
        void Error(string message);
    }
}
