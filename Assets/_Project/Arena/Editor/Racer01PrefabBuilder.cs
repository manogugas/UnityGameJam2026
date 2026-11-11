using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarArena.EditorTools
{
    /// <summary>
    /// The Racer 01 body (Models/Racer01.fbx, source in BlenderSource/Racer01.blend). Every part is its own
    /// GameObject; the breakable ones get a box collider and a DetachablePart. Used by the player car
    /// (ArenaSceneBuilder) and by the standalone Prefabs/Racer01.prefab.
    /// Model frame: ground at y = 0, front +Z, driver on the left (-X).
    /// </summary>
    public static class Racer01Body
    {
        public const string Root = "Assets/_Project/Arena";
        public const string ModelPath = Root + "/Models/Racer01.fbx";
        const string MatDir = Root + "/Materials/Racer01";

        // Wheel centres in the model frame.
        public const float WheelX = 0.80f, WheelFrontZ = 1.30f, WheelRearZ = -1.25f, WheelY = 0.33f;
        // Dashboard pivot in the model frame; its local -Z faces the driver.
        public static readonly Vector3 DashPivot = new Vector3(0f, 0.74f, 0.55f);
        // Steering wheel hub and column tilt in the model frame.
        // Low enough that the driver sees both dials over the rim.
        public static readonly Vector3 SteeringHub = new Vector3(-0.38f, 0.665f, 0.03f);
        public static readonly Vector3 ColumnDir = new Vector3(0f, -0.45f, 1f);
        public static readonly Vector3 DriverEye = new Vector3(-0.38f, 1.10f, -0.35f);
        // Windscreen bottom/top centre lines.
        public static readonly Vector3 WindscreenBottom = new Vector3(0f, 0.885f, 0.74f), WindscreenTop = new Vector3(0f, 1.27f, 0.02f);

        public struct PartSpec
        {
            public string label; public float health, looseAt, jolt, mass, wobble;
            public Vector3 looseOffset, looseEuler;
        }

        static PartSpec P(string label, float health, float mass, float looseAt = 0.5f, float jolt = 0.1f, float wobble = 3f,
            Vector3 looseOffset = default, Vector3 looseEuler = default)
            => new PartSpec { label = label, health = health, mass = mass, looseAt = looseAt, jolt = jolt, wobble = wobble, looseOffset = looseOffset, looseEuler = looseEuler };

        // Parts that loosen and fly off. Everything else (chassis, cabin, roof, glass, interior, wheels) stays on.
        public static readonly Dictionary<string, PartSpec> Breakable = new Dictionary<string, PartSpec>
        {
            { "FrontBumper", P("F.bumper", 120f, 14f, 0.5f, 0.05f, 3f, new Vector3(0f, -0.06f, 0.03f), new Vector3(0f, 0f, 5f)) },
            { "RearBumper", P("R.bumper", 120f, 14f, 0.5f, 0.05f, 3f, new Vector3(0f, -0.06f, -0.03f), new Vector3(0f, 0f, -5f)) },
            { "Hood", P("Hood", 160f, 16f, 0.45f, 0.05f, 2f, new Vector3(0f, 0.04f, 0f), new Vector3(-8f, 0f, 0f)) },
            { "TrunkLid", P("Trunk", 130f, 10f, 0.45f, 0.05f, 2f, new Vector3(0f, 0.03f, 0f), new Vector3(6f, 0f, 0f)) },
            { "DoorL", P("L.door", 180f, 22f, 0.4f, 0.03f, 2f, new Vector3(-0.03f, 0f, 0f), new Vector3(0f, -10f, 0f)) },
            { "DoorR", P("R.door", 180f, 22f, 0.4f, 0.03f, 2f, new Vector3(0.03f, 0f, 0f), new Vector3(0f, 10f, 0f)) },
            { "FenderL", P("L.fender", 140f, 9f, 0.5f, 0.04f, 2f, new Vector3(-0.03f, -0.02f, 0f)) },
            { "FenderR", P("R.fender", 140f, 9f, 0.5f, 0.04f, 2f, new Vector3(0.03f, -0.02f, 0f)) },
            { "QuarterL", P("L.quarter", 150f, 10f, 0.5f, 0.04f, 2f, new Vector3(-0.03f, -0.02f, 0f)) },
            { "QuarterR", P("R.quarter", 150f, 10f, 0.5f, 0.04f, 2f, new Vector3(0.03f, -0.02f, 0f)) },
            { "Spoiler", P("Spoiler", 70f, 6f, 0.6f, 0.2f, 5f, new Vector3(0f, -0.03f, 0f), new Vector3(10f, 0f, 6f)) },
            { "MirrorL", P("L.mirror", 30f, 2f, 0.6f, 0.15f, 6f, new Vector3(0f, -0.04f, 0f), new Vector3(0f, -25f, 20f)) },
            { "MirrorR", P("R.mirror", 30f, 2f, 0.6f, 0.15f, 6f, new Vector3(0f, -0.04f, 0f), new Vector3(0f, 25f, -20f)) },
            { "Grille", P("Grille", 60f, 3f, 0.5f, 0.05f, 4f, new Vector3(0f, -0.02f, 0.02f)) },
            { "Taillights", P("Taillights", 60f, 3f, 0.5f, 0.05f, 4f, new Vector3(0f, -0.02f, -0.02f)) },
            { "HeadlightL_Outer", P("Headlight", 25f, 1f, 0.6f, 0.1f, 6f) },
            { "HeadlightL_Inner", P("Headlight", 25f, 1f, 0.6f, 0.1f, 6f) },
            { "HeadlightR_Outer", P("Headlight", 25f, 1f, 0.6f, 0.1f, 6f) },
            { "HeadlightR_Inner", P("Headlight", 25f, 1f, 0.6f, 0.1f, 6f) },
            { "FlareFL", P("Flare", 50f, 2f, 0.6f, 0.08f, 5f) },
            { "FlareFR", P("Flare", 50f, 2f, 0.6f, 0.08f, 5f) },
            { "FlareRL", P("Flare", 50f, 2f, 0.6f, 0.08f, 5f) },
            { "FlareRR", P("Flare", 50f, 2f, 0.6f, 0.08f, 5f) },
            { "HoodScoop", P("Scoop", 50f, 2f, 0.6f, 0.08f, 5f) },
        };

        public static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };

        public class Instance
        {
            public GameObject body;
            public readonly Dictionary<string, Transform> parts = new Dictionary<string, Transform>();
            public readonly List<DetachablePart> detachables = new List<DetachablePart>();
            public Transform this[string name] => parts.TryGetValue(name, out var t) ? t : null;
        }

        /// <summary>Instantiates the body under <paramref name="parent"/> at <paramref name="localPos"/> (model ground).</summary>
        /// <param name="windowsDown">Removes the door glass (player car: arm/tablet passes through the window).</param>
        /// <param name="skipBreakable">Parts that should stay plain meshes (e.g. the hood scoop used as an upgrade).</param>
        public static Instance Spawn(Transform parent, Vector3 localPos, PhysicsMaterial physMat, bool windowsDown, params string[] skipBreakable)
        {
            ConfigureImporter();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = "Body";
            body.transform.localPosition = localPos;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;

            var inst = new Instance { body = body };
            foreach (var t in body.GetComponentsInChildren<Transform>(true))
                if (t != body.transform) inst.parts[t.name] = t;

            // Front must face +Z.
            var bumper = inst["FrontBumper"];
            if (bumper != null && bumper.localPosition.z < 0f) Debug.LogError("Racer01: model faces -Z, check the FBX export axes.");

            RemapMaterials(body);

            void Reparent(string child, string newParent)
            {
                var c = inst[child]; var p = inst[newParent];
                if (c != null && p != null) c.SetParent(p, true);
            }
            Reparent("GloveboxLid", "Dashboard");
            if (windowsDown)
            {
                foreach (var g in new[] { "DoorGlassL", "DoorGlassR" })
                    if (inst[g] != null) { Object.DestroyImmediate(inst[g].gameObject); inst.parts.Remove(g); }
            }
            else
            {
                Reparent("DoorGlassL", "DoorL");
                Reparent("DoorGlassR", "DoorR");
            }

            foreach (var g in new[] { "Windscreen", "RearGlass", "QuarterGlassL", "QuarterGlassR", "DoorGlassL", "DoorGlassR" })
                if (inst[g] != null) inst[g].GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            foreach (var kv in Breakable)
            {
                if (skipBreakable.Contains(kv.Key)) continue;
                var t = inst[kv.Key];
                if (t == null) { Debug.LogWarning($"Racer01: part {kv.Key} missing in model"); continue; }
                AddBox(t, physMat);
                var dp = t.gameObject.AddComponent<DetachablePart>();
                var s = kv.Value;
                dp.displayName = s.label;
                dp.maxHealth = s.health;
                dp.looseAt = s.looseAt;
                dp.joltSensitivity = s.jolt;
                dp.detachedMass = s.mass;
                dp.wobble = s.wobble;
                dp.looseOffset = s.looseOffset;
                dp.looseEuler = s.looseEuler;
                inst.detachables.Add(dp);
            }
            return inst;
        }

        static void ConfigureImporter()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (importer == null) throw new System.Exception($"Racer01: no model at {ModelPath}");
            if (!importer.bakeAxisConversion || !importer.isReadable || importer.importAnimation || importer.importCameras || importer.importLights)
            {
                importer.bakeAxisConversion = true;
                importer.isReadable = true; // DeformableMesh dents the panels at runtime
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.SaveAndReimport();
            }
        }

        public static void AddBox(Transform t, PhysicsMaterial physMat)
        {
            if (t == null) return;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var b = mf.sharedMesh.bounds;
            var col = t.gameObject.AddComponent<BoxCollider>();
            col.center = b.center;
            col.size = Vector3.Max(b.size, Vector3.one * 0.04f);
            col.sharedMaterial = physMat;
        }

        static void RemapMaterials(GameObject body)
        {
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root + "/Materials", "Racer01");
            Material Existing(string name, Material fallback)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Materials/{name}.mat");
                return m != null ? m : fallback;
            }
            var map = new Dictionary<string, Material>
            {
                { "R01_Paint", Mat("Racer01_Paint", new Color(0.78f, 0.75f, 0.69f), 0.35f) },
                { "R01_Red", Mat("Racer01_Red", new Color(0.62f, 0.09f, 0.07f), 0.4f) },
                { "R01_Charcoal", Mat("Racer01_Charcoal", new Color(0.17f, 0.17f, 0.18f), 0.3f) },
                { "R01_Trim", Mat("Racer01_Trim", new Color(0.03f, 0.03f, 0.03f), 0.5f) },
                { "R01_Chassis", Mat("Racer01_Chassis", new Color(0.24f, 0.23f, 0.22f), 0.45f, 0.7f) },
                { "R01_Engine", Mat("Racer01_Engine", new Color(0.36f, 0.35f, 0.33f), 0.55f, 0.8f) },
                { "R01_Tyre", Mat("Racer01_Tyre", new Color(0.03f, 0.03f, 0.03f), 0.1f) },
                { "R01_Rim", Mat("Racer01_Rim", new Color(0.08f, 0.08f, 0.085f), 0.6f, 0.8f) },
                { "R01_Headlight", Mat("Racer01_Headlight", new Color(1f, 0.92f, 0.75f), 0.8f, 0f, new Color(1f, 0.85f, 0.55f) * 1.5f) },
                { "R01_Taillight", Mat("Racer01_Taillight", new Color(0.6f, 0.03f, 0.03f), 0.8f, 0f, new Color(0.7f, 0.02f, 0.02f)) },
                { "R01_Interior", Existing("CarInterior", Mat("Racer01_Interior", new Color(0.11f, 0.1f, 0.095f), 0.1f)) },
                { "R01_Seat", Mat("Racer01_Seat", new Color(0.32f, 0.07f, 0.06f), 0.25f) },
                { "R01_Bed", Existing("CarBed", Mat("Racer01_Bed", new Color(0.45f, 0.4f, 0.3f), 0.05f)) },
                { "R01_Glass", Existing("CarGlass", Mat("Racer01_Glass", new Color(0.05f, 0.06f, 0.07f), 0.9f)) },
            };
            foreach (var r in body.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (map.TryGetValue(mats[i].name, out var m)) mats[i] = m;
                    else Debug.LogWarning($"Racer01: no material mapping for {mats[i].name} on {r.name}");
                }
                r.sharedMaterials = mats;
            }
        }

        static Material Mat(string name, Color c, float smooth, float metal = 0f, Color? emission = null)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, p);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }
    }

    /// <summary>Standalone smashable Racer 01 (Prefabs/Racer01.prefab): rigidbody, damage, no driving.</summary>
    public static class Racer01PrefabBuilder
    {
        const string PrefabPath = Racer01Body.Root + "/Prefabs/Racer01.prefab";

        [MenuItem("Tools/Car Arena/Build Racer 01 Prefab")]
        public static void Build()
        {
            var physMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Racer01Body.Root + "/Materials/CarBody.physicMaterial");
            var root = new GameObject("Racer01");
            var inst = Racer01Body.Spawn(root.transform, Vector3.zero, physMat, false);

            Racer01Body.AddBox(inst["Chassis"], physMat);
            Racer01Body.AddBox(inst["CabinFrame"], physMat);
            var sw = inst["SteeringWheel"];
            if (sw != null)
            {
                sw.localPosition = Racer01Body.SteeringHub;
                sw.localRotation = Quaternion.LookRotation(Racer01Body.ColumnDir);
            }
            foreach (var w in Racer01Body.WheelNames)
                if (inst[w] != null) inst[w].gameObject.AddComponent<SphereCollider>().radius = 0.33f;

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1250f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.automaticCenterOfMass = false;
            rb.centerOfMass = new Vector3(0f, 0.45f, 0f);

            var dmg = root.AddComponent<CarDamage>();
            dmg.deformables = new DeformableMesh[0];
            dmg.parts = inst.detachables.ToArray();
            dmg.allowManualRepair = false;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"Racer01: prefab saved to {PrefabPath} with {inst.detachables.Count} breakable parts.");
        }
    }
}
