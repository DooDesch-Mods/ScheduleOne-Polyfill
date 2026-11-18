using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(PhoneAppNoIconProbe.Probe), "PhoneAppNoIconProbe", "1.0.0", "DooDesch")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace PhoneAppNoIconProbe
{
    /// <summary>
    /// Test-only mod for tests/phoneapp_icon_check.ps1: one S1API phone app with neither an icon file nor a
    /// sprite, the shape that makes S1API's icon loader throw without fix phoneapp-icon-without-file.
    /// </summary>
    public sealed class Probe : MelonMod
    {
        public override void OnInitializeMelon()
            => LoggerInstance.Msg("[probe] PhoneAppNoIconProbe loaded; its app asks for an icon file it does not have.");
    }

    /// <summary>S1API finds this type on the home screen and creates it; registering it reaches the icon loader.</summary>
    public sealed class NoIconApp : S1API.PhoneApp.PhoneApp
    {
        protected override string AppName => "NoIconProbe";
        protected override string AppTitle => "No Icon Probe";
        protected override string IconLabel => "Probe";
        protected override string IconFileName => null;

        protected override void OnCreatedUI(GameObject container)
            => MelonLogger.Msg("[probe] NoIconApp built its UI.");
    }
}
