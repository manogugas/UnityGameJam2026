using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using TMPro;
using Unity.Cinemachine;

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

        // Square arena: inner wall face at +-ArenaHalf, sand in the middle, asphalt band around it.
        const float ArenaHalf = 75f;
        const float SandHalf = 55f;
        const float BankDepth = 11f;
        const float BankHeight = 3.6f;
        const float WallHeight = 6f;
        // Stands (the crowd component uses the same numbers).
        const int StandRows = 14;
        const float StandRowDepth = 1.3f;
        const float StandFirstTop = 6.6f;
        const float StandRise = 0.75f;
        // Gates: openings in the south and west walls with a tunnel through the stands behind them.
        const float GateHalfWidth = 3.5f;
        const float GateHeight = 5f;
        const float TunnelLength = 18f;
        static readonly Vector3[] GateSides = { Vector3.back, Vector3.left };
        static readonly System.Collections.Generic.List<ArenaGate> builtGates = new System.Collections.Generic.List<ArenaGate>();

        static Material mSand, mAsphalt, mBank, mConcrete, mRamp, mObstacle, mTyre, mCrate, mWreck;
        static Material mStone, mStone2, mGate, mClothRed, mClothPurple, mSkin;
        static Material[] mCrowd;
        static Material mHealthFill, mWarnLamp;
        const string AudioDir = Root + "/Audio";
        static AudioClip[] crashClips;
        static AudioClip crowdLoopClip, ambientLoopClip;
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
            LoadAudio();
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
            var playerGate = builtGates[0];
            car.transform.SetPositionAndRotation(playerGate.spawnPoint.position, playerGate.spawnPoint.rotation);
            PrefabUtility.SaveAsPrefabAssetAndConnect(car, PrefabPath, InteractionMode.AutomatedAction);

            var enemyPrefabs = new[]
            {
                BuildEnemyPrefab(asphalt, DriverArchetype.Rammer, "Rammer", new Color(0.62f, 0.16f, 0.12f), 1250f, 60f, 125f, 2700f),
                BuildEnemyPrefab(asphalt, DriverArchetype.Flanker, "Flanker", new Color(0.85f, 0.65f, 0.12f), 1100f, 50f, 120f, 2500f),
                BuildEnemyPrefab(asphalt, DriverArchetype.Bruiser, "Bruiser", new Color(0.2f, 0.24f, 0.18f), 1900f, 110f, 165f, 3600f),
            };
            var director = BuildDirector(car, enemyPrefabs);
            BuildIntro(car, playerGate, director);

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
            mStone = Mat("Stone", new Color(0.8f, 0.74f, 0.62f), 0.05f);
            mStone2 = Mat("StoneDark", new Color(0.68f, 0.62f, 0.52f), 0.05f);
            mGate = Mat("Gate", new Color(0.07f, 0.06f, 0.05f), 0.1f);
            mClothRed = Mat("ClothRed", new Color(0.62f, 0.08f, 0.06f), 0.1f);
            mClothPurple = Mat("ClothPurple", new Color(0.36f, 0.1f, 0.42f), 0.1f);
            mSkin = Mat("CrowdSkin", new Color(0.86f, 0.66f, 0.52f), 0.2f);
            mHealthFill = Mat("HealthFill", new Color(0.25f, 1f, 0.3f), 0.5f);
            mHealthFill.EnableKeyword("_EMISSION");
            mHealthFill.SetColor("_EmissionColor", new Color(0.25f, 1f, 0.3f));
            mHealthFill.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mWarnLamp = Mat("WarningLamp", new Color(0.12f, 0.1f, 0.08f), 0.6f);
            mWarnLamp.EnableKeyword("_EMISSION");
            mWarnLamp.SetColor("_EmissionColor", Color.black);
            mCrowd = new[]
            {
                Mat("CrowdRed", new Color(0.7f, 0.2f, 0.18f), 0.1f),
                Mat("CrowdBlue", new Color(0.2f, 0.33f, 0.68f), 0.1f),
                Mat("CrowdWhite", new Color(0.86f, 0.82f, 0.72f), 0.1f),
                Mat("CrowdGreen", new Color(0.3f, 0.5f, 0.25f), 0.1f),
                Mat("CrowdOchre", new Color(0.78f, 0.56f, 0.2f), 0.1f),
            };
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

        /// <summary>Picks up the recorded clips (if present) and sets sensible import settings for them.</summary>
        static void LoadAudio()
        {
            var crashes = new System.Collections.Generic.List<AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir + "/SFX/Car" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null && clip.name.StartsWith("Break")) crashes.Add(clip);
            }
            crashes.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            crashClips = crashes.ToArray();
            crowdLoopClip = LoadLoop(AudioDir + "/Music/Ambient/Big Crowd Loop.wav");
            ambientLoopClip = LoadLoop(AudioDir + "/Music/Ambient/Town Square Loop.wav");
        }

        /// <summary>Long ambience loops stream as Vorbis instead of sitting decompressed in memory.</summary>
        static AudioClip LoadLoop(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return null;
            var settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.Streaming || settings.compressionFormat != AudioCompressionFormat.Vorbis)
            {
                settings.loadType = AudioClipLoadType.Streaming;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.6f;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
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
            RenderSettings.fogStartDistance = 130f;
            RenderSettings.fogEndDistance = 320f;
            RenderSettings.fogColor = new Color(0.75f, 0.78f, 0.82f);
        }

        // ---------------------------------------------------------------- arena

        static void BuildArena(SurfaceProfile asphalt, SurfaceProfile sand)
        {
            var arena = new GameObject("Arena").transform;

            // Old ring-arena meshes are no longer used.
            foreach (var old in new[] { "SandDisk", "AsphaltRing", "BankedRing" })
                AssetDatabase.DeleteAsset($"{MeshDir}/{old}.asset");

            // Floor: sand square in the middle, asphalt band around it running under the wall.
            Tag(Prim(PrimitiveType.Cube, "SandFloor", arena, new Vector3(0f, -0.1f, 0f), new Vector3(SandHalf * 2f, 0.2f, SandHalf * 2f), mSand, true), sand);
            float outer = ArenaHalf + 2f;
            float band = outer - SandHalf, bandMid = SandHalf + band * 0.5f;
            Tag(Prim(PrimitiveType.Cube, "AsphaltN", arena, new Vector3(0f, -0.1f, bandMid), new Vector3(outer * 2f, 0.2f, band), mAsphalt, true), asphalt);
            Tag(Prim(PrimitiveType.Cube, "AsphaltS", arena, new Vector3(0f, -0.1f, -bandMid), new Vector3(outer * 2f, 0.2f, band), mAsphalt, true), asphalt);
            Tag(Prim(PrimitiveType.Cube, "AsphaltE", arena, new Vector3(bandMid, -0.1f, 0f), new Vector3(band, 0.2f, SandHalf * 2f), mAsphalt, true), asphalt);
            Tag(Prim(PrimitiveType.Cube, "AsphaltW", arena, new Vector3(-bandMid, -0.1f, 0f), new Vector3(band, 0.2f, SandHalf * 2f), mAsphalt, true), asphalt);

            // Banked wall ramps along the north and east sides (half the perimeter), tapering out before the corners.
            var bank = SaveMesh(ProcMesh.BankStrip((ArenaHalf - 5f) * 2f, BankDepth, BankHeight, 70, 16, 12f, "BankStrip"), "BankStrip");
            var bankN = MeshObj("BankedRampN", arena, new Vector3(0f, 0f, ArenaHalf - BankDepth - 0.05f), bank, mBank);
            bankN.AddComponent<MeshCollider>();
            Tag(bankN, asphalt);
            var bankE = MeshObj("BankedRampE", arena, new Vector3(ArenaHalf - BankDepth - 0.05f, 0f, 0f), bank, mBank, Quaternion.Euler(0f, 90f, 0f));
            bankE.AddComponent<MeshCollider>();
            Tag(bankE, asphalt);

            // Podium wall (the arena edge) with a parapet.
            var wall = Empty("PodiumWall", arena, Vector3.zero).transform;
            foreach (var dir in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                var rot = Quaternion.LookRotation(-dir);
                Vector3 along = Vector3.Cross(Vector3.up, dir);
                Vector3 centre = dir * (ArenaHalf + 1f);
                if (System.Array.IndexOf(GateSides, dir) >= 0)
                {
                    // Wall with an opening for the gate: left part, right part and a lintel above.
                    float segLen = outer - GateHalfWidth, segMid = (outer + GateHalfWidth) * 0.5f;
                    foreach (float sgn in new[] { -1f, 1f })
                        Prim(PrimitiveType.Cube, "Wall", wall, centre + along * (sgn * segMid) + Vector3.up * (WallHeight * 0.5f),
                            new Vector3(segLen, WallHeight, 2f), mStone2, true, rot);
                    Prim(PrimitiveType.Cube, "WallLintel", wall, centre + Vector3.up * ((GateHeight + WallHeight) * 0.5f),
                        new Vector3(GateHalfWidth * 2f, WallHeight - GateHeight, 2f), mStone2, true, rot);
                }
                else
                {
                    Prim(PrimitiveType.Cube, "Wall", wall, centre + Vector3.up * (WallHeight * 0.5f),
                        new Vector3(outer * 2f, WallHeight, 2f), mStone2, true, rot);
                }
                Prim(PrimitiveType.Cube, "Parapet", wall, dir * (ArenaHalf + 0.3f) + Vector3.up * (WallHeight + 0.4f),
                    new Vector3(outer * 2f, 0.8f, 0.6f), mStone, false, rot);
            }
            // Gates on the flat sides (south, west): the player's and the enemies' entrances.
            builtGates.Clear();
            foreach (var side in GateSides) builtGates.Add(Gate(arena, side, asphalt));

            BuildStands(arena);

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

            // Slalom of barriers on the flat south band, leaving the gate in the middle clear.
            float[] slalomX = { -45f, -33f, -21f, 21f, 33f, 45f };
            for (int i = 0; i < slalomX.Length; i++)
            {
                float z = -bandMid + (i % 2 == 0 ? 5f : -5f);
                Prim(PrimitiveType.Cube, "Barrier" + i, obstacles, new Vector3(slalomX[i], 0.5f, z), new Vector3(5f, 1f, 0.7f), mConcrete, true);
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
                    Prop(t, 18f);
                }
            int n = 0;
            for (int row = 0; row < 3; row++)
                for (int c = 0; c < 3 - row; c++)
                {
                    var crate = Prim(PrimitiveType.Cube, "Crate" + n++, props,
                        new Vector3(-22f + (c - (2 - row) * 0.5f) * 1.05f, 0.5f + row * 1.0f, -8f), Vector3.one, mCrate, true);
                    Prop(crate, 40f);
                }

            Wreck("Wreck0", props, new Vector3(20f, 0f, -40f), 30f);
            Wreck("Wreck1", props, new Vector3(-25f, 0f, -36f), -70f);
            Wreck("Wreck3", props, new Vector3(-48f, 0f, 22f), 10f);
        }

        /// <summary>Loose physics prop. Interpolated, otherwise it visibly stutters when resting on
        /// (or seen from) the interpolated player car.</summary>
        static Rigidbody Prop(GameObject go, float mass)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return rb;
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
            rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        // ---------------------------------------------------------------- stands

        /// <summary>
        /// Gate with a working portcullis and a tunnel behind it through the stands. Local +Z points into
        /// the arena; the wall occupies local z 0..-2 and the tunnel continues outward from there.
        /// </summary>
        static ArenaGate Gate(Transform parent, Vector3 n, SurfaceProfile floorSurface)
        {
            var g = Empty("Gate", parent, n * ArenaHalf, Quaternion.LookRotation(-n)).transform;
            float w = GateHalfWidth;
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "Jamb", g, new Vector3((w + 0.6f) * s, 2.9f, 0.2f), new Vector3(1.2f, 5.8f, 0.6f), mStone);
            Prim(PrimitiveType.Cube, "Lintel", g, new Vector3(0f, GateHeight + 0.45f, 0.25f), new Vector3(2f * w + 2.6f, 1.1f, 0.7f), mStone);
            // Gatehouse above the gate: hides the raised portcullis.
            Prim(PrimitiveType.Cube, "Gatehouse", g, new Vector3(0f, WallHeight + 2.8f, -1f), new Vector3(2f * w + 3f, 5.6f, 2.4f), mStone2);
            Prim(PrimitiveType.Cube, "GatehouseBanner", g, new Vector3(0f, WallHeight + 2.6f, 0.22f), new Vector3(3f, 4.4f, 0.06f), mClothRed);

            // Tunnel through the stands.
            float zMid = -2f - TunnelLength * 0.5f;
            var floor = Prim(PrimitiveType.Cube, "TunnelFloor", g, new Vector3(0f, -0.1f, zMid), new Vector3(2f * w + 1f, 0.2f, TunnelLength + 0.2f), mAsphalt, true);
            Tag(floor, floorSurface);
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "TunnelWall", g, new Vector3((w + 0.25f) * s, GateHeight * 0.5f, zMid), new Vector3(0.5f, GateHeight, TunnelLength), mStone2, true);
            Prim(PrimitiveType.Cube, "TunnelRoof", g, new Vector3(0f, GateHeight + 0.25f, zMid), new Vector3(2f * w + 1f, 0.5f, TunnelLength), mStone2, true);
            Prim(PrimitiveType.Cube, "TunnelEnd", g, new Vector3(0f, GateHeight * 0.5f, -2f - TunnelLength - 0.25f), new Vector3(2f * w + 1f, GateHeight, 0.5f), mGate, true);
            var lamp = Empty("TunnelLight", g, new Vector3(0f, GateHeight - 0.6f, -2f - TunnelLength * 0.45f)).AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.7f, 0.4f);
            lamp.range = 14f;
            lamp.intensity = 3f;

            // Portcullis: a kinematic body so cars collide with it properly while it moves.
            var port = Empty("Portcullis", g, new Vector3(0f, GateHeight * 0.5f, -1f));
            var prb = port.AddComponent<Rigidbody>();
            prb.isKinematic = true;
            prb.interpolation = RigidbodyInterpolation.Interpolate;
            var pcol = port.AddComponent<BoxCollider>();
            pcol.size = new Vector3(2f * w, GateHeight, 0.3f);
            for (int b = 0; b < 7; b++)
                Prim(PrimitiveType.Cube, "Bar", port.transform, new Vector3(-w + 0.25f + b * (2f * w - 0.5f) / 6f, 0f, 0f), new Vector3(0.14f, GateHeight, 0.14f), mTrim);
            for (int b = -1; b <= 1; b++)
                Prim(PrimitiveType.Cube, "CrossBar", port.transform, new Vector3(0f, b * 1.6f, 0.05f), new Vector3(2f * w, 0.14f, 0.12f), mTrim);

            var gate = g.gameObject.AddComponent<ArenaGate>();
            gate.portcullis = port.transform;
            gate.openHeight = GateHeight + 0.3f;
            gate.spawnPoint = Empty("SpawnPoint", g, new Vector3(0f, 0.9f, -2f - TunnelLength + 4.5f)).transform;
            gate.entryPoint = Empty("EntryPoint", g, new Vector3(0f, 0.9f, 16f)).transform;
            return gate;
        }

        /// <summary>Tiered stone seating on all four sides, a colonnade with banners on top, and the emperor's box.</summary>
        static void BuildStands(Transform arena)
        {
            var stands = Empty("Stands", arena, Vector3.zero).transform;
            float first = ArenaHalf + 2f;
            float lastTop = StandFirstTop + StandRise * (StandRows - 1);
            foreach (var n in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
            {
                var side = Empty("Side", stands, Vector3.zero).transform;
                var rot = Quaternion.LookRotation(-n);
                Vector3 along = Vector3.Cross(Vector3.up, n);
                for (int i = 0; i < StandRows; i++)
                {
                    float dist = first + StandRowDepth * (i + 0.5f);
                    float top = StandFirstTop + StandRise * i;
                    float len = 2f * (first + StandRowDepth * (i + 1));
                    var rowMat = i % 2 == 0 ? mStone : mStone2;
                    if (System.Array.IndexOf(GateSides, n) >= 0)
                    {
                        // Leave room for the gate tunnel: two halves plus a block above the tunnel roof.
                        float gap = GateHalfWidth + 0.5f, halfLen = len * 0.5f - gap, mid = (len * 0.5f + gap) * 0.5f;
                        foreach (float sgn in new[] { -1f, 1f })
                            Prim(PrimitiveType.Cube, "Row" + i, side, n * dist + along * (sgn * mid) + Vector3.up * (top * 0.5f),
                                new Vector3(halfLen, top, StandRowDepth), rowMat, false, rot);
                        float roof = GateHeight + 0.5f;
                        Prim(PrimitiveType.Cube, "Row" + i + "Over", side, n * dist + Vector3.up * ((top + roof) * 0.5f),
                            new Vector3(2f * gap, top - roof, StandRowDepth), rowMat, false, rot);
                    }
                    else
                    {
                        Prim(PrimitiveType.Cube, "Row" + i, side, n * dist + Vector3.up * (top * 0.5f),
                            new Vector3(len, top, StandRowDepth), rowMat, false, rot);
                    }
                }
                float backDist = first + StandRowDepth * StandRows + 0.75f;
                float backTop = lastTop + 8f;
                Prim(PrimitiveType.Cube, "BackWall", side, n * backDist + Vector3.up * (backTop * 0.5f),
                    new Vector3(2f * (backDist + 0.75f), backTop, 1.5f), mStone2, false, rot);

                // Colonnade on the top row, with alternating red and purple banners between the columns.
                float colDist = first + StandRowDepth * (StandRows - 0.5f);
                float colH = 6f, topLen = 2f * colDist;
                int k = 0;
                for (float x = -topLen * 0.5f + 4f; x <= topLen * 0.5f - 4f; x += 8f, k++)
                {
                    Prim(PrimitiveType.Cylinder, "Column", side, n * colDist + along * x + Vector3.up * (lastTop + colH * 0.5f),
                        new Vector3(0.9f, colH * 0.5f, 0.9f), mStone);
                    if (k % 2 == 0 && x + 4f < topLen * 0.5f - 4f)
                        Prim(PrimitiveType.Cube, "Banner", side, n * (colDist - 0.7f) + along * (x + 4f) + Vector3.up * (lastTop + colH - 2.4f),
                            new Vector3(2.4f, 4f, 0.08f), (k / 2) % 2 == 0 ? mClothRed : mClothPurple, false, rot);
                }
                Prim(PrimitiveType.Cube, "Entablature", side, n * colDist + Vector3.up * (lastTop + colH + 0.5f),
                    new Vector3(topLen, 1f, 1.8f), mStone2, false, rot);
            }

            // Emperor's box (pulvinar) in the middle of the north stand.
            var box = Empty("EmperorsBox", stands, Vector3.forward * (first + 3.25f)).transform;
            Prim(PrimitiveType.Cube, "Podium", box, Vector3.up * 5.25f, new Vector3(14f, 10.5f, 6.5f), mStone2);
            Prim(PrimitiveType.Cube, "Drape", box, new Vector3(0f, 10f, -3.3f), new Vector3(14.2f, 1.6f, 0.1f), mClothRed);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Prim(PrimitiveType.Cylinder, "BoxColumn", box, new Vector3(6.3f * x, 12.75f, 2.7f * z), new Vector3(0.6f, 2.25f, 0.6f), mStone);
            Prim(PrimitiveType.Cube, "Canopy", box, new Vector3(0f, 15.2f, 0f), new Vector3(15f, 0.5f, 7.2f), mClothPurple);
            Prim(PrimitiveType.Cube, "Throne", box, new Vector3(0f, 11.4f, 1.2f), new Vector3(1.6f, 1.8f, 1.2f), mClothPurple);

            // The crowd is generated at runtime from these numbers (no mesh assets).
            var crowdGo = new GameObject("Crowd");
            crowdGo.transform.SetParent(stands, false);
            crowdGo.AddComponent<AudioSource>();
            var crowd = crowdGo.AddComponent<ArenaCrowd>();
            crowd.cube = unitCube;
            crowd.clothes = mCrowd;
            crowd.skin = mSkin;
            crowd.firstRowDistance = first;
            crowd.rowDepth = StandRowDepth;
            crowd.firstRowTop = StandFirstTop;
            crowd.rowRise = StandRise;
            crowd.rows = StandRows;
            crowd.crowdLoop = crowdLoopClip;
            crowd.ambientLoop = ambientLoopClip;
            crowd.exclusions = new[] { new Bounds(new Vector3(0f, 10f, first + 3.25f), new Vector3(14.5f, 30f, 7f)) };
        }

        // ---------------------------------------------------------------- enemies

        static ArenaDirector BuildDirector(GameObject playerCar, GameObject[] enemyPrefabs)
        {
            var go = new GameObject("ArenaDirector");
            var director = go.AddComponent<ArenaDirector>();
            director.player = playerCar.GetComponent<CarController>();
            director.enemyPrefabs = enemyPrefabs;
            director.gates = builtGates.ToArray();
            director.enemyCount = 4;
            director.waitForIntro = true;
            director.crowd = Object.FindAnyObjectByType<ArenaCrowd>();
            var hud = playerCar.GetComponent<CarHud>();
            hud.director = director;
            PrefabUtility.RecordPrefabInstancePropertyModifications(hud); // keep the scene-only reference on the prefab instance
            var combat = playerCar.GetComponent<CombatHud>();
            combat.director = director;
            PrefabUtility.RecordPrefabInstancePropertyModifications(combat);
            return director;
        }

        static NoiseSettings Noise(string preset) =>
            AssetDatabase.LoadAssetAtPath<NoiseSettings>($"Packages/com.unity.cinemachine/Presets/Noise/{preset}.asset");

        static CinemachineCamera VCam(string name, Transform parent, float fov, string noise, float amplitude, float frequency)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<CinemachineCamera>();
            var lens = LensSettings.Default;
            lens.FieldOfView = fov;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 900f;
            cam.Lens = lens;
            cam.Priority.Value = 0;
            if (noise != null)
            {
                var perlin = go.AddComponent<CinemachineBasicMultiChannelPerlin>();
                perlin.NoiseProfile = Noise(noise);
                perlin.AmplitudeGain = amplitude;
                perlin.FrequencyGain = frequency;
            }
            return cam;
        }

        static CinemachineBlendDefinition Blend(CinemachineBlendDefinition.Styles style, float time) => new CinemachineBlendDefinition(style, time);

        static void BuildIntro(GameObject playerCar, ArenaGate playerGate, ArenaDirector director)
        {
            var carT = playerCar.transform;
            var driverCam = playerCar.GetComponentInChildren<CinemachineCamera>();

            // Blends between the shots: a long swoop from the orbit down to the gate, then shorter moves.
            string blendsPath = Root + "/Settings/IntroBlends.asset";
            Directory.CreateDirectory(Root + "/Settings");
            var blends = AssetDatabase.LoadAssetAtPath<CinemachineBlenderSettings>(blendsPath);
            if (blends == null)
            {
                blends = ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
                AssetDatabase.CreateAsset(blends, blendsPath);
            }
            blends.CustomBlends = new[]
            {
                new CinemachineBlenderSettings.CustomBlend { From = "CM Overview", To = "CM Gate", Blend = Blend(CinemachineBlendDefinition.Styles.EaseInOut, 2.6f) },
                new CinemachineBlenderSettings.CustomBlend { From = "CM Gate", To = "CM Chase", Blend = Blend(CinemachineBlendDefinition.Styles.EaseInOut, 1.3f) },
                new CinemachineBlenderSettings.CustomBlend { From = "CM Chase", To = "CM Driver", Blend = Blend(CinemachineBlendDefinition.Styles.EaseInOut, 1.8f) },
                new CinemachineBlenderSettings.CustomBlend { From = "CM Gate", To = "CM Driver", Blend = Blend(CinemachineBlendDefinition.Styles.EaseInOut, 2f) },
            };
            EditorUtility.SetDirty(blends);

            // The one real camera: renders whichever Cinemachine camera is live, and carries the audio listener.
            var mainGo = new GameObject("Main Camera");
            mainGo.tag = "MainCamera";
            var unityCam = mainGo.AddComponent<Camera>();
            unityCam.nearClipPlane = 0.03f;
            unityCam.farClipPlane = 900f;
            unityCam.fieldOfView = 70f;
            mainGo.AddComponent<AudioListener>();
            var brain = mainGo.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.DefaultBlend = Blend(CinemachineBlendDefinition.Styles.EaseInOut, 1.5f);
            brain.CustomBlends = blends;

            var go = new GameObject("Intro");
            var centre = Empty("ArenaCentre", go.transform, new Vector3(0f, 6f, 0f)).transform;

            // 1. Orbit over the arena.
            var overview = VCam("CM Overview", go.transform, 50f, "Handheld_wideangle_mild", 0.8f, 0.5f);
            overview.Follow = centre;
            overview.LookAt = centre;
            var orbit = overview.gameObject.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 125f;
            orbit.VerticalAxis.Value = 26f;
            orbit.HorizontalAxis.Value = 200f;
            overview.gameObject.AddComponent<CinemachineRotationComposer>();
            overview.Priority.Value = 100; // live from the first frame

            // 2. Tele shot of the gate, tracking the car as it rolls out.
            Transform g = playerGate.transform;
            var gate = VCam("CM Gate", go.transform, 40f, "Handheld_tele_mild", 1f, 0.8f);
            gate.transform.SetPositionAndRotation(g.position + g.forward * 24f + g.right * 10f + Vector3.up * 3.2f,
                Quaternion.LookRotation(g.position + Vector3.up * 2.5f - (g.position + g.forward * 24f + g.right * 10f + Vector3.up * 3.2f)));
            gate.LookAt = carT;
            var gateAim = gate.gameObject.AddComponent<CinemachineRotationComposer>();
            gateAim.TargetOffset = new Vector3(0f, 0.8f, 0f);
            gateAim.Damping = new Vector2(1.2f, 1.2f);
            gate.gameObject.AddComponent<CinemachineImpulseListener>();

            // 3. Chase camera behind the car.
            var chase = VCam("CM Chase", go.transform, 62f, "Handheld_normal_mild", 0.6f, 1f);
            chase.Follow = carT;
            chase.LookAt = carT;
            var follow = chase.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 2.4f, -7f);
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LockToTargetWithWorldUp;
            follow.TrackerSettings.PositionDamping = new Vector3(0.8f, 0.5f, 0.9f);
            var chaseAim = chase.gameObject.AddComponent<CinemachineRotationComposer>();
            chaseAim.TargetOffset = new Vector3(0f, 1f, 0f);
            chaseAim.Damping = new Vector2(0.5f, 0.5f);
            chase.gameObject.AddComponent<CinemachineImpulseListener>();

            var intro = go.AddComponent<IntroCinematic>();
            intro.brain = brain;
            intro.player = playerCar.GetComponent<CarController>();
            intro.playerGate = playerGate;
            intro.director = director;
            intro.crowd = Object.FindAnyObjectByType<ArenaCrowd>();
            intro.overviewCam = overview;
            intro.gateCam = gate;
            intro.chaseCam = chase;
            intro.driverCam = driverCam;
        }

        // ---------------------------------------------------------------- car

        static GameObject BuildCar(SurfaceProfile defaultSurface)
            => BuildCar(defaultSurface, "PlayerCar", true, null, null);

        /// <summary>Builds a car. Enemies get the same physics/damage but no camera, cockpit gauges or HUD,
        /// a different paint job, an AI driver and 3D engine sound.</summary>
        static GameObject BuildCar(SurfaceProfile defaultSurface, string name, bool player, Material body, Material panel)
        {
            var savedBody = mBody;
            var savedPanel = mPanel;
            if (body != null) mBody = body;
            if (panel != null) mPanel = panel;
            try { return BuildCarInternal(defaultSurface, name, player); }
            finally { mBody = savedBody; mPanel = savedPanel; }
        }

        static GameObject BuildCarInternal(SurfaceProfile defaultSurface, string name, bool player)
        {
            var root = new GameObject(name);
            var t = root.transform;
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1250f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var input = root.AddComponent<CarInput>();
            if (player)
            {
                var so = new SerializedObject(input);
                so.FindProperty("actions").objectReferenceValue = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputPath);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

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
            GameObject head = null;
            if (player)
            {
                // The driver's view is a Cinemachine camera driven by CarCamera moving this transform;
                // the scene's main camera (CinemachineBrain) renders it and blends to/from the intro shots.
                head = Empty("DriverHead", t, new Vector3(-0.38f, 0.82f, -0.5f));
                var vcam = head.AddComponent<CinemachineCamera>();
                vcam.name = "CM Driver";
                var lens = LensSettings.Default;
                lens.FieldOfView = 70f;
                lens.NearClipPlane = 0.03f;
                lens.FarClipPlane = 600f;
                vcam.Lens = lens;
                vcam.Priority.Value = 10;
                var listener = head.AddComponent<CinemachineImpulseListener>();
                listener.Gain = 0.6f;
                listener.Use2DDistance = true;
            }

            // ---- Components
            var controller = root.AddComponent<CarController>();
            controller.input = input;
            controller.wheels = wheels;
            controller.steeringWheel = spin.transform;
            controller.defaultSurface = defaultSurface;

            var damage = root.AddComponent<CarDamage>();
            damage.input = input;
            damage.deformables = new[] { frontBody, rearBody, hood, roof, doorL, doorR };

            if (player)
            {
                var gauges = dash.AddComponent<DashGauges>();
                gauges.car = controller;
                gauges.speedNeedle = speedNeedle;
                gauges.rpmNeedle = rpmNeedle;
                gauges.maxSpeedKmh = 200f;
                gauges.maxRpm = 8000f;
            }
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

            var health = root.AddComponent<CarHealth>();
            health.car = controller;
            health.damage = damage;
            health.isPlayer = player;
            health.displayName = player ? "You" : name;
            health.smoke = EngineSmoke(t);

            root.AddComponent<AudioSource>();
            var audio = root.AddComponent<CarAudio>();
            audio.car = controller;
            audio.damage = damage;
            audio.spatial = !player;
            audio.crashClips = crashClips;

            var impulse = root.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Dissipating;
            impulse.ImpulseDefinition.ImpulseDuration = 0.3f;
            impulse.ImpulseDefinition.DissipationDistance = 70f;
            root.AddComponent<CrashImpulse>().damage = damage;

            if (player)
            {
                var camCtl = head.AddComponent<CarCamera>();
                camCtl.car = controller;
                camCtl.input = input;
                camCtl.damage = damage;

                var hud = root.AddComponent<CarHud>();
                hud.car = controller;
                hud.damage = damage;
                hud.health = health;

                root.AddComponent<CombatHud>().player = health;

                // Health gauge in the middle of the dashboard, right of the steering wheel.
                var hg = Empty("HealthGauge", dash.transform, new Vector3(0.03f, 0.045f, -0.2285f));
                Prim(PrimitiveType.Cube, "Frame", hg.transform, new Vector3(0f, 0f, 0.001f), new Vector3(0.25f, 0.05f, 0.004f), mTrim);
                var pivot = Empty("FillPivot", hg.transform, new Vector3(-0.11f, 0f, -0.002f));
                var fill = Prim(PrimitiveType.Cube, "Fill", pivot.transform, new Vector3(0.11f, 0f, 0f), new Vector3(0.22f, 0.03f, 0.003f), mHealthFill);
                Label(hg.transform, "HULL", new Vector3(-0.085f, 0.04f, -0.002f), 0.1f);
                var readout = Label(hg.transform, "100", new Vector3(0.09f, 0.04f, -0.002f), 0.13f);
                var bar = hg.AddComponent<DashHealthBar>();
                bar.health = health;
                bar.fillPivot = pivot.transform;
                bar.fillRenderer = fill.GetComponent<MeshRenderer>();
                bar.readout = readout;

                // Warning lamps in the gap between the two dials, and the TC OFF button on the dash side.
                var lamps = Empty("WarningLamps", dash.transform, new Vector3(-0.38f, 0.04f, -0.2305f));
                var tcLamp = Prim(PrimitiveType.Cube, "TCLamp", lamps.transform, new Vector3(0f, 0.035f, 0f), new Vector3(0.028f, 0.026f, 0.002f), mWarnLamp);
                var tcOffLamp = Prim(PrimitiveType.Cube, "TCOffLamp", lamps.transform, new Vector3(0f, -0.002f, 0f), new Vector3(0.028f, 0.026f, 0.002f), mWarnLamp);
                LabelDark(lamps.transform, "TC", new Vector3(0f, 0.035f, -0.0015f), 0.075f);
                LabelDark(lamps.transform, "TC" + "\n" + "OFF", new Vector3(0f, -0.002f, -0.0015f), 0.05f);

                var btnRoot = Empty("TCOffButton", dash.transform, new Vector3(-0.66f, 0.0f, -0.226f));
                Prim(PrimitiveType.Cube, "Bezel", btnRoot.transform, new Vector3(0f, 0f, -0.002f), new Vector3(0.042f, 0.032f, 0.004f), mInterior);
                var btn = Empty("Button", btnRoot.transform, new Vector3(0f, 0f, -0.008f));
                Prim(PrimitiveType.Cube, "Cap", btn.transform, Vector3.zero, new Vector3(0.034f, 0.024f, 0.01f), mTrim);
                var led = Prim(PrimitiveType.Cube, "Led", btn.transform, new Vector3(0f, 0.0075f, -0.0052f), new Vector3(0.01f, 0.003f, 0.001f), mWarnLamp);
                Label(btn.transform, "TC OFF", new Vector3(0f, -0.003f, -0.0056f), 0.045f);
                Label(btnRoot.transform, "ASSISTS (B)", new Vector3(0f, 0.026f, -0.003f), 0.05f);

                var warn = lamps.AddComponent<DashWarningLights>();
                warn.car = controller;
                warn.tcLamp = tcLamp.GetComponent<MeshRenderer>();
                warn.tcOffLamp = tcOffLamp.GetComponent<MeshRenderer>();
                warn.button = btn.transform;
                warn.buttonLed = led.GetComponent<MeshRenderer>();
            }
            else
            {
                root.AddComponent<AICarDriver>();
            }

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

        /// <summary>Black text, for printing on top of a lamp.</summary>
        static TextMeshPro LabelDark(Transform parent, string text, Vector3 localPos, float fontSize)
        {
            var tmp = Label(parent, text, localPos, fontSize);
            tmp.color = new Color(0.05f, 0.04f, 0.03f);
            tmp.fontStyle = FontStyles.Bold;
            tmp.lineSpacing = -30f;
            return tmp;
        }

        static TextMeshPro Label(Transform parent, string text, Vector3 localPos, float fontSize)
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
            return tmp;
        }

        static ParticleSystem EngineSmoke(Transform car)
        {
            var go = Empty("EngineSmoke", car, new Vector3(0f, 0.42f, 1.6f), Quaternion.Euler(-80f, 0f, 0f));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.04f;
            main.maxParticles = 300;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.35f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
            color.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultParticleMaterial : null;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static GameObject BuildEnemyPrefab(SurfaceProfile defaultSurface, DriverArchetype archetype, string name,
            Color paint, float mass, float health, float powerKW, float torque)
        {
            var body = Mat("Enemy" + archetype + "Body", paint, 0.35f, 0.25f);
            var panel = Mat("Enemy" + archetype + "Panel", paint * 0.88f, 0.35f, 0.25f);
            var car = BuildCar(defaultSurface, name, false, body, panel);
            var controller = car.GetComponent<CarController>();
            controller.mass = mass;
            controller.maxPowerKW = powerKW;
            controller.maxWheelTorque = torque;
            car.GetComponent<Rigidbody>().mass = mass;
            car.GetComponent<CarHealth>().maxHealth = health;
            car.GetComponent<AICarDriver>().archetype = archetype;
            string path = Root + "/Prefabs/Enemy" + archetype + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(car, path);
            Object.DestroyImmediate(car);
            return prefab;
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
