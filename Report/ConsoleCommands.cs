using System;
using HarmonyLib;
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
        /// <summary>What the plugin names its untouched copies. Spelled out again rather than shared:
        /// this mod and the plugin deliberately have no types in common, because the plugin runs where
        /// this one cannot exist.</summary>
        private const string BackupSuffix = ".polyfill-orig";

        private static readonly string[][] Listing =
        {
            new[] { "polyfill",        "what Polyfill found in your mods at startup", "polyfill" },
            new[] { "polyfilllist",    "every mod, with its verdict", "polyfilllist" },
            new[] { "polyfillshow",    "everything one mod asks for that is missing", "polyfillshow hitman" },
            new[] { "polyfillunfixed", "only what cannot be pointed at anything", "polyfillunfixed hitman" },
            new[] { "polyfillexport",  "write one file with everything, ready to send", "polyfillexport" },
            new[] { "polyfillprobe",   "can the runtime resolve this type by name?", "polyfillprobe Il2CppScheduleOne.Weather.WeatherConditions" },
            new[] { "polyfillprefab",  "does the game still have this prefab, and what is near it", "polyfillprefab Basic Metal Glass Door" },
            new[] { "polyfillfixes",   "the per-mod fixes, and switch one off", "polyfillfixes off s1mapi-prefabs" },
            new[] { "polyfillrestore", "undo every repair, restart to take effect", "polyfillrestore" },
            new[] { "polyfillhelp",    "list the polyfill commands", "polyfillhelp" },
        };

        internal static void DeclareForTools()
        {
#if HASH_API
            foreach (string[] one in Listing) Hash.Api.HashCommands.Add(one[0], one[1], one[2]);
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
            if (command != "polyfill" && command != "polyfilllist" && command != "polyfillshow"
                && command != "polyfillunfixed" && command != "polyfillhelp" && command != "polyfillprobe"
                && command != "polyfillrestore" && command != "polyfillexport" && command != "polyfillprefab"
                && command != "polyfillfixes")
                return false;                                   // not ours - let the game have it

            string signature = string.Join(" ", parts);
            int frame = Time.frameCount;
            if (frame == _lastFrame && signature == _lastSignature) return true;   // the other overload
            _lastFrame = frame; _lastSignature = signature;

            try
            {
                ReportReader.Load();                            // always fresh: the file is the truth
                string argument = parts.Length > 1 ? string.Join(" ", parts, 1, parts.Length - 1) : null;
                switch (command)
                {
                    case "polyfill": Summary(); break;
                    case "polyfilllist": List(); break;
                    case "polyfillshow": Show(argument, onlyUnfixable: false); break;
                    case "polyfillunfixed": Show(argument, onlyUnfixable: true); break;
                    case "polyfillexport": Export(); break;
                    case "polyfillprobe": Probe(argument); break;
                    case "polyfillprefab": PrefabLookup.Explain(argument); break;
                    case "polyfillfixes": ListFixes(argument); break;
                    case "polyfillrestore": Restore(); break;
                    case "polyfillhelp": Help(); break;
                }
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
                           + $"{mod.HarmonyChecked} Harmony targets checked)");

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

            var method = type.GetMethod(memberName, System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static);
            if (method == null)
            { Core.Log.Warning($"{typeName}::{memberName} does NOT exist at runtime."); return; }

            Core.Log.Msg($"{typeName}::{memberName} exists -> returns {method.ReturnType.Name}, "
                       + $"{method.GetParameters().Length} parameter(s), declared on {method.DeclaringType.Name}");

            if (method.GetParameters().Length != 0 || method.IsStatic)
            { Core.Log.Msg("  not called: the probe only invokes zero-argument instance members."); return; }

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
                              + $"{mod.HarmonyChecked} Harmony target{(mod.HarmonyChecked == 1 ? "" : "s")} checked");

                Section(text, mod, "MATCHED - Polyfill points these at something", wantFixable: true);
                Section(text, mod, "MISSING - nothing to point at", wantFixable: false);
            }

            string path = Path.Combine(Path.GetDirectoryName(ReportReader.Path), "polyfill-report.txt");
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

        private static void Section(System.Text.StringBuilder text, ModLine mod, string title, bool wantFixable)
        {
            var lines = new List<FindingLine>();
            foreach (var finding in mod.Findings)
                if (finding.Fixable == wantFixable) lines.Add(finding);
            if (lines.Count == 0) return;

            text.AppendLine();
            text.AppendLine("  " + title);
            foreach (var finding in lines)
            {
                text.AppendLine($"    {finding.Kind}  {finding.Symbol}");
                if (wantFixable) text.AppendLine($"        -> {finding.Hint}");
                else text.AppendLine($"        {finding.Reason}");
                if (!string.IsNullOrEmpty(finding.Site)) text.AppendLine($"        at {finding.Site}");
            }
        }

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
                backups = Directory.GetFiles(directory, "*" + BackupSuffix).Length;

            if (backups == 0)
            { Core.Log.Msg("nothing to restore - no assembly has been changed."); return; }

            try
            {
                string marker = Path.Combine(
                    MelonLoader.Utils.MelonEnvironment.UserDataDirectory ?? ".", "Polyfill", "restore-pending");
                Directory.CreateDirectory(Path.GetDirectoryName(marker));
                File.WriteAllText(marker, "requested from the console\n");
            }
            catch (Exception e) { Core.Log.Error("could not request the restore: " + e.Message); return; }

            Core.Log.Msg($"{backups} assembly/assemblies will be restored on the next launch - they are in "
                       + "use right now. Switch Polyfill off in MelonPreferences too, or the launch after "
                       + "that repairs them again.");
        }

        private static void Help()
        {
            foreach (string[] one in Listing) Core.Log.Msg($"{one[0],-16} {one[1]}");
        }

        private static void Missing()
        {
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
