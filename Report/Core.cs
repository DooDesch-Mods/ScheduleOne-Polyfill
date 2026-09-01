using MelonLoader;

[assembly: MelonInfo(typeof(Polyfill.Report.Core), "Polyfill", DooDesch.ModVersion.Current, "DooDesch",
    "https://github.com/DooDesch-Mods/ScheduleOne-Polyfill")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Polyfill.Report
{
    /// <summary>
    /// The in-game view of what Polyfill decided at startup.
    /// </summary>
    /// <remarks>
    /// The work happens in Polyfill.Boot.dll, in Plugins/, long before this mod exists. All this does is
    /// read the report that plugin left behind and put it in front of the player - which is the only honest
    /// answer to "what did you do to my mods": here is the list, in the game, per mod, with the reason.
    ///
    /// If this mod is absent, nothing breaks. If the plugin is absent, this says so instead of pretending.
    /// </remarks>
    public sealed class Core : MelonMod
    {
        internal static MelonLogger.Instance Log { get; private set; }

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            // The mod is loaded in its own right on a machine where the plugin may have been removed,
            // so it installs the store itself rather than assuming somebody else already did.
            Polyfill.Boot.MelonConsentStore.Install();
            ConsoleCommands.DeclareForTools();

            // Two readings of one number. The plugin cannot ask Unity that early and the version it used is
            // the one every decision was made against, so a disagreement is worth a line and not a change of
            // mind. In practice they agree; a day they do not is a day somebody needs to know.
            string clash = null;
            try { clash = Contract.GameVersionSource.Disagreement(UnityEngine.Application.version); } catch { }
            if (clash != null) LoggerInstance.Warning(clash);

            ReportReader.Load();
            if (ReportReader.Mods.Count == 0)
            {
                LoggerInstance.Warning(ReportReader.Problem
                    ?? "no startup report found - is Polyfill.Boot.dll in your Plugins folder?");
                return;
            }

            int blocked = 0;
            foreach (var mod in ReportReader.Mods) if (mod.Verdict != "clean") blocked++;
            // WHERE TO LOOK depends on whether anybody can type. A dedicated server runs its own
            // console with a closed command registry, so `polyfill` is answered there with "Unknown
            // command" - pointing an operator at it sends them somewhere that cannot help.
            string where = Contract.Headless.Yes()
                ? "The full list is in UserData/Polyfill/last-run.txt."
                : "Type `polyfill` in the console for the detail.";

            LoggerInstance.Msg(blocked == 0
                ? $"{ReportReader.Mods.Count} mod(s) checked against Schedule I {ReportReader.GameVersion} - "
                  + "nothing is missing. " + where
                : $"{blocked} of {ReportReader.Mods.Count} mod(s) ask for something this game version does not "
                  + "have. " + where);
        }

        private bool _fixesRun;

        /// <summary>
        /// Run the per-mod fixes on the first frame where the game can answer them.
        /// </summary>
        /// <remarks>
        /// These fixes read live state - the spawnable prefab list, for one - which does not exist until the
        /// scene and its NetworkManager are up. So the trigger is not a lifecycle event but the state
        /// itself: the first frame it is there, they run, once. Nothing here polls after that.
        /// </remarks>
        public override void OnUpdate()
        {
            if (_fixesRun) return;
            var live = PrefabLookup.Names();
            if (live == null || live.Count == 0) return;

            _fixesRun = true;
            ModFixes.Fixes.Run(LoggerInstance);

            // From HERE and not from the plugin, because of what there is to watch. Every repair and every
            // mod fix has run and the world exists, so from this frame on an exception is a statement about
            // playing rather than about loading.
            Watch.Begin(ReportReader.Report, LoggerInstance);
        }

        /// <summary>
        /// Send the session when the player leaves, and not before.
        /// </summary>
        /// <remarks>
        /// THIS IS WHAT MAKES THE REPORT WORTH ANYTHING. Sent at load it could only say "every name this
        /// mod asks for exists", which is not the question anybody has - a mod that starts and then throws
        /// on every interaction passes that test. Sent at the end it carries how long the session lasted
        /// and what went wrong during it.
        ///
        /// A session that ends in a crash is lost, and that is accepted rather than worked around: the
        /// alternative is a second message format and two halves to stitch together, for a case the next
        /// launch reports anyway.
        /// </remarks>
        public override void OnApplicationQuit()
        {
            Watch.End();
            Share.Run(ReportReader.Report, Watch.Minutes, Watch.Troubles);
        }
    }
}
