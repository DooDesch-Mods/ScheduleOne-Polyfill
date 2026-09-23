using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A mod's patch on the 0.4.6 player load runs when 0.4.7 loads the player, with the same arguments.
    /// </summary>
    /// <remarks>
    /// 0.4.6 had two ways a saved player came back, by role (PlayerManager.cs:108-140, Player.cs:2698 on
    /// 0.4.6f13):
    /// <code>
    /// host's own player   PlayerManager.LoadPlayer -> Player.Load(data, containerPath)
    /// a joining player    Player (server RPC)      -> PlayerManager.TryGetPlayerData(code, out data, out inventory, ...)
    /// </code>
    /// 0.4.7 sends both through one (PlayerManager.cs:152-240, Player.cs:2768-2811 on 0.4.7f6):
    /// <code>
    /// PlayerManager.TryGetPlayerData(code, isHost, out FullPlayerData) -> Player.SetPlayerData_Client(fullData)
    /// </code>
    /// The plugin puts both old methods back as stand-ins, so patch classes register - which alone is what
    /// makes OG Backpack save again, because its WriteData hook shares a class with its Load hook. This is
    /// the other half: those stand-ins are never called, so the patches on them are called from here, by
    /// role, exactly where the old method would have run.
    ///
    /// The moment is the server's SetPlayerData_Client, once per player it loads from the save, and the
    /// role is whether the server owns that player:
    /// - a joining player: the TryGetPlayerData postfixes, with the bundle unpacked into the old values and
    ///   anything they write back through ref put back into the bundle before it is sent. OG Backpack
    ///   appends the backpack to the inventory string there, and its LoadInventory prefix takes it off
    ///   again on the other side.
    /// - the host's own player: the Player.Load prefixes, with the container path the save was read from,
    ///   before the data is applied, and its postfixes after. 0.4.6 never ran the TryGetPlayerData patches
    ///   for the host, so neither does this - running both would load a backpack twice.
    ///
    /// The patches are CALLED, not moved, for the reason PatchesOnSplitMethods gives: Harmony will not bind
    /// a parameter the target does not have, and neither real method has these. A patch that asks for a
    /// value nothing here can fill is left alone and named.
    /// </remarks>
    internal sealed class PlayerLoadRelay : Fix
    {
        internal override string Id => "player-load-relay";
        internal override string Mod => "*";
        internal override string ModVersions => "*";
        internal override string GameVersions => ">=0.4.7";

        internal override string What
            => "a mod that loads its own player data when a save loads (backpacks) gets to run again";

        internal override string StandsDownBecause
            => "a mod that loads its own player data (a backpack) on Player.Load or TryGetPlayerData will not "
             + "see the save load, so what it keeps is not restored.";

        private const string ManagerType = "Il2CppScheduleOne.PlayerScripts.PlayerManager";
        private const string PlayerType = "Il2CppScheduleOne.PlayerScripts.Player";

        private static MelonLogger.Instance _log;
        private static readonly List<MethodInfo> JoiningPostfixes = new();
        private static readonly List<MethodInfo> HostBefore = new();
        private static readonly List<MethodInfo> HostAfter = new();

        /// <summary>The host's load that has started and not finished: the player, its data, its folder.</summary>
        private static object _hostPlayer, _hostData;
        private static string _hostPath;

        private static readonly HashSet<string> JoiningNames = new(StringComparer.Ordinal)
            { "__instance", "__result", "playerCode", "data", "inventoryString", "appearanceString",
              "clothingString", "variables" };
        private static readonly HashSet<string> HostNames = new(StringComparer.Ordinal)
            { "__instance", "data", "containerPath" };

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            var manager = AccessTools.TypeByName(ManagerType);
            var player = AccessTools.TypeByName(PlayerType);
            if (manager == null || player == null)
            { log.Warning($"[fix] {Id}: PlayerManager or Player is not on this build."); return false; }

            // The server's send, not the lookup: the lookup hands its answer back through an out parameter,
            // which a postfix typed against the 0.4.6 interop cannot declare, and HarmonyX does not copy an
            // out value into __args. The send takes the same bundle as an ordinary argument, runs once per
            // player the server loads, and knows which player it is for.
            var send = Declared(player, "SetPlayerData_Client", m => m.Length == 2);
            var applied = Declared(player, null, m => m.Length == 2, "RpcLogic___SetPlayerData_Client");
            if (send == null || applied == null)
            { log.Warning($"[fix] {Id}: the 0.4.7 load path (SetPlayerData_Client) is not here."); return false; }
            _managerType = manager;

            var oldLookup = Declared(manager, "TryGetPlayerData", m => m.Length == 6);
            var oldLoad = Declared(player, "Load", m => m.Length == 2 && m[1].ParameterType == typeof(string));

            Collect(oldLookup, prefixes: null, postfixes: JoiningPostfixes, JoiningNames, "TryGetPlayerData");
            Collect(oldLoad, prefixes: HostBefore, postfixes: HostAfter, HostNames, "Player.Load");

            int total = JoiningPostfixes.Count + HostBefore.Count + HostAfter.Count;
            if (total == 0)
            {
                log.Msg($"[fix] {Id}: no mod patches the 0.4.6 player load; nothing to relay.");
                return false;
            }

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.playerload");
            harmony.Patch(send, prefix: new HarmonyMethod(typeof(PlayerLoadRelay), nameof(BeforeSend)));
            if (HostAfter.Count > 0)
                harmony.Patch(applied, postfix: new HarmonyMethod(typeof(PlayerLoadRelay), nameof(AfterApplied)));

            log.Msg($"[fix] {Id}: {JoiningPostfixes.Count} joining-player patch(es), {HostBefore.Count + HostAfter.Count} "
                  + "host-load patch(es) now run where 0.4.7 loads the player.");
            return true;
        }

        /// <summary>The one method declared on the type that fits, by name or name prefix.</summary>
        private static MethodInfo Declared(Type type, string name, Func<ParameterInfo[], bool> fits, string prefix = null)
        {
            MethodInfo found = null;
            foreach (var method in type.GetMethods(AccessTools.all))
            {
                if (method.DeclaringType != type) continue;
                if (name != null && method.Name != name) continue;
                if (prefix != null && !method.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!fits(method.GetParameters())) continue;
                if (found != null) return null;
                found = method;
            }
            return found;
        }

        private static void Collect(MethodInfo standIn, List<MethodInfo> prefixes, List<MethodInfo> postfixes,
                                    HashSet<string> names, string label)
        {
            if (standIn == null)
            {
                _log.Msg($"[fix] player-load-relay: the old {label} was not put back, so nothing patches it.");
                return;
            }
            HarmonyLib.Patches info;
            try { info = HarmonyLib.Harmony.GetPatchInfo(standIn); }
            catch (Exception e) { _log.Warning($"[fix] player-load-relay: {label}: " + e.Message); return; }
            if (info == null)
            {
                _log.Msg($"[fix] player-load-relay: nothing patches the old {label}.");
                return;
            }

            void Take(IEnumerable<HarmonyLib.Patch> patches, List<MethodInfo> into, string kind)
            {
                foreach (var patch in patches)
                {
                    if (patch.owner != null && patch.owner.StartsWith("doodesch.polyfill", StringComparison.Ordinal)) continue;
                    if (into == null)
                    {
                        _log.Warning($"[fix] player-load-relay: {patch.owner}'s {kind} on {label} is not relayed - "
                                   + "0.4.7 decides nothing before the lookup that a prefix could change.");
                        continue;
                    }
                    var missing = patch.PatchMethod.GetParameters().FirstOrDefault(p => !names.Contains(p.Name));
                    if (!patch.PatchMethod.IsStatic || missing != null)
                    {
                        _log.Warning($"[fix] player-load-relay: {patch.owner}'s {kind} on {label} takes "
                                   + $"'{missing?.Name}', which the 0.4.7 load cannot fill. Left alone.");
                        continue;
                    }
                    into.Add(patch.PatchMethod);
                    _log.Msg($"[fix] player-load-relay: {patch.owner} {kind} on {label} -> the 0.4.7 load");
                }
            }

            Take(info.Prefixes, prefixes, "prefix");
            Take(info.Postfixes, postfixes, "postfix");
        }

        private static Type _managerType;

        /// <summary>
        /// The server is about to send a player their data: the joining player's postfixes, or the start of
        /// the host's own load.
        /// </summary>
        private static void BeforeSend(object __instance, object fullData)
        {
            if (fullData == null)
            {
                _log?.Msg("[fix] player-load-relay: a player with no saved data is being sent nothing; nothing to relay.");
                return;
            }

            try
            {
                object data = Read(fullData, "BasicData");
                string code = Read(data, "PlayerCode") as string;
                var manager = _managerType?.GetProperty("Instance", AccessTools.all | BindingFlags.FlattenHierarchy)?.GetValue(null);
                string path = manager == null || data == null ? null : ContainerPath(manager, data);
                if (path == null)
                {
                    // OnSpawnServer sends every newcomer the state of the players already here, built on the
                    // spot rather than read from the save. Neither old method ran for that.
                    _log?.Msg($"[fix] player-load-relay: data for {code ?? "a player"} is not from the save "
                            + "(a player already in the game, sent to a newcomer); nothing to relay.");
                    return;
                }

                bool host = Read(__instance, "IsOwner") is true;
                _log?.Msg($"[fix] player-load-relay: {code} is loading from {System.IO.Path.GetFileName(path)} "
                        + $"({(host ? "the host's own player" : "a joining player")}) - relaying.");

                if (!host)
                {
                    foreach (var patch in JoiningPostfixes) Joining(patch, manager, code, fullData, data, true);
                    return;
                }

                _hostPlayer = __instance;
                _hostData = data;
                _hostPath = path;
                foreach (var patch in HostBefore) Host(patch);
            }
            catch (Exception e) { _log?.Warning("[fix] player-load-relay: " + e.Message); }
        }

        /// <summary>After the data was applied to the host's player: the Player.Load postfixes.</summary>
        private static void AfterApplied(object __instance)
        {
            if (_hostPlayer == null || !Same(__instance, _hostPlayer))
            {
                _log?.Msg("[fix] player-load-relay: data applied to a player other than the host's own load; "
                        + "no Player.Load postfix to run.");
                return;
            }
            try { foreach (var patch in HostAfter) Host(patch); }
            finally { _hostPlayer = null; _hostData = null; _hostPath = null; }
        }

        private static void Joining(MethodInfo patch, object manager, string code, object bundle, object data, bool result)
        {
            var wanted = patch.GetParameters();
            var arguments = new object[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
                arguments[i] = wanted[i].Name switch
                {
                    "__instance" => manager,
                    "__result" => result,
                    "playerCode" => code,
                    "data" => data,
                    "inventoryString" => Read(bundle, "InventoryString") ?? "",
                    "clothingString" => Read(bundle, "ClothingString") ?? "",
                    "appearanceString" => "",
                    "variables" => Read(bundle, "Variables"),
                    _ => null,
                };

            if (!Call(patch, arguments)) return;

            // What a postfix hands back through ref goes where 0.4.7 reads it.
            for (int i = 0; i < wanted.Length; i++)
            {
                if (!wanted[i].ParameterType.IsByRef) continue;
                if (wanted[i].Name == "inventoryString") Write(bundle, "InventoryString", arguments[i]);
                else if (wanted[i].Name == "clothingString") Write(bundle, "ClothingString", arguments[i]);
            }
        }

        private static void Host(MethodInfo patch)
        {
            var wanted = patch.GetParameters();
            var arguments = new object[wanted.Length];
            for (int i = 0; i < wanted.Length; i++)
                arguments[i] = wanted[i].Name switch
                {
                    "__instance" => _hostPlayer,
                    "data" => _hostData,
                    "containerPath" => _hostPath,
                    _ => null,
                };
            Call(patch, arguments);
        }

        private static bool Call(MethodInfo patch, object[] arguments)
        {
            try { patch.Invoke(null, arguments); return true; }
            catch (Exception e)
            {
                _log?.Warning($"[fix] player-load-relay: {patch.DeclaringType?.Name}.{patch.Name} threw: "
                            + (e.InnerException ?? e).Message);
                return false;
            }
        }

        /// <summary>The folder 0.4.7 read this player's save from, as the old Load was given it.</summary>
        private static string ContainerPath(object manager, object data)
        {
            var datas = Read(manager, "loadedPlayerData");
            var paths = Read(manager, "loadedPlayerDataPaths");
            if (datas == null || paths == null) return null;

            int count = (int)datas.GetType().GetProperty("Count").GetValue(datas);
            var item = datas.GetType().GetProperty("Item");
            var path = paths.GetType().GetProperty("Item");
            for (int i = 0; i < count; i++)
                if (Same(item.GetValue(datas, new object[] { i }), data))
                    return (string)path.GetValue(paths, new object[] { i });
            return null;
        }

        /// <summary>Two interop wrappers are the same object when they wrap the same native pointer.</summary>
        private static bool Same(object a, object b)
        {
            if (a == null || b == null) return false;
            if (ReferenceEquals(a, b)) return true;
            var pa = a.GetType().GetProperty("Pointer")?.GetValue(a);
            var pb = b.GetType().GetProperty("Pointer")?.GetValue(b);
            return pa != null && Equals(pa, pb);
        }

        private static object Read(object target, string member)
            => target?.GetType().GetProperty(member, AccessTools.all)?.GetValue(target);

        private static void Write(object target, string member, object value)
            => target?.GetType().GetProperty(member, AccessTools.all)?.SetValue(target, value);
    }
}
