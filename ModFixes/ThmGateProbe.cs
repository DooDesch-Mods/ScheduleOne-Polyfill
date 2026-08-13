#if DEBUG
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Say which of T.H.M's five conditions is the one that is false.
    /// </summary>
    /// <remarks>
    /// Using a weapon has to pass five checks in a row, and every one of them returns from Update without a
    /// word:
    /// <code>
    /// IsCableEquipped()                     the item is in your hand
    /// Player.Local.Crouched                 you are crouched
    /// GameInput.GetButtonDown(Interact)     you pressed the key
    /// FindAimedNpc() != null                an NPC is in front of you
    /// Dot(npc.forward, toYou) &lt;= 0.2      you are behind them
    /// </code>
    /// From outside, all five failures look identical: nothing happens. That is what makes this worth a
    /// probe rather than another guess - two guesses have already been wrong here, and each cost a restart
    /// and a round of "still nothing".
    ///
    /// It calls T.H.M's OWN methods rather than reimplementing them, so what it reports is what the mod
    /// sees, including the parts that swallow their exceptions. The key press is the one thing it cannot
    /// observe, and it is also the one thing already proven, so the line says so instead of guessing.
    ///
    /// Armed for a stretch rather than answered on the spot: the state that matters only exists while you
    /// are crouched behind somebody, which is not a position you can hold while typing.
    /// </remarks>
    internal static class ThmGateProbe
    {
        private const int Seconds = 30;

        private static bool _running;
        private static MethodInfo _cableEquipped, _syringeEquipped, _findAimed;
        private static PropertyInfo _miniGameActive;
        private static string _last = "";

        internal static void Arm()
        {
            if (!Bind()) return;
            if (_running) { Report.Core.Log.Msg("[thm] already watching."); return; }

            _running = true;
            Report.Core.Log.Msg($"[thm] watching for {Seconds}s. Equip a weapon, crouch, walk behind an NPC. "
                              + "A line appears whenever the answer changes.");
            MelonLoader.MelonCoroutines.Start(Watch());
        }

        private static bool Bind()
        {
            if (_findAimed != null) return true;

            Assembly thm = null;
            foreach (var one in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(one.GetName()?.Name, "Kowyx_THM", StringComparison.OrdinalIgnoreCase))
                { thm = one; break; }
            if (thm == null) { Report.Core.Log.Warning("[thm] T.H.M is not loaded."); return false; }

            _cableEquipped = AccessTools.Method(thm.GetType("HitmanMod.StrangleHandler", false), "IsCableEquipped");
            _syringeEquipped = AccessTools.Method(thm.GetType("HitmanMod.PoisonHandler", false), "IsSyringeEquipped");
            _findAimed = AccessTools.Method(thm.GetType("HitmanMod.StrangleHandler", false), "FindAimedNpc");
            _miniGameActive = AccessTools.Property(thm.GetType("HitmanMod.StrangleMiniGame", false), "IsActive");

            if (_findAimed == null) { Report.Core.Log.Warning("[thm] FindAimedNpc is not where it was."); return false; }
            return true;
        }

        private static IEnumerator Watch()
        {
            for (int second = 0; second < Seconds; second++)
            {
                yield return new WaitForSecondsRealtime(1f);

                string line = State();
                if (line != _last) { _last = line; Report.Core.Log.Msg("[thm] " + line); }
            }
            _running = false;
            Report.Core.Log.Msg("[thm] done watching.");
        }

        internal static string State()
        {
            if (!Bind()) return "T.H.M is not loaded.";

            var text = new System.Text.StringBuilder();
            text.Append("cable ").Append(Ask(_cableEquipped))
                .Append("  syringe ").Append(Ask(_syringeEquipped));

            // The mini-game is the first thing StrangleHandler.Update asks about, and it returns on the spot
            // when one is running. An invisible one that never ends therefore looks exactly like a weapon
            // that does nothing.
            text.Append("  minigame ").Append(MiniGame());

            Player player = null;
            try { player = Player.Local; } catch { }
            text.Append("  crouched ").Append(player == null ? "?" : player.Crouched ? "yes" : "NO");

            NPC npc = null;
            try { npc = _findAimed.Invoke(null, null) as NPC; }
            catch (Exception e) { text.Append("  aimed THREW ").Append((e.InnerException ?? e).GetType().Name); }

            if (npc == null) { text.Append("  aimed NOBODY"); return text.ToString(); }

            text.Append("  aimed ").Append(Name(npc));
            try
            {
                var toPlayer = (player.transform.position - npc.transform.position).normalized;
                float dot = Vector3.Dot(npc.transform.forward, toPlayer);
                text.Append("  facing-you ").Append(dot.ToString("0.00"))
                    .Append(dot <= 0.2f ? " (behind them, ok)" : " (THEY SEE YOU, must be <= 0.20)");
            }
            catch (Exception e) { text.Append("  angle failed: ").Append(e.Message); }

            return text.ToString();
        }

        internal static string MiniGame()
        {
            if (_miniGameActive == null) return "?";
            try { return (bool)_miniGameActive.GetValue(null) ? "RUNNING" : "no"; }
            catch (Exception e) { return "THREW " + (e.InnerException ?? e).GetType().Name; }
        }

        private static string Ask(MethodInfo method)
        {
            if (method == null) return "?";
            try { return (bool)method.Invoke(null, null) ? "yes" : "no"; }
            catch (Exception e) { return "THREW " + (e.InnerException ?? e).GetType().Name; }
        }

        private static string Name(NPC npc)
        {
            try { return npc.FullName ?? npc.name; } catch { return "?"; }
        }
    }
}
#endif
