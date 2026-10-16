using System.Reflection;

namespace Polyfill.Contract
{
    /// <summary>
    /// Patch methods the plugin moved onto another class's method because IL2CPP compiled both to one function,
    /// with the classes each one was written for.
    /// </summary>
    /// <remarks>
    /// The plugin moves the patch (FoldedTargetsShareOneDetour); the mod's PatchesOnFoldedCode needs to know
    /// which classes it was aimed at, so a patch written for the sewer office runs for the sewer office and not
    /// for every class sharing the function. Kept in the AppDomain's data for the same reason as
    /// <see cref="ReaimedPatches"/>: each half compiles its own copy of this class. The value only uses BCL
    /// types so both copies read it.
    /// </remarks>
    internal static class FoldedAims
    {
        private const string Slot = "doodesch.polyfill.folded-aims";
        private static readonly object Gate = new();

        internal static void Add(MethodInfo patch, Type aimedAt)
        {
            if (patch == null || aimedAt == null) return;
            lock (Gate)
            {
                var map = AppDomain.CurrentDomain.GetData(Slot) as Dictionary<MethodInfo, List<Type>>;
                if (map == null)
                {
                    map = new Dictionary<MethodInfo, List<Type>>();
                    AppDomain.CurrentDomain.SetData(Slot, map);
                }
                lock (map)
                {
                    if (!map.TryGetValue(patch, out var types)) map[patch] = types = new List<Type>();
                    if (!types.Contains(aimedAt)) types.Add(aimedAt);
                }
            }
        }

        /// <summary>The classes <paramref name="patch"/> was written for, or null when it was never moved.</summary>
        internal static Type[] For(MethodInfo patch)
        {
            if (patch == null) return null;
            var map = AppDomain.CurrentDomain.GetData(Slot) as Dictionary<MethodInfo, List<Type>>;
            if (map == null) return null;
            lock (map) return map.TryGetValue(patch, out var types) ? types.ToArray() : null;
        }

        internal static bool Has(MethodInfo patch) => For(patch) != null;
    }
}
