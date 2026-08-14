using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Bridges.Steps.S0_4_5f2_To_0_4_6f5
{
    /// <summary>
    /// A screen the game deleted, answered with "it is not open" - which is true.
    /// </summary>
    /// <remarks>
    /// THIS IS A DIFFERENT KIND OF BRIDGE AND THE DIFFERENCE MATTERS. Every other one points an old name at
    /// something the game still has. This one points at nothing, because there is nothing: 0.4.6 removed
    /// <c>ScheduleOne.UI.Management.NPCSelector</c> and did not replace it, and the game says so itself -
    /// <c>Debug.LogError("NPCSelector not implemented")</c> at
    /// `ScheduleOne.UI.Management/NPCFieldUI.cs:79`. The README lists that case under what this project
    /// cannot do, and it is right to, with one exception that this is:
    ///
    /// THE ONLY USE IS A NULL CHECK. OverTheCounter reads the property to ask "is that screen currently
    /// open", and a screen that does not exist is not open. Answering null is not an approximation of the
    /// old behaviour - it IS the old behaviour, for a game that no longer has the screen.
    ///
    /// What it buys is out of proportion to its size. A MissingMethodException is thrown when the METHOD is
    /// compiled, so <c>ManagerClipboardPatch.UpdatePrefix</c> dies on its first call, before its own button
    /// check - and a Harmony prefix that throws takes the original with it, so the GAME'S OWN clipboard
    /// stops updating too. One deleted screen costs the whole clipboard and sixty errors a second.
    /// Answering null makes the method compile, the check fall through, and both the vanilla clipboard and
    /// OverTheCounter's manager panel work again.
    ///
    /// Three people arrived at the same place independently before this was written: the community repatch
    /// deletes the line, ibn666 posted a hand-edited DLL that comments it out, and Polyfill 0.6.1 took the
    /// whole patch off. This is the same decision, made once, where nobody has to edit a DLL for it.
    ///
    /// The type is created empty and never instantiated - nothing can be built from it, and the only value
    /// that will ever have its type is null.
    /// </remarks>
    internal static class Removed
    {
        internal const string NpcSelector = "Il2CppScheduleOne.UI.Management.NPCSelector";

        /// <summary>
        /// <c>ManagementInterface.NPCSelector</c>, answering null.
        /// </summary>
        /// <remarks>
        /// The type has to exist before the getter can name it: a mod that reads the property holds the
        /// result in a local, and a local whose type will not load stops the method from compiling just as
        /// surely as the missing property does.
        /// </remarks>
        internal static MethodDefinition EmitNpcSelectorGetter(ModuleDefinition module, TypeDefinition type)
        {
            var stub = Stub(module);
            if (stub == null) return null;

            var getter = new MethodDefinition("get_NPCSelector",
                MethodAttributes.Public | MethodAttributes.HideBySig, stub);

            var il = getter.Body.GetILProcessor();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
            return getter;
        }

        /// <summary>The empty stand-in, made once per module.</summary>
        private static TypeDefinition Stub(ModuleDefinition module)
        {
            var existing = module.GetType(NpcSelector);
            if (existing != null) return existing;

            int dot = NpcSelector.LastIndexOf('.');
            var stub = new TypeDefinition(NpcSelector.Substring(0, dot), NpcSelector.Substring(dot + 1),
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.BeforeFieldInit,
                module.ImportReference(module.TypeSystem.Object));

            // AN EMPTY TYPE IS NOT ENOUGH, and shipping one cost a release. The caller reads the property
            // off the result, and the CLR resolves that method when the CALLER is compiled - the null check
            // in front of it is not reached and does not protect it:
            //
            //     NPCSelector npcselector = instance.NPCSelector;
            //     if (npcselector != null && npcselector.IsOpen) return true;
            //
            // gave MissingMethodException: 'Boolean NPCSelector.get_IsOpen()'. So the stand-in carries the
            // member it stands in for, answering the same thing the null did: the screen is not open.
            var isOpen = new MethodDefinition("get_IsOpen",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.TypeSystem.Boolean);
            var il = isOpen.Body.GetILProcessor();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            stub.Methods.Add(isOpen);

            var property = new PropertyDefinition("IsOpen", PropertyAttributes.None,
                                                  module.TypeSystem.Boolean) { GetMethod = isOpen };
            stub.Properties.Add(property);

            module.Types.Add(stub);
            return stub;
        }
    }
}
