using HarmonyLib;
using Il2CppScheduleOne.Instancing;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Take the trees a mod cleared out of the renderer that actually draws them.
    /// </summary>
    /// <remarks>
    /// Until 0.4.5f2 the map's trees were Unity terrain instances: one entry in
    /// <c>TerrainData.treeInstances</c> per tree, drawn by the terrain and collided with by its
    /// TerrainCollider. Removing an entry removed the tree, visibly and physically, and that is what every
    /// building mod was written against.
    ///
    /// 0.4.6f5 added <c>ScheduleOne.Instancing.InstancingManager</c> - a type that does not exist in
    /// 0.4.5f2 at all. It draws the trees itself, from a BAKED TEXTURE of positions, straight to the GPU:
    /// <code>
    /// _instancingShader.SetTexture(_kernelID, "PositionData", data.PositionData);
    /// _instancingShader.Dispatch(...);
    /// Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, args);
    /// </code>
    /// Nothing in that path reads <c>treeInstances</c>. So a mod clearing its building site still removes
    /// the collision, because that is the terrain's, and the tree stays on screen because that is not.
    /// Reported as "I walk through it and still see it", which is exactly the shape of the split.
    ///
    /// This closes the other half: after the mod has cleared an area, the same area is cleared out of the
    /// position texture. The texture is copied through the GPU rather than read directly, because a baked
    /// asset is usually not CPU-readable, and the copy is assigned back - the manager reads the property
    /// fresh every frame, so it picks the new one up without being told.
    ///
    /// It refuses rather than guesses. The decode is checked against the map before anything is written:
    /// if the values do not read as world coordinates, it says so and leaves the texture alone.
    /// </remarks>
    internal sealed class S1MapiInstancedTrees : Fix
    {
        internal override string Id => "s1mapi-instanced-trees";
        internal override string Mod => "S1MAPI";
        internal override string ModVersions => "*";
        internal override string GameVersions => "0.4.6*";
        internal override string What => "trees cleared for a building stop being drawn, not only walked through";

        internal override string StandsDownBecause
            => "Trees a mod cleared for its building may be drawn again. The renderer that draws them arrived in 0.4.6f5 and nobody has checked whether it still works this way.";

        private static MelonLogger.Instance _log;

        /// <summary>Where the map is. A decode that puts trees outside this is a decode we do not have.</summary>
        private const float MapExtent = 1200f;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;

            Type clearer = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { clearer = assembly.GetType("S1MAPI.Building.Structural.TerrainClearer", false); } catch { }
                if (clearer != null) break;
            }
            if (clearer == null) { log.Warning("[fix] s1mapi-instanced-trees: TerrainClearer is not where it was."); return false; }

            var clearArea = AccessTools.Method(clearer, "ClearArea");
            if (clearArea == null) { log.Warning("[fix] s1mapi-instanced-trees: ClearArea is gone."); return false; }

            new HarmonyLib.Harmony("doodesch.polyfill.fixes").Patch(
                clearArea, postfix: new HarmonyMethod(typeof(S1MapiInstancedTrees), nameof(ClearAreaPostfix)));
            return true;
        }

        /// <summary>Harmony hands the original's own argument over by name.</summary>
        private static void ClearAreaPostfix(Bounds bounds) => Clear(bounds);

        private static void Clear(Bounds bounds)
        {
            InstancingManager manager;
            try { manager = UnityEngine.Object.FindObjectOfType<InstancingManager>(); }
            catch (Exception e) { _log?.Warning("[fix] s1mapi-instanced-trees: " + e.Message); return; }
            if (manager == null) return;

            var baked = manager.BackedInstanceObjects;
            if (baked == null || baked.Count == 0) return;

            int removed = 0;
            for (int i = 0; i < baked.Count; i++)
            {
                var data = baked[i];
                if (data == null || data.PositionData == null) continue;
                removed += ClearOne(data, bounds);
            }

            if (removed > 0) _log?.Msg($"[fix] s1mapi-instanced-trees: took {removed} drawn tree(s) out of the "
                                     + "renderer that the terrain clear only removed the collision of.");
        }

        private static int ClearOne(InstanceObjectData data, Bounds bounds)
        {
            var source = data.PositionData;
            int width = source.width, height = source.height;
            if (width <= 0 || height <= 0) return 0;

            Texture2D copy;
            try { copy = Readable(source, width, height); }
            catch (Exception e) { _log?.Warning("[fix] s1mapi-instanced-trees: could not read the positions: " + e.Message); return 0; }
            if (copy == null) return 0;

            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color> pixels;
            try { pixels = copy.GetPixels(); }
            catch (Exception e) { _log?.Warning("[fix] s1mapi-instanced-trees: could not read the positions: " + e.Message); return 0; }

            Vector3 offset = data.PositionOffset;

            // Prove the decode before trusting it. A baked position texture holds world coordinates; a
            // packed one holds 0..1 and would need a scale we do not have. Rather than write nonsense into
            // the map, measure first and stand down if the numbers are not coordinates.
            float low = float.MaxValue, high = float.MinValue;
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.a <= 0f) continue;                       // an empty slot
                low = Mathf.Min(low, Mathf.Min(pixel.r, pixel.b));
                high = Mathf.Max(high, Mathf.Max(pixel.r, pixel.b));
            }
            if (low > high) return 0;                              // nothing occupied

            if (high <= 1.001f && low >= -0.001f)
            {
                _log?.Warning("[fix] s1mapi-instanced-trees: the positions are packed 0..1, not world "
                            + $"coordinates ({low:F3}..{high:F3}), and the scale is not in the game's code. "
                            + "Left alone.");
                return 0;
            }
            if (high > MapExtent || low < -MapExtent)
            {
                _log?.Warning($"[fix] s1mapi-instanced-trees: positions read {low:F1}..{high:F1}, which is not "
                            + "this map. Left alone.");
                return 0;
            }

            int removed = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var pixel = pixels[i];
                if (pixel.a <= 0f) continue;

                var where = new Vector3(pixel.r + offset.x, pixel.g + offset.y, pixel.b + offset.z);
                // Height is ignored: a building sits on the ground and the bounds are its room, while a
                // tree's stored height is its base. Matching on the footprint is what the terrain clear
                // does too.
                if (where.x < bounds.min.x || where.x > bounds.max.x) continue;
                if (where.z < bounds.min.z || where.z > bounds.max.z) continue;

                // Emptied rather than moved: the shader culls on the alpha slot the same way the manager
                // counts occupancy, so a cleared slot costs nothing per frame.
                pixels[i] = new Color(0f, 0f, 0f, 0f);
                removed++;
            }

            if (removed == 0) return 0;

            try
            {
                copy.SetPixels(pixels);
                copy.Apply(false, false);
                data.PositionData = copy;                          // read fresh every frame by the manager
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] s1mapi-instanced-trees: could not write the positions back: " + e.Message);
                return 0;
            }
            return removed;
        }

        /// <summary>
        /// A CPU-readable copy, taken through the GPU.
        /// </summary>
        /// <remarks>
        /// A baked texture asset ships with Read/Write off, so <c>GetPixels</c> on the original throws.
        /// Blitting it into a render target and reading that back works regardless, and it is also what
        /// keeps the original asset untouched - what gets modified and handed back is a copy.
        /// </remarks>
        private static Texture2D Readable(Texture2D source, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat,
                                                    RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;

                var copy = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
                copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                copy.Apply(false, false);
                return copy;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
