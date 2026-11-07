using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using TMPro;

namespace CarArena.EditorTools
{
    /// <summary>Builds the greybox arena test scene and the player car prefab. Re-run any time.</summary>
    public static class ArenaSceneBuilder
    {
        const string Root = "Assets/_Project/Arena";
        const string MatDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";
        const string SurfDir = Root + "/Surfaces";
        const string PrefabPath = Root + "/Prefabs/PlayerCar.prefab";
        const string ScenePath = Root + "/Scenes/ArenaTest.unity";
        const string InputPath = Root + "/Input/CarControls.inputactions";

        const float SandRadius = 55f;
        const float WallRadius = 77f;
        const float BankInner = 64f;
        const float BankHeight = 3.6f;

        static Material mSand, mAsphalt, mBank, mConcrete, mRamp, mObstacle, mTyre, mCrate, mWreck;
        static Material mBody, mPanel, mTrim, mInterior, mSeat, mGlass, mCrack, mMirror, mGauge, mBed, mGaugeFace, mNeedle, mRedline;
        static Mesh unitCube;
        static PhysicsMaterial carPhysMat;

        [MenuItem("Tools/Car Arena/Build Test Scene")]
        public static void Build()
        {
            foreach (var d in new[] { MatDir, MeshDir, SurfDir, Root + "/Prefabs", Root + "/Scenes" })
                Directory.CreateDirectory(d);
            AssetDatabase.Refresh();

            CreateMaterials();
            var asphalt = Surface("Asphalt", s =>
            {
                s.displayName = "Asphalt"; s.grip = 1.2f; s.slideGrip = 0.88f; s.peakSlipRatio = 0.1f;
                s.peakSlipAngle = 7f; s.rollingResistance = 0.015f; s.roughness = 0.003f; s.loose = false;
            });
            var sand = Surface("Sand", s =>
            {
                s.displayName = "Sand"; s.grip = 0.85f; s.slideGrip = 0.92f; s.peakSlipRatio = 0.16f;
                s.peakSlipAngle = 11f; s.rollingResistance = 0.06f; s.roughness = 0.025f; s.loose = true;
            });
            carPhysMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(MatDir + "/CarBody.physicMaterial");
            if (carPhysMat == null)
            {
                carPhysMat = new PhysicsMaterial("CarBody")
                {
                    dynamicFriction = 0.15f, staticFriction = 0.2f, bounciness = 0.05f,
                    frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum
                };
                AssetDatabase.CreateAsset(carPhysMat, MatDir + "/CarBody.physicMaterial");
            }
            unitCube = SaveMesh(ProcMesh.Box(Vector3.one, Vector3Int.one, default, "UnitCube"), "UnitCube");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildEnvironment();
            BuildArena(asphalt, sand);
            var car = BuildCar(asphalt);
            car.transform.SetPositionAndRotation(new Vector3(0f, 0.9f, -42f), Quaternion.identity);
            PrefabUtility.SaveAsPrefabAssetAndConnect(car, PrefabPath, InteractionMode.AutomatedAction);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[CarArena] Built " + ScenePath);
        }

        // ---------------------------------------------------------------- assets

        static void CreateMaterials()
        {
            mSand = Mat("Sand", new Color(0.78f, 0.68f, 0.5f), 0.05f);
            mAsphalt = Mat("Asphalt", new Color(0.22f, 0.22f, 0.24f), 0.15f);
            mBank = Mat("AsphaltBank", new Color(0.3f, 0.3f, 0.33f), 0.15f);
            mConcrete = Mat("Concrete", new Color(0.62f, 0.62f, 0.6f), 0.1f);
            mRamp = Mat("Ramp", new Color(0.85f, 0.55f, 0.2f), 0.2f);
            mObstacle = Mat("Obstacle", new Color(0.75f, 0.25f, 0.2f), 0.2f);
            mTyre = Mat("Tyre", new Color(0.08f, 0.08f, 0.08f), 0.3f);
            mCrate = Mat("Crate", new Color(0.55f, 0.42f, 0.25f), 0.1f);
            mWreck = Mat("Wreck", new Color(0.35f, 0.4f, 0.45f), 0.3f, 0.3f);
            mBody = Mat("CarBody", new Color(0.55f, 0.6f, 0.66f), 0.45f, 0.2f);
            mPanel = Mat("CarPanel", new Color(0.5f, 0.55f, 0.6f), 0.45f, 0.2f);
            mTrim = Mat("CarTrim", new Color(0.12f, 0.12f, 0.13f), 0.3f);
            mInterior = Mat("CarInterior", new Color(0.2f, 0.19f, 0.18f), 0.15f);
            mSeat = Mat("CarSeat", new Color(0.35f, 0.25f, 0.2f), 0.1f);
            mMirror = Mat("CarMirror", new Color(0.85f, 0.9f, 0.95f), 0.95f, 0.8f);
            mGauge = Mat("CarGauge", new Color(0.9f, 0.85f, 0.7f), 0.5f);
            mBed = Mat("CarBed", new Color(0.55f, 0.6f, 0.45f), 0.05f);
            mGaugeFace = Mat("GaugeFace", new Color(0.04f, 0.04f, 0.045f), 0.6f);
            mNeedle = Mat("GaugeNeedle", new Color(1f, 0.42f, 0.08f), 0.4f);
            mNeedle.EnableKeyword("_EMISSION");
            mNeedle.SetColor("_EmissionColor", new Color(0.6f, 0.2f, 0.02f));
            mRedline = Mat("GaugeRedline", new Color(0.85f, 0.1f, 0.08f), 0.4f);

            mGlass = Mat("CarGlass", new Color(0.7f, 0.85f, 0.95f, 0.18f), 0.95f);
            mGlass.SetFloat("_Surface", 1f);
            mGlass.SetFloat("_Blend", 0f);
            mGlass.SetFloat("_Cull", 0f);
            mGlass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mGlass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mGlass.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mGlass.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mGlass.SetFloat("_ZWrite", 0f);
            mGlass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mGlass.SetOverrideTag("RenderType", "Transparent");
            mGlass.renderQueue = (int)RenderQueue.Transparent;
            mGlass.SetShaderPassEnabled("ShadowCaster", false);

            string crackPath = MatDir + "/Crack.mat";
            mCrack = AssetDatabase.LoadAssetAtPath<Material>(crackPath);
            if (mCrack == null)
            {
                mCrack = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mCrack, crackPath);
            }
            mCrack.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.05f));
            EditorUtility.SetDirty(mCrack);
        }

        static Material Mat(string name, Color c, float smooth, float metal = 0f)
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
            EditorUtility.SetDirty(m);
            return m;
        }

        static SurfaceProfile Surface(string name, System.Action<SurfaceProfile> init)
        {
            string p = $"{SurfDir}/{name}.asset";
            var s = AssetDatabase.LoadAssetAtPath<SurfaceProfile>(p);
            if (s != null) return s; // keep hand-tuned values
            s = ScriptableObject.CreateInstance<SurfaceProfile>();
            init(s);
            AssetDatabase.CreateAsset(s, p);
            return s;
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string p = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            mesh.name = name;
            AssetDatabase.CreateAsset(mesh, p);
            return mesh;
        }

        // ---------------------------------------------------------------- helpers

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale,
            Material mat, bool keepCollider = false, Quaternion? localRot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject MeshObj(string name, Transform parent, Vector3 localPos, Mesh mesh, Material mat, Quaternion? localRot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Empty(string name, Transform parent, Vector3 localPos, Quaternion? localRot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            return go;
        }

        /// <summary>Thin box between two local points.</summary>
        static GameObject Strut(string name, Transform parent, Vector3 a, Vector3 b, float thickness, Material mat)
        {
            Vector3 d = b - a;
            return Prim(PrimitiveType.Cube, name, parent, (a + b) * 0.5f, new Vector3(thickness, thickness, d.magnitude), mat,
                false, Quaternion.LookRotation(d));
        }

        static void Tag(GameObject go, SurfaceProfile s) => go.AddComponent<DrivingSurface>().profile = s;

        // ---------------------------------------------------------------- environment

        static void BuildEnvironment()
        {
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.5f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 80f;
            RenderSettings.fogEndDistance = 320f;
            RenderSettings.fogColor = new Color(0.75f, 0.78f, 0.82f);
        }

        // ---------------------------------------------------------------- arena

        static void BuildArena(SurfaceProfile asphalt, SurfaceProfile sand)
        {
            var arena = new GameObject("Arena").transform;

            var sandGo = MeshObj("SandFloor", arena, Vector3.zero, SaveMesh(ProcMesh.Disk(SandRadius, 96), "SandDisk"), mSand);
            sandGo.AddComponent<MeshCollider>();
            Tag(sandGo, sand);

            var ringGo = MeshObj("AsphaltRing", arena, Vector3.zero,
                SaveMesh(ProcMesh.Annulus(SandRadius, WallRadius + 1.5f, 4, 160, 0f, 360f, null), "AsphaltRing"), mAsphalt);
            ringGo.AddComponent<MeshCollider>();
            Tag(ringGo, asphalt);

            // Banked wall ramp around half the ring (angles 0..180 => the +Z half).
            var bankMesh = ProcMesh.Annulus(BankInner, WallRadius - 0.9f, 16, 120, 0f, 180f, (rt, at) =>
            {
                float taper = Mathf.SmoothStep(0f, 1f, at / 0.1f) * Mathf.SmoothStep(0f, 1f, (1f - at) / 0.1f);
                return 0.02f + BankHeight * Mathf.Pow(rt, 1.7f) * taper;
            }, "BankedRing");
            var bankGo = MeshObj("BankedRamp", arena, Vector3.zero, SaveMesh(bankMesh, "BankedRing"), mBank);
            bankGo.AddComponent<MeshCollider>();
            Tag(bankGo, asphalt);

            // Ring wall
            var wall = Empty("RingWall", arena, Vector3.zero).transform;
            int segs = 80;
            float segLen = 2f * Mathf.PI * (WallRadius + 1f) / segs + 0.3f;
            for (int i = 0; i < segs; i++)
            {
                float a = (i + 0.5f) / segs * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Prim(PrimitiveType.Cube, "Wall" + i, wall, dir * (WallRadius + 1f) + Vector3.up * 3f,
                    new Vector3(segLen, 6f, 2f), mConcrete, true, Quaternion.LookRotation(-dir));
            }

            // Central ramps
            var ramps = Empty("Ramps", arena, Vector3.zero).transform;
            var kicker = SaveMesh(ProcMesh.Wedge(6f, 7f, 1.8f), "Kicker");
            var tableUp = SaveMesh(ProcMesh.Wedge(8f, 6f, 1.5f), "TableRamp");
            var small = SaveMesh(ProcMesh.Wedge(5f, 5f, 1.1f), "SmallKicker");
            Ramp("Kicker", ramps, kicker, new Vector3(0f, 0f, -28f), 0f);
            Ramp("TableUp", ramps, tableUp, new Vector3(0f, 0f, -9f), 0f);
            Prim(PrimitiveType.Cube, "TableTop", ramps, new Vector3(0f, 0.75f, 1f), new Vector3(8f, 1.5f, 8f), mRamp, true);
            Ramp("TableDown", ramps, tableUp, new Vector3(0f, 0f, 11f), 180f);
            Ramp("SideKickerE", ramps, kicker, new Vector3(30f, 0f, 8f), -60f);
            Ramp("SideKickerW", ramps, kicker, new Vector3(-30f, 0f, 8f), 60f);
            Ramp("BackKicker", ramps, kicker, new Vector3(0f, 0f, 38f), 180f);
            Ramp("SpineA", ramps, small, new Vector3(26f, 0f, -26f), 45f);
            Ramp("SpineB", ramps, small, new Vector3(26f + 10f * 0.7071f, 0f, -26f + 10f * 0.7071f), 225f);
            Ramp("SpineC", ramps, small, new Vector3(-30f, 0f, -22f), -30f);

            // Static obstacles
            var obstacles = Empty("Obstacles", arena, Vector3.zero).transform;
            Vector3[] blocks =
            {
                new Vector3(15, 0, -15), new Vector3(-15, 0, -15), new Vector3(18, 0, 26), new Vector3(-18, 0, 26),
                new Vector3(42, 0, -4), new Vector3(-42, 0, -4), new Vector3(10, 0, 46), new Vector3(-12, 0, 47),
            };
            for (int i = 0; i < blocks.Length; i++)
                Prim(PrimitiveType.Cube, "Block" + i, obstacles, blocks[i] + Vector3.up * 0.6f, new Vector3(3f, 1.2f, 1.2f),
                    mConcrete, true, Quaternion.Euler(0f, i * 37f, 0f));
            Vector3[] pillars = { new Vector3(36, 0, -36), new Vector3(-36, 0, -36), new Vector3(36, 0, 32), new Vector3(-36, 0, 32) };
            for (int i = 0; i < pillars.Length; i++)
                Prim(PrimitiveType.Cylinder, "Pillar" + i, obstacles, pillars[i] + Vector3.up * 2f, new Vector3(3f, 2f, 3f), mObstacle, true);

            // Slalom of barriers on the flat half of the asphalt ring (-Z half).
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Lerp(200f, 340f, i / 6f) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? 60f : 71f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Prim(PrimitiveType.Cube, "Barrier" + i, obstacles, dir * r + Vector3.up * 0.5f, new Vector3(5f, 1f, 0.7f),
                    mConcrete, true, Quaternion.LookRotation(dir));
            }

            // Movable props
            var props = Empty("Props", arena, Vector3.zero).transform;
            Vector3[] tyres = { new Vector3(8, 0, -35), new Vector3(-8, 0, -35), new Vector3(44, 0, -16), new Vector3(-44, 0, -16), new Vector3(6, 0, 20), new Vector3(-6, 0, 20) };
            for (int i = 0; i < tyres.Length; i++)
                for (int k = 0; k < 3; k++)
                {
                    var t = Prim(PrimitiveType.Cylinder, $"Tyre{i}_{k}", props, tyres[i] + Vector3.up * (0.15f + k * 0.3f),
                        new Vector3(0.75f, 0.15f, 0.75f), mTyre, true);
                    Object.DestroyImmediate(t.GetComponent<Collider>());
                    var mc = t.AddComponent<MeshCollider>();
                    mc.convex = true;
                    t.AddComponent<Rigidbody>().mass = 18f;
                }
            int n = 0;
            for (int row = 0; row < 3; row++)
                for (int c = 0; c < 3 - row; c++)
                {
                    var crate = Prim(PrimitiveType.Cube, "Crate" + n++, props,
                        new Vector3(-22f + (c - (2 - row) * 0.5f) * 1.05f, 0.5f + row * 1.0f, -8f), Vector3.one, mCrate, true);
                    crate.AddComponent<Rigidbody>().mass = 40f;
                }

            Wreck("Wreck0", props, new Vector3(20f, 0f, -40f), 30f);
            Wreck("Wreck1", props, new Vector3(-25f, 0f, -36f), -70f);
            Wreck("Wreck2", props, new Vector3(0f, 0f, -65f), 90f);
            Wreck("Wreck3", props, new Vector3(-48f, 0f, 22f), 10f);
        }

        static void Ramp(string name, Transform parent, Mesh mesh, Vector3 pos, float yaw)
        {
            var go = MeshObj(name, parent, pos, mesh, mRamp, Quaternion.Euler(0f, yaw, 0f));
            go.AddComponent<MeshCollider>().convex = true;
        }

        static void Wreck(string name, Transform parent, Vector3 pos, float yaw)
        {
            var root = Empty(name, parent, pos + Vector3.up * 0.75f, Quaternion.Euler(0f, yaw, 0f));
            Prim(PrimitiveType.Cube, "Lower", root.transform, Vector3.zero, new Vector3(1.8f, 0.8f, 4.2f), mWreck, true);
            Prim(PrimitiveType.Cube, "Cabin", root.transform, new Vector3(0f, 0.65f, -0.4f), new Vector3(1.6f, 0.6f, 2.1f), mWreck, true);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1100f;
            rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
            rb.linearDamping = 0.3f;
            rb.angularDamping = 0.5f;
        }

        // ---------------------------------------------------------------- car

        static GameObject BuildCar(SurfaceProfile defaultSurface)
        {
            var root = new GameObject("PlayerCar");
            var t = root.transform;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1250f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var input = root.AddComponent<CarInput>();
            var so = new SerializedObject(input);
            so.FindProperty("actions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Colliders
            var colLower = Empty("ColliderLower", t, Vector3.zero).AddComponent<BoxCollider>();
            colLower.size = new Vector3(1.8f, 0.6f, 4.3f);
            colLower.sharedMaterial = carPhysMat;
            var colCabin = Empty("ColliderCabin", t, new Vector3(0f, 0.67f, -0.65f)).AddComponent<BoxCollider>();
            colCabin.size = new Vector3(1.6f, 0.74f, 2.4f);
            colCabin.sharedMaterial = carPhysMat;

            // ---- Exterior body
            var bodyRoot = Empty("Body", t, Vector3.zero).transform;
            // Lower body is a tub: engine bay box, boot box, sills and a floor pan, so the cabin has footwells.
            var frontBody = MeshObj("FrontBody", bodyRoot, new Vector3(0f, 0f, 1.375f),
                SaveMesh(ProcMesh.Box(new Vector3(1.8f, 0.6f, 1.55f), new Vector3Int(8, 4, 8), default, "CarFrontBody"), "CarFrontBody"), mBody)
                .AddComponent<DeformableMesh>();
            frontBody.maxDeform = 0.4f;
            var rearBody = MeshObj("RearBody", bodyRoot, new Vector3(0f, 0f, -1.575f),
                SaveMesh(ProcMesh.Box(new Vector3(1.8f, 0.6f, 1.15f), new Vector3Int(8, 4, 6), default, "CarRearBody"), "CarRearBody"), mBody)
                .AddComponent<DeformableMesh>();
            rearBody.maxDeform = 0.4f;
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cube, "Sill", bodyRoot, new Vector3(0.84f * s, -0.17f, -0.2f), new Vector3(0.12f, 0.27f, 1.6f), mBody);
                Prim(PrimitiveType.Cube, "QuarterPanel", bodyRoot, new Vector3(0.86f * s, 0.46f, -1.575f), new Vector3(0.08f, 0.32f, 1.15f), mPanel);
            }
            Prim(PrimitiveType.Cube, "FloorPan", bodyRoot, new Vector3(0f, -0.24f, -0.2f), new Vector3(1.6f, 0.04f, 1.6f), mBody);

            var hoodHinge = Empty("HoodHinge", bodyRoot, new Vector3(0f, 0.3f, 0.62f));
            var hoodLatch = hoodHinge.AddComponent<HoodLatch>();
            hoodLatch.carBody = rb;
            var hood = MeshObj("Hood", hoodHinge.transform, new Vector3(0f, 0.03f, 0.76f),
                SaveMesh(ProcMesh.Box(new Vector3(1.74f, 0.06f, 1.52f), new Vector3Int(10, 1, 12), default, "CarHood"), "CarHood"), mPanel)
                .AddComponent<DeformableMesh>();
            hood.maxDeform = 0.38f;

            var roof = MeshObj("Roof", bodyRoot, new Vector3(0f, 1.05f, -0.72f),
                SaveMesh(ProcMesh.Box(new Vector3(1.6f, 0.06f, 1.56f), new Vector3Int(8, 1, 8), default, "CarRoof"), "CarRoof"), mPanel)
                .AddComponent<DeformableMesh>();
            roof.maxDeform = 0.25f;

            var doorMesh = SaveMesh(ProcMesh.Box(new Vector3(0.08f, 0.67f, 1.6f), new Vector3Int(1, 6, 10), default, "CarDoor"), "CarDoor");
            var doorL = MeshObj("DoorL", bodyRoot, new Vector3(-0.86f, 0.285f, -0.2f), doorMesh, mPanel).AddComponent<DeformableMesh>();
            var doorR = MeshObj("DoorR", bodyRoot, new Vector3(0.86f, 0.285f, -0.2f), doorMesh, mPanel).AddComponent<DeformableMesh>();
            doorL.maxDeform = doorR.maxDeform = 0.3f;

            Prim(PrimitiveType.Cube, "RearDeck", bodyRoot, new Vector3(0f, 0.33f, -2.1f), new Vector3(1.74f, 0.06f, 0.15f), mPanel);
            for (int s = -1; s <= 1; s += 2)
            {
                Strut("PillarA" + (s < 0 ? "L" : "R"), bodyRoot, new Vector3(0.81f * s, 0.32f, 0.62f), new Vector3(0.78f * s, 1.05f, 0.06f), 0.07f, mTrim);
                Strut("PillarB" + (s < 0 ? "L" : "R"), bodyRoot, new Vector3(0.82f * s, 0.6f, -0.6f), new Vector3(0.79f * s, 1.05f, -0.6f), 0.08f, mTrim);
                Strut("PillarC" + (s < 0 ? "L" : "R"), bodyRoot, new Vector3(0.79f * s, 1.05f, -1.48f), new Vector3(0.83f * s, 0.32f, -2.04f), 0.12f, mTrim);
            }

            // Glass
            Vector3 wsBottom = new Vector3(0f, 0.32f, 0.62f), wsTop = new Vector3(0f, 1.05f, 0.06f);
            Vector3 slant = wsTop - wsBottom;
            Vector3 wsNormal = Vector3.Cross(slant.normalized, Vector3.left).normalized; // points forward/up
            var windscreen = MeshObj("Windscreen", bodyRoot, (wsBottom + wsTop) * 0.5f,
                SaveMesh(ProcMesh.Box(new Vector3(1.56f, slant.magnitude, 0.01f), Vector3Int.one, default, "CarWindscreen"), "CarWindscreen"),
                mGlass, Quaternion.LookRotation(wsNormal, slant.normalized));
            windscreen.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var wsCracks = windscreen.AddComponent<CrackDecals>();
            wsCracks.crackMaterial = mCrack;
            wsCracks.stripMesh = unitCube;
            wsCracks.halfSize = new Vector2(0.76f, slant.magnitude * 0.5f);
            wsCracks.surfaceOffset = -0.008f;

            Vector3 rwTop = new Vector3(0f, 1.05f, -1.48f), rwBottom = new Vector3(0f, 0.34f, -2.02f);
            Vector3 rSlant = rwTop - rwBottom;
            var rearGlass = MeshObj("RearWindow", bodyRoot, (rwTop + rwBottom) * 0.5f,
                SaveMesh(ProcMesh.Box(new Vector3(1.52f, rSlant.magnitude, 0.01f), Vector3Int.one, default, "CarRearWindow"), "CarRearWindow"),
                mGlass, Quaternion.LookRotation(Vector3.Cross(rSlant.normalized, Vector3.right).normalized, rSlant.normalized));
            rearGlass.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Lights (just visual blocks)
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cube, "Headlight", bodyRoot, new Vector3(0.62f * s, 0.12f, 2.16f), new Vector3(0.35f, 0.14f, 0.04f), mGauge);
                Prim(PrimitiveType.Cube, "Taillight", bodyRoot, new Vector3(0.65f * s, 0.15f, -2.16f), new Vector3(0.3f, 0.12f, 0.04f), mObstacle);
            }

            // ---- Detachable exterior parts
            var bumperMesh = SaveMesh(ProcMesh.Box(new Vector3(1.86f, 0.24f, 0.22f), new Vector3Int(6, 1, 1), default, "CarBumper"), "CarBumper");
            var bumperF = Bumper("FrontBumper", "F.bumper", t, bumperMesh, new Vector3(0f, -0.12f, 2.24f), 1f);
            var bumperR = Bumper("RearBumper", "R.bumper", t, bumperMesh, new Vector3(0f, -0.12f, -2.24f), -1f);
            var mirrorL = SideMirror("MirrorL", "L.mirror", t, -1f);
            var mirrorR = SideMirror("MirrorR", "R.mirror", t, 1f);

            // ---- Interior
            var interior = Empty("Interior", t, Vector3.zero).transform;
            Prim(PrimitiveType.Cube, "Carpet", interior, new Vector3(0f, -0.215f, -0.2f), new Vector3(1.54f, 0.01f, 1.6f), mInterior);
            Prim(PrimitiveType.Cube, "Headliner", interior, new Vector3(0f, 1.015f, -0.72f), new Vector3(1.56f, 0.02f, 1.54f), mInterior);
            Prim(PrimitiveType.Cube, "Firewall", interior, new Vector3(0f, 0.42f, 0.6f), new Vector3(1.66f, 0.22f, 0.04f), mInterior);
            // Toe board: floor rises up to the firewall in the footwells.
            Vector3 toeA = new Vector3(0f, -0.21f, 0.32f), toeB = new Vector3(0f, 0.08f, 0.6f);
            Prim(PrimitiveType.Cube, "ToeBoard", interior, (toeA + toeB) * 0.5f, new Vector3(1.54f, 0.02f, (toeB - toeA).magnitude), mInterior,
                false, Quaternion.LookRotation(toeB - toeA));
            Prim(PrimitiveType.Cube, "Tunnel", interior, new Vector3(0f, -0.11f, 0.05f), new Vector3(0.3f, 0.22f, 1.2f), mInterior);
            Prim(PrimitiveType.Cube, "Console", interior, new Vector3(0f, 0.02f, -0.48f), new Vector3(0.22f, 0.2f, 0.55f), mTrim);
            Prim(PrimitiveType.Cube, "CenterStackLower", interior, new Vector3(0f, 0.17f, 0.42f), new Vector3(0.3f, 0.34f, 0.3f), mInterior);
            Prim(PrimitiveType.Cylinder, "GearLever", interior, new Vector3(0f, 0.2f, -0.3f), new Vector3(0.025f, 0.08f, 0.025f), mTrim, false, Quaternion.Euler(-15f, 0f, 0f));
            Prim(PrimitiveType.Sphere, "GearKnob", interior, new Vector3(0f, 0.28f, -0.32f), new Vector3(0.06f, 0.06f, 0.06f), mTrim);
            // Pedals (automatic: brake + throttle)
            Prim(PrimitiveType.Cube, "BrakePedal", interior, new Vector3(-0.44f, -0.05f, 0.4f), new Vector3(0.09f, 0.07f, 0.02f), mTrim, false, Quaternion.Euler(-35f, 0f, 0f));
            Strut("BrakeArm", interior, new Vector3(-0.44f, -0.03f, 0.41f), new Vector3(-0.44f, 0.22f, 0.52f), 0.02f, mTrim);
            Prim(PrimitiveType.Cube, "Throttle", interior, new Vector3(-0.28f, -0.1f, 0.44f), new Vector3(0.05f, 0.13f, 0.015f), mTrim, false, Quaternion.Euler(-40f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cube, "DoorCard", interior, new Vector3(0.81f * s, 0.28f, -0.2f), new Vector3(0.03f, 0.6f, 1.55f), mInterior);
                Prim(PrimitiveType.Cube, "Armrest", interior, new Vector3(0.775f * s, 0.32f, -0.45f), new Vector3(0.07f, 0.05f, 0.45f), mSeat);
            }

            var dash = Empty("Dashboard", interior, new Vector3(0f, 0.44f, 0.38f));
            Prim(PrimitiveType.Cube, "DashBody", dash.transform, Vector3.zero, new Vector3(1.6f, 0.2f, 0.45f), mInterior);
            var dashTop = Empty("DashTop", dash.transform, new Vector3(0f, 0.101f, 0f), Quaternion.Euler(-90f, 0f, 0f));
            var dashCracks = dashTop.AddComponent<CrackDecals>();
            dashCracks.crackMaterial = mCrack;
            dashCracks.stripMesh = unitCube;
            dashCracks.halfSize = new Vector2(0.78f, 0.2f);
            dashCracks.surfaceOffset = 0.003f;
            dashCracks.maxCracks = 6;
            dashCracks.stripWidth = 0.006f;
            // Instrument binnacle: speedometer (left) and tachometer (right) in front of the driver.
            Prim(PrimitiveType.Cube, "GaugePanel", dash.transform, new Vector3(-0.38f, 0.04f, -0.2265f), new Vector3(0.42f, 0.18f, 0.005f), mTrim);
            Prim(PrimitiveType.Cube, "BinnacleHood", dash.transform, new Vector3(-0.38f, 0.135f, -0.24f), new Vector3(0.44f, 0.02f, 0.035f), mInterior);
            var speedNeedle = Gauge("Speedometer", dash.transform, new Vector3(-0.475f, 0.04f, -0.231f), 200f, 20f, 10f, 1f, 2, "km/h", -1f);
            var rpmNeedle = Gauge("Tachometer", dash.transform, new Vector3(-0.285f, 0.04f, -0.231f), 8000f, 1000f, 500f, 1000f, 1, "x1000 rpm", 6500f);
            Prim(PrimitiveType.Cube, "CenterStack", dash.transform, new Vector3(0f, -0.05f, -0.226f), new Vector3(0.25f, 0.12f, 0.01f), mTrim);

            var glove = Empty("GloveboxHinge", dash.transform, new Vector3(0.42f, -0.08f, -0.228f));
            Prim(PrimitiveType.Cube, "GloveboxLid", glove.transform, new Vector3(0f, 0.065f, 0f), new Vector3(0.4f, 0.13f, 0.015f), mTrim);
            Prim(PrimitiveType.Cube, "GloveboxJunk", dash.transform, new Vector3(0.42f, -0.02f, -0.15f), new Vector3(0.25f, 0.06f, 0.12f), mBed);

            var visor = Empty("SunVisorHinge", interior, new Vector3(-0.38f, 1.0f, 0.04f));
            Prim(PrimitiveType.Cube, "SunVisor", visor.transform, new Vector3(0f, -0.012f, -0.075f), new Vector3(0.32f, 0.02f, 0.13f), mSeat);

            // Steering wheel: tilted column pivot, spinning child.
            var column = Empty("SteeringColumn", interior, new Vector3(-0.38f, 0.52f, -0.1f), Quaternion.LookRotation(new Vector3(0f, -0.45f, 1f), Vector3.up));
            Prim(PrimitiveType.Cylinder, "Column", column.transform, new Vector3(0f, 0f, 0.19f), new Vector3(0.05f, 0.19f, 0.05f), mTrim, false, Quaternion.Euler(90f, 0f, 0f));
            var spin = Empty("SteeringWheel", column.transform, Vector3.zero);
            int ringSegs = 18;
            for (int i = 0; i < ringSegs; i++)
            {
                float a = i / (float)ringSegs * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.17f;
                Prim(PrimitiveType.Cube, "Rim" + i, spin.transform, p, new Vector3(0.065f, 0.03f, 0.03f), mTrim, false,
                    Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f));
            }
            Prim(PrimitiveType.Cube, "Spoke", spin.transform, new Vector3(0f, -0.06f, 0.01f), new Vector3(0.025f, 0.13f, 0.02f), mTrim);
            Prim(PrimitiveType.Cube, "SpokeBar", spin.transform, new Vector3(0f, 0f, 0.01f), new Vector3(0.32f, 0.03f, 0.02f), mTrim);
            Prim(PrimitiveType.Cube, "Hub", spin.transform, new Vector3(0f, 0f, 0.015f), new Vector3(0.09f, 0.07f, 0.03f), mTrim);

            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cube, "SeatRiser", interior, new Vector3(0.38f * s, -0.12f, -0.58f), new Vector3(0.4f, 0.2f, 0.4f), mTrim);
                Prim(PrimitiveType.Cube, "SeatCushion", interior, new Vector3(0.38f * s, 0.04f, -0.58f), new Vector3(0.5f, 0.13f, 0.52f), mSeat, false, Quaternion.Euler(-4f, 0f, 0f));
                Prim(PrimitiveType.Cube, "SeatBack", interior, new Vector3(0.38f * s, 0.42f, -0.88f), new Vector3(0.48f, 0.66f, 0.12f), mSeat, false, Quaternion.Euler(-12f, 0f, 0f));
            }
            Prim(PrimitiveType.Cube, "Mattress", interior, new Vector3(0f, 0.4f, -1.6f), new Vector3(1.4f, 0.14f, 0.85f), mBed, false, Quaternion.Euler(0f, 3f, 0f));
            Prim(PrimitiveType.Cube, "Pillow", interior, new Vector3(-0.45f, 0.5f, -1.65f), new Vector3(0.4f, 0.1f, 0.3f), mGauge, false, Quaternion.Euler(0f, -8f, 4f));

            var rearView = RearViewMirror(interior);

            // ---- Wheels
            var wheels = new CarWheel[4];
            Vector3[] mounts = { new Vector3(-0.8f, 0f, 1.35f), new Vector3(0.8f, 0f, 1.35f), new Vector3(-0.8f, 0f, -1.35f), new Vector3(0.8f, 0f, -1.35f) };
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < 4; i++)
            {
                var mount = Empty(names[i], t, mounts[i]);
                var w = mount.AddComponent<CarWheel>();
                w.axle = i < 2 ? CarWheel.Axle.Front : CarWheel.Axle.Rear;
                var vis = Empty("Visual", mount.transform, Vector3.down * w.restLength);
                Prim(PrimitiveType.Cylinder, "Tyre", vis.transform, Vector3.zero, new Vector3(0.68f, 0.12f, 0.68f), mTyre, false, Quaternion.Euler(0f, 0f, 90f));
                Prim(PrimitiveType.Cube, "Hubcap", vis.transform, new Vector3(0.125f * Mathf.Sign(mounts[i].x), 0f, 0f), new Vector3(0.01f, 0.3f, 0.08f), mGauge);
                w.visual = vis.transform;
                wheels[i] = w;
            }

            // ---- Driver head / camera
            var head = Empty("DriverHead", t, new Vector3(-0.38f, 0.82f, -0.5f));
            head.tag = "MainCamera";
            var cam = head.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 600f;
            cam.fieldOfView = 70f;
            head.AddComponent<AudioListener>();

            // ---- Components
            var controller = root.AddComponent<CarController>();
            controller.input = input;
            controller.wheels = wheels;
            controller.steeringWheel = spin.transform;
            controller.defaultSurface = defaultSurface;

            var damage = root.AddComponent<CarDamage>();
            damage.input = input;
            damage.deformables = new[] { frontBody, rearBody, hood, roof, doorL, doorR };

            var gauges = dash.AddComponent<DashGauges>();
            gauges.car = controller;
            gauges.speedNeedle = speedNeedle;
            gauges.rpmNeedle = rpmNeedle;
            gauges.maxSpeedKmh = 200f;
            gauges.maxRpm = 8000f;
            damage.parts = new[] { bumperF, bumperR, mirrorL, mirrorR, rearView };
            damage.hood = hood;
            damage.hoodLatch = hoodLatch;

            var interiorDamage = root.AddComponent<InteriorDamage>();
            interiorDamage.damage = damage;
            interiorDamage.carBody = rb;
            interiorDamage.windscreen = wsCracks;
            interiorDamage.dashCracks = dashCracks;
            interiorDamage.dashboard = dash.transform;
            interiorDamage.gloveboxHinge = glove.transform;
            interiorDamage.sunVisorHinge = visor.transform;

            var camCtl = head.AddComponent<CarCamera>();
            camCtl.car = controller;
            camCtl.input = input;
            camCtl.damage = damage;

            root.AddComponent<AudioSource>();
            var audio = root.AddComponent<CarAudio>();
            audio.car = controller;
            audio.damage = damage;

            var hud = root.AddComponent<CarHud>();
            hud.car = controller;
            hud.damage = damage;

            return root;
        }

        /// <summary>Analog gauge on the dash face (local -Z faces the driver). Returns the needle pivot.</summary>
        static Transform Gauge(string name, Transform parent, Vector3 localPos, float max, float majorStep, float minorStep,
            float labelDivisor, int labelEvery, string unit, float redFrom)
        {
            const float sweep = 270f, radius = 0.072f;
            var root = Empty(name, parent, localPos).transform;
            Prim(PrimitiveType.Cylinder, "Bezel", root, new Vector3(0f, 0f, 0.002f), new Vector3(radius * 2.2f, 0.002f, radius * 2.2f), mGauge,
                false, Quaternion.Euler(90f, 0f, 0f));
            Prim(PrimitiveType.Cylinder, "Face", root, Vector3.zero, new Vector3(radius * 2f, 0.002f, radius * 2f), mGaugeFace,
                false, Quaternion.Euler(90f, 0f, 0f));

            int minorCount = Mathf.RoundToInt(max / minorStep);
            int majorEvery = Mathf.RoundToInt(majorStep / minorStep);
            for (int i = 0; i <= minorCount; i++)
            {
                float value = i * minorStep;
                bool major = i % majorEvery == 0;
                float theta = DashGauges.AngleFor(value / max, sweep);
                float rad = theta * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                float len = major ? 0.013f : 0.007f;
                Vector2 p = dir * (radius * 0.92f - len * 0.5f);
                var mat = redFrom > 0f && value >= redFrom ? mRedline : mGauge;
                Prim(PrimitiveType.Cube, "Tick", root, new Vector3(p.x, p.y, -0.0025f), new Vector3(major ? 0.003f : 0.0018f, len, 0.001f), mat,
                    false, Quaternion.Euler(0f, 0f, -theta));

                int majorIndex = i / majorEvery;
                if (major && majorIndex % labelEvery == 0)
                {
                    Vector2 lp = dir * (radius * 0.6f);
                    Label(root, ((int)(value / labelDivisor)).ToString(), new Vector3(lp.x, lp.y, -0.003f), 0.17f);
                }
            }
            Label(root, unit, new Vector3(0f, -0.036f, -0.003f), 0.1f);

            var needle = Empty("Needle", root, new Vector3(0f, 0f, -0.0045f)).transform;
            Prim(PrimitiveType.Cube, "NeedleBar", needle, new Vector3(0f, radius * 0.38f, 0f), new Vector3(0.0035f, radius * 0.95f, 0.0015f), mNeedle);
            Prim(PrimitiveType.Cylinder, "Cap", root, new Vector3(0f, 0f, -0.0055f), new Vector3(0.014f, 0.001f, 0.014f), mTrim,
                false, Quaternion.Euler(90f, 0f, 0f));
            needle.localRotation = Quaternion.Euler(0f, 0f, -DashGauges.AngleFor(0f, sweep));
            return needle;
        }

        static void Label(Transform parent, string text, Vector3 localPos, float fontSize)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = new Color(0.92f, 0.9f, 0.85f);
            tmp.rectTransform.sizeDelta = new Vector2(0.05f, 0.015f);
            tmp.rectTransform.localPosition = localPos;
            tmp.rectTransform.localRotation = Quaternion.identity;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static DetachablePart Bumper(string name, string label, Transform parent, Mesh mesh, Vector3 pos, float side)
        {
            var go = MeshObj(name, parent, pos, mesh, mTrim);
            var col = go.AddComponent<BoxCollider>();
            col.sharedMaterial = carPhysMat;
            var p = go.AddComponent<DetachablePart>();
            p.displayName = label;
            p.maxHealth = 100f;
            p.looseAt = 0.55f;
            p.detachedMass = 14f;
            p.looseOffset = new Vector3(0f, -0.07f, 0.03f * side);
            p.looseEuler = new Vector3(4f * side, 3f, 9f);
            p.wobble = 2.5f;
            return p;
        }

        static DetachablePart SideMirror(string name, string label, Transform parent, float side)
        {
            var go = Empty(name, parent, new Vector3(0.97f * side, 0.52f, 0.5f));
            Prim(PrimitiveType.Cube, "Arm", go.transform, new Vector3(-0.06f * side, -0.02f, 0f), new Vector3(0.14f, 0.04f, 0.05f), mTrim);
            Prim(PrimitiveType.Cube, "Housing", go.transform, new Vector3(0.03f * side, 0.04f, 0f), new Vector3(0.2f, 0.13f, 0.07f), mPanel);
            Prim(PrimitiveType.Cube, "Glass", go.transform, new Vector3(0.03f * side, 0.04f, -0.037f), new Vector3(0.17f, 0.1f, 0.005f), mMirror);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.02f, 0f);
            col.size = new Vector3(0.26f, 0.16f, 0.1f);
            col.sharedMaterial = carPhysMat;
            var p = go.AddComponent<DetachablePart>();
            p.displayName = label;
            p.maxHealth = 30f;
            p.looseAt = 0.6f;
            p.joltSensitivity = 0.15f;
            p.detachedMass = 2f;
            p.looseOffset = new Vector3(0f, -0.04f, 0f);
            p.looseEuler = new Vector3(0f, 25f * side, -20f * side);
            p.wobble = 6f;
            return p;
        }

        static DetachablePart RearViewMirror(Transform interior)
        {
            var go = Empty("RearViewMirror", interior, new Vector3(0f, 0.97f, 0.08f), Quaternion.Euler(8f, -12f, 0f));
            Prim(PrimitiveType.Cube, "Stalk", go.transform, new Vector3(0f, 0.03f, 0.02f), new Vector3(0.02f, 0.05f, 0.02f), mTrim);
            Prim(PrimitiveType.Cube, "Frame", go.transform, Vector3.zero, new Vector3(0.22f, 0.06f, 0.025f), mTrim);
            Prim(PrimitiveType.Cube, "Glass", go.transform, new Vector3(0f, 0f, -0.014f), new Vector3(0.2f, 0.045f, 0.004f), mMirror);
            var p = go.AddComponent<DetachablePart>();
            p.displayName = "Rear-view";
            p.mode = DetachablePart.Mode.InteriorDrop;
            p.maxHealth = 50f;
            p.looseAt = 0.7f;
            p.joltSensitivity = 1f;
            p.looseEuler = new Vector3(18f, 25f, 12f);
            p.wobble = 8f;
            p.dropLocalPosition = new Vector3(0.18f, 0.6f, 0.3f);
            p.dropLocalEuler = new Vector3(85f, 30f, 0f);
            return p;
        }
    }
}
