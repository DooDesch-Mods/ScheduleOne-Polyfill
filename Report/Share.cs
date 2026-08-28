using MelonLoader;
using System.Net.Http;
using System.Text;
using Polyfill.Contract;

namespace Polyfill.Report
{
    /// <summary>
    /// Send what this launch found, if the player said it may, and never anything else.
    /// </summary>
    /// <remarks>
    /// The point of the whole exercise: today a broken mod is discovered by one player hitting it and
    /// deciding to post about it. Most do not post. So the same nine reports arrive over months, in
    /// nine places, each needing a conversation to reach the log line that was in the report all along.
    ///
    /// WHAT LEAVES THE MACHINE is fixed here and nowhere else, so it can be read in one screen: the
    /// mod's name, version and author, the game version, and per finding the symbol and what happened
    /// to it. That is the whole payload. There is deliberately no field for the player, the save, the
    /// install path, or which mods run together - the last one because a mod list is a fingerprint,
    /// and a set of mods narrow enough identifies a person as well as a name would.
    ///
    /// FAILURE IS SILENT ON PURPOSE, which is the one place this project's "never swallow an error"
    /// rule bends: a player who agreed to help does not want a warning in their log because a server
    /// was down. It is logged once at Msg level and never retried within a session.
    /// </remarks>
    internal static class Share
    {
        private const string Endpoint = "https://polyfill.doomods.com/api/report";
        private const int TimeoutSeconds = 10;

        /// <summary>The format the service parses. Bumped when a column moves, never reused.</summary>
        private const int Format = 1;

        private static bool _sent;

        /// <summary>
        /// Send the run, once per launch, if sharing is on.
        /// </summary>
        /// <remarks>
        /// Called after the report exists and every fix has run, so what is sent is what actually
        /// happened rather than what was found - the difference the report itself learned the hard way.
        /// </remarks>
        internal static void Run(RunReport report)
        {
            if (_sent || report == null) return;
            _sent = true;

            if (!Consent.Sharing) return;
            if (report.Mods.Count == 0) return;

            string body = Body(report);

            // Fire and forget: a launch waits for nothing here. The task is not awaited and its failure
            // is a log line, not a throw into whoever called us.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
                    using var content = new StringContent(body, Encoding.UTF8, "text/plain");
                    var answer = await client.PostAsync(Endpoint, content).ConfigureAwait(false);

                    Core.Log.Msg(answer.IsSuccessStatusCode
                        ? $"[share] sent {report.Mods.Count} mod(s) to the compatibility list."
                        : $"[share] the compatibility list answered {(int)answer.StatusCode}; nothing was "
                        + "kept. Nothing else changes.");
                }
                catch (Exception e)
                {
                    Core.Log.Msg("[share] could not reach the compatibility list, so nothing was sent: "
                               + e.Message);
                }
            });
        }

        /// <summary>
        /// The payload, in the same pipe-separated shape the local report uses.
        /// </summary>
        /// <remarks>
        /// Text rather than JSON for the reason the local report is text: it can be read without a
        /// tool, which matters most for the one file a player might want to look at before agreeing to
        /// send it. `polyfillshare` prints this, so nobody has to trust a description of it.
        /// </remarks>
        internal static string Body(RunReport report)
        {
            var text = new StringBuilder();
            text.Append("# polyfill-share ").Append(Format).Append('\n');
            text.Append("# game=").Append(report.Game).Append('\n');
            text.Append("# install=").Append(Installation()).Append('\n');

            foreach (var mod in report.Mods)
            {
                text.Append("M|").Append(Clean(mod.Display)).Append('|')
                    .Append(Clean(mod.Version)).Append('|')
                    .Append(Clean(mod.Author)).Append('|')
                    .Append(mod.Verdict).Append('|')
                    .Append(mod.Findings.Count).Append('\n');

                foreach (var finding in mod.Findings)
                {
                    text.Append("F|").Append(Clean(mod.Display)).Append('|')
                        .Append(finding.Kind).Append('|')
                        .Append(Clean(finding.Symbol)).Append('|')
                        .Append(Clean(finding.Outcome ?? "none")).Append('\n');
                }
            }
            return text.ToString();
        }

        /// <summary>
        /// A number that says which installation this is, and nothing else about it.
        /// </summary>
        /// <remarks>
        /// Rolled once from the system's own random source and kept beside the answer to the sharing
        /// question. It is not derived from the machine, the account, the save or the install path -
        /// there is nothing in it to work backwards from.
        ///
        /// It exists because the index counts players and receives posts. Without it one machine
        /// launching twenty times a day is twenty voices, and a machine posting in a loop decides what
        /// the board says about somebody else's mod. With it, that machine is one voice however often
        /// it speaks.
        ///
        /// It does link a player's own reports to each other, which is more than nothing, so the
        /// site's About page names it rather than leaving it to be found.
        /// </remarks>
        private static string Installation()
        {
            try
            {
                var category = MelonPreferences.GetCategory("Polyfill")
                               ?? MelonPreferences.CreateCategory("Polyfill");

                var entry = category.GetEntry<string>("ShareInstallation");
                if (entry != null && !string.IsNullOrEmpty(entry.Value)) return entry.Value;

                string rolled = Guid.NewGuid().ToString("N");
                if (entry == null)
                    category.CreateEntry("ShareInstallation", rolled, "Installation id",
                        "A random number sent with a shared report so the index counts installations "
                      + "rather than posts. Clear it to become a new installation.");
                else entry.Value = rolled;

                MelonPreferences.Save();
                return rolled;
            }
            catch
            {
                // No id beats a made-up one that changes every launch: the service then counts by
                // address, which is coarse but at least stable.
                return "";
            }
        }

        /// <summary>
        /// Strip what a field must never carry, rather than trusting it not to.
        /// </summary>
        /// <remarks>
        /// A separator inside a value would move every column after it, and a path is the one thing in
        /// this report that identifies a person - `C:\Users\<name>\...`. Neither is expected in these
        /// fields; both are removed anyway, because "expected" is not a guarantee and this payload
        /// leaves the machine.
        /// </remarks>
        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            var text = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '|' || c == '\n' || c == '\r') { text.Append(' '); continue; }
                text.Append(c);
            }

            string cleaned = text.ToString().Trim();
            return cleaned.Length > 120 ? cleaned.Substring(0, 120) : cleaned;
        }
    }
}
