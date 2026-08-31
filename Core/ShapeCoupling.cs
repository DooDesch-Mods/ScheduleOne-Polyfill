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
    /// Deliberately narrow. It matches a method that names the app canvas AND a known vanilla app AND
    /// instantiates something, in one body. A mod that merely mentions one of those strings is not
    /// flagged. It will miss obfuscated names, reflection, and a clone split across helper methods -
    /// and missing a case costs nothing, while a false one costs an author an afternoon.
    /// </remarks>
    internal static class ShapeCoupling
    {
        /// <summary>The container every phone app lives under; nothing else in the game is called this.</summary>
        private const string Canvas = "AppsCanvas";

        /// <summary>Vanilla apps a mod might clone. Named, because "any string" would match anything.</summary>
        private static readonly string[] Apps =
        {
            "ProductManagerApp", "DeliveryApp", "MessagesApp", "ContactsApp", "ManagementApp",
            "ProductManager", "OrganisationApp", "OrganizationApp", "SettingsApp", "MapApp",
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

        private static void Inspect(MethodDefinition method, Contract.ModReport report)
        {
            if (!method.HasBody) return;

            bool canvas = false, instantiates = false;
            string app = null;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string text)
                {
                    if (text == Canvas) canvas = true;
                    else if (app == null)
                        foreach (string name in Apps)
                            if (text == name) { app = name; break; }
                    continue;
                }

                // Instantiate is what makes it a CLONE rather than a mod reading the app it was given.
                if (instruction.Operand is MethodReference called && called.Name == "Instantiate")
                    instantiates = true;
            }

            if (!canvas || app == null || !instantiates) return;

            report.Findings.Add(new Contract.Finding
            {
                Kind = "shape-coupled",
                Note = true,
                Symbol = app,
                Reason = "this mod clones the game's " + app + " and rebuilds what is inside it, so its "
                       + "layout depends on the shape of a prefab the game is free to change. Every name "
                       + "it asks for is here - if it looks wrong in game, that is why, and Polyfill "
                       + "cannot repair it",
                Site = method.DeclaringType?.FullName + "/" + method.Name,
            });
        }
    }
}
