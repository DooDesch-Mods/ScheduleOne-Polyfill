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
            ConsoleCommands.DeclareForTools();

            ReportReader.Load();
            if (ReportReader.Mods.Count == 0)
            {
                LoggerInstance.Warning("no startup report found - is Polyfill.Boot.dll in your Plugins folder?");
                return;
            }

            int blocked = 0;
            foreach (var mod in ReportReader.Mods) if (mod.Verdict != "clean") blocked++;
            LoggerInstance.Msg(blocked == 0
                ? $"{ReportReader.Mods.Count} mod(s) checked against Schedule I {ReportReader.GameVersion} - "
                  + "nothing is missing. Type `polyfill` in the console for the detail."
                : $"{blocked} of {ReportReader.Mods.Count} mod(s) ask for something this game version does not "
                  + "have. Type `polyfill` in the console.");
        }
    }
}
