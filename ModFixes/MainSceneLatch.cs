using System.Threading;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Has the game scene been reached? Readable from any thread, written only from the main one.
    /// </summary>
    /// <remarks>
    /// The guards that need this run on threads that are not Unity's. Mules reloads MelonPreferences from
    /// a <c>Task.Run</c>, MelonLoader dispatches the preference callbacks synchronously on whatever thread
    /// called it, and so every mod's handler - and any prefix on one - is on a worker. Asking
    /// <c>SceneManager.GetActiveScene()</c> there is the same mistake the guard exists to prevent, and it
    /// was in the first version of it.
    ///
    /// So the answer is taken once, on the main thread, in <c>OnSceneWasLoaded</c>, and read as a flag.
    /// It is never lowered again: the loop only matters while the game starts, and a scene change back to
    /// the menu is not a reason to start guarding a mod that is by then working normally.
    /// </remarks>
    internal static class MainSceneLatch
    {
        /// <summary>The scene the game runs in. Anything else is the menu or a load screen.</summary>
        internal const string GameScene = "Main";

        private static int _reached;

        /// <summary>Main thread only, from the mod's own scene callback.</summary>
        internal static void Note(string sceneName)
        {
            if (sceneName == GameScene) Volatile.Write(ref _reached, 1);
        }

        internal static bool Reached => Volatile.Read(ref _reached) != 0;
    }
}
