using System.Runtime.InteropServices;
using MelonLoader;
using Polyfill.Contract;

namespace Polyfill.Boot
{
    /// <summary>
    /// Ask once, before the game exists, whether findings may be shared.
    /// </summary>
    /// <remarks>
    /// WHY A DESKTOP WINDOW IS SAFE HERE, when it would not be later. A modal dialog raised while a
    /// game is running can land behind an exclusive-fullscreen surface: the game freezes on a prompt
    /// nobody can see. That is a real failure and the reason this is not asked from the mod half.
    ///
    /// At <c>OnPreInitialization</c> there is no game to hide behind. MelonLoader's own log shows the
    /// order on this machine: plugins load at 19:12:09.1, the IL2CPP assembly generator starts at
    /// 19:12:10.0, and Unity's player window comes long after that. The dialog is the only window the
    /// process has.
    ///
    /// SILENCE IS NOT CONSENT, and every path here says no. No answer inside the deadline, a dialog
    /// that will not open, an exception, a preference that cannot be written - all of them leave
    /// sharing off. Only the Yes button turns it on.
    ///
    /// It also gives up. Three launches without an answer and the question stops; `polyfillshare on`
    /// still works for anyone who wants it later. A prompt that returns forever is a prompt people
    /// learn to dismiss without reading, which is worse than not asking.
    /// </remarks>
    internal static class FirstAsk
    {
        private const int Attempts = 3;
        private const int DeadlineSeconds = 60;

        private const uint YesNo = 0x00000004;
        private const uint IconQuestion = 0x00000020;
        private const uint DefaultSecond = 0x00000100;   // No is what Enter does
        private const uint TopMost = 0x00040000;
        private const uint SetForeground = 0x00010000;
        private const int IdYes = 6;
        private const int WmClose = 0x0010;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumWindow callback, IntPtr extra);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageW(IntPtr window, int message, IntPtr w, IntPtr l);

        private delegate bool EnumWindow(IntPtr window, IntPtr extra);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        /// <summary>
        /// One question, a short list of what is sent, one line of what never is.
        /// </summary>
        /// <remarks>
        /// A player who launched a game to play it reads a few lines, not paragraphs; anything longer
        /// gets whichever button ends it, which makes the answer meaningless whichever way it goes.
        /// Each item is one line, and the only line breaks are between items: MessageBoxW wraps long
        /// lines itself, and a hard break inside a sentence lands mid-line at the box's own width.
        ///
        /// The list matches the payload built in Report/Share.cs (Body). The console command that changes the answer
        /// later goes in the log line after it, where somebody looking for it will be.
        /// </remarks>
        private const string Question =
            "Send anonymous reports to help fix broken mods?\n\n"
          + "Sent:\n"
          + "• Which mods Polyfill repaired or could not\n"
          + "• Mod names, versions, authors and errors\n"
          + "• Play time and a random number for this install\n\n"
          + "Never sent: your name, your save, your folders.";

        /// <summary>
        /// Put the question on screen if it has not been answered, and record the answer.
        /// </summary>
        /// <remarks>
        /// Called from the plugin's earliest hook. Returns as soon as the answer is known, and the
        /// launch continues either way - the deadline exists so that a player who walked away comes
        /// back to a running game rather than a stopped one.
        /// </remarks>
        internal static void Once(MelonLogger.Instance log)
        {
            var state = Consent.Read();
            if (state.Answered || state.Asked >= Attempts) return;

            /*
             * NOBODY IS THERE TO ANSWER ON A SERVER, and asking anyway costs a minute of every startup.
             *
             * A dedicated server launches with --batchmode --nographics --dedicated-server and runs
             * where no one is looking at a screen. The question still went up: MessageBoxW either
             * draws on a desktop nobody watches or on none at all, and the launch then waits out the
             * full deadline before carrying on - three times, once per attempt, on three separate
             * startups. A server operator sees a minute of nothing and no reason for it.
             *
             * Measured on S1DedicatedServers v1.0.8: the dialog appeared and held the boot until
             * somebody clicked it. On a hosted box there is nobody to click.
             *
             * SILENCE IS NOT CONSENT, so this does not answer for them. It declines to ask and leaves
             * sharing off, which is what an unanswered question already meant.
             *
             * AND IT DOES NOT SAY "type polyfillshare on", because on a dedicated server nobody can.
             * S1DedicatedServers runs its own console with a closed command registry - an unknown word
             * gets "Unknown command" and is never passed to ScheduleOne.Console, which is the method
             * Polyfill's commands hang off. So the one instruction that works headless is the file:
             * MelonPreferences is on disk, an operator already edits it, and it is the same setting the
             * command would have written.
             *
             * AND IT WRITES NOTHING. Saying "not answered" out loud here would mean calling
             * Consent.Write(false, false) on every single startup, which overwrites the very setting the
             * line above just told the operator to make - they turn sharing on, the next boot turns it
             * back off, and nothing says why. Leaving the file alone already means what the write meant:
             * unanswered, and off unless somebody says otherwise.
             */
            if (Contract.Headless.Yes(out string why))
            {
                log.Msg($"[share] not asking - {why}. Nothing is shared. To send findings from this "
                      + $"server, set {Contract.Consent.SharingKey} = true under [Polyfill] in "
                      + "UserData/MelonPreferences.cfg.");
                return;
            }

            Consent.CountOneAsk(state);

            int answer = AskWithDeadline(log);
            bool yes = answer == IdYes;

            // The raw code, because "who said yes" is the one question this must never get wrong. 6 is
            // Yes, 7 is No, 0 is nobody - and a 0 that turned into sharing would be the whole point of
            // this file failing silently.
            log.Msg($"[share] the dialog returned {answer} "
                  + (answer == IdYes ? "(Yes)" : answer == 7 ? "(No)" : "(no answer)"));

            Consent.Write(yes, answered: answer != 0);

            if (answer == 0)
            {
                // state.Asked is the count before this launch, so this was ask number state.Asked + 1.
                int thisAsk = state.Asked + 1;
                if (thisAsk >= Attempts)
                    log.Msg($"[share] nobody answered, so nothing is shared. That was the last of {Attempts} "
                          + "asks; turn it on with `polyfillshare on`.");
                else
                    log.Msg("[share] nobody answered, so nothing is shared. Asking again next launch "
                          + $"({thisAsk + 1} of {Attempts}), or turn it on with `polyfillshare on`.");
                return;
            }

            log.Msg(yes
                ? "[share] on - anonymous findings will be sent. `polyfillshare off` stops it."
                : "[share] off - nothing is sent. `polyfillshare on` turns it on.");
        }

        /// <summary>
        /// The dialog, on its own thread, with a deadline that answers No.
        /// </summary>
        /// <remarks>
        /// A separate thread because <c>MessageBoxW</c> has no timeout of its own: the only way to end
        /// it from outside is to close its window, and to do that this thread has to still be running.
        /// The dialog is unowned on purpose - there is no game window to own it, and passing a handle
        /// that later dies would take the dialog with it.
        ///
        /// Returns 0 when nothing was chosen, which every caller reads as no.
        /// </remarks>
        private static int AskWithDeadline(MelonLogger.Instance log)
        {
            int answer = 0;
            uint dialogThread = 0;
            var done = new ManualResetEventSlim(false);

            var thread = new Thread(() =>
            {
                dialogThread = GetCurrentThreadId();
                try
                {
                    answer = MessageBoxW(IntPtr.Zero, Question, "Polyfill",
                                         YesNo | IconQuestion | DefaultSecond | TopMost | SetForeground);
                }
                catch (Exception e)
                {
                    log.Warning("[share] the question could not be shown, so nothing is shared: " + e.Message);
                }
                finally { done.Set(); }
            })
            { IsBackground = true, Name = "Polyfill consent" };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (done.Wait(TimeSpan.FromSeconds(DeadlineSeconds))) return answer;

            // Out of time. Close the dialog rather than leave the launch waiting on it - a player who
            // stepped away should come back to a game, not to a prompt.
            try
            {
                if (dialogThread != 0)
                    EnumThreadWindows(dialogThread, (window, _) =>
                    {
                        SendMessageW(window, WmClose, IntPtr.Zero, IntPtr.Zero);
                        return true;
                    }, IntPtr.Zero);
            }
            catch (Exception e)
            {
                log.Warning("[share] the unanswered question could not be closed: " + e.Message);
            }

            done.Wait(TimeSpan.FromSeconds(5));
            return 0;
        }
    }
}
