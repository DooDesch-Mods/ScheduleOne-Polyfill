using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Let OverTheCounter use a newer SteamNetworkLib than the one it was built against.
    /// </summary>
    /// <remarks>
    /// OverTheCounter compares the installed SteamNetworkLib against the version it compiled with and
    /// turns its multiplayer sync off unless they are EQUAL (ConfigSyncData.cs:59):
    /// <code>
    /// if (assembly.GetName().Version != RequiredSteamNetworkLibVersion) { ...disabled... }
    /// </code>
    /// Not a minimum, not a capability test - an equality. So 1.5.0.0 fails against a required 1.2.4.0 no
    /// matter what it can do, and the player loses co-op sync while being told to "update SteamNetworkLib
    /// to the correct version", which is the one thing that would not help.
    ///
    /// Measured on this pair rather than assumed: every one of OverTheCounter's 9 type references and 34
    /// member references into SteamNetworkLib resolves against the installed 1.5.0.0. Nothing it calls is
    /// gone or has changed shape.
    ///
    /// So the gate is opened, and only on evidence gathered again at runtime: the installed version must
    /// be the same or newer, never older, and every member the mod uses has to be there. Any of that
    /// missing and the fix stands down and leaves the mod's own verdict alone.
    ///
    /// What this cannot promise is the wire. Two players still have to agree with each other, and members
    /// existing says nothing about a changed protocol between 1.2.4 and 1.5. The check being local, both
    /// sides on the same library is the normal case and the one this restores; a mixed pair was never
    /// covered by that check either.
    /// </remarks>
    internal sealed class OverTheCounterNetworkLib : Fix
    {
        internal override string Id => "otc-networklib-version";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "2.0.10";
        internal override string GameVersions => "*";
        internal override string What => "multiplayer sync stops being switched off by a version number alone";

        /// <summary>What the mod actually calls, taken off its own metadata.</summary>
        private static readonly (string Type, string[] Members)[] Needed =
        {
            ("SteamNetworkLib.Sync.HostSyncVar`1",
                new[] { "set_Value", "Refresh", "add_OnValueChanged", "add_OnSyncError", "add_OnWriteIgnored" }),
            ("SteamNetworkLib.Sync.ClientSyncVar`1",
                new[] { "set_Value", "Refresh", "GetAllValues", "add_OnValueChanged", "add_OnSyncError" }),
        };

        internal override bool Apply(MelonLogger.Instance log)
        {
            var config = Find("OverTheCounter.SaveData.ConfigSyncData");
            if (config == null) { log.Warning("[fix] otc-networklib-version: ConfigSyncData is not where it was."); return false; }

            var cache = AccessTools.Field(config, "_networkLibAvailable");
            var requiredField = AccessTools.Field(config, "RequiredSteamNetworkLibVersion");
            if (cache == null || requiredField == null)
            { log.Warning("[fix] otc-networklib-version: the version gate is not where it was."); return false; }

            Assembly library = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = null;
                try { name = assembly.GetName().Name; } catch { }
                if (name != null && name.Contains("SteamNetworkLib")) { library = assembly; break; }
            }
            if (library == null) return false;                       // genuinely absent; the mod is right

            var installed = library.GetName().Version;
            var required = requiredField.GetValue(null) as Version;
            if (installed == null || required == null) return false;
            if (installed == required) return false;                 // the gate already opens on its own

            if (installed < required)
            {
                log.Warning($"[fix] otc-networklib-version: SteamNetworkLib {installed} is OLDER than the "
                          + $"{required} the mod was built against. Left switched off.");
                return false;
            }

            string missing = FirstMissing(library);
            if (missing != null)
            {
                log.Warning($"[fix] otc-networklib-version: SteamNetworkLib {installed} has no {missing}, "
                          + "which the mod calls. Left switched off.");
                return false;
            }

            try { cache.SetValue(null, (bool?)true); }
            catch (Exception e)
            { log.Warning("[fix] otc-networklib-version: could not open the gate: " + e.Message); return false; }

            // The mod has already read the gate once and said so during its own start-up, which is earlier
            // than anything here can be. That line is now out of date, and saying so beats leaving a
            // player with two entries that contradict each other.
            log.Msg($"[fix] otc-networklib-version: SteamNetworkLib {installed} carries everything the mod "
                  + $"asks of {required}, so its sync is on again. The mod's own \"multiplayer sync "
                  + "disabled\" line further up was written before this and no longer holds.");
            return true;
        }

        /// <summary>The first thing the mod calls that this build of the library does not have.</summary>
        private static string FirstMissing(Assembly library)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static;

            foreach (var (typeName, members) in Needed)
            {
                Type type = null;
                try { type = library.GetType(typeName, false); } catch { }
                if (type == null) return typeName;

                foreach (string member in members)
                {
                    bool found = false;
                    try { found = type.GetMethod(member, all) != null; } catch { }
                    if (!found) return typeName + "::" + member;
                }
            }
            return null;
        }

        private static Type Find(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = null;
                try { found = assembly.GetType(fullName, false); } catch { }
                if (found != null) return found;
            }
            return null;
        }
    }
}
