#if DEBUG
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.Tools;
using UnityEngine;

namespace Polyfill.Report
{
    /// <summary>
    /// Opens an employee's management panel from the console, so a repair of a mod that extends that panel
    /// can be looked at without a person holding the clipboard.
    /// </summary>
    /// <remarks>
    /// Mods such as MoreEmployeeAssignments patch the config panels the management clipboard shows. Those
    /// patches run only when a panel binds, and the game opens a panel only when the player equips the
    /// clipboard, aims at an employee and clicks. This command takes the clipboard's own door,
    /// ManagementClipboard.Open, with the n-th employee of the asked kind, so every patch on
    /// ManagementInterface.Open, ConfigPanel.Bind and the field UIs runs as it does for a player.
    ///
    /// DEBUG ONLY, for the same reason as PhoneApp: it opens a screen the player did not ask for.
    /// </remarks>
    internal static class ManagementPanel
    {
        internal static void Open(string argument)
        {
            var words = (argument ?? "").Trim().ToLowerInvariant().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                Core.Log.Msg("polyfillpanel <packager|botanist|chemist|cleaner> [n] - opens the management panel of the "
                           + "n-th such employee (default 1). `polyfillpanel close` shuts it again.");
                return;
            }
            string wanted = words[0];
            int number = 1;
            if (words.Length > 1 && (!int.TryParse(words[1], out number) || number < 1))
            {
                Core.Log.Warning($"polyfillpanel: '{words[1]}' is not a number from 1 up.");
                return;
            }

            var clipboard = Singleton<ManagementClipboard>.Instance;
            if (clipboard == null)
            {
                Core.Log.Warning("polyfillpanel: no ManagementClipboard in this scene; load a save first.");
                return;
            }

            if (wanted == "close")
            {
                if (!clipboard.IsOpen) { Core.Log.Msg("polyfillpanel: the management panel is not open."); return; }
                clipboard.Close();
                Core.Log.Msg("polyfillpanel: closed the management panel.");
                return;
            }

            if (wanted is not ("packager" or "botanist" or "chemist" or "cleaner"))
            {
                Core.Log.Warning($"polyfillpanel: '{wanted}' is not one of packager, botanist, chemist, cleaner.");
                return;
            }
            var all = new System.Collections.Generic.List<Employee>();
            foreach (var e in UnityEngine.Object.FindObjectsOfType<Employee>())
            {
                bool kind = wanted switch
                {
                    "packager" => e.TryCast<Packager>() != null,
                    "botanist" => e.TryCast<Botanist>() != null,
                    "chemist" => e.TryCast<Chemist>() != null,
                    _ => e.TryCast<Cleaner>() != null,
                };
                if (kind) all.Add(e);
            }
            if (all.Count < number)
            {
                Core.Log.Warning($"polyfillpanel: this save has {all.Count} {wanted}(s), not {number}.");
                return;
            }
            Employee employee = all[number - 1];

            if (clipboard.IsOpen) clipboard.Close();
            var selection = new Il2CppSystem.Collections.Generic.List<IConfigurable>();
            selection.Add(new IConfigurable(employee.Pointer));
            clipboard.Open(selection, null);
            Core.Log.Msg($"polyfillpanel: opened the management panel of {wanted} {number} of {all.Count}, '{employee.FullName}'.");
        }
    }
}
#endif
