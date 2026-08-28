using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Boot
{
    /// <summary>MelonLoader's logger, behind the interface the analysis speaks.</summary>
    /// <remarks>
    /// The whole of the MelonLoader dependency the analysis used to carry, in one file. Everything in
    /// <c>Core/</c> reads assemblies with Cecil, so with this in the way the same code answers the same
    /// question from a command line, for a mod author who has no game running.
    /// </remarks>
    internal sealed class MelonLog : ILog
    {
        private readonly MelonLogger.Instance _log;

        internal MelonLog(MelonLogger.Instance log) => _log = log;

        public void Msg(string message) => _log.Msg(message);
        public void Warning(string message) => _log.Warning(message);
        public void Error(string message) => _log.Error(message);
    }
}
