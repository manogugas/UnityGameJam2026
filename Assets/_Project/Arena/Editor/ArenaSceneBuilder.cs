using System.Linq;
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
        const float SandHalf = 74f; // sand run-off right up to the walls: the road is the only asphalt
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
        // Garage behind the player's gate: the tunnel opens into a workshop room (gate-local coordinates).
        const float GarageDepth = 24f;
        const float GarageHalfWidth = 11f;
        const float GarageHeight = 8f;
        const float GarageCarZ = -34f;
        static readonly Vector3[] GateSides = { Vector3.back, Vector3.left };
        // Figure-8 track on the SW-NE diagonal: loops of radius LoopRadius centred at +-(LoopOffset, LoopOffset).
        // With LoopRadius == LoopOffset the straights run along the X and Z axes and cross at 90 degrees in the
        // middle of the arena.
        const float LoopOffset = 30f;
        const float LoopRadius = 30f;
        const float TrackHalfWidth = 8f;
        const float IslandRadius = 21f;
        static readonly System.Collections.Generic.List<ArenaGate> builtGates = new System.Collections.Generic.List<ArenaGate>();

        static Material mSand, mAsphalt, mBank, mConcrete, mRamp, mObstacle, mTyre, mCrate, mWreck;
        static Material mStone, mStone2, mGate, mClothRed, mClothPurple, mSkin;
        static Material mRoundel, mNumberGold, mArmour;
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
                BuildEnemyPrefab(asphalt, DriverArchetype.Rammer, "Rammer", new Color(0.55f, 0.12f, 0.09f), new Color(0.12f, 0.12f, 0.13f), 1250f, 60f, 125f, 2700f),
                BuildEnemyPrefab(asphalt, DriverArchetype.Flanker, "Flanker", new Color(0.86f, 0.63f, 0.12f), new Color(0.1f, 0.1f, 0.11f), 1100f, 50f, 120f, 2500f),
                BuildEnemyPrefab(asphalt, DriverArchetype.Bruiser, "Bruiser", new Color(0.22f, 0.25f, 0.2f), new Color(0.85f, 0.62f, 0.12f), 1900f, 110f, 165f, 3600f),
            };
            var champion = BuildEnemyPrefab(asphalt, DriverArchetype.Bruiser, "Champion", new Color(0.86f, 0.68f, 0.2f), new Color(0.55f, 0.07f, 0.06f), 2300f, 220f, 200f, 4200f, "EnemyChampion");
            var director = BuildDirector(car, enemyPrefabs);
            BuildIntro(car, playerGate, director);
            BuildCampaign(car, playerGate, director, new[] { enemyPrefabs[0], enemyPrefabs[1], enemyPrefabs[2], champion });
            BuildChaseCamera(car);
            BuildObjectives(director);
            BuildRace(director, car);

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
            mRoundel = Mat("NumberRoundel", new Color(0.95f, 0.94f, 0.9f), 0.3f);
            mNumberGold = Mat("NumberFavourGold", new Color(1f, 0.75f, 0.15f), 0.7f, 0.8f);
            mNumberGold.EnableKeyword("_EMISSION");
            mNumberGold.SetColor("_EmissionColor", new Color(0.8f, 0.5f, 0.05f));
            mArmour = Mat("ArmourSteel", new Color(0.28f, 0.29f, 0.3f), 0.35f, 0.7f);
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

            // Car glass is unlit tinted transparency: lit glass (premultiplied, specular preserved) picked up the
            // garage lights and ambient and turned milky, hiding the tablet held in front of the windscreen.
            string glassPath = MatDir + "/CarGlass.mat";
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            mGlass = AssetDatabase.LoadAssetAtPath<Material>(glassPath);
            if (mGlass == null)
            {
                mGlass = new Material(unlit);
                AssetDatabase.CreateAsset(mGlass, glassPath);
            }
            mGlass.shader = unlit;
            mGlass.shaderKeywords = new string[0];
            mGlass.SetColor("_BaseColor", new Color(0.55f, 0.65f, 0.72f, 0.07f));
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
            EditorUtility.SetDirty(mGlass);

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
            foreach (var side in GateSides) builtGates.Add(Gate(arena, side, asphalt, side == GateSides[0]));

            BuildStands(arena);

            // Figure-8 track: an asphalt road looping round two round infield islands, its straights crossing
            // at right angles in the middle (where the T-bones happen). The NE loop runs up onto the banked
            // north and east walls; off the road is loose sand run-off. Both gates open beside the SW loop.
            var track = Empty("Track", arena, Vector3.zero).transform;
            var centreline = Figure8Centreline();
            var road = MeshObj("Road", track, Vector3.zero, SaveMesh(RibbonMesh(centreline, TrackHalfWidth, 0.02f, "Figure8Road"), "Figure8Road"), mAsphalt);
            road.AddComponent<MeshCollider>();
            road.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Tag(road, asphalt);

            // Race line + start/finish: 12 m up the north-south straight, just past the crossing.
            var raceTrack = track.gameObject.AddComponent<RaceTrack>();
            raceTrack.points = centreline.ToArray();
            raceTrack.halfWidth = TrackHalfWidth;
            raceTrack.startIndex = 6;
            StartLine(track, centreline[6], (centreline[7] - centreline[5]).normalized);

            var layout = track.gameObject.AddComponent<ArenaLayout>();
            var ne = new Vector2(LoopOffset, LoopOffset);
            var sw = new Vector2(-LoopOffset, -LoopOffset);
            layout.islandCentres = new[] { ne, sw };
            layout.islandRadii = new[] { IslandRadius, IslandRadius };

            var kerbRed = Mat("KerbRed", new Color(0.78f, 0.12f, 0.1f), 0.2f);
            var kerbWhite = Mat("KerbWhite", new Color(0.92f, 0.9f, 0.86f), 0.2f);
            var kerbs = Empty("Kerbs", track, Vector3.zero).transform;
            float outerKerb = LoopRadius + TrackHalfWidth + 0.45f;
            KerbArc(kerbs, ne, outerKerb, -90f, 180f, kerbRed, kerbWhite);
            KerbArc(kerbs, sw, outerKerb, 90f, 360f, kerbRed, kerbWhite);
            foreach (var c in new[] { ne, sw })
            {
                RoundIsland(track, c, IslandRadius);
                KerbArc(kerbs, c, IslandRadius + 0.45f, 0f, 360f, kerbRed, kerbWhite);
            }

            // Tyre walls on the outside of each loop's far side (loose: plough through them).
            var props = Empty("Props", arena, Vector3.zero).transform;
            int stack = 0;
            float tyreR = LoopRadius + TrackHalfWidth + 2.6f;
            foreach (float ang in new[] { 10f, 45f, 80f })
            {
                float a = ang * Mathf.Deg2Rad;
                TyreStack(props, new Vector3(ne.x + Mathf.Cos(a) * tyreR, 0f, ne.y + Mathf.Sin(a) * tyreR), stack++);
                TyreStack(props, new Vector3(sw.x - Mathf.Cos(a) * tyreR, 0f, sw.y - Mathf.Sin(a) * tyreR), stack++);
            }

            // A crate pile in the NW run-off, and a few old wrecks off the track.
            int n = 0;
            for (int row = 0; row < 3; row++)
                for (int c = 0; c < 3 - row; c++)
                {
                    var crate = Prim(PrimitiveType.Cube, "Crate" + n++, props,
                        new Vector3(-38f + (c - (2 - row) * 0.5f) * 1.05f, 0.5f + row * 1.0f, 38f), Vector3.one, mCrate, true);
                    Prop(crate, 40f);
                }
            Wreck("Wreck0", props, new Vector3(-62f, 0f, 62f), 40f);
            Wreck("Wreck1", props, new Vector3(48f, 0f, -48f), -60f);
            Wreck("Wreck2", props, new Vector3(56f, 0f, -20f), 15f);
        }

        /// <summary>Chequered start/finish strip across the road.</summary>
        static void StartLine(Transform parent, Vector3 at, Vector3 forward)
        {
            var white = Mat("CheckerWhite", new Color(0.92f, 0.92f, 0.9f), 0.2f);
            var black = Mat("CheckerBlack", new Color(0.05f, 0.05f, 0.05f), 0.2f);
            var root = Empty("StartFinish", parent, at + Vector3.up * 0.025f, Quaternion.LookRotation(forward, Vector3.up)).transform;
            const float cell = 0.8f;
            int across = Mathf.RoundToInt(TrackHalfWidth * 2f / cell);
            for (int x = 0; x < across; x++)
                for (int z = 0; z < 2; z++)
                {
                    var q = Prim(PrimitiveType.Cube, "Cell", root, new Vector3(-TrackHalfWidth + (x + 0.5f) * cell, 0f, (z - 0.5f) * cell),
                        new Vector3(cell, 0.01f, cell), (x + z) % 2 == 0 ? white : black);
                    q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
        }

        /// <summary>Centreline of the figure 8: north up the Z straight, round the NE loop, west along the X
        /// straight through the crossing, round the SW loop, and back north to the crossing.</summary>
        static System.Collections.Generic.List<Vector3> Figure8Centreline()
        {
            var pts = new System.Collections.Generic.List<Vector3>();
            float L = LoopOffset, R = LoopRadius;
            void Line(Vector2 a, Vector2 b)
            {
                int count = Mathf.Max(1, Mathf.CeilToInt((b - a).magnitude / 2f));
                for (int i = 0; i < count; i++)
                {
                    var q = Vector2.Lerp(a, b, i / (float)count);
                    pts.Add(new Vector3(q.x, 0f, q.y));
                }
            }
            void Arc(Vector2 c, float fromDeg, float toDeg)
            {
                int count = Mathf.CeilToInt(Mathf.Abs(toDeg - fromDeg) / 3f);
                for (int i = 0; i < count; i++)
                {
                    float a = Mathf.Lerp(fromDeg, toDeg, i / (float)count) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(c.x + Mathf.Cos(a) * R, 0f, c.y + Mathf.Sin(a) * R));
                }
            }
            Line(Vector2.zero, new Vector2(0f, L));
            Arc(new Vector2(L, L), 180f, -90f);
            Line(new Vector2(L, 0f), new Vector2(-L, 0f));
            Arc(new Vector2(-L, -L), 90f, 360f);
            Line(new Vector2(0f, -L), Vector2.zero);
            return pts;
        }

        /// <summary>Flat road strip along a closed centreline (faces up, UVs along the road).</summary>
        static Mesh RibbonMesh(System.Collections.Generic.List<Vector3> pts, float halfWidth, float y, string name)
        {
            int n = pts.Count;
            var verts = new Vector3[n * 2];
            var uvs = new Vector2[n * 2];
            var tris = new int[n * 6];
            float v = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = pts[(i - 1 + n) % n], next = pts[(i + 1) % n];
                Vector3 t = (next - prev).normalized;
                Vector3 left = new Vector3(-t.z, 0f, t.x);
                if (i > 0) v += Vector3.Distance(pts[i], pts[i - 1]) / (halfWidth * 2f);
                verts[i * 2] = pts[i] + left * halfWidth + Vector3.up * y;
                verts[i * 2 + 1] = pts[i] - left * halfWidth + Vector3.up * y;
                uvs[i * 2] = new Vector2(0f, v);
                uvs[i * 2 + 1] = new Vector2(1f, v);
                int j = (i + 1) % n;
                int a = i * 2, b = i * 2 + 1, c = j * 2, d = j * 2 + 1;
                tris[i * 6] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
            }
            var mesh = new Mesh { name = name, vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Raised round infield: stone plinth, an obelisk and a ring of columns with gold ornaments.</summary>
        static void RoundIsland(Transform parent, Vector2 c, float radius)
        {
            var root = Empty("Island", parent, new Vector3(c.x, 0f, c.y)).transform;
            var plinth = Prim(PrimitiveType.Cylinder, "Plinth", root, new Vector3(0f, 0.6f, 0f), new Vector3(radius * 2f, 0.6f, radius * 2f), mStone2);
            plinth.AddComponent<MeshCollider>().sharedMesh = plinth.GetComponent<MeshFilter>().sharedMesh;
            Prim(PrimitiveType.Cylinder, "Cap", root, new Vector3(0f, 1.22f, 0f), new Vector3(radius * 2f - 1f, 0.03f, radius * 2f - 1f), mStone);
            Prim(PrimitiveType.Cube, "ObeliskBase", root, new Vector3(0f, 2f, 0f), new Vector3(3f, 1.6f, 3f), mStone, true);
            Prim(PrimitiveType.Cube, "Obelisk", root, new Vector3(0f, 8.8f, 0f), new Vector3(1.4f, 12f, 1.4f), mStone, true);
            Prim(PrimitiveType.Cube, "ObeliskTip", root, new Vector3(0f, 15.1f, 0f), new Vector3(0.9f, 0.9f, 0.9f), mNumberGold, false, Quaternion.Euler(45f, 0f, 45f));
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius * 0.6f);
                Prim(PrimitiveType.Cylinder, "Column", root, pos + Vector3.up * 4.2f, new Vector3(1.1f, 3f, 1.1f), mStone, true);
                Prim(PrimitiveType.Sphere, "Ornament", root, pos + Vector3.up * 7.7f, new Vector3(1.2f, 1.2f, 1.2f), mNumberGold);
                if (i % 2 == 0)
                    Prim(PrimitiveType.Cube, "Banner", root, pos * 0.55f + Vector3.up * 3f, new Vector3(0.1f, 2.4f, 4f),
                        i % 4 == 0 ? mClothRed : mClothPurple, false, Quaternion.LookRotation(pos));
            }
        }

        /// <summary>Alternating red/white kerb tiles along an arc (visual only).</summary>
        static void KerbArc(Transform parent, Vector2 c, float radius, float fromDeg, float toDeg, Material red, Material white)
        {
            float arcLen = Mathf.Abs(toDeg - fromDeg) * Mathf.Deg2Rad * radius;
            int count = Mathf.Max(1, Mathf.CeilToInt(arcLen / 2.5f));
            float tile = arcLen / count;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Lerp(fromDeg, toDeg, (i + 0.5f) / count) * Mathf.Deg2Rad;
                var pos = new Vector3(c.x + Mathf.Cos(a) * radius, 0.03f, c.y + Mathf.Sin(a) * radius);
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                var go = Prim(PrimitiveType.Cube, "Kerb", parent, pos, new Vector3(0.9f, 0.06f, tile * 0.98f), i % 2 == 0 ? red : white,
                    false, Quaternion.LookRotation(tangent));
                go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        static void TyreStack(Transform parent, Vector3 pos, int index)
        {
            for (int k = 0; k < 3; k++)
            {
                var t = Prim(PrimitiveType.Cylinder, $"Tyre{index}_{k}", parent, pos + Vector3.up * (0.15f + k * 0.3f),
                    new Vector3(0.75f, 0.15f, 0.75f), mTyre, true);
                Object.DestroyImmediate(t.GetComponent<Collider>());
                var mc = t.AddComponent<MeshCollider>();
                mc.convex = true;
                Prop(t, 18f);
            }
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
        static ArenaGate Gate(Transform parent, Vector3 n, SurfaceProfile floorSurface, bool garage = false)
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
            if (garage) BuildGarageRoom(g, floorSurface);
            else Prim(PrimitiveType.Cube, "TunnelEnd", g, new Vector3(0f, GateHeight * 0.5f, -2f - TunnelLength - 0.25f), new Vector3(2f * w + 1f, GateHeight, 0.5f), mGate, true);
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
            gate.spawnPoint = Empty("SpawnPoint", g, new Vector3(0f, 0.9f, garage ? GarageCarZ : -2f - TunnelLength + 4.5f)).transform;
            gate.entryPoint = Empty("EntryPoint", g, new Vector3(0f, 0.9f, 16f)).transform;
            return gate;
        }

        /// <summary>
        /// Workshop behind the player's gate, where you wait between matches: brick walls, timber roof,
        /// sunlit windows with light shafts, hanging lamps, laurel banners, a car lift with a wreck on it,
        /// shelves of parts, tool chests, a bust and a legionary's armour. The middle lane stays clear for
        /// driving out; the tablet arm stands to the right of the car (built with the campaign).
        /// </summary>
        static void BuildGarageRoom(Transform g, SurfaceProfile floorSurface)
        {
            var room = Empty("Garage", g, Vector3.zero).transform;
            float z0 = -2f - TunnelLength, z1 = z0 - GarageDepth, zc = (z0 + z1) * 0.5f;
            float hw = GarageHalfWidth, h = GarageHeight, open = GateHalfWidth + 0.5f;

            var brickTex = ArenaArt.Bricks();
            var brickWide = TexMat("GarageBrickWide", brickTex, new Vector2(6f, 2f), new Color(1f, 0.95f, 0.9f));
            var brickNarrow = TexMat("GarageBrickNarrow", brickTex, new Vector2(2f, 2f), new Color(1f, 0.95f, 0.9f));
            var floorMat = Mat("GarageFloor", new Color(0.3f, 0.29f, 0.27f), 0.25f);
            var wood = Mat("GarageWood", new Color(0.28f, 0.18f, 0.1f), 0.15f);
            var redPaint = Mat("GarageRedPaint", new Color(0.62f, 0.08f, 0.06f), 0.45f, 0.2f);
            var rust = Mat("GarageRust", new Color(0.42f, 0.24f, 0.14f), 0.1f, 0.3f);
            var steel = Mat("GarageSteel", new Color(0.36f, 0.37f, 0.39f), 0.5f, 0.6f);
            var yellow = Mat("GarageLine", new Color(0.85f, 0.65f, 0.1f), 0.3f);
            var marble = Mat("GarageMarble", new Color(0.82f, 0.8f, 0.76f), 0.4f);
            var bronze = Mat("GarageBronze", new Color(0.62f, 0.45f, 0.22f), 0.6f, 0.8f);
            var windowGlow = GlowMat("GarageWindow", new Color(1f, 0.85f, 0.6f), new Color(2.4f, 1.8f, 1.1f), false);
            var bulb = GlowMat("GarageBulb", new Color(1f, 0.9f, 0.7f), new Color(3f, 2.4f, 1.6f), false);
            var bannerMat = TexMat("BannerLaurel", ArenaArt.LaurelBanner(), Vector2.one, Color.white);
            bannerMat.SetFloat("_AlphaClip", 1f);
            bannerMat.SetFloat("_Cutoff", 0.5f);
            bannerMat.EnableKeyword("_ALPHATEST_ON");
            bannerMat.SetFloat("_Cull", 0f);

            // Shell
            Tag(Prim(PrimitiveType.Cube, "Floor", room, new Vector3(0f, -0.1f, zc), new Vector3(hw * 2f + 1f, 0.2f, GarageDepth + 0.5f), floorMat, true), floorSurface);
            foreach (float sx in new[] { -1f, 1f })
            {
                float segW = hw + 0.5f - open;
                Prim(PrimitiveType.Cube, "FrontWall", room, new Vector3(sx * (open + segW * 0.5f), h * 0.5f, z0 - 0.25f), new Vector3(segW, h, 0.5f), brickNarrow, true);
                Prim(PrimitiveType.Cube, "SideWall", room, new Vector3(sx * (hw + 0.25f), h * 0.5f, zc), new Vector3(0.5f, h, GarageDepth + 1f), brickWide, true);
            }
            Prim(PrimitiveType.Cube, "Lintel", room, new Vector3(0f, (GateHeight + h) * 0.5f, z0 - 0.25f), new Vector3(open * 2f, h - GateHeight, 0.5f), brickNarrow, true);
            Prim(PrimitiveType.Cube, "BackWall", room, new Vector3(0f, h * 0.5f, z1 - 0.25f), new Vector3(hw * 2f + 1f, h, 0.5f), brickWide, true);
            Prim(PrimitiveType.Cube, "Roof", room, new Vector3(0f, h + 0.25f, zc), new Vector3(hw * 2f + 1f, 0.5f, GarageDepth + 1f), wood, true);
            for (float z = z0 - 2f; z > z1; z -= 3.5f)
                Prim(PrimitiveType.Cube, "Beam", room, new Vector3(0f, h - 0.3f, z), new Vector3(hw * 2f, 0.45f, 0.35f), wood);
            foreach (float x in new[] { -hw * 0.5f, hw * 0.5f })
                Prim(PrimitiveType.Cube, "Purlin", room, new Vector3(x, h - 0.65f, zc), new Vector3(0.3f, 0.3f, GarageDepth), wood);

            // Floor markings: lane edges for the drive out, hazard stripes at the tunnel mouth.
            foreach (float sx in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, "LaneLine", room, new Vector3(sx * 4.2f, 0.005f, zc), new Vector3(0.14f, 0.01f, GarageDepth - 1f), yellow);
            for (int i = 0; i < 8; i++)
                Prim(PrimitiveType.Cube, "Hazard", room, new Vector3(-3.5f + i, 0.006f, z0 - 0.8f), new Vector3(0.45f, 0.01f, 0.9f), yellow, false, Quaternion.Euler(0f, 35f, 0f));

            // Sunlit windows high on the left wall; spot lights throw the sun patches across the floor.
            for (int i = 0; i < 4; i++)
            {
                float z = z0 - 3f - i * 5.5f;
                Prim(PrimitiveType.Cube, "Window", room, new Vector3(-hw + 0.02f, 5.6f, z), new Vector3(0.05f, 2.2f, 1.6f), windowGlow);
                Prim(PrimitiveType.Cube, "WindowBar", room, new Vector3(-hw + 0.05f, 5.6f, z), new Vector3(0.05f, 2.2f, 0.08f), wood);
                Prim(PrimitiveType.Cube, "WindowSill", room, new Vector3(-hw + 0.1f, 4.45f, z), new Vector3(0.2f, 0.1f, 1.8f), wood);
                if (i % 2 == 0)
                {
                    var sun = Empty("WindowLight", room, new Vector3(-hw + 0.6f, 5.6f, z - 2.7f), Quaternion.Euler(52f, 90f, 0f)).AddComponent<Light>();
                    sun.type = LightType.Spot;
                    sun.spotAngle = 55f;
                    sun.range = 16f;
                    sun.intensity = 9f;
                    sun.color = new Color(1f, 0.78f, 0.5f);
                }
            }

            // Hanging work lamps.
            foreach (float z in new[] { z0 - 4f, GarageCarZ + 3f, z1 + 4f })
            {
                foreach (float x in new[] { -6.5f, 6.5f })
                {
                    Prim(PrimitiveType.Cylinder, "LampCord", room, new Vector3(x, h - 0.9f, z), new Vector3(0.03f, 0.6f, 0.03f), steel);
                    Prim(PrimitiveType.Cylinder, "LampShade", room, new Vector3(x, h - 1.6f, z), new Vector3(0.9f, 0.12f, 0.9f), steel);
                    Prim(PrimitiveType.Sphere, "LampBulb", room, new Vector3(x, h - 1.75f, z), new Vector3(0.3f, 0.2f, 0.3f), bulb);
                    var lamp = Empty("LampLight", room, new Vector3(x, h - 2f, z)).AddComponent<Light>();
                    lamp.type = LightType.Point;
                    lamp.range = 11f;
                    lamp.intensity = 3.2f;
                    lamp.color = new Color(1f, 0.8f, 0.55f);
                }
            }

            // Laurel banners: over the tunnel mouth, and on the walls.
            Banner(room, bannerMat, wood, new Vector3(0f, GateHeight + 1.55f, z0 - 0.52f), 0f, new Vector2(1.6f, 2.6f));
            Banner(room, bannerMat, wood, new Vector3(-7.75f, 5.2f, z0 - 0.52f), 0f, new Vector2(1.8f, 3.4f));
            Banner(room, bannerMat, wood, new Vector3(7.75f, 5.2f, z0 - 0.52f), 0f, new Vector2(1.8f, 3.4f));
            Banner(room, bannerMat, wood, new Vector3(hw - 0.02f, 5.4f, z0 - 9f), 90f, new Vector2(1.8f, 3.4f));
            Banner(room, bannerMat, wood, new Vector3(0f, 5.2f, z1 + 0.02f), 180f, new Vector2(2.2f, 3.8f));

            // Left: car lift with a stripped wreck up on it, tool chests, a workbench.
            var lift = Empty("CarLift", room, new Vector3(-7.6f, 0f, z0 - 5f)).transform;
            foreach (float sx in new[] { -1f, 1f })
            {
                Prim(PrimitiveType.Cube, "Post", lift, new Vector3(sx * 1.9f, 2.1f, 0f), new Vector3(0.4f, 4.2f, 0.45f), redPaint);
                Prim(PrimitiveType.Cube, "PostCap", lift, new Vector3(sx * 1.9f, 4.3f, 0f), new Vector3(0.6f, 0.2f, 0.6f), steel);
                Prim(PrimitiveType.Cube, "LiftArm", lift, new Vector3(sx * 1.1f, 1.95f, 0f), new Vector3(1.2f, 0.12f, 3.6f), steel);
            }
            JunkCar(lift, new Vector3(0f, 2.05f, 0f), 0f, rust, steel, true);
            JunkCar(room, new Vector3(-8.3f, 0f, GarageCarZ - 4f), 160f, redPaint, rust, false);
            ToolChest(room, new Vector3(-10.3f, 0f, z0 - 10.5f), 90f, redPaint, steel);
            ToolChest(room, new Vector3(9.6f, 0f, z0 - 12f), -90f, redPaint, steel);
            var bench = Empty("Workbench", room, new Vector3(-10.2f, 0f, GarageCarZ + 1f), Quaternion.Euler(0f, 90f, 0f)).transform;
            Prim(PrimitiveType.Cube, "Top", bench, new Vector3(0f, 0.95f, 0f), new Vector3(2.6f, 0.1f, 0.9f), wood);
            foreach (float sx in new[] { -1.2f, 1.2f })
                Prim(PrimitiveType.Cube, "Leg", bench, new Vector3(sx, 0.45f, 0f), new Vector3(0.1f, 0.9f, 0.8f), wood);
            Prim(PrimitiveType.Cube, "Vice", bench, new Vector3(0.9f, 1.12f, 0.2f), new Vector3(0.25f, 0.25f, 0.3f), steel);
            Prim(PrimitiveType.Cylinder, "Gear", bench, new Vector3(-0.5f, 1.04f, 0f), new Vector3(0.5f, 0.04f, 0.5f), steel);

            // Shelving with parts along both walls.
            Shelf(room, new Vector3(-10.4f, 0f, z0 - 1.6f), 90f, wood, steel, rust, 1);
            Shelf(room, new Vector3(10.4f, 0f, z0 - 1.8f), -90f, wood, steel, rust, 2);
            Shelf(room, new Vector3(10.4f, 0f, z0 - 7.6f), -90f, wood, steel, rust, 3);
            Shelf(room, new Vector3(-4f, 0f, z1 + 0.6f), 0f, wood, steel, rust, 4);
            Shelf(room, new Vector3(4f, 0f, z1 + 0.6f), 0f, wood, steel, rust, 5);

            // Right: wrecked jalopy, a bust on a plinth, a legionary's armour on a stand.
            JunkCar(room, new Vector3(8.2f, 0f, z0 - 4.6f), -25f, rust, redPaint, false);
            var bust = Empty("Bust", room, new Vector3(5.6f, 0f, z0 - 1.4f)).transform;
            Prim(PrimitiveType.Cube, "Plinth", bust, new Vector3(0f, 0.6f, 0f), new Vector3(0.7f, 1.2f, 0.7f), marble);
            Prim(PrimitiveType.Cube, "Shoulders", bust, new Vector3(0f, 1.45f, 0f), new Vector3(0.8f, 0.5f, 0.4f), marble);
            Prim(PrimitiveType.Cylinder, "Neck", bust, new Vector3(0f, 1.78f, 0f), new Vector3(0.22f, 0.12f, 0.22f), marble);
            Prim(PrimitiveType.Sphere, "Head", bust, new Vector3(0f, 2.08f, 0f), new Vector3(0.36f, 0.44f, 0.4f), marble);
            Prim(PrimitiveType.Cube, "Nose", bust, new Vector3(0f, 2.06f, 0.2f), new Vector3(0.07f, 0.12f, 0.08f), marble);
            var armour = Empty("Armour", room, new Vector3(9.8f, 0f, GarageCarZ + 4.5f), Quaternion.Euler(0f, -110f, 0f)).transform;
            Prim(PrimitiveType.Cylinder, "Pole", armour, new Vector3(0f, 0.9f, 0f), new Vector3(0.08f, 0.9f, 0.08f), wood);
            Prim(PrimitiveType.Cylinder, "Base", armour, new Vector3(0f, 0.04f, 0f), new Vector3(0.7f, 0.04f, 0.7f), wood);
            Prim(PrimitiveType.Cube, "Cuirass", armour, new Vector3(0f, 1.95f, 0f), new Vector3(0.62f, 0.75f, 0.36f), steel);
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cube, "Band", armour, new Vector3(0f, 1.62f + i * 0.17f, 0.185f), new Vector3(0.64f, 0.04f, 0.02f), bronze);
            foreach (float sx in new[] { -1f, 1f })
                Prim(PrimitiveType.Cube, "Pauldron", armour, new Vector3(sx * 0.4f, 2.27f, 0f), new Vector3(0.28f, 0.14f, 0.4f), steel, false, Quaternion.Euler(0f, 0f, sx * -20f));
            Prim(PrimitiveType.Sphere, "Helmet", armour, new Vector3(0f, 2.62f, 0f), new Vector3(0.4f, 0.42f, 0.44f), bronze);
            Prim(PrimitiveType.Cube, "Crest", armour, new Vector3(0f, 2.9f, -0.02f), new Vector3(0.08f, 0.22f, 0.5f), redPaint);
            Prim(PrimitiveType.Cube, "CheekGuards", armour, new Vector3(0f, 2.5f, 0.14f), new Vector3(0.42f, 0.18f, 0.08f), bronze);

            // Loose bits on the floor: tyre stacks, barrels, crates, engine blocks.
            foreach (var pos in new[] { new Vector3(-5.3f, 0f, z0 - 1.2f), new Vector3(5f, 0f, z0 - 9.5f), new Vector3(-5.2f, 0f, z1 + 2.2f) })
                for (int k = 0; k < 3; k++)
                    Prim(PrimitiveType.Cylinder, "Tyre", room, pos + Vector3.up * (0.15f + k * 0.3f), new Vector3(0.75f, 0.15f, 0.75f), mTyre);
            foreach (var pos in new[] { new Vector3(6.8f, 0f, GarageCarZ + 0.2f), new Vector3(7.5f, 0f, GarageCarZ - 0.6f), new Vector3(-6.2f, 0f, z0 - 11f) })
                Prim(PrimitiveType.Cylinder, "Barrel", room, pos + Vector3.up * 0.55f, new Vector3(0.6f, 0.55f, 0.6f), pos.x > 0f ? redPaint : steel);
            foreach (var pos in new[] { new Vector3(-6.5f, 0f, z0 - 1.4f), new Vector3(9.4f, 0f, z1 + 2f), new Vector3(8.5f, 0f, z1 + 2.2f) })
                Prim(PrimitiveType.Cube, "Crate", room, pos + Vector3.up * 0.45f, Vector3.one * 0.9f, mCrate, false, Quaternion.Euler(0f, pos.x * 13f, 0f));
            foreach (var pos in new[] { new Vector3(-6.2f, 0f, z0 - 7.6f), new Vector3(6.6f, 0f, z0 - 13f) })
                EngineBlock(room, pos, steel, rust);
        }

        static Material TexMat(string name, Texture2D tex, Vector2 tiling, Color tint)
        {
            var m = Mat(name, tint, 0.15f);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Banner(Transform parent, Material mat, Material rod, Vector3 pos, float yaw, Vector2 size)
        {
            var root = Empty("Banner", parent, pos, Quaternion.Euler(0f, yaw, 0f)).transform;
            var q = Prim(PrimitiveType.Quad, "Cloth", root, Vector3.zero, new Vector3(size.x, size.y, 1f), mat);
            q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
            Prim(PrimitiveType.Cylinder, "Rod", root, new Vector3(0f, size.y * 0.5f + 0.02f, -0.03f), new Vector3(0.06f, size.x * 0.56f, 0.06f), rod,
                false, Quaternion.Euler(0f, 0f, 90f));
        }

        /// <summary>Boxy old car (decor only, no colliders). Stripped = no doors/glass, wheels missing.</summary>
        static void JunkCar(Transform parent, Vector3 pos, float yaw, Material body, Material trim, bool stripped)
        {
            var root = Empty("JunkCar", parent, pos, Quaternion.Euler(0f, yaw, 0f)).transform;
            float lift = stripped ? 0.05f : 0.32f;
            Prim(PrimitiveType.Cube, "Lower", root, new Vector3(0f, lift + 0.35f, 0f), new Vector3(1.8f, 0.6f, 4.3f), body);
            Prim(PrimitiveType.Cube, "Cabin", root, new Vector3(0f, lift + 0.95f, -0.35f), new Vector3(1.6f, 0.6f, 2f), stripped ? trim : body);
            Prim(PrimitiveType.Cube, "Bumper", root, new Vector3(0f, lift + 0.2f, 2.2f), new Vector3(1.85f, 0.18f, 0.15f), trim);
            if (!stripped)
            {
                Prim(PrimitiveType.Cube, "Glass", root, new Vector3(0f, lift + 0.95f, 0.66f), new Vector3(1.5f, 0.5f, 0.04f), mGlass, false, Quaternion.Euler(-30f, 0f, 0f));
                foreach (var w in new[] { new Vector3(-0.85f, 0f, 1.35f), new Vector3(0.85f, 0f, 1.35f), new Vector3(-0.85f, 0f, -1.35f) })
                    Prim(PrimitiveType.Cylinder, "Wheel", root, w + Vector3.up * 0.34f, new Vector3(0.66f, 0.12f, 0.66f), mTyre, false, Quaternion.Euler(0f, 0f, 90f));
                Prim(PrimitiveType.Cube, "Hood", root, new Vector3(0.1f, lift + 0.95f, 1.3f), new Vector3(1.6f, 0.05f, 1.3f), body, false, Quaternion.Euler(-35f, 8f, 0f));
            }
        }

        static void ToolChest(Transform parent, Vector3 pos, float yaw, Material paint, Material steel)
        {
            var root = Empty("ToolChest", parent, pos, Quaternion.Euler(0f, yaw, 0f)).transform;
            Prim(PrimitiveType.Cube, "Body", root, new Vector3(0f, 0.55f, 0f), new Vector3(1.3f, 1.1f, 0.6f), paint);
            for (int i = 0; i < 5; i++)
                Prim(PrimitiveType.Cube, "Handle", root, new Vector3(0f, 0.25f + i * 0.2f, 0.31f), new Vector3(0.9f, 0.03f, 0.03f), steel);
            Prim(PrimitiveType.Cube, "Top", root, new Vector3(0f, 1.32f, 0f), new Vector3(1.2f, 0.42f, 0.5f), paint);
        }

        static void Shelf(Transform parent, Vector3 pos, float yaw, Material wood, Material steel, Material rust, int seed)
        {
            var root = Empty("Shelf", parent, pos, Quaternion.Euler(0f, yaw, 0f)).transform;
            var rng = new System.Random(seed * 97);
            foreach (float sx in new[] { -1.45f, 1.45f })
                Prim(PrimitiveType.Cube, "Upright", root, new Vector3(sx, 1.5f, 0f), new Vector3(0.08f, 3f, 0.6f), wood);
            for (int level = 0; level < 4; level++)
            {
                float y = 0.2f + level * 0.85f;
                Prim(PrimitiveType.Cube, "Board", root, new Vector3(0f, y, 0f), new Vector3(3f, 0.06f, 0.6f), wood);
                float x = -1.25f;
                while (x < 1.2f)
                {
                    float wdt = 0.25f + (float)rng.NextDouble() * 0.4f;
                    float hgt = 0.2f + (float)rng.NextDouble() * 0.45f;
                    int kind = rng.Next(3);
                    var m = kind == 0 ? steel : kind == 1 ? rust : mCrate;
                    if (kind == 2 && rng.Next(2) == 0)
                        Prim(PrimitiveType.Cylinder, "Part", root, new Vector3(x + wdt * 0.5f, y + hgt * 0.5f + 0.03f, 0f), new Vector3(wdt, hgt * 0.5f, wdt), steel);
                    else
                        Prim(PrimitiveType.Cube, "Part", root, new Vector3(x + wdt * 0.5f, y + hgt * 0.5f + 0.03f, 0f), new Vector3(wdt * 0.9f, hgt, 0.45f), m);
                    x += wdt + 0.06f;
                }
            }
        }

        static void EngineBlock(Transform parent, Vector3 pos, Material steel, Material rust)
        {
            var root = Empty("EngineBlock", parent, pos, Quaternion.Euler(0f, pos.z * 17f, 0f)).transform;
            Prim(PrimitiveType.Cube, "Pallet", root, new Vector3(0f, 0.06f, 0f), new Vector3(1.2f, 0.12f, 1f), mCrate);
            Prim(PrimitiveType.Cube, "Block", root, new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.6f, 0.8f), steel);
            Prim(PrimitiveType.Cube, "Head", root, new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 0.2f, 0.85f), rust);
            for (int i = 0; i < 4; i++)
                Prim(PrimitiveType.Cylinder, "Pipe", root, new Vector3(0.38f, 0.6f, -0.3f + i * 0.2f), new Vector3(0.07f, 0.12f, 0.07f), rust, false, Quaternion.Euler(0f, 0f, 90f));
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
            director.layout = Object.FindAnyObjectByType<ArenaLayout>();
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
            // Priority stays 0: IntroCinematic raises it when the intro plays (the campaign starts in the garage).

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

            var vis = BuildRacerBody(t, rb, player);
            var bodyRoot = vis.bodyRoot;

            // ---- Wheels
            var wheels = new CarWheel[4];
            string[] names = { "FL", "FR", "RL", "RR" };
            for (int i = 0; i < 4; i++)
            {
                var mount = Empty(names[i], t, vis.wheelMounts[i]);
                var w = mount.AddComponent<CarWheel>();
                w.axle = i < 2 ? CarWheel.Axle.Front : CarWheel.Axle.Rear;
                var visual = Empty("Visual", mount.transform, Vector3.down * w.restLength);
                if (vis.wheelMeshes != null)
                {
                    var wm = vis.wheelMeshes[i];
                    wm.SetParent(visual.transform, false);
                    wm.localPosition = Vector3.zero;
                    wm.localRotation = Quaternion.identity;
                }
                else
                {
                    Prim(PrimitiveType.Cylinder, "Tyre", visual.transform, Vector3.zero, new Vector3(0.68f, 0.12f, 0.68f), mTyre, false, Quaternion.Euler(0f, 0f, 90f));
                    Prim(PrimitiveType.Cube, "Hubcap", visual.transform, new Vector3(0.125f * Mathf.Sign(vis.wheelMounts[i].x), 0f, 0f), new Vector3(0.01f, 0.3f, 0.08f), mGauge);
                }
                w.visual = visual.transform;
                wheels[i] = w;
            }

            // ---- Driver head / camera
            GameObject head = null;
            if (player)
            {
                // The driver's view is a Cinemachine camera driven by CarCamera moving this transform;
                // the scene's main camera (CinemachineBrain) renders it and blends to/from the intro shots.
                head = Empty("DriverHead", t, vis.headPos);
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
            controller.steeringWheel = vis.spin;
            controller.defaultSurface = defaultSurface;

            var damage = root.AddComponent<CarDamage>();
            damage.input = input;
            damage.deformables = vis.deformables.ToArray();

            if (player)
            {
                var gauges = vis.dash.gameObject.AddComponent<DashGauges>();
                gauges.car = controller;
                gauges.speedNeedle = vis.speedNeedle;
                gauges.rpmNeedle = vis.rpmNeedle;
                gauges.maxSpeedKmh = 200f;
                gauges.maxRpm = 8000f;
            }
            damage.parts = vis.parts.ToArray();
            damage.hood = vis.hood;
            damage.hoodLatch = vis.hoodLatch;
            if (player) damage.topHeight = vis.topHeight;

            var interiorDamage = root.AddComponent<InteriorDamage>();
            interiorDamage.damage = damage;
            interiorDamage.carBody = rb;
            interiorDamage.windscreen = vis.wsCracks;
            interiorDamage.dashCracks = vis.dashCracks;
            interiorDamage.dashboard = vis.dash;
            interiorDamage.gloveboxHinge = vis.glove;
            interiorDamage.sunVisorHinge = vis.visor;

            if (vis.racer) BuildRacerNumberPlates(root, vis); else BuildNumberPlates(root, bodyRoot);

            var health = root.AddComponent<CarHealth>();
            health.car = controller;
            health.damage = damage;
            health.isPlayer = player;
            health.incomingDamageScale = player ? 0.8f : 1f;
            health.displayName = player ? "You" : name;
            health.smoke = EngineSmoke(t, vis.smokePos);

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

            BuildGun(root, vis, player, controller, health);
            if (player) root.AddComponent<CarBoost>();

            if (player)
            {
                var camCtl = head.AddComponent<CarCamera>();
                camCtl.vcam = head.GetComponent<CinemachineCamera>();
                root.AddComponent<SpeedEffects>().driverCamera = camCtl;
                camCtl.car = controller;
                camCtl.input = input;
                camCtl.damage = damage;

                var hud = root.AddComponent<CarHud>();
                hud.car = controller;
                hud.damage = damage;
                hud.health = health;

                root.AddComponent<CombatHud>().player = health;
                BuildUpgradeVisuals(root, vis, controller, health);

                // Health gauge in the middle of the dashboard, right of the steering wheel.
                var hg = Empty("HealthGauge", vis.dash, vis.healthGaugePos);
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
                var lamps = Empty("WarningLamps", vis.dash, vis.lampsPos);
                var tcLamp = Prim(PrimitiveType.Cube, "TCLamp", lamps.transform, new Vector3(0f, 0.035f, 0f), new Vector3(0.028f, 0.026f, 0.002f), mWarnLamp);
                var tcOffLamp = Prim(PrimitiveType.Cube, "TCOffLamp", lamps.transform, new Vector3(0f, -0.002f, 0f), new Vector3(0.028f, 0.026f, 0.002f), mWarnLamp);
                LabelDark(lamps.transform, "TC", new Vector3(0f, 0.035f, -0.0015f), 0.075f);
                LabelDark(lamps.transform, "TC" + "\n" + "OFF", new Vector3(0f, -0.002f, -0.0015f), 0.05f);

                var btnRoot = Empty("TCOffButton", vis.dash, vis.tcButtonPos);
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

        /// <summary>Greybox body + interior (enemy cars).</summary>
        static CarVisuals BuildGreyboxBody(Transform t, Rigidbody rb)
        {
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

            var rearView = RearViewMirror(interior, new Vector3(0f, 0.97f, 0.08f), new Vector3(0.18f, 0.6f, 0.3f));

            return new CarVisuals
            {
                bodyRoot = bodyRoot, interior = interior, dash = dash.transform, glove = glove.transform, visor = visor.transform,
                spin = spin.transform, hoodHinge = hoodHinge.transform, hoodLatch = hoodLatch, hood = hood,
                deformables = new System.Collections.Generic.List<DeformableMesh> { frontBody, rearBody, hood, roof, doorL, doorR },
                parts = new System.Collections.Generic.List<DetachablePart> { bumperF, bumperR, mirrorL, mirrorR, rearView },
                wsCracks = wsCracks, dashCracks = dashCracks, speedNeedle = speedNeedle, rpmNeedle = rpmNeedle,
                headPos = new Vector3(-0.38f, 0.82f, -0.5f),
                wheelMounts = new[] { new Vector3(-0.8f, 0f, 1.35f), new Vector3(0.8f, 0f, 1.35f), new Vector3(-0.8f, 0f, -1.35f), new Vector3(0.8f, 0f, -1.35f) },
                healthGaugePos = new Vector3(0.03f, 0.045f, -0.2285f), lampsPos = new Vector3(-0.38f, 0.04f, -0.2305f),
                tcButtonPos = new Vector3(-0.66f, 0.0f, -0.226f), smokePos = new Vector3(0f, 0.42f, 1.6f),
            };
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

        static ParticleSystem EngineSmoke(Transform car, Vector3 pos)
        {
            var go = Empty("EngineSmoke", car, pos, Quaternion.Euler(-80f, 0f, 0f));
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

        // ---------------------------------------------------------------- campaign pieces

        /// <summary>Racing number: white roundel + black digits on the roof and both doors; gold rims and a
        /// floating gold diamond over the roof when the emperor marks the car.</summary>
        static void BuildNumberPlates(GameObject root, Transform bodyRoot)
        {
            var group = Empty("NumberPlates", bodyRoot, Vector3.zero).transform;
            var texts = new System.Collections.Generic.List<TextMeshPro>();
            var rims = new System.Collections.Generic.List<GameObject>();

            // Roof
            Prim(PrimitiveType.Cylinder, "RoofRoundel", group, new Vector3(0f, 1.083f, -0.72f), new Vector3(0.56f, 0.003f, 0.56f), mRoundel);
            rims.Add(Prim(PrimitiveType.Cylinder, "RoofRim", group, new Vector3(0f, 1.0815f, -0.72f), new Vector3(0.66f, 0.003f, 0.66f), mNumberGold));
            texts.Add(PlateText(group, new Vector3(0f, 1.0875f, -0.72f), Quaternion.Euler(90f, 0f, 0f), 4.6f));

            // Doors (left faces -X, right faces +X)
            for (int s = -1; s <= 1; s += 2)
            {
                Prim(PrimitiveType.Cylinder, "DoorRoundel", group, new Vector3(0.903f * s, 0.42f, -0.25f), new Vector3(0.42f, 0.003f, 0.42f), mRoundel,
                    false, Quaternion.Euler(0f, 0f, 90f));
                rims.Add(Prim(PrimitiveType.Cylinder, "DoorRim", group, new Vector3(0.9015f * s, 0.42f, -0.25f), new Vector3(0.5f, 0.003f, 0.5f), mNumberGold,
                    false, Quaternion.Euler(0f, 0f, 90f)));
                texts.Add(PlateText(group, new Vector3(0.9075f * s, 0.42f, -0.25f), Quaternion.Euler(0f, s < 0 ? 90f : -90f, 0f), 3.4f));
            }
            foreach (var r in rims) r.SetActive(false);
            var beacon = Prim(PrimitiveType.Cube, "MarkBeacon", group, new Vector3(0f, 1.9f, -0.72f), new Vector3(0.38f, 0.38f, 0.38f), mNumberGold,
                false, Quaternion.Euler(45f, 0f, 45f));
            beacon.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            beacon.SetActive(false);

            var plates = root.AddComponent<CarNumberPlates>();
            plates.texts = texts.ToArray();
            plates.favourRims = rims.ToArray();
            plates.markBeacon = beacon;
        }

        static TextMeshPro PlateText(Transform parent, Vector3 pos, Quaternion rot, float size)
        {
            var go = new GameObject("Number");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = "";
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = new Color(0.06f, 0.06f, 0.06f);
            tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.35f);
            tmp.rectTransform.localPosition = pos;
            tmp.rectTransform.localRotation = rot;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return tmp;
        }

        /// <summary>Everything the car builder needs from a body style (greybox or Racer 01).</summary>
        class CarVisuals
        {
            public bool racer;
            public Transform bodyRoot, interior, dash, glove, visor, spin, hoodHinge;
            public HoodLatch hoodLatch;
            public DeformableMesh hood;
            public System.Collections.Generic.List<DeformableMesh> deformables = new System.Collections.Generic.List<DeformableMesh>();
            public System.Collections.Generic.List<DetachablePart> parts = new System.Collections.Generic.List<DetachablePart>();
            public CrackDecals wsCracks, dashCracks;
            public Transform speedNeedle, rpmNeedle;
            public Vector3 headPos;
            public Vector3[] wheelMounts;
            public Transform[] wheelMeshes;
            public Vector3 healthGaugePos, lampsPos, tcButtonPos, smokePos;
            public float topHeight = 0.85f;
            // Racer only
            public Transform doorL, doorR, roof, hoodPanel, scoop;
        }

        // Racer 01 model frame (ground at y = 0) -> car frame (wheel mounts at y = 0, wheels sag to about -0.31).
        static readonly Vector3 RacerOffset = new Vector3(0f, -0.64f, 0f);
        static Vector3 RM(float x, float y, float z) => new Vector3(x, y, z) + RacerOffset;
        static Vector3 RM(Vector3 p) => p + RacerOffset;

        /// <summary>
        /// Car body: the multi-part Racer 01 model with modelled interior, engine bay and underside.
        /// Enemies get the same body repainted (mBody = main paint, mPanel = stripes), their door glass
        /// up, and no working gauges.
        /// </summary>
        static CarVisuals BuildRacerBody(Transform t, Rigidbody rb, bool player)
        {
            var vis = new CarVisuals { racer = true };

            // Hull colliders; the breakable panels add their own boxes on top.
            var colLower = Empty("ColliderLower", t, RM(0f, 0.58f, 0f)).AddComponent<BoxCollider>();
            colLower.size = new Vector3(1.72f, 0.72f, 4.08f);
            colLower.sharedMaterial = carPhysMat;
            var colCabin = Empty("ColliderCabin", t, RM(0f, 1.11f, -0.43f)).AddComponent<BoxCollider>();
            colCabin.size = new Vector3(1.4f, 0.36f, 1.9f);
            colCabin.sharedMaterial = carPhysMat;

            // Windows down: the garage arm passes the tablet in through the passenger window.
            var r = Racer01Body.Spawn(t, RacerOffset, carPhysMat, player, "HoodScoop");
            var body = r.body.transform;
            if (!player) Repaint(body.gameObject, mBody, mPanel);
            vis.bodyRoot = body;
            vis.parts.AddRange(r.detachables);

            // Hood on a latch hinge at its rear edge (the Hood pivot is already there).
            var hoodT = r["Hood"];
            var hinge = Empty("HoodHinge", body, hoodT.localPosition);
            hoodT.SetParent(hinge.transform, true);
            var latch = hinge.AddComponent<HoodLatch>();
            latch.carBody = rb;
            vis.hoodHinge = hinge.transform;
            vis.hoodLatch = latch;
            vis.hoodPanel = hoodT;
            vis.scoop = r["HoodScoop"];
            if (vis.scoop != null) vis.scoop.SetParent(hoodT, true);

            foreach (var n in new[] { "Hood", "Roof", "DoorL", "DoorR", "FenderL", "FenderR", "QuarterL", "QuarterR", "TrunkLid" })
            {
                var d = r[n].gameObject.AddComponent<DeformableMesh>();
                d.maxDeform = n == "Roof" ? 0.22f : 0.32f;
                vis.deformables.Add(d);
                if (n == "Hood") vis.hood = d;
            }
            vis.doorL = r["DoorL"];
            vis.doorR = r["DoorR"];
            vis.roof = r["Roof"];

            // Windscreen cracks on a plane through the glass (local XY = glass, -Z toward the driver).
            Vector3 wsB = Racer01Body.WindscreenBottom, wsT = Racer01Body.WindscreenTop, slant = wsT - wsB;
            Vector3 wsNormal = Vector3.Cross(slant.normalized, Vector3.left).normalized;
            var wsPlane = Empty("WindscreenCracks", body, (wsB + wsT) * 0.5f, Quaternion.LookRotation(wsNormal, slant.normalized));
            vis.wsCracks = wsPlane.AddComponent<CrackDecals>();
            vis.wsCracks.crackMaterial = mCrack;
            vis.wsCracks.stripMesh = unitCube;
            vis.wsCracks.halfSize = new Vector2(0.68f, slant.magnitude * 0.45f);
            vis.wsCracks.surfaceOffset = -0.012f;

            // ---- Cockpit: modelled dashboard, functional bits added on its faces (local -Z faces the driver).
            vis.interior = Empty("Interior", t, Vector3.zero).transform;
            var dash = r["Dashboard"];
            vis.dash = dash;
            vis.glove = r["GloveboxLid"]; // pivot is the hinge at its bottom edge
            var dashTop = Empty("DashTop", dash, new Vector3(0f, 0.101f, 0f), Quaternion.Euler(-90f, 0f, 0f));
            vis.dashCracks = dashTop.AddComponent<CrackDecals>();
            vis.dashCracks.crackMaterial = mCrack;
            vis.dashCracks.stripMesh = unitCube;
            vis.dashCracks.halfSize = new Vector2(0.72f, 0.14f);
            vis.dashCracks.surfaceOffset = 0.003f;
            vis.dashCracks.maxCracks = 6;
            vis.dashCracks.stripWidth = 0.006f;
            // Binnacle face at local z = -0.21: speedometer left, tachometer right, warning lamps between.
            if (player)
            {
                vis.speedNeedle = Gauge("Speedometer", dash, new Vector3(-0.475f, 0.01f, -0.214f), 200f, 20f, 10f, 1f, 2, "km/h", -1f);
                vis.rpmNeedle = Gauge("Tachometer", dash, new Vector3(-0.285f, 0.01f, -0.214f), 8000f, 1000f, 500f, 1000f, 1, "x1000 rpm", 6500f);
            }
            vis.lampsPos = new Vector3(-0.38f, 0.01f, -0.216f);
            vis.healthGaugePos = new Vector3(0f, -0.03f, -0.202f); // centre stack face
            vis.tcButtonPos = new Vector3(-0.70f, -0.04f, -0.172f); // dash face left of the binnacle

            var visor = Empty("SunVisorHinge", vis.interior, RM(-0.38f, 1.222f, 0.0f));
            Prim(PrimitiveType.Cube, "SunVisor", visor.transform, new Vector3(0f, -0.012f, -0.075f), new Vector3(0.32f, 0.02f, 0.13f), mSeat);
            vis.visor = visor.transform;

            var column = Empty("SteeringColumn", vis.interior, RM(Racer01Body.SteeringHub), Quaternion.LookRotation(Racer01Body.ColumnDir));
            Prim(PrimitiveType.Cylinder, "Column", column.transform, new Vector3(0f, 0f, 0.19f), new Vector3(0.05f, 0.19f, 0.05f), mTrim, false, Quaternion.Euler(90f, 0f, 0f));
            var spin = Empty("SteeringWheel", column.transform, Vector3.zero).transform;
            var wheelMesh = r["SteeringWheel"];
            wheelMesh.SetParent(spin, false);
            wheelMesh.localPosition = Vector3.zero;
            wheelMesh.localRotation = Quaternion.identity;
            vis.spin = spin;

            vis.parts.Add(RearViewMirror(vis.interior, RM(0f, 1.19f, 0.0f), RM(0.18f, 0.86f, 0.45f)));

            vis.headPos = RM(Racer01Body.DriverEye);
            vis.wheelMounts = new[]
            {
                new Vector3(-Racer01Body.WheelX, 0f, Racer01Body.WheelFrontZ), new Vector3(Racer01Body.WheelX, 0f, Racer01Body.WheelFrontZ),
                new Vector3(-Racer01Body.WheelX, 0f, Racer01Body.WheelRearZ), new Vector3(Racer01Body.WheelX, 0f, Racer01Body.WheelRearZ),
            };
            vis.wheelMeshes = Racer01Body.WheelNames.Select(n => r[n]).ToArray();
            vis.smokePos = RM(0f, 0.84f, 1.5f);
            vis.topHeight = RM(0f, 1.2f, 0f).y;
            return vis;
        }

        /// <summary>
        /// Twin-barrel machine gun bolted to the rear right of the hood (visible from the driver's seat), with
        /// a tracer line, muzzle flash light and hit sparks for CarGun.
        /// </summary>
        static void BuildGun(GameObject root, CarVisuals vis, bool player, CarController controller, CarHealth health)
        {
            var body = vis.bodyRoot;
            var gunMetal = Mat("GunMetal", new Color(0.12f, 0.12f, 0.13f), 0.55f, 0.8f);
            var mount = Empty("Gun", body, new Vector3(0.38f, 0.9f, 0.95f)).transform;
            Prim(PrimitiveType.Cube, "Base", mount, new Vector3(0f, 0.02f, 0f), new Vector3(0.22f, 0.06f, 0.3f), mArmour);
            Prim(PrimitiveType.Cube, "Receiver", mount, new Vector3(0f, 0.09f, 0.08f), new Vector3(0.16f, 0.1f, 0.34f), gunMetal);
            foreach (float x in new[] { -0.045f, 0.045f })
                Prim(PrimitiveType.Cylinder, "Barrel", mount, new Vector3(x, 0.1f, 0.5f), new Vector3(0.04f, 0.3f, 0.04f), gunMetal, false, Quaternion.Euler(90f, 0f, 0f));
            Prim(PrimitiveType.Cube, "AmmoBox", mount, new Vector3(-0.12f, 0.07f, 0.02f), new Vector3(0.09f, 0.1f, 0.16f), Mat("GarageRedPaint", new Color(0.62f, 0.08f, 0.06f), 0.45f, 0.2f));
            var muzzle = Empty("Muzzle", mount, new Vector3(0f, 0.1f, 0.82f)).transform;

            var flashMat = GlowMat("MuzzleFlash", new Color(1f, 0.85f, 0.4f), new Color(4f, 2.6f, 0.8f), false);
            var flash = Prim(PrimitiveType.Sphere, "Flash", muzzle, new Vector3(0f, 0f, 0.06f), new Vector3(0.14f, 0.14f, 0.22f), flashMat);
            flash.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var light = Empty("FlashLight", muzzle, new Vector3(0f, 0f, 0.15f)).AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 3f;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.shadows = LightShadows.None;

            string tracerPath = MatDir + "/Tracer.mat";
            var tracerMat = AssetDatabase.LoadAssetAtPath<Material>(tracerPath);
            if (tracerMat == null)
            {
                tracerMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(tracerMat, tracerPath);
            }
            tracerMat.SetColor("_BaseColor", new Color(1f, 0.85f, 0.45f));
            EditorUtility.SetDirty(tracerMat);
            var tracerGo = Empty("Tracer", root.transform, Vector3.zero);
            var lr = tracerGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.sharedMaterial = tracerMat;
            lr.startWidth = 0.035f;
            lr.endWidth = 0.012f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;

            var sparkGo = Empty("HitSparks", root.transform, Vector3.zero);
            var ps = sparkGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.5f, 0.15f));
            main.gravityModifier = 1.2f;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.02f;
            var pr = sparkGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultParticleMaterial : null;
            pr.shadowCastingMode = ShadowCastingMode.Off;

            var gun = root.AddComponent<CarGun>();
            gun.car = controller;
            gun.health = health;
            gun.muzzle = muzzle;
            gun.playerControlled = player;
            gun.tracer = lr;
            gun.flash = light;
            gun.flashRenderer = flash.GetComponent<MeshRenderer>();
            gun.sparks = ps;
        }

        /// <summary>Swap the Racer's livery: main paint and stripe colours (enemy cars).</summary>
        static void Repaint(GameObject body, Material paint, Material stripe)
        {
            foreach (var mr in body.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (mats[i].name == "Racer01_Paint" && paint != null) { mats[i] = paint; changed = true; }
                    else if (mats[i].name == "Racer01_Red" && stripe != null) { mats[i] = stripe; changed = true; }
                }
                if (changed) mr.sharedMaterials = mats;
            }
        }

        /// <summary>Racing number on the Racer: roof roundel plus one on each door (they leave with the doors).</summary>
        static void BuildRacerNumberPlates(GameObject root, CarVisuals vis)
        {
            var body = vis.bodyRoot;
            var group = Empty("NumberPlates", body, Vector3.zero).transform;
            var texts = new System.Collections.Generic.List<TextMeshPro>();
            var rims = new System.Collections.Generic.List<GameObject>();

            Prim(PrimitiveType.Cylinder, "RoofRoundel", group, new Vector3(0f, 1.303f, -0.43f), new Vector3(0.5f, 0.003f, 0.5f), mRoundel);
            rims.Add(Prim(PrimitiveType.Cylinder, "RoofRim", group, new Vector3(0f, 1.3015f, -0.43f), new Vector3(0.6f, 0.003f, 0.6f), mNumberGold));
            texts.Add(PlateText(group, new Vector3(0f, 1.3075f, -0.43f), Quaternion.Euler(90f, 0f, 0f), 4.2f));

            for (int s = -1; s <= 1; s += 2)
            {
                var door = s < 0 ? vis.doorL : vis.doorR;
                var dg = Empty("DoorNumber", body, Vector3.zero).transform;
                Prim(PrimitiveType.Cylinder, "DoorRoundel", dg, new Vector3(0.884f * s, 0.60f, -0.05f), new Vector3(0.36f, 0.003f, 0.36f), mRoundel,
                    false, Quaternion.Euler(0f, 0f, 90f));
                rims.Add(Prim(PrimitiveType.Cylinder, "DoorRim", dg, new Vector3(0.8825f * s, 0.60f, -0.05f), new Vector3(0.43f, 0.003f, 0.43f), mNumberGold,
                    false, Quaternion.Euler(0f, 0f, 90f)));
                texts.Add(PlateText(dg, new Vector3(0.8885f * s, 0.60f, -0.05f), Quaternion.Euler(0f, s < 0 ? 90f : -90f, 0f), 3.0f));
                dg.SetParent(door, true);
            }
            foreach (var r in rims) r.SetActive(false);
            var beacon = Prim(PrimitiveType.Cube, "MarkBeacon", group, new Vector3(0f, 2.1f, -0.43f), new Vector3(0.38f, 0.38f, 0.38f), mNumberGold,
                false, Quaternion.Euler(45f, 0f, 45f));
            beacon.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            beacon.SetActive(false);

            var plates = root.AddComponent<CarNumberPlates>();
            plates.texts = texts.ToArray();
            plates.favourRims = rims.ToArray();
            plates.markBeacon = beacon;
        }

        /// <summary>Bolt-on parts that appear with upgrade levels (player car, Racer model frame).</summary>
        static void BuildUpgradeVisuals(GameObject root, CarVisuals vis, CarController controller, CarHealth health)
        {
            var body = vis.bodyRoot;
            var group = Empty("Upgrades", body, Vector3.zero).transform;

            // Armour: L1 door plates (ride on the doors), L2 hood plates, L3 roof cage
            var a1 = Empty("Armour1", vis.doorL, Vector3.zero).transform;
            var a1r = Empty("Armour1R", vis.doorR, Vector3.zero).transform;
            for (int s = -1; s <= 1; s += 2)
            {
                var plate = Prim(PrimitiveType.Cube, "DoorPlate", s < 0 ? a1 : a1r, Vector3.zero, new Vector3(0.03f, 0.14f, 1.2f), mArmour);
                plate.transform.position = body.TransformPoint(new Vector3(0.9f * s, 0.36f, -0.05f));
            }
            a1.gameObject.AddComponent<ActiveFollower>().followers = new[] { a1r.gameObject };

            var a2 = Empty("Armour2", vis.hoodPanel, Vector3.zero).transform; // hood-local: hinge at the rear edge
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "HoodPlate", a2, new Vector3(0.42f * s, -0.04f, 0.66f), new Vector3(0.36f, 0.025f, 0.95f), mArmour, false, Quaternion.Euler(6f, 0f, 0f));
            for (int b = -2; b <= 2; b++)
                Prim(PrimitiveType.Cube, "Rivet", a2, new Vector3(b * 0.28f, 0.0f, 0.22f), new Vector3(0.04f, 0.02f, 0.04f), mTrim);

            var a3 = Empty("Armour3", group, Vector3.zero).transform;
            for (int s = -1; s <= 1; s++)
                Prim(PrimitiveType.Cube, "CageBar", a3, new Vector3(0.55f * s, 1.335f, -0.43f), new Vector3(0.05f, 0.05f, 1.0f), mArmour);
            for (int b = 0; b < 3; b++)
                Prim(PrimitiveType.Cube, "CageCross", a3, new Vector3(0f, 1.36f, -0.85f + b * 0.42f), new Vector3(1.25f, 0.05f, 0.05f), mArmour);

            // Ram plate: L1 bull bar, L2 double bar, L3 plough with spikes
            var r1 = Empty("Ram1", group, Vector3.zero).transform;
            Prim(PrimitiveType.Cube, "Bar", r1, new Vector3(0f, 0.38f, 2.33f), new Vector3(1.6f, 0.08f, 0.08f), mArmour);
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "Post", r1, new Vector3(0.55f * s, 0.32f, 2.27f), new Vector3(0.08f, 0.2f, 0.12f), mArmour);
            var r2 = Empty("Ram2", group, Vector3.zero).transform;
            Prim(PrimitiveType.Cube, "TopBar", r2, new Vector3(0f, 0.57f, 2.31f), new Vector3(1.5f, 0.07f, 0.07f), mArmour);
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cube, "Upright", r2, new Vector3(0.25f * s, 0.47f, 2.32f), new Vector3(0.06f, 0.22f, 0.06f), mArmour);
            var r3 = Empty("Ram3", group, Vector3.zero).transform;
            Prim(PrimitiveType.Cube, "Plough", r3, new Vector3(0f, 0.36f, 2.42f), new Vector3(1.95f, 0.38f, 0.05f), mArmour, false, Quaternion.Euler(-25f, 0f, 0f));
            for (int b = -1; b <= 1; b++)
                Prim(PrimitiveType.Cube, "Spike", r3, new Vector3(b * 0.6f, 0.42f, 2.52f), new Vector3(0.06f, 0.06f, 0.22f), mTrim);

            // Engine: L2 the modelled hood scoop, L3 side pipes (L1 is all under the hood)
            var e2 = vis.scoop;
            var e3 = Empty("Engine3", group, Vector3.zero).transform;
            for (int s = -1; s <= 1; s += 2)
                Prim(PrimitiveType.Cylinder, "SidePipe", e3, new Vector3(0.93f * s, 0.26f, -0.1f), new Vector3(0.09f, 0.55f, 0.09f), mMirror, false, Quaternion.Euler(90f, 0f, 0f));

            var up = root.AddComponent<CarUpgrades>();
            up.car = controller;
            up.health = health;
            up.armourLevels = new[] { a1.gameObject, a2.gameObject, a3.gameObject };
            up.ramPlateLevels = new[] { r1.gameObject, r2.gameObject, r3.gameObject };
            up.engineLevels = new GameObject[] { null, e2 != null ? e2.gameObject : null, e3.gameObject };
            foreach (var g in new[] { a1, a1r, a2, a3, r1, r2, r3, e2, e3 }) if (g != null) g.gameObject.SetActive(false);
        }

        /// <summary>The tablet the driver holds up in front of the windscreen (screen built at runtime by TabletUI).</summary>
        /// <summary>
        /// Rugged workshop tablet held by a floor-standing arm to the right of the parked car. The arm swings
        /// it out in front of the windscreen (TabletArm). Tablet = UI layer, so the garage camera's feed
        /// doesn't show it.
        /// </summary>
        static TabletArm BuildTabletArm(GameObject playerCar, ArenaGate playerGate)
        {
            const int uiLayer = 5;
            var tan = Mat("ArmPaint", new Color(0.78f, 0.62f, 0.4f), 0.35f, 0.15f);
            var dark = Mat("ArmDark", new Color(0.16f, 0.16f, 0.17f), 0.45f, 0.6f);
            var chrome = Mat("ArmChrome", new Color(0.8f, 0.82f, 0.85f), 0.85f, 1f);
            var bronze = Mat("GarageBronze", new Color(0.62f, 0.45f, 0.22f), 0.6f, 0.8f);

            Transform g = playerGate.transform;
            var root = new GameObject("TabletArm").transform;
            // Beside the passenger door: the arm reaches in through the open window.
            root.SetPositionAndRotation(g.TransformPoint(new Vector3(3.2f, 0f, GarageCarZ + 0.2f)), g.rotation);

            // Pedestal + turret + shoulder
            Prim(PrimitiveType.Cylinder, "BasePlate", root, new Vector3(0f, 0.05f, 0f), new Vector3(1.4f, 0.05f, 1.4f), dark);
            Prim(PrimitiveType.Cylinder, "Pedestal", root, new Vector3(0f, 0.62f, 0f), new Vector3(0.75f, 0.57f, 0.75f), tan);
            Prim(PrimitiveType.Cube, "Hazard", root, new Vector3(0f, 0.3f, 0.38f), new Vector3(0.5f, 0.2f, 0.02f), Mat("GarageLine", new Color(0.85f, 0.65f, 0.1f), 0.3f));
            var turret = Empty("Turret", root, new Vector3(0f, 1.2f, 0f)).transform;
            Prim(PrimitiveType.Cube, "Housing", turret, new Vector3(0f, 0.15f, 0f), new Vector3(0.8f, 0.4f, 0.8f), tan);
            Prim(PrimitiveType.Cylinder, "ShoulderJoint", turret, new Vector3(0f, 0.35f, 0f), new Vector3(0.55f, 0.48f, 0.55f), dark, false, Quaternion.Euler(0f, 0f, 90f));
            var shoulder = Empty("Shoulder", turret, new Vector3(0f, 0.35f, 0f)).transform;

            Transform Segment(string name, float thick, float tall)
            {
                var seg = Empty(name, root, Vector3.zero).transform;
                Prim(PrimitiveType.Cube, "Beam", seg, new Vector3(0f, 0f, 0.5f), new Vector3(thick, tall, 1f), tan);
                Prim(PrimitiveType.Cylinder, "Piston", seg, new Vector3(0f, tall * 0.5f + 0.06f, 0.45f), new Vector3(0.08f, 0.38f, 0.08f), chrome, false, Quaternion.Euler(90f, 0f, 0f));
                Prim(PrimitiveType.Cylinder, "PistonSleeve", seg, new Vector3(0f, tall * 0.5f + 0.06f, 0.2f), new Vector3(0.13f, 0.18f, 0.13f), dark, false, Quaternion.Euler(90f, 0f, 0f));
                return seg;
            }
            var upper = Segment("UpperArm", 0.26f, 0.3f);
            var fore = Segment("Forearm", 0.15f, 0.17f); // slim enough to pass the window
            var elbow = Prim(PrimitiveType.Sphere, "ElbowJoint", root, Vector3.zero, Vector3.one * 0.36f, dark).transform;
            var wrist = Prim(PrimitiveType.Sphere, "WristJoint", root, Vector3.zero, Vector3.one * 0.17f, dark).transform;
            var link = Empty("WristLink", root, Vector3.zero).transform;
            Prim(PrimitiveType.Cube, "Link", link, new Vector3(0f, 0f, 0.5f), new Vector3(0.07f, 0.07f, 1f), dark);

            // Tablet: rugged frame with bronze corner bumpers, screen on the -Z face (toward the driver).
            const float tw = 0.40f, th = 0.25f; // fits through the side window, fills the view from the seat
            var tablet = new GameObject("Tablet").transform;
            tablet.SetParent(root, false);
            Prim(PrimitiveType.Cube, "Frame", tablet, Vector3.zero, new Vector3(tw, th, 0.03f), dark);
            Prim(PrimitiveType.Cube, "BackPlate", tablet, new Vector3(0f, 0f, 0.025f), new Vector3(0.2f, 0.15f, 0.03f), tan);
            Prim(PrimitiveType.Cube, "EdgeMount", tablet, new Vector3(tw * 0.5f + 0.02f, 0f, 0.01f), new Vector3(0.04f, 0.12f, 0.04f), tan);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                    Prim(PrimitiveType.Cube, "Bumper", tablet, new Vector3(sx * (tw * 0.5f + 0.006f), sy * (th * 0.5f + 0.006f), 0.006f), new Vector3(0.032f, 0.032f, 0.036f), bronze);
            var screen = new GameObject("Screen", typeof(RectTransform));
            screen.transform.SetParent(tablet, false);
            var rt = screen.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1600f, 1000f);
            rt.localPosition = new Vector3(0f, 0f, -0.016f);
            rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one * (tw - 0.03f) / 1600f;
            var canvas = screen.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            screen.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            foreach (var tr in tablet.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = uiLayer;

            var arm = root.gameObject.AddComponent<TabletArm>();
            arm.car = playerCar.transform;
            arm.tablet = tablet;
            arm.turret = turret;
            arm.shoulder = shoulder;
            arm.upperArm = upper;
            arm.forearm = fore;
            arm.elbowJoint = elbow;
            arm.wristJoint = wrist;
            arm.wristLink = link;
            // Presented: square in front of the windscreen, facing the driver's eyes.
            // Path: folded beside the pedestal -> outside the passenger window (edge-on) -> in through the
            // window -> turned to face the driver above the steering wheel (component defaults).
            arm.stowedPosition = new Vector3(0.5f, 1.9f, 0.9f);
            arm.stowedEuler = new Vector3(0f, 90f, 0f);
            // Below the sun visor (even when crash damage drops it) and above the steering wheel.
            arm.presentedPosition = new Vector3(-0.37f, 0.42f, -0.02f);
            // Racer side window: low roof, narrow at the top; pass the tablet edge-on through its middle.
            arm.outsidePosition = new Vector3(1.75f, 0.45f, -0.08f);
            arm.insidePosition = new Vector3(0.25f, 0.45f, -0.08f);
            return arm;
        }

        static void BuildCampaign(GameObject playerCar, ArenaGate playerGate, ArenaDirector director, GameObject[] enemyPrefabs)
        {
            var settings = CampaignAsset(enemyPrefabs);

            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<UnityEngine.EventSystems.EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // Garage view camera + a light that shines on the car while the tablet is up.
            var camGo = new GameObject("GarageCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 80f;
            cam.cullingMask = ~(1 << 5);
            var garage = camGo.AddComponent<GarageCamera>();
            garage.target = playerCar.transform;
            garage.radius = new Vector2(4.6f, 5.6f);
            garage.height = 2.1f;
            var lightGo = new GameObject("GarageLight");
            lightGo.transform.SetPositionAndRotation(playerGate.spawnPoint.position + Vector3.up * 4.2f, Quaternion.Euler(90f, 0f, 0f));
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.range = 12f;
            light.spotAngle = 80f;
            light.intensity = 7f;
            light.color = new Color(1f, 0.88f, 0.7f);
            light.enabled = false;
            garage.showroomLight = light;

            var go = new GameObject("Campaign");
            var overlay = go.AddComponent<MatchSummaryUI>();
            var tabletUI = go.AddComponent<TabletUI>();
            var campaign = go.AddComponent<CampaignDirector>();
            go.AddComponent<PauseMenu>().campaign = campaign;
            var arm = BuildTabletArm(playerCar, playerGate);
            tabletUI.campaign = campaign;
            tabletUI.arm = arm;
            tabletUI.canvas = arm.tablet.GetComponentInChildren<Canvas>(true);
            tabletUI.garageCamera = garage;
            tabletUI.driverCam = playerCar.GetComponentInChildren<CinemachineCamera>();
            tabletUI.driverHead = playerCar.GetComponentInChildren<CarCamera>();
            tabletUI.focusFov = 70f;   // the tablet is in the cabin now: no zoom needed
            tabletUI.focusPitch = 0f;
            tabletUI.iconRepair = ArenaArt.IconRepair();
            tabletUI.iconArmour = ArenaArt.IconArmour();
            tabletUI.iconEngine = ArenaArt.IconEngine();
            tabletUI.iconTyres = ArenaArt.IconTyres();
            tabletUI.iconRam = ArenaArt.IconRam();
            tabletUI.iconLaurel = ArenaArt.IconLaurel();
            tabletUI.iconBoost = ArenaArt.IconBoost();
            tabletUI.iconGun = ArenaArt.IconGun();

            campaign.settings = settings;
            campaign.player = playerCar.GetComponent<CarController>();
            campaign.arena = director;
            campaign.intro = Object.FindAnyObjectByType<IntroCinematic>();
            campaign.tablet = tabletUI;
            campaign.garageCamera = garage;
            campaign.overlay = overlay;
            campaign.playerGate = playerGate;
            campaign.crowd = Object.FindAnyObjectByType<ArenaCrowd>();
            if (campaign.intro != null) campaign.intro.autoPlay = false;
            director.waitForIntro = true;
        }

        /// <summary>Experimental third-person camera (switched on from the F3 debug menu or F4).</summary>
        static void BuildChaseCamera(GameObject playerCar)
        {
            var cam = VCam("CM Third Person", null, 66f, null, 0f, 0f);
            var listener = cam.gameObject.AddComponent<CinemachineImpulseListener>();
            listener.Gain = 0.35f;
            listener.Use2DDistance = true;
            var chase = cam.gameObject.AddComponent<ChaseCamera>();
            chase.car = playerCar.GetComponent<CarController>();
            chase.input = playerCar.GetComponent<CarInput>();
            chase.vcam = cam;
            chase.intro = Object.FindAnyObjectByType<IntroCinematic>();
            chase.campaign = Object.FindAnyObjectByType<CampaignDirector>();
            chase.crowd = Object.FindAnyObjectByType<ArenaCrowd>();
            chase.carAudio = playerCar.GetComponent<CarAudio>();
            var hud = playerCar.GetComponent<CarHud>();
            hud.chaseCamera = chase;
            PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
        }

        // ---------------------------------------------------------------- side objectives

        static Material GlowMat(string name, Color baseColor, Color emission, bool transparent)
        {
            var m = Mat(name, baseColor, 0.3f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_Cull", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Race mode: grid, laps and positions on the figure-8 (RaceDirector), hooked into the campaign.</summary>
        static void BuildRace(ArenaDirector director, GameObject playerCar)
        {
            var go = new GameObject("RaceDirector");
            var race = go.AddComponent<RaceDirector>();
            race.track = Object.FindAnyObjectByType<RaceTrack>();
            race.arena = director;
            race.player = playerCar.GetComponent<CarController>();
            race.crowd = Object.FindAnyObjectByType<ArenaCrowd>();
            race.laps = 3;
            race.checkpointMarkers = BuildCheckpointGates(race.track);
            var campaign = Object.FindAnyObjectByType<CampaignDirector>();
            if (campaign != null) campaign.race = race;
        }

        /// <summary>An arch over the road at every checkpoint (and the finish line); the banner is what lights up.
        /// No colliders: the gates are markers, not obstacles.</summary>
        static Renderer[] BuildCheckpointGates(RaceTrack track)
        {
            if (track == null) return new Renderer[0];
            track.Build();
            var postMat = Mat("GatePost", new Color(0.16f, 0.16f, 0.18f), 0.35f, 0.4f);
            var bannerMat = GlowMat("GateBanner", new Color(0.35f, 0.35f, 0.38f), Color.black, false);
            var stripeMat = Mat("GateStripe", new Color(0.95f, 0.8f, 0.15f), 0.2f);
            var root = Empty("CheckpointGates", track.transform, Vector3.zero, Quaternion.identity).transform;
            int n = track.CheckpointCount;
            var banners = new Renderer[n];
            float half = track.halfWidth + 1.2f, h = 4.6f;
            for (int k = 0; k < n; k++)
            {
                var pose = track.CheckpointPose(k);
                var g = Empty(k == 0 ? "FinishArch" : $"Checkpoint{k}", root, pose.position, pose.rotation).transform;
                foreach (float side in new[] { -1f, 1f })
                {
                    Prim(PrimitiveType.Cube, "Post", g, new Vector3(side * half, h * 0.5f, 0f), new Vector3(0.45f, h, 0.45f), postMat);
                    Prim(PrimitiveType.Cube, "Stripe", g, new Vector3(side * half, 0.9f, 0f), new Vector3(0.5f, 0.35f, 0.5f), stripeMat);
                }
                Prim(PrimitiveType.Cube, "Beam", g, new Vector3(0f, h + 0.15f, 0f), new Vector3(2f * half + 0.45f, 0.3f, 0.45f), postMat);
                var banner = Prim(PrimitiveType.Cube, "Banner", g, new Vector3(0f, h - 0.45f, 0f), new Vector3(2f * half - 0.6f, 0.6f, 0.12f), bannerMat);
                banners[k] = banner.GetComponent<MeshRenderer>();
            }
            return banners;
        }

        static void BuildObjectives(ArenaDirector director)
        {
            var go = new GameObject("ArenaObjectives");
            var objectives = go.AddComponent<ArenaObjectives>();
            objectives.director = director;
            objectives.crowd = Object.FindAnyObjectByType<ArenaCrowd>();
            objectives.giftPrefab = GiftPrefab();
            director.objectives = objectives;
            objectives.layout = director.layout;
            var campaign = Object.FindAnyObjectByType<CampaignDirector>();
            if (campaign != null) campaign.objectives = objectives;

            // Boost pads on the four half-straights, all pointing at the crossing: boost in, T-bone harder.
            var padOn = GlowMat("BoostChevronOn", new Color(1f, 0.55f, 0.1f), new Color(2.2f, 1.0f, 0.15f), false);
            var padOff = GlowMat("BoostChevronOff", new Color(0.35f, 0.2f, 0.08f), new Color(0.12f, 0.05f, 0f), false);
            var padBase = Mat("BoostPadBase", new Color(0.12f, 0.12f, 0.13f), 0.3f);
            var pads = new System.Collections.Generic.List<BoostPad>
            {
                BoostPadAt(go.transform, new Vector3(0f, 0f, 19f), 180f, padBase, padOn, padOff),
                BoostPadAt(go.transform, new Vector3(0f, 0f, -19f), 0f, padBase, padOn, padOff),
                BoostPadAt(go.transform, new Vector3(19f, 0f, 0f), -90f, padBase, padOn, padOff),
                BoostPadAt(go.transform, new Vector3(-19f, 0f, 0f), 90f, padBase, padOn, padOff),
            };
            objectives.pads = pads.ToArray();
        }

        static BoostPad BoostPadAt(Transform parent, Vector3 pos, float yaw, Material baseMat, Material on, Material off)
        {
            var root = Empty("BoostPad", parent, pos, Quaternion.Euler(0f, yaw, 0f));
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(3.8f, 2f, 9f);
            box.center = new Vector3(0f, 1f, 0f);
            var plate = Prim(PrimitiveType.Cube, "Plate", root.transform, new Vector3(0f, 0.012f, 0f), new Vector3(3.8f, 0.02f, 9f), baseMat);
            plate.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var renderers = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < 5; i++)
            {
                float z = -3.4f + i * 1.7f;
                foreach (float side in new[] { -1f, 1f })
                {
                    var arm = Prim(PrimitiveType.Cube, "Chevron" + i, root.transform, new Vector3(side * 0.72f, 0.03f, z),
                        new Vector3(1.75f, 0.02f, 0.38f), off, false, Quaternion.Euler(0f, side * 32f, 0f));
                    var r = arm.GetComponent<MeshRenderer>();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    renderers.Add(r);
                }
            }
            var pad = root.AddComponent<BoostPad>();
            pad.chevrons = renderers.ToArray();
            pad.renderersPerChevron = 2;
            pad.onMaterial = on;
            pad.offMaterial = off;
            return pad;
        }

        static EmperorGift GiftPrefab()
        {
            var gold = mNumberGold;
            var beamMat = GlowMat("GiftBeam", new Color(1f, 0.8f, 0.3f, 0.22f), new Color(0.9f, 0.6f, 0.15f), true);
            var root = new GameObject("EmperorGift");
            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.8f, 2.6f, 2.8f);
            trigger.center = new Vector3(0f, 0.7f, 0f);
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var visual = Empty("Visual", root.transform, Vector3.zero).transform;
            Prim(PrimitiveType.Cube, "Chest", visual, new Vector3(0f, 0.1f, 0f), new Vector3(1.3f, 0.8f, 0.9f), mCrate);
            Prim(PrimitiveType.Cube, "Lid", visual, new Vector3(0f, 0.6f, 0f), new Vector3(1.36f, 0.24f, 0.96f), mClothPurple);
            foreach (float x in new[] { -0.45f, 0.45f })
                Prim(PrimitiveType.Cube, "Band", visual, new Vector3(x, 0.3f, 0f), new Vector3(0.12f, 0.92f, 0.98f), gold);
            Prim(PrimitiveType.Cube, "Lock", visual, new Vector3(0f, 0.38f, 0.47f), new Vector3(0.2f, 0.24f, 0.06f), gold);
            Prim(PrimitiveType.Cylinder, "Coins", visual, new Vector3(0f, 0.76f, 0f), new Vector3(0.7f, 0.06f, 0.5f), gold);

            var beam = Prim(PrimitiveType.Cylinder, "Beam", root.transform, new Vector3(0f, 14f, 0f), new Vector3(0.7f, 14f, 0.7f), beamMat);
            beam.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var lightGo = Empty("Glow", root.transform, new Vector3(0f, 1.6f, 0f));
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 4f;
            light.color = new Color(1f, 0.78f, 0.35f);
            light.shadows = LightShadows.None;

            var gift = root.AddComponent<EmperorGift>();
            gift.visual = visual;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Root + "/Prefabs/EmperorGift.prefab");
            Object.DestroyImmediate(root);
            return prefab.GetComponent<EmperorGift>();
        }

        static CampaignSettings CampaignAsset(GameObject[] enemyPrefabs)
        {
            string path = Root + "/Settings/CampaignSettings.asset";
            Directory.CreateDirectory(Root + "/Settings");
            var s = AssetDatabase.LoadAssetAtPath<CampaignSettings>(path);
            if (s == null)
            {
                s = ScriptableObject.CreateInstance<CampaignSettings>();
                MatchDefinition M(string n, float scale, params EnemyType[] e) => new MatchDefinition { name = n, statScale = scale, enemies = e };
                s.matches = new[]
                {
                    M("First Blood", 1f, EnemyType.Rammer),
                    M("Double Trouble", 1f, EnemyType.Rammer, EnemyType.Rammer),
                    M("Wings", 1f, EnemyType.Rammer, EnemyType.Flanker),
                    M("The Heavy", 1.05f, EnemyType.Bruiser, EnemyType.Rammer),
                    M("Pack Hunt", 1.05f, EnemyType.Flanker, EnemyType.Flanker, EnemyType.Rammer),
                    M("Iron Wall", 1.1f, EnemyType.Bruiser, EnemyType.Bruiser, EnemyType.Flanker),
                    M("Gauntlet", 1.15f, EnemyType.Rammer, EnemyType.Rammer, EnemyType.Flanker, EnemyType.Bruiser),
                    M("The Champion", 1.2f, EnemyType.Champion, EnemyType.Flanker, EnemyType.Flanker),
                };
                s.matches[7].championMatch = true;
                s.upgrades = new[]
                {
                    new UpgradeTrack { type = UpgradeType.Armour, displayName = "Armour plating",
                        levelText = new[] { "+25 hull", "+50 hull", "+80 hull" }, costs = new[] { 150, 300, 500 } },
                    new UpgradeTrack { type = UpgradeType.Engine, displayName = "Engine",
                        levelText = new[] { "+15% power", "+30% power", "+50% power" }, costs = new[] { 150, 300, 500 } },
                    new UpgradeTrack { type = UpgradeType.Tyres, displayName = "Tyres",
                        levelText = new[] { "+6% grip", "+12% grip", "+20% grip" }, costs = new[] { 120, 250, 400 } },
                    new UpgradeTrack { type = UpgradeType.RamPlate, displayName = "Ram plate",
                        levelText = new[] { "Ram: -20% taken, +15% dealt", "Ram: -38% taken, +30% dealt", "Ram: -55% taken, +50% dealt" },
                        costs = new[] { 150, 300, 500 } },
                };
                AssetDatabase.CreateAsset(s, path);
            }
            s.enemyPrefabs = enemyPrefabs; // always point at the freshly built prefabs
            // Boost and gun tracks were added later: append them to existing settings assets.
            var tracks = new System.Collections.Generic.List<UpgradeTrack>(s.upgrades ?? new UpgradeTrack[0]);
            if (s.Track(UpgradeType.Boost) == null)
                tracks.Add(new UpgradeTrack { type = UpgradeType.Boost, displayName = "Boost tank",
                    levelText = new[] { "Lasts +20%, refills +30%", "Lasts +45%, refills +60%", "Lasts +80%, refills x2" }, costs = new[] { 120, 250, 400 } });
            if (s.Track(UpgradeType.Gun) == null)
                tracks.Add(new UpgradeTrack { type = UpgradeType.Gun, displayName = "Machine gun",
                    levelText = new[] { "+20% damage, +25% ammo", "+40% damage, +50% ammo", "+65% damage, +80% ammo" }, costs = new[] { 120, 250, 400 } });
            s.upgrades = tracks.ToArray();
            if (s.championNumber < 10) s.championNumber = 10; // numbers are two digits now
            if (s.giftMoney == new Vector2Int(20, 45)) s.giftMoney = new Vector2Int(10, 20); // gifts are a bonus, fights pay
            EditorUtility.SetDirty(s);
            return s;
        }

        static GameObject BuildEnemyPrefab(SurfaceProfile defaultSurface, DriverArchetype archetype, string name,
            Color paint, Color stripe, float mass, float health, float powerKW, float torque, string fileName = null)
        {
            fileName = fileName ?? "Enemy" + archetype;
            // Same Racer 01 body as the player, own livery: main paint + stripes.
            var body = Mat(fileName + "Body", paint, 0.35f, 0.25f);
            var panel = Mat(fileName + "Panel", stripe, 0.4f, 0.2f);
            var car = BuildCar(defaultSurface, name, false, body, panel);
            var controller = car.GetComponent<CarController>();
            controller.mass = mass;
            controller.maxPowerKW = powerKW;
            controller.maxWheelTorque = torque;
            car.GetComponent<Rigidbody>().mass = mass;
            car.GetComponent<CarHealth>().maxHealth = health;
            car.GetComponent<AICarDriver>().archetype = archetype;
            string path = Root + "/Prefabs/" + fileName + ".prefab";
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

        static DetachablePart RearViewMirror(Transform interior, Vector3 pos, Vector3 dropPos)
        {
            var go = Empty("RearViewMirror", interior, pos, Quaternion.Euler(8f, -12f, 0f));
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
            p.dropLocalPosition = dropPos;
            p.dropLocalEuler = new Vector3(85f, 30f, 0f);
            return p;
        }
    }
}
