using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Polyfill.Bridges.Steps.S0_4_5f2_To_0_4_6f5
{
    /// <summary>
    /// The key hints at the edge of the screen, given back to the name mods still call.
    /// </summary>
    /// <remarks>
    /// 0.4.6f5 replaced <c>ScheduleOne.UI.InputPromptsCanvas</c> - one Singleton with one module slot -
    /// with <c>ScheduleOne.UI.Input.InputPromptsManager</c>, which keeps a dictionary of panels that are up
    /// at the same time. Polyfill already put the old NAME back, as a stand-in around the manager's native
    /// class, and that alone is what stops a mod dying: a method mentioning a type that is not there fails
    /// to compile at its first call, past every try/catch in the mod, so the whole method is lost and not
    /// just the hint. Over The Counter lost the ability to give a manager a route that way.
    ///
    /// WHAT THE STAND-IN DID NOT DO IS WORK. Its three members answered with constants, so a route
    /// selector opened with no key hints and closed without removing any. The hint row has been dead for
    /// every mod that uses it since 0.4.6f5.
    ///
    /// It does not have to be. The stand-in carries the manager's native class, so <c>this</c> already
    /// points at the live manager, and the manager still has each piece the old canvas offered:
    ///
    /// <code>
    /// 0.4.5f2 InputPromptsCanvas      0.4.6f13 InputPromptsManager
    /// LoadModule(string key)          LoadModule(string id)                 :271
    /// UnloadModule()                  UnloadModule(string id)               :382
    /// currentModuleLabel              HasActivePrompt(string id)            :480
    /// </code>
    ///
    /// The one thing 0.4.6 does not keep is WHICH module is the current one, because it no longer has a
    /// current one. That is what <c>polyfillLoadedModule</c> is for: the id this stand-in last loaded. It
    /// is not invented state - it is exactly the field the old canvas kept
    /// (<c>currentModuleLabel</c>, InputPromptsCanvas.cs:22), and the old class loaded one module at a
    /// time and unloaded the previous one first, which is reproduced here.
    ///
    /// <c>currentModuleLabel</c> asks the manager whether that id is still up rather than trusting the
    /// field, so a module the game removed by another route reads as gone instead of as still loaded.
    ///
    /// AN UNKNOWN ID IS NOT SWALLOWED, and that matters here because 0.4.6 dropped most of the ids. It
    /// loads GenericTask, Station, HarvestPlant, TrashGrabber and eight more; the modules 0.4.5f2 had for
    /// objectselector, npcselector, building, phone, gun, consumable and exitonly are not in it, and
    /// <c>GetInputPromptData("objectselector")</c> answers null on a running 0.4.6f13. So a mod that asks
    /// for one of those gets the game's own "No input data found for id" and no hints - which is what the
    /// old canvas did with a key it did not have (InputPromptsCanvas.cs:30). Nothing here maps a dropped
    /// id onto a surviving one: the hints would then say something the mod did not ask for.
    ///
    /// Over The Counter's route selector is one of those. Its Close now runs and its label check is
    /// truthful; the hint row it wanted is content the game removed.
    /// </remarks>
    internal sealed partial class Set
    {
        /// <summary>Where the stand-in keeps the id it loaded, added on first use.</summary>
        private static FieldDefinition Slot(ModuleDefinition module, TypeDefinition facade)
        {
            foreach (var field in facade.Fields)
                if (field.Name == "polyfillLoadedModule") return field;

            var slot = new FieldDefinition("polyfillLoadedModule",
                FieldAttributes.Private | FieldAttributes.Static, module.TypeSystem.String);
            facade.Fields.Add(slot);
            return slot;
        }

        /// <summary>
        /// The one method of that name taking one string, or null when there is none or several.
        /// </summary>
        /// <remarks>
        /// A COUNT IS NOT A SIGNATURE HERE. <c>UnloadModule</c> has two one-argument overloads on the
        /// manager - <c>UnloadModule(string)</c> and <c>UnloadModule(InputPromptsData)</c> - and picking by
        /// count alone would take whichever the metadata happens to list first.
        /// </remarks>
        private static MethodDefinition TakingOneString(TypeDefinition type, string name)
        {
            if (type == null) return null;

            MethodDefinition found = null;
            foreach (var method in type.Methods)
            {
                if (method.Name != name || method.Parameters.Count != 1) continue;
                if (method.Parameters[0].ParameterType.MetadataType != MetadataType.String) continue;
                if (found != null) return null;
                found = method;
            }
            return found;
        }

        /// <summary>Puts a wrapper of the manager, built from this stand-in's own pointer, on the stack.</summary>
        private static void PushManager(ILProcessor il, MethodReference pointer, MethodReference make)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, pointer);
            il.Emit(OpCodes.Newobj, make);
        }

        /// <summary><c>LoadModule(key)</c> - unload whatever is up, load this one, remember it.</summary>
        private static MethodDefinition EmitLoadModule(ModuleDefinition module, TypeDefinition facade,
                                                       TypeDefinition manager)
        {
            var load = TakingOneString(manager, "LoadModule");
            var unload = TakingOneString(manager, "UnloadModule");
            var pointer = Core.ShadowTypes.PointerGetter(manager);
            var make = Core.ShadowTypes.PointerConstructorOf(manager);
            if (load == null || unload == null || pointer == null || make == null) return null;

            var slot = Slot(module, facade);
            var method = new MethodDefinition("LoadModule",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("key", ParameterAttributes.None,
                                                          module.TypeSystem.String));

            var il = method.Body.GetILProcessor();
            var then = il.Create(OpCodes.Ldarg_0);

            // The old canvas had one slot and unloaded the previous module before instantiating the next
            // (InputPromptsCanvas.cs:34). The manager would keep both panels up.
            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Brfalse, then);
            PushManager(il, module.ImportReference(pointer), module.ImportReference(make));
            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Callvirt, module.ImportReference(unload));

            il.Append(then);                                  // ldarg.0, the start of the manager wrapper
            il.Emit(OpCodes.Call, module.ImportReference(pointer));
            il.Emit(OpCodes.Newobj, module.ImportReference(make));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, module.ImportReference(load));
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stsfld, slot);
            il.Emit(OpCodes.Ret);

            return method;
        }

        /// <summary><c>UnloadModule()</c> - take down the id this stand-in loaded, and forget it.</summary>
        private static MethodDefinition EmitUnloadModule(ModuleDefinition module, TypeDefinition facade,
                                                         TypeDefinition manager)
        {
            var unload = TakingOneString(manager, "UnloadModule");
            var pointer = Core.ShadowTypes.PointerGetter(manager);
            var make = Core.ShadowTypes.PointerConstructorOf(manager);
            if (unload == null || pointer == null || make == null) return null;

            var slot = Slot(module, facade);
            var method = new MethodDefinition("UnloadModule",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);

            var il = method.Body.GetILProcessor();
            var done = il.Create(OpCodes.Ret);

            // NOTHING LOADED IS NOT AN ERROR. The old canvas cleared its label and destroyed its module
            // only if there was one; calling it twice was harmless and mods do call it twice.
            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Brfalse, done);
            PushManager(il, module.ImportReference(pointer), module.ImportReference(make));
            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Callvirt, module.ImportReference(unload));
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stsfld, slot);
            il.Append(done);

            return method;
        }

        /// <summary>
        /// <c>currentModuleLabel</c> - the id this stand-in loaded, while the manager still shows it.
        /// </summary>
        /// <remarks>
        /// EMPTY, NEVER NULL. The old property was initialised to <c>string.Empty</c> and set back to it on
        /// unload (InputPromptsCanvas.cs:22,42), so a caller could read its Length without a check. A null
        /// here would turn a working comparison into a NullReferenceException in code that never had one.
        /// </remarks>
        private static MethodDefinition EmitCurrentModuleLabel(ModuleDefinition module, TypeDefinition facade,
                                                               TypeDefinition manager)
        {
            var active = TakingOneString(manager, "HasActivePrompt");
            var pointer = Core.ShadowTypes.PointerGetter(manager);
            var make = Core.ShadowTypes.PointerConstructorOf(manager);
            if (active == null || active.ReturnType.MetadataType != MetadataType.Boolean
                || pointer == null || make == null) return null;

            var slot = Slot(module, facade);
            var method = new MethodDefinition("get_currentModuleLabel",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.TypeSystem.String);

            var il = method.Body.GetILProcessor();
            var none = il.Create(OpCodes.Ldstr, "");

            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Brfalse, none);
            PushManager(il, module.ImportReference(pointer), module.ImportReference(make));
            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Callvirt, module.ImportReference(active));
            il.Emit(OpCodes.Brfalse, none);
            il.Emit(OpCodes.Ldsfld, slot);
            il.Emit(OpCodes.Ret);
            il.Append(none);
            il.Emit(OpCodes.Ret);

            return method;
        }
    }
}
