using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// The management clipboard works again, minus the one screen the game deleted.
    /// </summary>
    /// <remarks>
    /// OverTheCounter prefixes <c>ManagementClipboard_Equippable.Update</c> and, past its button check,
    /// reads <c>ManagementInterface.NPCSelector</c>. 0.4.6 removed that screen outright and did not replace
    /// it - the game left its own stub behind, <c>Debug.LogError("NPCSelector not implemented")</c> at
    /// `ScheduleOne.UI.Management/NPCFieldUI.cs:79` - so there is nothing to point the name at and Polyfill
    /// reports it unrepairable, correctly.
    ///
    /// THE DAMAGE IS FAR WIDER THAN THE MISSING FEATURE, and that is the part worth repairing. A
    /// MissingMethodException is thrown when the method is COMPILED, not when the line runs, so the prefix
    /// dies on its very first call - before the button check, before anything. A prefix that throws takes
    /// the original method with it, so the vanilla clipboard never updates either:
    /// <code>
    /// MissingMethodException: 'Il2CppScheduleOne.UI.Management.NPCSelector
    ///                          Il2CppScheduleOne.Management.ManagementInterface.get_NPCSelector()'
    ///   at OverTheCounter.Patches.ManagerClipboardPatch.UpdatePrefix
    ///   at DMD&lt;ManagementClipboard_Equippable::Update&gt;
    /// </code>
    /// Reported as "you cannot use the clipboard on employees", which is the whole clipboard rather than
    /// one screen of it.
    ///
    /// So the patch is taken off. What comes back is the game's own clipboard, which opens and assigns and
    /// works. What does not come back is OverTheCounter's manager selection, because the screen it needs is
    /// gone from the game - said once, plainly, rather than left for somebody to discover.
    /// </remarks>
    internal sealed class OverTheCounterClipboard : Fix
    {
        internal override string Id => "otc-clipboard";
        internal override string Mod => "OverTheCounter";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.6";
        internal override string What => "the management clipboard stops being dead in your hands";

        internal override string StandsDownBecause
            => "The management clipboard will not react at all while OverTheCounter is installed.";


        internal override bool Apply(MelonLogger.Instance log)
        {
            Type patch = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { patch = assembly.GetType("OverTheCounter.Patches.ManagerClipboardPatch", false); }
                catch { }
                if (patch != null) break;
            }
            if (patch == null) { log.Warning("[fix] otc-clipboard: ManagerClipboardPatch is not where it was."); return false; }

            var prefix = AccessTools.Method(patch, "UpdatePrefix");
            if (prefix == null) { log.Warning("[fix] otc-clipboard: UpdatePrefix is gone."); return false; }

            var update = AccessTools.Method(typeof(Il2CppScheduleOne.Tools.ManagementClipboard_Equippable),
                                            "Update");
            if (update == null)
            { log.Warning("[fix] otc-clipboard: the clipboard's Update is not where it was."); return false; }

            try
            {
                new HarmonyLib.Harmony("doodesch.polyfill.fixes").Unpatch(update, prefix);
            }
            catch (Exception e)
            { log.Warning("[fix] otc-clipboard: could not take the patch off: " + e.Message); return false; }

            log.Warning("[fix] otc-clipboard: OverTheCounter's clipboard patch asks for the NPC selector "
                      + "screen, which 0.4.6 removed and did not replace. The patch cannot run at all, so it "
                      + "has been taken off: the clipboard works as the game's own again, and "
                      + "OverTheCounter's manager selection stays gone.");
            return true;
        }

        // Taken off rather than wrapped, and the order those were tried in is worth keeping.
        //
        // A transpiler on the prefix: refused, "IL Compile Error". A finalizer on the prefix: refused the
        // same way. Both need Harmony to build a wrapper around a method whose body mentions a type that
        // cannot load, and no amount of wrapping fixes that - the body is the problem.
        //
        // A finalizer on the game's own Update would have caught the exception, and that is still not the
        // repair: once a prefix throws, Harmony skips the original for that frame. Swallowing it would buy
        // a quiet log and a clipboard that still does nothing.
        //
        // Removing the prefix is the only thing that gives the clipboard back, and it costs nothing that
        // still works: the patch cannot execute a single instruction on this build.
    }
}
