namespace Polyfill.Contract
{
    /// <summary>
    /// Patch classes the plugin aimed at the game's method before Harmony applied them, told to the mod.
    /// </summary>
    /// <remarks>
    /// The plugin and the mod are two assemblies and each compiles its own copy of <c>Contract</c>, so a
    /// static field here would be two fields. The list sits in the AppDomain's data instead, as a
    /// <c>List&lt;string&gt;</c>: a type both halves share without sharing one of their own.
    ///
    /// A key is whose patch and what it sits on, <c>assembly|Type::Method</c>, the form Report/Reconcile.cs
    /// matches a finding by.
    /// </remarks>
    internal static class ReaimedPatches
    {
        private const string Slot = "doodesch.polyfill.reaimed-patches";
        private static readonly object Gate = new();

        internal static void Add(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (Gate)
            {
                var list = AppDomain.CurrentDomain.GetData(Slot) as List<string>;
                if (list == null)
                {
                    list = new List<string>();
                    AppDomain.CurrentDomain.SetData(Slot, list);
                }
                lock (list) if (!list.Contains(key)) list.Add(key);
            }
        }

        internal static string[] All()
        {
            var list = AppDomain.CurrentDomain.GetData(Slot) as List<string>;
            if (list == null) return Array.Empty<string>();
            lock (list) return list.ToArray();
        }
    }
}
