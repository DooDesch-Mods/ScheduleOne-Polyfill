using System.Net.Http;
using System.Text;
using Polyfill.Contract;

namespace Polyfill.Report
{
    /// <summary>
    /// `polyfillcheck`: the log check on polyfill.doomods.com, asked from the console.
    /// </summary>
    /// <remarks>
    /// The page answers a dropped Latest.log. A player who is in the game already has everything it reads
    /// in memory, so this sends that and opens the answer, without anybody finding a log file.
    ///
    /// Typing the command is the player asking for it, so this does not wait for the share consent the
    /// session report needs. What it sends is what the page sends for a dropped log, and the help line
    /// says it goes to the site.
    ///
    /// Unlike the session report, a failure here is an error in the log: somebody typed a command and is
    /// waiting for a page that will not open.
    /// </remarks>
    internal static class LogCheck
    {
        internal const string Site = "https://polyfill.doomods.com/check";
        private const string Endpoint = "https://polyfill.doomods.com/api/check";
        private const int TimeoutSeconds = 15;

        private static volatile bool _busy;

        /// <summary>A link for the main thread to open. Unity may not be asked from the request's thread.</summary>
        private static volatile string _toOpen;

        internal static void Run(string argument)
        {
            if (_busy) { Core.Log.Msg("[check] the last check is still on its way."); return; }

            var report = ReportReader.Report;
            if (report == null || report.Mods.Count == 0)
            {
                Core.Log.Warning("[check] there is no startup report to check, so nothing was sent. "
                    + $"Drop MelonLoader/Latest.log on {Site} instead.");
                return;
            }

            string game = report.Game;
            if (!CheckExcerpt.Version.IsMatch(game ?? ""))
            {
                try { game = UnityEngine.Application.version; }
                catch (Exception e) { Core.Log.Warning("[check] the game did not say its version: " + e.Message); }
            }

            var errors = new List<CheckExcerpt.Error>();
            foreach (var trouble in Watch.Troubles)
                errors.Add(new CheckExcerpt.Error
                {
                    Mod = trouble.Mod, Kind = trouble.Kind, Frame = trouble.Frame, Site = trouble.Site,
                    Count = trouble.Count,
                });

            var built = CheckExcerpt.Build(report, errors, game, MelonLoaderVersion(), DooDesch.ModVersion.Current);
            if (built == null)
            {
                Core.Log.Error($"[check] the game version '{game}' is not one the site takes, so nothing was sent.");
                return;
            }
            if (built.Skipped > 0)
                Core.Log.Warning($"[check] {built.Skipped} mod(s) have no usable name or version and are left out.");

            string body = CheckExcerpt.Request(built.Json);
            _busy = true;
            Core.Log.Msg($"[check] sending {built.Mods} mod(s) to {Site} ...");

            // Off the main thread: the game does not stop while the site answers.
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    var answer = await client.PostAsync(Endpoint, content).ConfigureAwait(false);
                    string text = await answer.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!answer.IsSuccessStatusCode)
                    {
                        Core.Log.Error($"[check] the site answered {(int)answer.StatusCode}: "
                            + CheckExcerpt.ReasonOf(text));
                        return;
                    }

                    string id = CheckExcerpt.IdOf(text);
                    if (id == null)
                    {
                        Core.Log.Error("[check] the site checked the mods but gave no link. "
                            + $"Drop MelonLoader/Latest.log on {Site} instead.");
                        return;
                    }

                    string link = Site + "/" + id;
                    Core.Log.Msg("[check] your result: " + link);
                    if (Headless.Yes()) Core.Log.Msg("[check] no browser on a server; open the link above.");
                    else _toOpen = link;
                }
                catch (Exception e)
                {
                    Core.Log.Error("[check] could not reach the site, so nothing was checked: " + e.Message);
                }
                finally
                {
                    _busy = false;
                }
            });
        }

        /// <summary>Open a finished check in the browser. Called every frame; costs one read when idle.</summary>
        internal static void OnUpdate()
        {
            string link = _toOpen;
            if (link == null) return;
            _toOpen = null;
            try
            {
                UnityEngine.Application.OpenURL(link);
                Core.Log.Msg("[check] opened the result in your browser.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[check] could not open the browser: " + e.Message + ". Open the link above.");
            }
        }

        private static string MelonLoaderVersion()
        {
            try { return MelonLoader.Properties.BuildInfo.Version; }
            catch (Exception e)
            {
                Core.Log.Warning("[check] MelonLoader did not say its version: " + e.Message);
                return "";
            }
        }
    }
}
