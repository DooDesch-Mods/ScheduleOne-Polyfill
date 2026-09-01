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

        internal override bool NeedsAScreen => true;

        internal override string What => "the trees actually get bigger";

        internal override string StandsDownBecause
            => "Bigger Trees will apply its setting and nothing will change on screen, because the terrain "
             + "has drawn no trees since 0.4.6f5.";

        private const string TerrainPath = "Hyland Point/Main Terrain";

        /// <summary>How long to give the mod once a map is up. It applies after five seconds and retries
        /// for another forty.</summary>
        private const int WaitSeconds = 75;

        private static MelonLogger.Instance _log;
        private static MelonPreferences_Entry<float> _factor;

        /// <summary>
        /// Decide here what can be decided here, and only arm the rest.
        /// </summary>
        /// <remarks>
        /// Returning true makes Fixes print <see cref="What"/> and record the fix as applied, so a size of 1
        /// has to be answered now rather than 75 seconds later - otherwise the boot line promises bigger
        /// trees to somebody who asked for none.
        ///
        /// The renderer is NOT probed here. Fixes run on the first frame FishNet's registry answers, which
        /// is in the main menu; the map, its terrain and its InstancingManager do not exist yet.
        /// </remarks>
        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            ReadPreference();

            float factor = _factor?.Value ?? 2f;
            if (factor <= 1.0001f)
            {
                log.Msg($"[fix] biggertrees-instance-scale: the size is set to {factor:0.##}, so the trees "
                      + "are left as they are. `TreeScale` in MelonPreferences changes it.");
                return false;
            }

            MelonCoroutines.Start(Mirror());
            return true;
        }

        /// <summary>
        /// Once a map is up, wait for the mod to put its number on the terrain, then size the trees.
        /// </summary>
        /// <remarks>
        /// The terrain value is the trigger and not the factor: it says the mod ran, which is what decides
        /// whether the player asked for bigger trees at all.
        ///
        /// TWO WAITS, NOT ONE. This starts in the main menu, where there is no terrain and a player may sit
        /// for as long as they like, so the countdown only begins once a terrain exists - and it begins
        /// again for the next one, because loading a second save in the same session builds a new map that
        /// nothing else would resize.
        /// </remarks>
        private static IEnumerator Mirror()
        {
            int done = 0, waitingFor = 0, waited = 0;

            while (true)
            {
                yield return new WaitForSecondsRealtime(1f);

                var terrain = FindTerrain();
                if (terrain == null) continue;

                int id = terrain.GetInstanceID();
                if (id == done) continue;
                if (id != waitingFor) { waitingFor = id; waited = 0; }

                if (ModHasApplied(terrain))
                {
                    done = id;
                    if (!Resize(_factor?.Value ?? 2f))
                        Fixes.Record("biggertrees-instance-scale", "did nothing");
                    continue;
                }

                if (++waited < WaitSeconds) continue;

                done = id;
                Fixes.Record("biggertrees-instance-scale", "did nothing");
                _log?.Msg("[fix] biggertrees-instance-scale: the terrain's tree LOD bias never moved off 1, "
                        + "so Bigger Trees did not apply and nothing was resized.");
            }
        }

        private static bool ModHasApplied(Terrain terrain)
        {
            try { return terrain.treeLODBiasMultiplier > 1.0001f; }
            catch { return false; }
        }

        /// <summary>Multiply every tree's size. False when nothing was changed.</summary>
        private static bool Resize(float factor)
        {
            InstancingManager manager = null;
            try { manager = UnityEngine.Object.FindObjectOfType<InstancingManager>(); }
            catch (Exception e) { _log?.Warning("[fix] biggertrees-instance-scale: " + e.Message); return false; }

            if (manager == null)
            {
                _log?.Warning("[fix] biggertrees-instance-scale: this build has no instanced renderer, so "
                            + "the mod's own setting works as it always did.");
                return false;
            }

            var baked = manager.BackedInstanceObjects;
            if (baked == null || baked.Count == 0) return false;

            int resized = 0, trees = 0;
            _largest = 0f;
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
                return false;
            }

            // The largest resulting size is printed, not just the factor: it is the number that would grow
            // on every reload if this ever went back to scaling its own output, and one line in a log is a
            // cheaper regression test than a memory of what the trees looked like last time.
            _log?.Msg($"[fix] biggertrees-instance-scale: {trees} tree(s) resized to {factor:0.##}x across "
                    + $"{resized} level(s) of detail, largest now {_largest:0.###}. `TreeScale` in "
                    + "MelonPreferences changes it.");
            return true;
        }

        /// <summary>
        /// The sizes as the game shipped them, per level of detail, kept the first time one is touched.
        /// </summary>
        /// <remarks>
        /// THE FACTOR IS APPLIED TO THE ORIGINAL, NEVER TO THE LAST RESULT, and the difference is the whole
        /// of a reported bug: the trees grew every time you reloaded. Reported as "die, load the last save,
        /// they are bigger again", and the arithmetic is exactly that - 2x, then 4x, then 8x.
        ///
        /// The two facts that make it happen are both easy to miss. <c>InstanceObjectData</c> is a
        /// ScriptableObject, so the texture written into it OUTLIVES the map it was written for; and loading
        /// a save builds a new terrain, whose instance id is not the one already dealt with, so the work
        /// runs again - on top of its own output.
        ///
        /// Keeping the pristine pixels rather than a "done" flag also makes the setting live: change
        /// <c>TreeScale</c> and load a save, and the size is the new multiple of the original rather than a
        /// multiple of whatever it happened to be.
        /// </remarks>
        private static readonly Dictionary<int, Color[]> Original = new();

        /// <summary>The biggest size written in the last pass, so a log line can show it did not creep.</summary>
        private static float _largest;

        /// <summary>
        /// Set the size of every instance in one level of detail. Returns how many were touched.
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

                // First sight of this level of detail is the one that defines its sizes. Every later run
                // starts from that, so the result is the same whether a save is loaded once or five times.
                int key = data.GetInstanceID();
                if (!Original.TryGetValue(key, out var pristine) || pristine.Length != pixels.Length)
                {
                    // Copied element by element: GetPixels hands back an interop array, which is a wrapper
                    // around native memory rather than a managed one, so it has no Clone of its own.
                    pristine = new Color[pixels.Length];
                    for (int i = 0; i < pixels.Length; i++) pristine[i] = pixels[i];
                    Original[key] = pristine;
                }

                int occupied = 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    var pixel = pristine[i];
                    if (pixel.a <= 0f) { pixels[i] = pixel; continue; }
                    occupied++;
                    float sized = pixel.a * factor;
                    if (sized > _largest) _largest = sized;
                    pixels[i] = new Color(pixel.r, pixel.g, pixel.b, sized);
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
                              "Only with the Bigger Trees mod installed. 1 leaves them alone.");
            }
            catch { }
        }
    }
}
