#if DEBUG
using HarmonyLib;
using MelonLoader;

namespace Polyfill.Report
{
    /// <summary>
    /// Opens a vanilla phone app from the console, so a UI repair can be looked at without a person.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS. Polyfill repairs other mods' user interfaces - it switches off inherited layout
    /// groups, it puts back methods a phone app calls, it moves patches onto the screen the game really
    /// draws. Every one of those repairs is invisible until somebody opens the phone, and nothing in the
    /// game's own sixty-three console commands opens it. So the only way to check whether a UI repair
    /// worked, or whether it left a stray panel behind, was to ask a person to click and describe what
    /// they saw.
    ///
    /// That is not a small inconvenience. A stray white rectangle over the Messages chat took an evening
    /// of one-click experiments to not identify, because each round trip cost somebody's attention and
    /// answered exactly one bit. With this, the same question is a command and a screenshot.
    ///
    /// DEBUG ONLY. It opens a screen the player did not ask for, which is a fine thing for a developer
    /// and a terrible one in a release. The `#if DEBUG` is the guarantee, checked by the release build
    /// carrying no reference to it at all.
    ///
    /// It goes through the game's own SetOpen rather than activating objects by hand, so anything that
    /// hooks the app - Polyfill's repairs, and other mods' takeovers - sees exactly what it sees when
    /// somebody presses the icon. A screenshot after this is a screenshot of the real thing.
    /// </remarks>
    internal static class PhoneApp
    {
        /// <summary>The apps the game draws itself, by the name a reader would type.</summary>
        private static readonly (string Word, string Type)[] Known =
        {
            ("messages",  "Il2CppScheduleOne.UI.Phone.Messages.MessagesApp"),
            ("contacts",  "Il2CppScheduleOne.UI.Phone.ContactsApp"),
            ("map",       "Il2CppScheduleOne.UI.Phone.Map.MapApp"),
            ("products",  "Il2CppScheduleOne.UI.Phone.ProductManagerApp"),
            ("dealers",   "Il2CppScheduleOne.UI.Phone.DealerManagementApp"),
            ("delivery",  "Il2CppScheduleOne.UI.Phone.Delivery.DeliveryApp"),
            ("journal",   "Il2CppScheduleOne.UI.Phone.Journal.JournalApp"),
        };

        internal static void Open(string argument)
        {
            string wanted = (argument ?? "").Trim().ToLowerInvariant();
            if (wanted.Length == 0)
            {
                Core.Log.Msg("polyfillapp <app> - opens one of: "
                           + string.Join(", ", Names()) + ". `polyfillapp close` shuts it again.");
                return;
            }

            if (wanted == "close") { Close(); return; }

            foreach (var (word, typeName) in Known)
            {
                if (word != wanted) continue;

                // THE PHONE FIRST. An app's SetOpen only decides which screen is in front; the apps canvas
                // is not on screen until the phone itself is up, so asking an app to open on a closed phone
                // reports success and shows nothing - which is exactly what the first version of this did.
                Phone(true);
                Set(word, typeName, open: true);
                return;
            }

            Core.Log.Warning($"there is no app called '{wanted}'. Try one of: " + string.Join(", ", Names()));
        }

        private static void Close()
        {
            foreach (var (word, typeName) in Known) Set(word, typeName, open: false, quiet: true);
            Phone(false);
            Core.Log.Msg("[app] the phone and every vanilla app were asked to close.");
        }

        private static IEnumerable<string> Names()
        {
            foreach (var (word, _) in Known) yield return word;
        }


        /// <summary>Put the phone up or away, through the menu that owns it.</summary>
        /// <remarks>
        /// NOT Phone.SetIsOpen, which is what the first version called and why nothing appeared. That
        /// method sets a flag and raises two events; the phone is put on screen by GameplayMenu.Open,
        /// which also turns on the overlay light, hands the player the lowered-phone equippable, opens the
        /// menu interface, registers a UI element with the camera and starts the animation coroutine
        /// (GameplayMenu.cs:248-267). SetIsOpen is one line inside that.
        ///
        /// The screen is chosen first, because the same menu shows either the phone or the character and
        /// Open honours whichever is current (GameplayMenu.cs:252-259).
        /// </remarks>
        private static void Phone(bool open)
        {
            try
            {
                var menu = AccessTools.TypeByName("Il2CppScheduleOne.UI.GameplayMenu");
                var instance = menu == null
                    ? null
                    : AccessTools.PropertyGetter(menu, "Instance")?.Invoke(null, null);
                if (instance == null)
                {
                    Core.Log.Warning("the gameplay menu has no instance yet - load a save first.");
                    return;
                }

                if (open)
                {
                    var setScreen = AccessTools.Method(menu, "SetScreen");
                    var screen = AccessTools.TypeByName("Il2CppScheduleOne.UI.GameplayMenu/EGameplayScreen")
                              ?? AccessTools.Inner(menu, "EGameplayScreen");
                    if (setScreen != null && screen != null)
                        setScreen.Invoke(instance, new[] { Enum.ToObject(screen, 0) });   // 0 is Phone
                }

                var door = AccessTools.Method(menu, open ? "Open" : "Close", Type.EmptyTypes);
                if (door == null)
                {
                    Core.Log.Warning($"GameplayMenu.{(open ? "Open" : "Close")}() is not on this build, so "
                                   + "the phone cannot be moved from here.");
                    return;
                }

                door.Invoke(instance, null);
            }
            catch (Exception e)
            {
                Core.Log.Warning("the phone threw on " + (open ? "open" : "close") + ": "
                               + (e.InnerException ?? e).Message);
            }
        }

        /// <summary>Ask one app to open or close, through the same method the icon calls.</summary>
        private static void Set(string word, string typeName, bool open, bool quiet = false)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    if (!quiet) Core.Log.Warning($"{typeName} is not on this build, so '{word}' cannot open.");
                    return;
                }

                // The app is a singleton the game reaches through PlayerSingleton<T>. Reading its own
                // Instance is enough, and it is null until the phone has been built once.
                var instance = AccessTools.PropertyGetter(type, "Instance")?.Invoke(null, null);
                if (instance == null)
                {
                    if (!quiet) Core.Log.Warning($"'{word}' has no instance yet - load a save first.");
                    return;
                }

                var setOpen = AccessTools.Method(type, "SetOpen", new[] { typeof(bool) });
                if (setOpen == null)
                {
                    if (!quiet) Core.Log.Warning($"'{word}' has no SetOpen(bool) on this build.");
                    return;
                }

                setOpen.Invoke(instance, new object[] { open });
                if (!quiet) Core.Log.Msg($"[app] {word} was asked to {(open ? "open" : "close")}.");
            }
            catch (Exception e)
            {
                // Said rather than swallowed: an app that refuses to open is the interesting case, and a
                // silent return here would read exactly like one that opened and drew nothing.
                if (!quiet)
                    Core.Log.Warning($"'{word}' threw on {(open ? "open" : "close")}: "
                                   + (e.InnerException ?? e).Message);
            }
        }
    }
}
#endif
