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
    /// Use a T.H.M weapon on an NPC with nobody at the keyboard.
    /// </summary>
    /// <remarks>
    /// A repair for a mod nobody can drive is a repair nobody can check. T.H.M's weapons need a player who
    /// is holding the item, crouched, standing behind somebody and pressing a key, and an agent has a
    /// console and nothing else - so every round of this has ended with "try it and tell me". That is the
    /// loop this closes.
    ///
    /// It sets the SAME STATE A PLAYER WOULD, and then lets the mod's own Update run:
    /// <code>
    /// equip     Player.SetEquippedSlotIndex on the slot holding the item
    /// crouch    the Crouched backing field, which interop exposes even though the setter is private
    /// place     behind the target, facing it, at the distance the mod's raycast reaches
    /// press     GetButtonDown answers true for Interact for exactly one frame
    /// </code>
    /// The press is faked for a whole frame rather than for one call, because that is what pressing a key
    /// actually does - every caller that frame sees it. Anything else would be a different experiment.
    ///
    /// Nothing here is a repair and nothing ships: it is debug-only, it is a console command per the
    /// workspace rule, and it exists so that "the syringe works now" is something that can be shown rather
    /// than hoped.
    /// </remarks>
    internal static class ThmRig
    {
        private const string Syringe = "thm_poison_syringe";
        private const string Cable = "thm_fibre_glass_cable";

        /// <summary>Interact, as this build numbers it. Read rather than assumed, since that number is the
        /// whole reason this mod needed a fix.</summary>
        private static int _interact = -1;

        private static bool _pressed;
        private static bool _hooked;

        internal static void Run(string what)
        {
            string id = what == "poison" ? Syringe : what == "strangle" ? Cable : null;
            if (id == null)
            {
                Report.Core.Log.Msg("say which: `polyfillprefab thm run poison` or `... run strangle`.");
                return;
            }
            MelonLoader.MelonCoroutines.Start(Attempt(id, what));
        }

        /// <summary>
        /// Put an item in the player's hand and watch what happens for a few seconds.
        /// </summary>
        /// <remarks>
        /// Written for the management clipboard, and the reason is a lesson rather than a convenience. Its
        /// repair takes a broken patch off; "the errors stopped" is easy to show and is NOT the claim. The
        /// claim is that the game's own clipboard runs again, and the only way to see that is to hold one
        /// and watch its Update tick without throwing.
        ///
        /// What this still cannot answer is whether the screen it opens behaves - that needs a mouse, and
        /// a mouse is the one thing an agent does not have.
        /// </remarks>
        internal static void Equip(string id)
        {
            // A number is taken as the slot index, because not everything worth equipping is in the hotbar:
            // the management clipboard has a slot of its own, index 8 in the game's own numbering
            // (`PlayerInventory.IndexAllSlots`), and no item id will find it.
            if (!int.TryParse(id, out int slot)) slot = SlotHolding(id);
            if (slot < 0)
            {
                Report.Core.Log.Warning($"[rig] '{id}' is not in the hotbar. A number equips that slot "
                                      + "directly - 8 is the clipboard.");
                return;
            }
            try
            {
                Player.Local.SetEquippedSlotIndex(slot);
                Report.Core.Log.Msg($"[rig] '{id}' equipped from slot {slot}. Anything it throws lands in "
                                  + "the log from here on.");
            }
            catch (Exception e) { Report.Core.Log.Warning("[rig] equip failed: " + e.Message); }
        }

        private static IEnumerator Attempt(string id, string what)
        {
            var player = Player.Local;
            if (player == null) { Report.Core.Log.Warning("[rig] no local player."); yield break; }

            int slot = SlotHolding(id);
            if (slot < 0)
            {
                Report.Core.Log.Warning($"[rig] '{id}' is not in the hotbar. Buy or spawn one first.");
                yield break;
            }

            var target = Nearest(player, 25f);
            if (target == null) { Report.Core.Log.Warning("[rig] no NPC within 25 m."); yield break; }

            Report.Core.Log.Msg($"[rig] {what} on {Name(target)}: equipping slot {slot}, crouching, "
                              + "stepping behind them.");

            try { player.SetEquippedSlotIndex(slot); } catch (Exception e)
            { Report.Core.Log.Warning("[rig] equip failed: " + e.Message); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);

            Place(player, target);
            Crouch();

            // A frame for the mod's Update to see the new state before the press arrives.
            yield return null;
            yield return null;

            // The camera and not the player is what both weapons raycast along, and the look direction is
            // driven by mouse input every frame - so it is aimed here, immediately before the press, rather
            // than with the rest of the placing.
            _target = target;
            _asked = 0;
            HookMod();
            _armed = true;
            yield return null;
            yield return null;

            Report.Core.Log.Msg("[rig] before: " + Describe(target));
            Report.Core.Log.Msg("[rig] gates:  " + ThmGateProbe.State());

            Press();
            yield return new WaitForSecondsRealtime(0.5f);

            if (what == "strangle")
            {
                yield return Play();
                yield return new WaitForSecondsRealtime(1f);
            }

            // Thirty seconds because the syringe is a SLOW toxin - measured, it lands its blow about
            // twenty-one seconds after the injection. A shorter watch reported a living target and called
            // the poison broken when it was merely still working.
            for (int second = 1; second <= 30; second++)
            {
                yield return new WaitForSecondsRealtime(1f);
                Report.Core.Log.Msg($"[rig] +{second}s  minigame {ThmGateProbe.MiniGame()}  {Describe(target)}");
                if (Dead(target)) break;
            }

            Report.Core.Log.Msg("[rig] done: " + Describe(target));
        }

        private static bool Dead(NPC npc)
        {
            try { return npc.Health.IsDead; } catch { return false; }
        }

        /// <summary>
        /// Play the strangling mini-game to the end, hitting the green zone every round.
        /// </summary>
        /// <remarks>
        /// Three rounds, and a miss ends it - so this waits until the indicator is inside the green band and
        /// presses then. Both are private static fields on the mini-game and are read rather than guessed,
        /// which is the difference between proving the kill works and pressing hopefully.
        /// </remarks>
        private static IEnumerator Play()
        {
            Assembly thm = null;
            foreach (var one in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(one.GetName()?.Name, "Kowyx_THM", StringComparison.OrdinalIgnoreCase))
                { thm = one; break; }

            var type = thm?.GetType("HitmanMod.StrangleMiniGame", false);
            var pos = AccessTools.Field(type, "_pos");
            var low = AccessTools.Field(type, "_greenMin");
            var high = AccessTools.Field(type, "_greenMax");
            if (pos == null || low == null || high == null)
            { Report.Core.Log.Warning("[rig] the mini-game's bar is not where it was."); yield break; }

            for (int round = 1; round <= 3; round++)
            {
                int waited = 0;
                while (waited++ < 600)
                {
                    if (ThmGateProbe.MiniGame() != "RUNNING") { Report.Core.Log.Msg("[rig] the mini-game closed."); yield break; }

                    float at = (float)pos.GetValue(null);
                    float from = (float)low.GetValue(null), to = (float)high.GetValue(null);
                    if (at >= from && at <= to)
                    {
                        Report.Core.Log.Msg($"[rig] round {round}: striking at {at:0.00}, green is "
                                          + $"{from:0.00}..{to:0.00}");
                        _pressed = true;
                        yield return null;
                        _pressed = false;
                        break;
                    }
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(0.2f);
            }
        }

        /// <summary>
        /// Put the player where the mod's own checks want them: one metre behind the target, facing it.
        /// </summary>
        /// <remarks>
        /// One metre because both weapons raycast three metres and fall back to a four metre sweep, and
        /// because the angle check is against the target's forward - standing far away and behind still
        /// passes the angle and fails the reach, which is a different failure than the one being tested.
        /// </remarks>
        private static void Place(Player player, NPC target)
        {
            try
            {
                var behind = target.transform.position - target.transform.forward * 1f;
                behind.y = target.transform.position.y;
                player.transform.position = behind;
                player.transform.rotation = Quaternion.LookRotation(
                    target.transform.position - behind, Vector3.up);
            }
            catch (Exception e) { Report.Core.Log.Warning("[rig] placing failed: " + e.Message); }
        }

        /// <summary>
        /// Say the player is crouched.
        /// </summary>
        /// <remarks>
        /// Written through the backing field because the property's setter is private, and written EVERY
        /// FRAME because player movement recomputes it from input on its own update - a single write is
        /// gone again before the mod that reads it gets its turn.
        /// </remarks>
        private static void Crouch()
        {
            try { Player.Local._Crouched_k__BackingField = true; } catch { }
        }

        /// <summary>Point the camera at the target, since that is what the weapons raycast along.</summary>
        private static void Aim(NPC target)
        {
            try
            {
                var camera = Camera.main;
                if (camera == null) { Report.Core.Log.Warning("[rig] no main camera."); return; }

                var eye = camera.transform.position;
                var at = target.transform.position + Vector3.up * 1.4f;      // the head, not the feet
                camera.transform.rotation = Quaternion.LookRotation(at - eye, Vector3.up);
            }
            catch (Exception e) { Report.Core.Log.Warning("[rig] aiming failed: " + e.Message); }
        }

        /// <summary>Hold Interact down for one frame, which is what a key press is.</summary>
        private static void Press()
        {
            if (!Hook()) return;
            _pressed = true;
            Report.Core.Log.Msg($"[rig] pressing Interact (button {_interact}) for one frame.");
            MelonLoader.MelonCoroutines.Start(Release());
        }

        /// <summary>
        /// Hold it for a handful of frames, not one.
        /// </summary>
        /// <remarks>
        /// A coroutine resumes after the frame's Update phase, and T.H.M reads the button from MelonLoader's
        /// OnUpdate, which has already run by then. Releasing on the next frame therefore had a fair chance
        /// of clearing the flag before the mod ever asked - and an unheard press is indistinguishable from a
        /// press that was heard and ignored, which is the difference this rig exists to tell.
        /// </remarks>
        private static IEnumerator Release()
        {
            // Both of these are re-applied from input every frame - the look direction by the camera
            // controller, the crouch by player movement - so setting either once and pressing a frame later
            // presses with neither still in place. They are held for as long as the press is.
            // ONE frame, and the reason is worth keeping. Held for ten, the press opened the mini-game and
            // then answered its first prompt in the same breath - the game closed, the next frame opened it
            // again, and from outside that reads as "nothing happens". A key press is an edge, not a state.
            yield return null;
            _pressed = false;
            _armed = false;

            Report.Core.Log.Msg($"[rig] the press was read {_asked} time(s) while it was held. "
                              + (_asked == 0
                                 ? "Nothing asked for Interact at all - the mod's Update is not reaching "
                                 + "its button check."
                                 : "So the mod saw the press and did not act on it."));
        }

        private static NPC _target;
        private static int _asked;

        /// <summary>
        /// Put the player in the tested state immediately before T.H.M reads it, and say what it found.
        /// </summary>
        /// <remarks>
        /// The whole reason this exists: a coroutine resumes AFTER the frame's Update phase, so anything it
        /// writes is a frame late for a mod that reads it during Update. Crouch was written from a
        /// coroutine and the probe then read back its own write - which looked like proof and was not. This
        /// runs as a prefix on the mod's own Update, which is the one place where "what the mod sees" and
        /// "what the rig set" are the same thing.
        /// </remarks>
        private static void UpdatePrefix()
        {
            if (!_armed) return;
            Crouch();
            Aim(_target);
        }

        private static void AimedPostfix(NPC __result)
        {
            if (!_armed) return;
            Report.Core.Log.Msg("[rig] the mod looked for an NPC and found "
                              + (__result == null ? "NOBODY" : Name(__result)));
        }

        private static void ShowPrefix()
        {
            Report.Core.Log.Msg("[rig] the mod started the strangling mini-game.");
        }

        /// <summary>
        /// What the kill has to work with, read as fields.
        /// </summary>
        /// <remarks>
        /// Fields and not method hooks, deliberately. TryKillTarget is a private static called from exactly
        /// one place, which is what the JIT inlines first - so "my prefix never fired" is not evidence the
        /// method did not run. The two targets it reads are fields, and a field cannot be inlined away.
        /// </remarks>
        private static void CompletePrefix(bool success)
        {
            Report.Core.Log.Msg($"[rig] the mini-game finished, success {success}.");

            var type = _thm?.GetType("HitmanMod.StrangleHandler", false);
            object s1api = AccessTools.Field(type, "_pendingTarget")?.GetValue(null);
            object game = AccessTools.Field(type, "_pendingGameNpc")?.GetValue(null);

            Report.Core.Log.Msg("[rig] the kill has: S1API target "
                              + (s1api == null ? "NULL" : "set") + ", game NPC "
                              + (game == null ? "NULL" : "set")
                              + ". Both null means nothing can be killed and nothing is said about it.");

            _watching = game as NPC;
        }

        private static NPC _watching;

        /// <summary>
        /// What the mod's own completion threw, if anything, and what the target looks like afterwards.
        /// </summary>
        /// <remarks>
        /// A finalizer and not a postfix: a postfix does not run when the method throws, and the caller here
        /// is <c>StrangleMiniGame.Update</c>, whose bare <c>catch { }</c> swallows whatever comes out. That
        /// swallow is why every run so far ended in silence with a living target.
        /// </remarks>
        private static void CompleteFinalizer(Exception __exception)
        {
            if (__exception != null)
                Report.Core.Log.Error("[rig] the mod's completion THREW and the mini-game swallowed it: "
                                    + (__exception.InnerException ?? __exception));

            try
            {
                if (_watching != null)
                    Report.Core.Log.Msg($"[rig] target right after the mod's own attempt: "
                                      + $"health {_watching.Health.Health:0}  dead {_watching.Health.IsDead}");
            }
            catch { }
        }

        private static Assembly _thm;

        private static void TryKillPrefix()
        {
            Report.Core.Log.Msg("[rig] the mod is trying to kill the target.");
        }

        private static void KillPrefix()
        {
            Report.Core.Log.Msg("[rig] S1API's NPC.Kill() was called.");
        }

        private static void DamagePrefix(Il2CppScheduleOne.NPCs.NPCHealth __instance, float damage,
                                         bool isLethal)
        {
            string who = "?";
            try { who = __instance.gameObject.name; } catch { }
            Report.Core.Log.Msg($"[rig] TakeDamage({damage:0.##}, lethal {isLethal}) on {who}");
        }

        private static bool _armed;

        /// <summary>Hook the mod's own entry points, so the state is set and read in the same frame.</summary>
        private static void HookMod()
        {
            if (_modHooked) return;

            Assembly thm = null;
            foreach (var one in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(one.GetName()?.Name, "Kowyx_THM", StringComparison.OrdinalIgnoreCase))
                { thm = one; break; }
            if (thm == null) { Report.Core.Log.Warning("[rig] T.H.M is not loaded."); return; }
            _thm = thm;

            var harmony = new HarmonyLib.Harmony("doodesch.polyfill.rig");
            Patch(harmony, thm, "HitmanMod.StrangleHandler", "Update", nameof(UpdatePrefix), null);
            Patch(harmony, thm, "HitmanMod.PoisonHandler", "Update", nameof(UpdatePrefix), null);
            Patch(harmony, thm, "HitmanMod.StrangleHandler", "FindAimedNpc", null, nameof(AimedPostfix));
            Patch(harmony, thm, "HitmanMod.StrangleMiniGame", "Show", nameof(ShowPrefix), null);
            Patch(harmony, thm, "HitmanMod.StrangleHandler", "OnMiniGameComplete", nameof(CompletePrefix), null);
            try
            {
                var complete = AccessTools.Method(thm.GetType("HitmanMod.StrangleHandler", false),
                                                  "OnMiniGameComplete");
                if (complete != null)
                    harmony.Patch(complete,
                        finalizer: new HarmonyMethod(typeof(ThmRig), nameof(CompleteFinalizer)));
            }
            catch (Exception e) { Report.Core.Log.Warning("[rig] finalizer: " + e.Message); }
            Patch(harmony, thm, "HitmanMod.StrangleHandler", "TryKillTarget", nameof(TryKillPrefix), null);

            // The last unlit stretch: the mini-game is won, TryKillTarget runs, and the target lives. Both
            // ends of the blow are watched - the library call the mod trusts, and the game method that is
            // supposed to end it - because "the kill did not happen" has two very different causes and only
            // one line of log tells them apart.
            foreach (var one in AppDomain.CurrentDomain.GetAssemblies())
            {
                var s1api = one.GetType("S1API.Entities.NPC", false);
                if (s1api == null) continue;
                var kill = AccessTools.Method(s1api, "Kill");
                if (kill != null)
                    harmony.Patch(kill, prefix: new HarmonyMethod(typeof(ThmRig), nameof(KillPrefix)));
                break;
            }

            try
            {
                var damage = AccessTools.Method(typeof(Il2CppScheduleOne.NPCs.NPCHealth), "TakeDamage");
                if (damage != null)
                    harmony.Patch(damage, prefix: new HarmonyMethod(typeof(ThmRig), nameof(DamagePrefix)));
            }
            catch (Exception e) { Report.Core.Log.Warning("[rig] TakeDamage: " + e.Message); }

            _modHooked = true;
        }

        private static bool _modHooked;

        private static void Patch(HarmonyLib.Harmony harmony, Assembly thm, string type, string method,
                                  string prefix, string postfix)
        {
            try
            {
                var target = AccessTools.Method(thm.GetType(type, false), method);
                if (target == null) { Report.Core.Log.Warning($"[rig] {type}.{method} not found."); return; }

                harmony.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(ThmRig), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(ThmRig), postfix));
            }
            catch (Exception e) { Report.Core.Log.Warning($"[rig] {type}.{method}: {e.Message}"); }
        }

        private static bool Hook()
        {
            if (_hooked) return true;

            try
            {
                var type = typeof(Il2CppScheduleOne.GameInput).GetNestedType("ButtonCode");
                _interact = (int)Enum.Parse(type, "Interact");
            }
            catch (Exception e)
            { Report.Core.Log.Warning("[rig] this build has no Interact button: " + e.Message); return false; }

            // Named with its parameter type: GetButton, GetButtonDown and GetButtonUp all take a ButtonCode,
            // and a lookup by name alone is one overload away from patching the wrong one.
            var method = AccessTools.Method(typeof(Il2CppScheduleOne.GameInput), "GetButtonDown",
                new[] { typeof(Il2CppScheduleOne.GameInput.ButtonCode) });
            if (method == null) { Report.Core.Log.Warning("[rig] GetButtonDown is gone."); return false; }

            new HarmonyLib.Harmony("doodesch.polyfill.rig").Patch(
                method, prefix: new HarmonyMethod(typeof(ThmRig), nameof(ButtonPrefix)));
            Report.Core.Log.Msg("[rig] GetButtonDown hooked.");
            return _hooked = true;
        }

        /// <summary>
        /// While armed, Interact reads as pressed.
        /// </summary>
        /// <remarks>
        /// The parameter is declared with the ENUM type the method actually takes. Harmony injects by name
        /// AND type; declaring it as int looks close enough to read and quietly hands the prefix a zero, so
        /// the comparison never matches and the press never happens - which is exactly the silent-default
        /// trap this project keeps a note about.
        /// </remarks>
        private static bool ButtonPrefix(Il2CppScheduleOne.GameInput.ButtonCode buttonCode, ref bool __result)
        {
            if (!_pressed || (int)buttonCode != _interact) return true;
            _asked++;

            // Aimed HERE, not from the coroutine. A coroutine resumes after the frame's Update phase, so
            // anything it points at is already a frame stale by the time the mod looks - and the mod looks
            // for an NPC in the very next statement after this call returns. This is the only moment in the
            // frame where the camera is guaranteed to be pointing where the test needs it.
            Aim(_target);

            __result = true;
            return false;
        }

        private static int SlotHolding(string id)
        {
            try
            {
                var inventory = Il2CppScheduleOne.DevUtilities.PlayerSingleton<PlayerInventory>.Instance;
                if (inventory?.hotbarSlots == null) return -1;

                for (int i = 0; i < inventory.hotbarSlots.Count; i++)
                {
                    object slot = inventory.hotbarSlots[i];
                    object instance = Read(slot, "ItemInstance");
                    object definition = Read(instance, "Definition");
                    if (string.Equals(Read(definition, "ID") as string, id, StringComparison.Ordinal))
                        return i;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// One property off an object, whatever type it turns out to be.
        /// </summary>
        /// <remarks>
        /// Reflection and not a cast, the whole way from the slot down to the id. ItemInstance and
        /// BaseItemDefinition live in Il2CppScheduleOne.Core, and merely NAMING them in a typed expression
        /// makes the compiler demand a reference to that assembly - which is not something a debug command
        /// gets to add to the shipped mod.
        /// </remarks>
        private static object Read(object instance, string property)
        {
            if (instance == null) return null;
            try
            {
                return instance.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance
                                                              | BindingFlags.FlattenHierarchy)
                               ?.GetValue(instance);
            }
            catch { return null; }
        }

        private static NPC Nearest(Player player, float within)
        {
            NPC best = null;
            float closest = within;
            try
            {
                foreach (var npc in UnityEngine.Object.FindObjectsOfType<NPC>())
                {
                    if (npc == null) continue;
                    float distance;
                    try { distance = Vector3.Distance(npc.transform.position, player.transform.position); }
                    catch { continue; }
                    if (distance >= closest) continue;
                    closest = distance;
                    best = npc;
                }
            }
            catch { }
            return best;
        }

        /// <summary>Everything about the target that a weapon is supposed to change.</summary>
        private static string Describe(NPC npc)
        {
            var text = new System.Text.StringBuilder(Name(npc));
            try { text.Append("  health ").Append(npc.Health == null ? "NO HEALTH COMPONENT"
                                                                     : npc.Health.Health.ToString("0")); }
            catch (Exception e) { text.Append("  health threw ").Append(e.GetType().Name); }

            // MaxHealth is the number every kill in every mod multiplies by. It was a field holding 100 and
            // is now a property reading npc.NPCData.Health.MaxHealth, so it is worth reading rather than
            // assuming - a zero here turns a lethal blow into nothing at all, silently.
            try { text.Append("  max ").Append(npc.Health.MaxHealth.ToString("0.##")); }
            catch (Exception e) { text.Append("  max threw ").Append(e.GetType().Name); }
            try { text.Append("  dead ").Append(npc.Health.IsDead); } catch { text.Append("  dead ?"); }
            try { text.Append("  out ").Append(npc.Health.IsKnockedOut); } catch { text.Append("  out ?"); }
            return text.ToString();
        }

        private static string Name(NPC npc)
        {
            try { return npc.FullName ?? npc.name; } catch { return "?"; }
        }
    }
}
#endif
