using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Core
{
    /// <summary>
    /// Mods that clone a piece of the game's UI and rebuild its contents.
    /// </summary>
    /// <remarks>
    /// EVERYTHING ELSE POLYFILL CHECKS IS A NAME. This is not a name, and it is the reason a mod can
    /// read "clean" here and be visibly broken in the game.
    ///
    /// Media Player is the case this was written for. It does not build a phone app; it clones the
    /// game's <c>ProductManagerApp</c> out of <c>AppsCanvas</c>, destroys the children of its
    /// <c>Container</c> and builds its own panels in there. Every name it asks for exists, so the load
    /// check says clean - and 0.4.6 rebuilt that prefab, so its buttons end up crushed into a band at
    /// the top of the screen and its panels do not show at all. The Container carries a
    /// <c>VerticalLayoutGroup</c> with ChildControlWidth and ChildControlHeight set, which makes the
    /// layout group and not the children's anchors the authority over where they go
    /// (_ripped/0.4.6f12/ExportedProject/Assets/GameObject/Player.prefab:8294,133508).
    ///
    /// THIS REPAIRS NOTHING, and it is not trying to. A mod's assumption about the shape of somebody
    /// else's prefab is not something a name-level layer can promise to keep true. What it does is
    /// stop the report being confidently wrong: a note rather than a missing name, so the verdict stays
    /// what it honestly is while the reader learns there is a second way this mod can fail.
    ///
    /// Deliberately narrow. One method body has to NAME a piece of the game's interface and
    /// INSTANTIATE something. Mentioning the phone is not enough. It will miss obfuscated names,
    /// reflection, and a clone split across helper methods - and missing a case costs nothing, while a
    /// false one costs an author an afternoon looking for a problem that is not there.
    /// </remarks>
    internal static class ShapeCoupling
    {
        /// <summary>
        /// Marks that name a piece of the game's own interface.
        /// </summary>
        /// <remarks>
        /// CONTAINS, not equals, and that is the whole correction. The first version asked for the bare
        /// literal "AppsCanvas" plus a name from a list of vanilla apps, and missed Mod Manager
        /// entirely: it spells the same place as a path,
        /// "Player_Local/CameraContainer/Camera/OverlayCamera/GameplayMenu/Phone/phone/AppsCanvas", and
        /// takes its template from "MainMenu/Home/Bank/Panel" - neither of which is an app name.
        ///
        /// Still narrow: a body has to name one of these AND instantiate something in the same method.
        /// A mod that merely mentions the phone is not flagged.
        /// </remarks>
        private static readonly string[] Marks =
        {
            "AppsCanvas", "GameplayMenu/Phone", "MainMenu/", "HomeScreen",
        };

        /// <summary>Vanilla apps, still named: they make the note say WHICH piece was borrowed.</summary>
        private static readonly string[] Apps =
        {
            "ProductManagerApp", "DeliveryApp", "MessagesApp", "ContactsApp", "ManagementApp",
            "OrganisationApp", "OrganizationApp", "SettingsApp", "MapApp",
        };

        internal static void Check(ModuleDefinition module, Contract.ModReport report)
        {
            try
            {
                foreach (var type in module.Types)
                    foreach (var method in Methods(type))
                        Inspect(method, report);
            }
            catch { }        // a note is a nicety; it must never be the reason a mod fails to be analysed
        }

        private static IEnumerable<MethodDefinition> Methods(TypeDefinition type)
        {
            foreach (var method in type.Methods) yield return method;
            foreach (var nested in type.NestedTypes)
                foreach (var method in Methods(nested)) yield return method;
        }

        /// <summary>
        /// Is this string a place in the game's hierarchy, rather than a sentence that mentions one?
        /// </summary>
        /// <remarks>
        /// A PATH OR THE WHOLE NAME, never a substring of prose. Matching "HomeScreen" anywhere flagged
        /// four mods on their own log lines - "[NetEye] CreateAppIcon: HomeScreen missing." is a
        /// message, not a lookup, and two of the four were our own. A path has a slash; a bare object
        /// name is the entire string. Neither describes an error message.
        /// </remarks>
        private static bool NamesTheGame(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 200) return false;

            foreach (string name in Marks)
            {
                if (text == name.TrimEnd('/')) return true;
                if (text.Contains('/') && text.Contains(name)) return true;
            }
            return false;
        }

        /// <summary>The tail of a path: the full one is mostly camera rig and reads as noise.</summary>
        private static string Shorten(string path)
        {
            int cut = path.LastIndexOf('/');
            return cut > 0 && cut < path.Length - 1 ? path.Substring(cut + 1) : path;
        }

        private static void Inspect(MethodDefinition method, Contract.ModReport report)
        {
            if (!method.HasBody) return;

            bool instantiates = false;
            string mark = null, app = null;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string text)
                {
                    if (mark == null && NamesTheGame(text)) mark = text;
                    if (app == null)
                        foreach (string name in Apps)
                            if (text.Contains(name)) { app = name; break; }
                    continue;
                }

                // Instantiate is what makes it a CLONE rather than a mod reading the screen it was given.
                if (instruction.Operand is MethodReference called && called.Name == "Instantiate")
                    instantiates = true;
            }

            if (mark == null || !instantiates) return;

            report.Findings.Add(new Contract.Finding
            {
                Kind = "shape-coupled",
                Note = true,
                Symbol = app ?? Shorten(mark),
                Reason = "this mod clones a piece of the game's own interface (" + Shorten(mark)
                       + ") and rebuilds what is inside it, so how it looks depends on the shape of "
                       + "something the game is free to change. Every name it asks for is here - if it "
                       + "looks wrong in game, that is why, and Polyfill cannot repair it",
                Site = method.DeclaringType?.FullName + "/" + method.Name,
            });
        }
    }
}
