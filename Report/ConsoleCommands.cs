using System;
using HarmonyLib;
using Polyfill.Contract;
using UnityEngine;

namespace Polyfill.Report
{
    /// <summary>
    /// The console surface: what Polyfill found, per mod, in the game rather than in a log file.
    /// </summary>
    /// <remarks>
    /// A console command and not a hotkey, per the workspace rule - it takes arguments, it is listable, and
    /// it is the only form an agent can drive to verify itself.
    ///
    /// Both Console.SubmitCommand overloads are patched. The string body calls the list body, so either
    /// prefix may be the one that fires depending on the caller, and catching both is the reliable path.
    /// Side effects are therefore deduplicated per frame and per command text.
    /// </remarks>
    internal static class ConsoleCommands
    {
        /// <summary>
        /// What runs for each word. The keys must be exactly the table's names.
        /// </summary>
        /// <remarks>
        /// A dictionary rather than a switch so that "every command in the table has a handler, and no
        /// handler exists for a word nobody can type" is a thing a test can assert. The table itself is in
        /// Contract, next to the version arithmetic, because the plugin has to know which words are ours
        /// too - and because it used to be written out three times, of which the middle one decided whether
        /// the game or Polyfill got the word.
        /// </remarks>
        private static readonly Dictionary<string, Action<string>> Handlers = new(StringComparer.Ordinal)
        {
            ["polyfill"] = _ => Summary(),
            ["polyfilllist"] = _ => List(),
            ["polyfillshow"] = argument => Show(argument, onlyUnfixable: false),
            ["polyfillunfixed"] = argument => Show(argument, onlyUnfixable: true),
            ["polyfillexport"] = _ => Export(),
            ["polyfillprobe"] = Probe,
            ["polyfillprefab"] = PrefabLookup.Explain,
            ["polyfillfixes"] = ListFixes,
            ["polyfillrestore"] = _ => Restore(),
            ["polyfillregen"] = _ => Regenerate(),
            ["polyfillhelp"] = _ => Help(),
        };

        internal static void DeclareForTools()
        {
#if HASH_API
            foreach (var one in CommandTable.All) Hash.Api.HashCommands.Add(one.Name, one.Help, one.Example);
#endif
        }

        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return Dispatch(raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        internal static bool TryHandle(Il2CppSystem.Collections.Generic.List<string> args)
        {
            if (args == null || args.Count == 0) return false;
            var parts = new string[args.Count];
            for (int i = 0; i < args.Count; i++) parts[i] = args[i];
            return Dispatch(parts);
        }

        private static int _lastFrame = -1;
        private static string _lastSignature = "";

        private static bool Dispatch(string[] parts)
        {
            if (parts.Length == 0) return false;
            string command = parts[0].ToLowerInvariant();
            if (!CommandTable.Owns(command)) return false;      // not ours - let the game have it

            string signature = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && signature == _lastSignature) return true;   // the other overload
            _lastFrame = frame; _lastSignature = signature;

            try
            {
                ReportReader.Load();                            // always fresh: the file is the truth
                string argument = parts.Length > 1 ? string.Join(" ", parts, 1, parts.Length - 1) : null;
                if (Handlers.TryGetValue(command, out var run)) run(argument);
            }
            catch (Exception e) { Core.Log.Error(e.ToString()); }
            return true;
        }

        private static void Summary()
        {
            var mods = ReportReader.Mods;
            if (mods.Count == 0) { Missing(); return; }

            int clean = 0, adaptable = 0, blocked = 0, findings = 0;
            foreach (var mod in mods)
            {
                findings += mod.Findings.Count;
                switch (mod.Verdict)
                {
                    case "clean": clean++; break;
                    case "adaptable": adaptable++; break;
                    default: blocked++; break;
                }
            }

            Core.Log.Msg($"Schedule I {ReportReader.GameVersion} - {mods.Count} mod(s): "
                       + $"{clean} need nothing, {adaptable} could be adapted, {blocked} ask for something gone.");
            if (findings == 0)
            {
                Core.Log.Msg("Nothing is missing. Every mod you have fits this build of the game.");
                return;
            }
            Core.Log.Msg($"{findings} missing reference(s) in total. `polyfilllist` names the mods.");
        }

        private static void List()
        {
            var mods = ReportReader.Mods;
            if (mods.Count == 0) { Missing(); return; }

            foreach (var mod in mods)
            {
                int fixable = 0;
                foreach (var finding in mod.Findings) if (finding.Fixable) fixable++;
                string tail = mod.Findings.Count == 0
                    ? "-"
                    : $"{mod.Findings.Count} missing, {fixable} with a candidate";
                Core.Log.Msg($"{mod.Verdict,-9} {mod.Display} {mod.Version}  {tail}");
            }
            Core.Log.Msg("`polyfillshow <mod>` for the detail. Full file: " + ReportReader.Path);
        }

        private static void Show(string term, bool onlyUnfixable)
        {
            if (string.IsNullOrWhiteSpace(term))
            { Core.Log.Warning("name a mod, e.g. `polyfillshow sideload`."); return; }

            var hits = ReportReader.Find(term);
            if (hits.Count == 0) { Core.Log.Warning($"no mod matches '{term}'."); return; }
            if (hits.Count > 4)
            { Core.Log.Warning($"'{term}' matches {hits.Count} mods; be more specific."); return; }

            foreach (var mod in hits)
            {
                Core.Log.Msg($"{mod.Display} {mod.Version} - {mod.Verdict} "
                           + $"({mod.TypeRefs} type refs, {mod.MemberRefs} member refs, "
                           + $"{mod.HarmonyTargetsChecked} Harmony targets checked)");

                int shown = 0, skipped = 0;
                foreach (var finding in mod.Findings)
                {
                    if (onlyUnfixable && finding.Fixable) continue;
                    // A console window is about forty lines; the file has all of it.
                    if (shown >= 20) { skipped++; continue; }
                    shown++;

                    Core.Log.Msg($"  {finding.Kind}  {finding.Symbol}");
                    Core.Log.Msg($"      {finding.Reason}");
                    if (finding.Fixable) Core.Log.Msg($"      -> {finding.Hint}");
                    if (!string.IsNullOrEmpty(finding.Site)) Core.Log.Msg($"      at {finding.Site}");
                }

                if (shown == 0)
                    Core.Log.Msg(onlyUnfixable
                        ? "  nothing unfixable - everything missing has a candidate."
                        : "  nothing missing.");
                if (skipped > 0)
                    Core.Log.Msg($"  ... and {skipped} more - the full list is in {ReportReader.Path}");
            }
        }

        /// <summary>
        /// Ask the RUNTIME, not Cecil, whether a name resolves.
        /// </summary>
        /// <remarks>
        /// The whole middleman design rests on one assumption: that a name put back into an interop assembly
        /// is a name the CLR will find. Cecil answering yes proves nothing - it reads metadata with its own
        /// resolver and follows forwarders by its own rules. Only the runtime's own type loader settles it,
        /// and only from inside the running game.
        /// </remarks>
        /// <summary>
        /// What the per-mod fixes did, and the switch for one of them.
        /// </summary>
        /// <remarks>
        /// These fixes run without being asked, because a player does not know their mod is broken - that
        /// is the whole problem. What they get instead is this: every fix by name, what it was for, and
        /// whether it ran. Switching one off takes effect on the next launch, since by the time anyone can
        /// type the fix has already happened.
        /// </remarks>
        private static void ListFixes(string argument)
        {
            var parts = (argument ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && (parts[0] == "off" || parts[0] == "on"))
            {
                if (!ModFixes.Fixes.Known(parts[1]))
                { Core.Log.Warning($"there is no fix called '{parts[1]}'. Type `polyfillfixes`."); return; }

                ModFixes.Fixes.Set(parts[1], parts[0] == "on");
                Core.Log.Msg($"{parts[1]} is {(parts[0] == "on" ? "on" : "off")} from the next launch.");
                return;
            }

            if (ModFixes.Fixes.Results.Count == 0)
            { Core.Log.Msg("the per-mod fixes have not run yet - load a save first."); return; }

            Core.Log.Msg("per-mod fixes:");
            foreach (var outcome in ModFixes.Fixes.Results)
            {
                Core.Log.Msg($"  {outcome.Fix.Id}  [{outcome.State}]");
                Core.Log.Msg($"    for {outcome.Fix.Mod} {outcome.Fix.ModVersions} on game "
                           + $"{outcome.Fix.GameVersions}: {outcome.Fix.What}");
            }
            Core.Log.Msg("switch one off with `polyfillfixes off <id>`.");
        }

        private static void Probe(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            { Core.Log.Warning("name a type, e.g. `polyfillprobe Il2CppScheduleOne.Weather.WeatherConditions`."); return; }
            name = name.Trim();

            int separator = name.IndexOf("::", StringComparison.Ordinal);
            if (separator > 0)
            {
                ProbeMember(name.Substring(0, separator), name.Substring(separator + 2));
                return;
            }

            Type direct = null;
            try { direct = Type.GetType(name, false); } catch (Exception e) { Core.Log.Msg("  Type.GetType threw: " + e.GetType().Name); }
            Core.Log.Msg($"Type.GetType(\"{name}\") -> {(direct == null ? "null" : direct.AssemblyQualifiedName)}");

            foreach (string assembly in new[] { "Assembly-CSharp", "Il2CppScheduleOne.Core" })
            {
                Type viaAssembly = null;
                string note = "";
                try
                {
                    var loaded = System.Reflection.Assembly.Load(assembly);
                    viaAssembly = loaded?.GetType(name, false);
                }
                catch (Exception e) { note = " (" + e.GetType().Name + ")"; }
                Core.Log.Msg($"  in {assembly}: {(viaAssembly == null ? "not found" + note : "FOUND, really " + viaAssembly.Assembly.GetName().Name)}");
            }
        }

        /// <summary>
        /// Does an injected member exist at runtime, and does calling it return the right thing?
        /// </summary>
        /// <remarks>
        /// Finding it proves the injection reached the loaded assembly. CALLING it proves the body we
        /// emitted actually runs and reaches the game - which is the part that could be subtly wrong, and
        /// the part no amount of metadata inspection can tell you.
        ///
        /// A zero-argument instance member is invoked against a live object of that type when one can be
        /// found in the scene. Anything else is reported as present-but-uncalled, which is honest rather
        /// than a claim the probe did not earn.
        /// </remarks>
        private static void ProbeMember(string typeName, string memberName)
        {
            Type type = null;
            foreach (string assembly in new[] { "Assembly-CSharp", "Il2CppScheduleOne.Core" })
            {
                try { type = System.Reflection.Assembly.Load(assembly)?.GetType(typeName, false); } catch { }
                if (type != null) break;
            }
            if (type == null) { Core.Log.Warning($"type {typeName} not found at runtime."); return; }

            // Collected by hand rather than through GetMethod(name), which THROWS on a name carried by more
            // than one method - and Polyfill makes that happen on purpose: a member the game kept but now
            // hands a renamed type back from is repaired by declaring the old return type beside it. The
            // probe that reports on that repair must not be the first thing it breaks.
            var matches = new List<System.Reflection.MethodInfo>();
            foreach (var candidate in type.GetMethods(System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static))
                if (candidate.Name == memberName) matches.Add(candidate);

            if (matches.Count == 0)
            { Core.Log.Warning($"{typeName}::{memberName} does NOT exist at runtime."); return; }

            Core.Log.Msg(matches.Count == 1
                ? $"{typeName}::{memberName} exists:"
                : $"{typeName}::{memberName} exists {matches.Count} times:");
            foreach (var one in matches)
                Core.Log.Msg($"  returns {one.ReturnType.Name}, {one.GetParameters().Length} parameter(s), "
                           + $"declared on {one.DeclaringType.Name}");

            var method = matches[0];

            if (method.GetParameters().Length != 0)
            { Core.Log.Msg("  not called: the probe only invokes members that take nothing."); return; }

            // A STATIC GETTER IS HALF OF WHAT POLYFILL REPAIRS, so refusing to call one left the more
            // interesting half unverifiable. Singleton<T>.Instance is the case that made this matter: the
            // type resolving proves the managed shape, and only the call proves the native class behind it.
            if (method.IsStatic)
            {
                try
                {
                    object result = method.Invoke(null, null);
                    Core.Log.Msg($"  CALLED as a static -> {(result ?? "null")}");
                }
                catch (Exception e) { Core.Log.Error("  call FAILED: " + (e.InnerException ?? e)); }
                return;
            }

            try
            {
                var found = UnityEngine.Object.FindObjectOfType(Il2CppInterop.Runtime.Il2CppType.From(type));
                if (found == null) { Core.Log.Msg("  not called: no instance of this type is in the scene."); return; }

                // FindObjectOfType hands back a UnityEngine.Object wrapper whatever the native type is, and
                // reflection wants an instance of the declaring type. Every interop wrapper is an IntPtr and
                // a constructor that takes one, so the same native object is re-wrapped as the right type.
                object instance = Activator.CreateInstance(type, found.Pointer);

                object value = method.Invoke(instance, null);
                Core.Log.Msg($"  CALLED on a live instance -> {(value ?? "null")}");
            }
            catch (Exception e)
            {
                Core.Log.Error("  call FAILED: " + (e.InnerException ?? e));
            }
        }

        /// <summary>
        /// Write one file covering every mod, ready to send without editing it first.
        /// </summary>
        /// <remarks>
        /// The alternative was asking people to run `polyfillunfixed` per mod and paste the console
        /// output, which nobody does past the second mod - and the mods worth hearing about are exactly
        /// the ones on a machine with twenty of them.
        ///
        /// It is NOT `last-run.txt` renamed. That file carries the full path of every mod, which on a
        /// player's machine reads `C:\Users\&lt;their real name&gt;\...`. A file written to be sent to a
        /// stranger must not carry that, so only file names go in here. Nothing else identifying is in
        /// the report to begin with.
        ///
        /// Both halves are written per mod: what Polyfill matched, and what it could not. The matches
        /// matter as much as the gaps - a wrong match is the failure mode that looks like success, and
        /// it is only visible by reading what it claimed.
        /// </remarks>
        private static void Export()
        {
            var mods = ReportReader.Mods;
            if (mods.Count == 0) { Missing(); return; }

            var text = new System.Text.StringBuilder();
            int clean = 0, adaptable = 0, blocked = 0;
            foreach (var mod in mods)
                switch (mod.Verdict)
                { case "clean": clean++; break; case "adaptable": adaptable++; break; default: blocked++; break; }

            text.AppendLine("Polyfill report");
            text.AppendLine("===============");
            text.AppendLine();
            text.AppendLine("Generated  " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + " UTC");
            text.AppendLine("Game       Schedule I " + ReportReader.GameVersion);
            text.AppendLine("Polyfill   " + DooDesch.ModVersion.Current);
            text.AppendLine($"Mods       {mods.Count} checked - {clean} need nothing, "
                          + $"{adaptable} could be adapted, {blocked} {(blocked == 1 ? "asks" : "ask")} "
                          + "for something that is gone");
            text.AppendLine();
            text.AppendLine("Send this file to https://support.doodesch.de/polyfill");
            text.AppendLine("It holds no file paths and nothing that identifies you.");

            foreach (var mod in mods)
            {
                if (mod.Verdict == "clean") continue;            // nothing to say about a mod that fits

                text.AppendLine();
                text.AppendLine(new string('-', 78));
                text.AppendLine($"{mod.Display}  {mod.Version}"
                              + (string.IsNullOrEmpty(mod.Author) ? "" : $"  by {mod.Author}")
                              + $"   [{mod.Verdict}]");
                text.AppendLine($"  file {Path.GetFileName(mod.Path)}"
                              + $"   {mod.TypeRefs} type refs, {mod.MemberRefs} member refs, "
                              + $"{mod.HarmonyTargetsChecked} Harmony target{(mod.HarmonyTargetsChecked == 1 ? "" : "s")} checked");

                Section(text, mod, "REPAIRED - Polyfill put these back",
                        f => f.Outcome == Outcome.Applied);

                // The section that did not exist, and the most useful one there is for a mod author:
                // Polyfill had a candidate and did not trust it. That decision reached the log and nothing
                // else, so the one file anybody is asked to send never mentioned it.
                Section(text, mod, "REFUSED - there was a candidate and Polyfill did not trust it",
                        f => f.Outcome == Outcome.Refused);

                Section(text, mod, "MATCHED - a candidate, not yet applied",
                        f => f.Fixable && f.Outcome != Outcome.Applied && f.Outcome != Outcome.Refused);

                Section(text, mod, "MISSING - nothing to point at",
                        f => !f.Fixable && f.Outcome != Outcome.Refused);
            }

            string path = PolyfillPaths.Report(MelonLoader.Utils.MelonEnvironment.UserDataDirectory);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text.ToString());
            }
            catch (Exception e) { Core.Log.Error("could not write the report: " + e.Message); return; }

            Core.Log.Msg($"written to {path}");
            Core.Log.Msg("Send that file to https://support.doodesch.de/polyfill - it covers every mod at "
                       + "once and holds no file paths.");
        }

        private static void Section(System.Text.StringBuilder text, ModReport mod, string title,
                                    Func<Finding, bool> wanted)
        {
            var lines = new List<Finding>();
            foreach (var finding in mod.Findings)
                if (wanted(finding)) lines.Add(finding);
            if (lines.Count == 0) return;

            text.AppendLine();
            text.AppendLine("  " + title);
            foreach (var finding in lines)
            {
                text.AppendLine($"    {finding.Kind}  {finding.Symbol}");

                // What was put back says everything the candidate said, so printing both is the same
                // sentence twice - and for a hand-written rule that sentence is three lines long.
                if (finding.Outcome == Outcome.Applied)
                    text.AppendLine($"        -> {Detail(finding)}");
                else if (finding.Outcome == Outcome.Refused)
                {
                    if (!string.IsNullOrEmpty(finding.Hint)) text.AppendLine($"        candidate  {finding.Hint}");
                    text.AppendLine($"        refused    {finding.OutcomeDetail}");
                }
                else if (!string.IsNullOrEmpty(finding.Hint)) text.AppendLine($"        -> {finding.Hint}");
                else text.AppendLine($"        {finding.Reason}");
                if (!string.IsNullOrEmpty(finding.Site)) text.AppendLine($"        at {finding.Site}");
            }
        }

        private static string Detail(Finding finding)
            => !string.IsNullOrEmpty(finding.OutcomeDetail) ? finding.OutcomeDetail
             : !string.IsNullOrEmpty(finding.Hint) ? finding.Hint
             : finding.Reason;

        /// <summary>
        /// Put the game's generated assemblies back exactly as MelonLoader wrote them.
        /// </summary>
        /// <remarks>
        /// Polyfill changes files in the player's game folder. Whatever the reasoning, that needs an undo
        /// the player can reach without a file manager, and it has to be reachable from inside the game
        /// where they noticed the problem.
        ///
        /// It cannot do it here. The assemblies are loaded, and Windows will not let a mapped file be
        /// replaced - the first version of this tried, swallowed the error and reported "nothing to
        /// restore" while the backup sat right there. So the command leaves a marker and the plugin carries
        /// it out on the next launch, in the same window it does the repairs, where the files are free.
        /// </remarks>
        private static void Restore()
        {
            string directory = ReportReader.InteropDirectory;
            int backups = 0;
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                backups = Directory.GetFiles(directory, "*" + PolyfillPaths.BackupSuffix).Length;

            if (backups == 0)
            { Core.Log.Msg("nothing to restore - no assembly has been changed."); return; }

            try
            {
                string marker = PolyfillPaths.RestorePending(
                    MelonLoader.Utils.MelonEnvironment.UserDataDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(marker));
                File.WriteAllText(marker, "requested from the console\n");
            }
            catch (Exception e) { Core.Log.Error("could not request the restore: " + e.Message); return; }

            Core.Log.Msg($"{backups} assembly/assemblies will be restored on the next launch - they are in "
                       + "use right now. Switch Polyfill off in MelonPreferences too, or the launch after "
                       + "that repairs them again.");
        }

        /// <summary>
        /// Ask MelonLoader to build the game's generated assemblies again.
        /// </summary>
        /// <remarks>
        /// The way out of the two dead ends Polyfill can find itself in and cannot fix on its own: an
        /// assembly it repaired whose untouched copy is gone, and one whose copy no longer matches what it
        /// was built from. In both, Polyfill refuses to touch that assembly for good reasons, and refusing
        /// forever is not a state to leave a player in.
        ///
        /// The generator keeps what it built from in a config file and rebuilds when that no longer matches.
        /// Deleting the file is the supported way to say "build it again" - MelonLoader read it at startup
        /// and does not read it twice, so this is safe while the game runs and takes effect on the next
        /// launch. Nothing of the player's is deleted: the assemblies themselves are a cache.
        /// </remarks>
        private static void Regenerate()
        {
            // Qualified from the root: inside this namespace, `Core` is this mod's own logger holder.
            string config = global::Polyfill.Core.GeneratorIdentity.ConfigPath();
            if (string.IsNullOrEmpty(config) || !File.Exists(config))
            {
                Core.Log.Msg("MelonLoader's generator config is not where it was, so this cannot ask for a "
                           + "rebuild. Deleting MelonLoader/Il2CppAssemblies does the same thing by hand.");
                return;
            }

            try { File.Delete(config); }
            catch (Exception e) { Core.Log.Error("could not ask for a rebuild: " + e.Message); return; }

            Core.Log.Msg("MelonLoader will build the game's generated assemblies again on the next launch, "
                       + "which takes a few minutes. Polyfill starts from those.");
        }

        private static void Help()
        {
            foreach (var one in CommandTable.All) Core.Log.Msg($"{one.Name,-16} {one.Help}");
        }

        private static void Missing()
        {
            // Two different failures used to read the same: nothing was written, and something was written
            // that this build will not read. The second one names both files and is fixed in one step.
            if (!string.IsNullOrEmpty(ReportReader.Problem))
            { Core.Log.Warning(ReportReader.Problem); return; }

            Core.Log.Warning("no report was written this run. Polyfill.Boot.dll belongs in Plugins/, "
                           + "not Mods/ - check the startup log for a line from it.");
        }
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand),
        new Type[] { typeof(string) })]
    internal static class Polyfill_Console_SubmitCommand_String_Patch
    {
        private static bool Prefix(string args) => !ConsoleCommands.TryHandle(args);
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.Console), nameof(Il2CppScheduleOne.Console.SubmitCommand),
        new Type[] { typeof(Il2CppSystem.Collections.Generic.List<string>) })]
    internal static class Polyfill_Console_SubmitCommand_List_Patch
    {
        private static bool Prefix(Il2CppSystem.Collections.Generic.List<string> args)
            => !ConsoleCommands.TryHandle(args);
    }
}
