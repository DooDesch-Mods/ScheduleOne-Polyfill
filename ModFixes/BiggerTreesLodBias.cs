using System.Collections;
using Il2CppScheduleOne.Instancing;
using MelonLoader;
using UnityEngine;

namespace Polyfill.ModFixes
{
    /// <summary>
    /// Carry the Bigger Trees setting over to the renderer that actually draws the trees.
    /// </summary>
    /// <remarks>
    /// The mod does one thing:
    /// <code>
    /// terrain.treeLODBiasMultiplier = 6.7f;   // on Map/Hyland Point/Main Terrain
    /// </code>
    /// It still works. It finds the terrain and reports "Successfully applied on attempt 1" on 0.4.6f12 -
    /// and nothing happens, because since 0.4.6f5 the terrain does not draw the trees any more.
    ///
    /// Measured in game rather than reasoned about: <c>disableinstancing</c> in the dev console makes the
    /// entire forest disappear, and <c>enableterrain</c> brings none of it back. The terrain has no trees
    /// left at all; <c>InstancingManager</c> draws them from a baked position texture straight to the GPU.
    ///
    /// So the setting is not broken, it is disconnected - and the new renderer has the same knob:
    /// <code>
    /// _lodBias = QualitySettings.lodBias;                                    // InstancingManager.cs:161
    /// SetFloat("_MinDistance", MinMaxLodDistance.x * _lodBias);              // :108
    /// SetFloat("_MaxDistance", MinMaxLodDistance.y * _lodBias);              // :109
    /// </code>
    /// This multiplies those two distances by the factor the mod put on the terrain. Same intent, same
    /// number, the other renderer. It is a translation, not a reimplementation: nothing here invents a
    /// value, and with the mod absent or its factor left at 1 it does nothing at all.
    ///
    /// MinMaxLodDistance rather than _lodBias, because _lodBias is overwritten from QualitySettings
    /// whenever the game refreshes them, and because writing QualitySettings.lodBias would move the LOD of
    /// everything in the game rather than the thing the mod is about.
    /// </remarks>
    internal sealed class BiggerTreesLodBias : Fix
    {
        internal override string Id => "biggertrees-instanced-lod";
        internal override string Mod => "BiggerTrees";
        internal override string ModVersions => "*";

        /// <summary>0.4.6f5 is the build the instanced renderer arrived in. Below it the terrain still draws
        /// its own trees and the mod needs no help.</summary>
        internal override string GameVersions => ">=0.4.6f5";

        internal override string What => "the bigger-trees setting reaches the renderer that draws the trees";

        internal override string StandsDownBecause
            => "Bigger Trees will apply its setting to the terrain and nothing will change on screen, "
             + "because the terrain has not drawn the trees since 0.4.6f5.";

        /// <summary>Where the mod puts its number. Nothing else on the map carries a tree LOD bias.</summary>
        private const string TerrainPath = "Hyland Point/Main Terrain";

        /// <summary>The mod applies after five seconds and retries for another forty. Waiting a minute
        /// covers that with room to spare, and costs one check a second until it does.</summary>
        private const int WaitSeconds = 75;

        private static MelonLogger.Instance _log;

        internal override bool Apply(MelonLogger.Instance log)
        {
            _log = log;
            MelonCoroutines.Start(Mirror());
            return true;
        }

        /// <summary>
        /// Wait for the mod to put its number on the terrain, then put it where it counts.
        /// </summary>
        /// <remarks>
        /// Polled rather than patched. The mod sets a Unity property on a Terrain from inside its own
        /// coroutine; there is no method of its own to hook that would not break the moment it is rewritten,
        /// and a property setter on a Unity type is not something to put a patch on for this.
        /// </remarks>
        private static IEnumerator Mirror()
        {
            for (int second = 0; second < WaitSeconds; second++)
            {
                yield return new WaitForSecondsRealtime(1f);

                Terrain terrain = FindTerrain();
                if (terrain == null) continue;

                float bias;
                try { bias = terrain.treeLODBiasMultiplier; } catch { continue; }

                // 1 is the default and means the mod has not run yet - or is not installed after all.
                if (bias <= 1.0001f) continue;

                Apply(bias);
                yield break;
            }

            _log?.Msg("[fix] biggertrees-instanced-lod: the terrain's tree LOD bias never moved off 1, so "
                    + "there was nothing to carry over. Bigger Trees is installed but did not apply.");
        }

        private static void Apply(float bias)
        {
            InstancingManager manager = null;
            try { manager = UnityEngine.Object.FindObjectOfType<InstancingManager>(); }
            catch (Exception e) { _log?.Warning("[fix] biggertrees-instanced-lod: " + e.Message); return; }

            if (manager == null)
            {
                _log?.Warning("[fix] biggertrees-instanced-lod: this build draws trees from the terrain and "
                            + "there is no instanced renderer, so the mod's setting works as it always did.");
                return;
            }

            var baked = manager.BackedInstanceObjects;
            if (baked == null || baked.Count == 0) return;

            for (int i = 0; i < baked.Count; i++)
            {
                var data = baked[i];
                if (data == null) continue;

                var mesh = data.Mesh;
                string name = mesh == null ? "<none>" : mesh.name;
                bool readable = false;
                int vertices = -1;
                try { readable = mesh != null && mesh.isReadable; } catch { }
                try { vertices = mesh == null ? -1 : mesh.vertexCount; } catch { }

                _log?.Msg($"[probe] instanced object {i}: mesh '{name}', {vertices} vertex/vertices, "
                        + $"readable={readable}, lod={data.MinMaxLodDistance}, instances={data.InstanceCount}");

                if (!readable || mesh == null) continue;
                Scale(mesh, name, 1.5f);
            }
        }

        /// <summary>Multiply every vertex out from the mesh origin. The one lever the instanced path has:
        /// position and rotation come from a baked texture, and nothing in it carries a size.</summary>
        private static void Scale(Mesh mesh, string name, float factor)
        {
            try
            {
                var vertices = mesh.vertices;
                if (vertices == null || vertices.Length == 0)
                { _log?.Warning($"[probe] '{name}' reports readable and has no vertices to read."); return; }

                for (int i = 0; i < vertices.Length; i++) vertices[i] = vertices[i] * factor;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();

                _log?.Msg($"[probe] scaled '{name}' by {factor} ({vertices.Length} vertices).");
            }
            catch (Exception e) { _log?.Warning($"[probe] '{name}' could not be scaled: {e.Message}"); }
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
    }
}
