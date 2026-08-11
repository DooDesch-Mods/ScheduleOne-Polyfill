using MelonLoader;

// "Polyfill.Boot" so the companion mod in Mods/ can be plain "Polyfill" - MelonLoader looks melons up by
// name, so two carrying the same one would shadow each other. Same split as SideHustle / SideHustle.Boot.
[assembly: MelonInfo(typeof(Polyfill.Boot.Plugin), "Polyfill.Boot", DooDesch.ModVersion.Current, "DooDesch",
    "https://github.com/DooDesch-Mods/ScheduleOne-Polyfill")]
[assembly: MelonGame("TVGS", "Schedule I")]

// First in, so the repairs are in place before anything can bind to the game - including a plugin that
// loads mods itself. It does not take the Mods pass away from anyone; see ModScan.
[assembly: MelonPriority(int.MinValue)]

namespace Polyfill.Boot
{
    /// <summary>
    /// Keep mods working across game updates by supplying what an update took away.
    /// </summary>
    /// <remarks>
    /// A mod compiled against an older Schedule I asks for names that no longer exist: a type that moved
    /// into another assembly, a member that was renamed, a FishNet RPC whose hash changed with its
    /// signature. Nothing about the mod is wrong; the game moved.
    ///
    /// Polyfill sits between the two. It never edits anyone's mod - the file on disk stays byte for byte
    /// what its author shipped. Instead it puts the missing names back into MelonLoader's own generated
    /// interop assemblies, pointing at wherever the thing lives today, and it answers reflection lookups
    /// that would otherwise return null.
    ///
    /// This phase does neither yet. It reads every mod and says what it cannot find. Nothing is loaded,
    /// deferred or changed by Polyfill - whoever loads the mods today keeps loading them. That report is
    /// what decides which repairs are worth building.
    ///
    /// The timing is not adjustable. OnPreModsLoaded is the only point where the interop assemblies are
    /// final (the generator has run) and no mod has bound to them yet - plugin construction and
    /// OnPreInitialization are both too early, and on the first launch after a game update the files are
    /// literally being rewritten then.
    /// </remarks>
    public sealed class Plugin : MelonPlugin
    {
        internal static MelonLogger.Instance Log { get; private set; }

        /// <summary>Master switch. Off means MelonLoader's own behaviour, untouched.</summary>
        internal static bool Enabled { get; private set; } = true;

        /// <summary>Report what would be repaired, and repair nothing. The mode to point a doubtful
        /// player at: it answers "what would you do to my game" without doing any of it.</summary>
        internal static bool DryRun { get; private set; }

        public override void OnPreInitialization()
        {
            Log = LoggerInstance;
            ReadPreferences();
            // Nothing that touches Il2CppAssemblies belongs here: on the first launch after a game update
            // the generator has not run yet and the folder is stale or absent.
        }

        public override void OnPreModsLoaded()
        {
            Log = LoggerInstance;
            if (!Enabled)
            {
                LoggerInstance.Msg("switched off in MelonPreferences; your game is exactly as it would be "
                                 + "without Polyfill installed.");
                return;
            }

            try
            {
                Diagnostics.RecordInteropLoadState(LoggerInstance);
                if (RestoreIfAsked()) return;
                ModScan.Run(LoggerInstance);

                // Layer 2. Off under DryRun like everything else: it answers the same questions the same way,
                // but it is still a change to a running game, and "repair nothing" has to mean nothing.
                if (DryRun)
                    LoggerInstance.Msg("[reflect] not installed - DryRun is on.");
                else
                    Dynamic.ReflectionFallback.Install(LoggerInstance, Core.InteropIndex.LocateDirectory());
            }
            catch (Exception e)
            {
                // Nothing here owns anything, so standing down costs the player nothing at all: the Mods
                // pass has not been touched and runs exactly as it would without this plugin installed.
                LoggerInstance.Error("[boot] Polyfill failed and stood down; your mods load normally: " + e);
            }
        }

        /// <summary>
        /// Carry out an undo the player asked for from inside the game, and repair nothing this launch.
        /// </summary>
        /// <remarks>
        /// The in-game command cannot do it itself - the assemblies are loaded and Windows will not let
        /// them be replaced - so it leaves a marker and this picks it up in the one window where the files
        /// are free. Nothing is repaired afterwards on the same launch, or the undo would be undone
        /// immediately and the player would see no change at all.
        /// </remarks>
        private static bool RestoreIfAsked()
        {
            string marker = Core.InteropAugmentor.PendingMarker(
                MelonLoader.Utils.MelonEnvironment.UserDataDirectory);
            if (!File.Exists(marker)) return false;

            try { File.Delete(marker); } catch { }

            string interop = Core.InteropIndex.LocateDirectory();
            int restored = interop == null ? 0 : Core.InteropAugmentor.Restore(interop, Log);
            Log.Msg(restored == 0
                ? "restore was requested but nothing had been changed."
                : $"{restored} assembly/assemblies restored as requested. Nothing was repaired this launch - "
                  + "switch Polyfill off in MelonPreferences if you want it to stay that way.");
            return true;
        }

        /// <summary>
        /// Read the switches. MelonPreferences.Load() runs before plugins, so a saved value is already in
        /// memory and a category created here picks it up.
        /// </summary>
        private static void ReadPreferences()
        {
            try
            {
                var category = MelonPreferences.GetCategory("Polyfill")
                               ?? MelonPreferences.CreateCategory("Polyfill");

                Enabled = (category.GetEntry<bool>("Enabled")
                           ?? category.CreateEntry("Enabled", true, "Repair old mods",
                               "Put names an older Schedule I had back into the game, so mods built against "
                               + "it keep working. Off means nothing is read and nothing is changed.")).Value;

                DryRun = (category.GetEntry<bool>("DryRun")
                          ?? category.CreateEntry("DryRun", false, "Report only, repair nothing",
                              "Work out what is missing and write the report, but leave the game untouched. "
                              + "Type `polyfill` in the console to read it.")).Value;
            }
            catch { Enabled = true; DryRun = false; }
        }
    }
}
