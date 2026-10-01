using System.Reflection;
using System.Reflection.Emit;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// A prefix that runs an <c>object[] __args</c> prefix and then hands back the arguments it changed,
    /// including the ones the patched method takes by value.
    /// </summary>
    /// <remarks>
    /// HarmonyX copies <c>__args</c> back into the real arguments only for parameters the original declares
    /// by-ref (HarmonyManipulator.EmitAssignRefsFromArgsArray), so a change to a by-value argument made through
    /// <c>__args</c> is lost. A prefix may still take a by-value argument as <c>ref</c> - Harmony passes the
    /// argument's address - so this builds one per patched method, with <c>ref T __N</c> for each position
    /// that has to come back:
    /// <code>
    /// bool Prefix(object __instance, object[] __args, MethodBase __originalMethod, ref T __N, ...)
    /// {
    ///     __args[N] = __N;                                  // what earlier prefixes left there, not the
    ///                                                       // snapshot Harmony took before any of them ran
    ///     bool run = body(__instance, __args, __originalMethod);
    ///     __N = (T)__args[N];
    ///     return run;
    /// }
    /// </code>
    /// The body must leave an instance of <c>T</c> at each of those positions; for a value type, never null.
    ///
    /// Harmony refuses a DynamicMethod handed to it directly, and takes one from a factory: a static method
    /// returning MethodInfo with a single MethodBase parameter, called with the method being patched
    /// (Patch.GetMethod). With a DynamicMethod patch it binds parameters by name only, and <c>__N</c> is
    /// resolved by position before any name lookup, so the real method's parameter names do not matter.
    /// </remarks>
    internal static class ByValueRefPrefix
    {
        internal static DynamicMethod Build(MethodInfo real, IReadOnlyList<int> positions, MethodInfo body)
        {
            var realParameters = real.GetParameters();
            var types = new List<Type> { typeof(object), typeof(object[]), typeof(MethodBase) };
            var elements = new List<Type>();
            foreach (int position in positions)
            {
                var type = realParameters[position].ParameterType;
                if (type.IsByRef) throw new ArgumentException($"{real.Name}'s parameter {position} is already by-ref.");
                elements.Add(type);
                types.Add(type.MakeByRefType());
            }

            var method = new DynamicMethod("PatchesOnDroppedArguments_" + real.Name, typeof(bool), types.ToArray(),
                                           typeof(ByValueRefPrefix), skipVisibility: true);
            method.DefineParameter(1, ParameterAttributes.None, "__instance");
            method.DefineParameter(2, ParameterAttributes.None, "__args");
            method.DefineParameter(3, ParameterAttributes.None, "__originalMethod");
            for (int j = 0; j < positions.Count; j++)
                method.DefineParameter(4 + j, ParameterAttributes.None, "__" + positions[j]);

            var il = method.GetILGenerator();
            var run = il.DeclareLocal(typeof(bool));
            for (int j = 0; j < positions.Count; j++)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, positions[j]);
                il.Emit(OpCodes.Ldarg, (short)(3 + j));
                il.Emit(OpCodes.Ldobj, elements[j]);
                il.Emit(OpCodes.Box, elements[j]);
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, body);
            il.Emit(OpCodes.Stloc, run);
            for (int j = 0; j < positions.Count; j++)
            {
                il.Emit(OpCodes.Ldarg, (short)(3 + j));
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, positions[j]);
                il.Emit(OpCodes.Ldelem_Ref);
                il.Emit(OpCodes.Unbox_Any, elements[j]);
                il.Emit(OpCodes.Stobj, elements[j]);
            }
            il.Emit(OpCodes.Ldloc, run);
            il.Emit(OpCodes.Ret);
            return method;
        }
    }
}
