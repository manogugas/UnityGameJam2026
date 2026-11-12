using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarArena.EditorTools
{
    /// <summary>
    /// Merges the scene's static renderers into one mesh per material per map cell (so off-screen parts of the
    /// map are still culled). Colliders, DrivingSurface tags and every other component stay on the original
    /// objects; only their MeshRenderer + MeshFilter go (objects left completely empty are deleted).
    /// Skipped: non-static objects, anything a script references (it may be toggled, recoloured or moved),
    /// text, and objects already produced by this pass. Re-running it is safe.
    /// </summary>
    public static class SceneMeshMerger
    {
        const string MeshFolder = "Assets/_Project/Arena/Meshes/Merged";
        const float CellSize = 50f;

        [MenuItem("Tools/Car Arena/Merge Static Meshes")]
        public static void MergeActiveScene()
        {
            var report = Merge();
            Debug.Log("[CarArena] " + report);
        }

        public static string Merge()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var referenced = ReferencedObjects();

            // Collect (renderer, submesh) parts by material, shadow mode and map cell.
            var groups = new Dictionary<(Material, ShadowCastingMode, bool, Vector2Int), List<CombineInstance>>();
            var sources = new List<MeshRenderer>();
            int before = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    before++;
                    if (!Mergeable(mr, referenced)) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf.sharedMesh;
                    var mats = mr.sharedMaterials;
                    Vector3 c = mr.bounds.center;
                    var cell = new Vector2Int(Mathf.FloorToInt(c.x / CellSize), Mathf.FloorToInt(c.z / CellSize));
                    for (int sub = 0; sub < mesh.subMeshCount && sub < mats.Length; sub++)
                    {
                        if (mats[sub] == null) continue;
                        var key = (mats[sub], mr.shadowCastingMode, mr.receiveShadows, cell);
                        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = mf.transform.localToWorldMatrix });
                    }
                    sources.Add(mr);
                }
            if (sources.Count == 0) return "nothing to merge";

            // Build the merged objects under one root, saved as mesh assets (keeps the scene file small).
            // A re-run only adds what's new: earlier merges stay (their sources are already stripped).
            Directory.CreateDirectory(MeshFolder);
            var mergedRoot = GameObject.Find("MergedStatic") ?? new GameObject("MergedStatic");
            int n = 0, asset = 0;
            foreach (var kv in groups)
            {
                var (mat, shadows, receive, cell) = kv.Key;
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = $"Merged_{n}_{mat.name}" };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                AddLightmapUVs(mesh);
                string path;
                while (File.Exists(path = $"{MeshFolder}/Merged_{asset}.asset")) asset++;
                AssetDatabase.CreateAsset(mesh, path);
                var go = new GameObject($"{mat.name}_{cell.x}_{cell.y}");
                go.transform.SetParent(mergedRoot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = shadows;
                r.receiveShadows = receive;
                go.isStatic = true;
                n++;
            }
            mergedRoot.isStatic = true;

            // Strip the visuals off the sources; delete objects left with nothing on them.
            int removed = 0;
            foreach (var mr in sources)
            {
                var go = mr.gameObject;
                Object.DestroyImmediate(mr);
                Object.DestroyImmediate(go.GetComponent<MeshFilter>());
                removed += DeleteIfEmpty(go.transform, referenced);
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            return $"merged {sources.Count} of {before} renderers into {n}; deleted {removed} empty objects";
        }

        /// <summary>Non-overlapping UV2 so the merged meshes can take baked lightmaps (Lighting window bakes).</summary>
        public static void AddLightmapUVs(Mesh mesh)
        {
            var settings = new UnwrapParam();
            UnwrapParam.SetDefaults(out settings);
            Unwrapping.GenerateSecondaryUVSet(mesh, settings);
        }

        [MenuItem("Tools/Car Arena/Add Lightmap UVs To Merged Meshes")]
        public static void AddLightmapUVsToMergedMeshes()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
                if (mesh == null) continue; // always regenerate: merged parts can carry overlapping UV2s from their sources
                AddLightmapUVs(mesh);
                EditorUtility.SetDirty(mesh);
            }
            AssetDatabase.SaveAssets();
        }

        static bool Mergeable(MeshRenderer mr, HashSet<Object> referenced)
        {
            var go = mr.gameObject;
            if (!go.isStatic || !go.activeInHierarchy || !mr.enabled) return false;
            if (go.GetComponentInParent<TMPro.TMP_Text>(true) != null) return false;
            var mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            if (mr.transform.root.name == "MergedStatic") return false;
            // A script points at this object or one of its parents: it may toggle, recolour or move it.
            for (var t = go.transform; t != null; t = t.parent)
                if (referenced.Contains(t.gameObject) || referenced.Contains(t)) return false;
            return !referenced.Contains(mr) && !referenced.Contains(mf);
        }

        /// <summary>Every GameObject, Transform, Renderer or MeshFilter referenced from a script field in the scene.</summary>
        static HashSet<Object> ReferencedObjects()
        {
            var set = new HashSet<Object>();
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                    if (it.propertyPath == "m_GameObject" || it.propertyPath == "m_Script") continue; // every component's link to its own object
                    // A GameObject / Transform reference means the script may toggle or move that object (and its
                    // children); a Renderer / MeshFilter reference means it may recolour or swap it. Other
                    // component references (RaceTrack, ArenaGate...) don't touch the meshes, so they don't count.
                    var o = it.objectReferenceValue;
                    if (o is GameObject || o is Transform || o is Renderer || o is MeshFilter) set.Add(o);
                }
            }
            return set;
        }

        /// <summary>Delete the object (and empty parents) if nothing but a Transform is left. Returns how many went.</summary>
        static int DeleteIfEmpty(Transform t, HashSet<Object> referenced)
        {
            int count = 0;
            while (t != null && t.childCount == 0 && t.GetComponents<Component>().Length == 1 &&
                   !referenced.Contains(t.gameObject) && !referenced.Contains(t))
            {
                var parent = t.parent;
                Object.DestroyImmediate(t.gameObject);
                count++;
                t = parent;
            }
            return count;
        }
    }
}
