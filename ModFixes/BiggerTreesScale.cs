using System.Collections;
using Il2CppScheduleOne.Instancing;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Make the trees bigger, which is what Bigger Trees promises and cannot do any more.
    /// </summary>
    /// <remarks>
    /// The mod does one thing:
    /// <code>
    /// terrain.treeLODBiasMultiplier = 6.7f;   // Map/Hyland Point/Main Terrain
    /// </code>
    /// It still runs and still reports success on 0.4.6f12. Nothing happens, and measuring the terrain says
    /// why: <c>treeDistance = 0</c>. The 2621 tree instances are still on it - which is why clearing them
    /// still removes their collision - but the terrain has been told to draw them out to a distance of
    /// zero. Since 0.4.6f5 <c>InstancingManager</c> draws them instead, from two baked textures.
    ///
    /// That renderer has no scale parameter anywhere in its code, and its three meshes are not readable, so
    /// for a while this looked impossible. It is not: the scale is IN the position texture. One pixel per
    /// tree, rgb the world position and ALPHA THE SIZE. Measured on this build: 2621 occupied pixels with
    /// alpha between 0.468 and 1.200 - a flag would not vary, and the tree-clearing module has been reading
    /// <c>a &lt;= 0</c> as "no tree here" since 0.4.1, which is what a size of zero means.
    ///
    /// So this multiplies that alpha. The manager re-reads the texture off the ScriptableObject every
    /// frame, so assigning a copy is enough and nothing has to be told about it. All three LOD entries get
    /// the same treatment or a tree would change size as you walk towards it.
    ///
    /// THE FACTOR IS THE PLAYER'S, and that is deliberate. 6.7 is a level-of-detail bias, not a size, and
    /// there is no honest way to turn one into the other - so it is a setting with a modest default rather
    /// than a number invented here and presented as the mod's own.
    /// </remarks>
    internal sealed class BiggerTreesScale : Fix
    {
        internal override string Id => "biggertrees-instance-scale";
        internal override string Mod => "BiggerTrees";
        internal override string ModVersions => "*";

        /// <summary>0.4.6f5 is the build the instanced renderer arrived in. Below it the terrain draws its
        /// own trees and the mod works as it always did.</summary>
        internal override string GameVersions => ">=0.4.6f5";

        internal override string What => "the trees actually get bigger";

        internal override string StandsDownBecause
            => "Bigger Trees will apply its setting and nothing will change on screen, because the terrain "
             + "has drawn no trees since 0.4.6f5.";

        private const string TerrainPath = "Hyland Point/Main Terrain";

        /// <summary>The mod applies after five seconds and retries for another forty.</summary>
        private const int WaitSeconds = 75;

        private static MelonLogger.Instance _log;
        private static MelonPreferences_Entry<float> _factor;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            ReadPreference();
            MelonCoroutines.Start(Mirror());
            return true;
        }

        /// <summary>
        /// Wait until the mod has put its number on the terrain, then size the trees.
        /// </summary>
        /// <remarks>
        /// The terrain value is the trigger and not the factor: it says the mod ran, which is what decides
        /// whether the player asked for bigger trees at all.
        /// </remarks>
        private static IEnumerator Mirror()
        {
            for (int second = 0; second < WaitSeconds; second++)
            {
                yield return new WaitForSecondsRealtime(1f);
                if (!ModHasApplied()) continue;

                Resize(_factor?.Value ?? 2f);
                yield break;
            }

            _log?.Msg("[fix] biggertrees-instance-scale: the terrain's tree LOD bias never moved off 1, so "
                    + "Bigger Trees did not apply and nothing was resized.");
        }

        private static bool ModHasApplied()
        {
            try
            {
                var terrain = FindTerrain();
                return terrain != null && terrain.treeLODBiasMultiplier > 1.0001f;
            }
            catch { return false; }
        }

        private static void Resize(float factor)
        {
            if (factor <= 1.0001f)
            {
                _log?.Msg($"[fix] biggertrees-instance-scale: the size is set to {factor:0.##}, so the trees "
                        + "were left as they are. Change TreeScale in MelonPreferences.");
                return;
            }

            InstancingManager manager = null;
            try { manager = UnityEngine.Object.FindObjectOfType<InstancingManager>(); }
            catch (Exception e) { _log?.Warning("[fix] biggertrees-instance-scale: " + e.Message); return; }

            if (manager == null)
            {
                _log?.Warning("[fix] biggertrees-instance-scale: this build has no instanced renderer, so "
                            + "the mod's own setting works as it always did.");
                return;
            }

            var baked = manager.BackedInstanceObjects;
            if (baked == null || baked.Count == 0) return;

            int resized = 0, trees = 0;
            for (int i = 0; i < baked.Count; i++)
            {
                int count = ScaleOne(baked[i], factor);
                if (count <= 0) continue;
                resized++;
                trees = Mathf.Max(trees, count);
            }

            if (resized == 0)
            {
                _log?.Warning("[fix] biggertrees-instance-scale: nothing carried a size to change.");
                return;
            }

            _log?.Msg($"[fix] biggertrees-instance-scale: {trees} tree(s) resized to {factor:0.##}x across "
                    + $"{resized} level(s) of detail. `TreeScale` in MelonPreferences changes it.");
        }

        /// <summary>
        /// Multiply the size of every instance in one level of detail. Returns how many were touched.
        /// </summary>
        /// <remarks>
        /// Untouched pixels are left exactly as they are rather than written back at zero: an empty slot is
        /// the absence of a tree, and multiplying nothing by anything must stay nothing.
        /// </remarks>
        private static int ScaleOne(InstanceObjectData data, float factor)
        {
            var source = data?.PositionData;
            if (source == null) return 0;

            int width = source.width, height = source.height;
            if (width <= 0 || height <= 0) return 0;

            try
            {
                var copy = Readable(source, width, height);
                var pixels = copy.GetPixels();

                int occupied = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pixels[i];
                    if (pixel.a <= 0f) continue;
                    occupied++;
                    pixels[i] = new Color(pixel.r, pixel.g, pixel.b, pixel.a * factor);
                }
                if (occupied == 0) return 0;

                copy.SetPixels(pixels);
                copy.Apply(false, false);
                data.PositionData = copy;
                return occupied;
            }
            catch (Exception e)
            {
                _log?.Warning("[fix] biggertrees-instance-scale: could not resize a level of detail: "
                            + e.Message);
                return 0;
            }
        }

        /// <summary>A CPU-readable copy of a texture that is not readable, taken through the GPU.</summary>
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

        private static Terrain FindTerrain()
        {
            try
            {
                var map = GameObject.Find("Map");
                var found = map?.transform.Find(TerrainPath);
                return found == null ? null : found.GetComponent<Terrain>();
            }
            catch { return null; }
        }

        private static void ReadPreference()
        {
            if (_factor != null) return;
            try
            {
                var category = MelonPreferences.GetCategory("Polyfill")
                               ?? MelonPreferences.CreateCategory("Polyfill");
                _factor = category.GetEntry<float>("TreeScale")
                          ?? category.CreateEntry("TreeScale", 2f, "How much bigger the trees get",
                              "Only with the Bigger Trees mod installed. 1 leaves them alone. The mod's own "
                              + "number is a level-of-detail setting rather than a size, so this is yours "
                              + "to pick.");
            }
            catch { }
        }
    }
}
