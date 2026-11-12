using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CarArena.EditorTools
{
    /// <summary>
    /// Set dressing for the colosseum: textured sand and road, a striped velarium (awnings) over the top tier,
    /// stair aisles through the seating, corner towers with flags, braziers on the parapet, frescoes and shields
    /// on the arena wall, bronze gladiator statues in the corners, barrel stacks, and the countryside outside
    /// (dry grass, cypresses, villas, hills). Decoration only: nothing here sits on the racing line, and the
    /// big static groups are merged into a few meshes per material.
    /// </summary>
    public static partial class ArenaSceneBuilder
    {
        static readonly Vector3[] Sides = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
        static Texture2D sandTex, sandGrain;

        static float StandLastTop => StandFirstTop + StandRise * (StandRows - 1);
        static float StandBackDist => ArenaHalf + 2f + StandRowDepth * StandRows + 0.75f;
        static float StandBackTop => StandLastTop + 8f;

        static void DressArena(Transform arena, Transform roadObject)
        {
            var dress = Empty("Dressing", arena, Vector3.zero).transform;

            // Ground textures.
            // Sand in two layers so it doesn't look tiled: the colour layer spans the whole floor once (no repeat
            // at all), plus fine featureless grain through the URP detail map (multiplied x2) every ~3.7 m.
            sandTex = ArenaArt.SandMacroTexture();
            sandGrain = ArenaArt.SandDetailTexture();
            ApplySand(mSand, sandTex, sandGrain, Vector2.one, new Vector2(40f, 40f), Color.white);
            EditorUtility.SetDirty(mSand);
            if (roadObject != null)
            {
                var road = TexMat("Road", ArenaArt.RoadTexture(), Vector2.one, Color.white);
                road.SetFloat("_Smoothness", 0.2f);
                roadObject.GetComponent<MeshRenderer>().sharedMaterial = road;
                CrossingPatch(roadObject);
            }

            var exclusions = new List<Bounds>();
            Velarium(dress);
            Aisles(dress, exclusions);
            CornerTowers(dress, exclusions);
            Braziers(dress);
            WallArt(dress);
            Statues(dress);
            Barrels(dress);
            Countryside(dress);

            // Keep the generated crowd off the stairs and out of the towers.
            var crowd = Object.FindAnyObjectByType<ArenaCrowd>();
            if (crowd != null)
            {
                if (crowd.exclusions != null) exclusions.AddRange(crowd.exclusions);
                crowd.exclusions = exclusions.ToArray();
            }
        }

        /// <summary>Lit material with a macro base map and a grain detail map (URP Lit "Detail Inputs", mul x2).</summary>
        static void ApplySand(Material m, Texture2D macro, Texture2D detail, Vector2 macroTiling, Vector2 detailTiling, Color tint)
        {
            m.SetTexture("_BaseMap", macro);
            m.SetTextureScale("_BaseMap", macroTiling);
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_DetailAlbedoMap", detail);
            m.SetTextureScale("_DetailAlbedoMap", detailTiling);
            m.SetFloat("_DetailAlbedoMapScale", 1f);
            m.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(m);
        }

        /// <summary>
        /// The square where the two straights cross: the road ribbon overlaps itself there (two coplanar surfaces
        /// flicker), so a slightly raised patch with its own box-junction texture covers it.
        /// </summary>
        static void CrossingPatch(Transform roadObject)
        {
            if (roadObject == null) return;
            var mat = TexMat("Crossing", ArenaArt.CrossingTexture(), Vector2.one, Color.white);
            mat.SetFloat("_Smoothness", 0.2f);
            float size = TrackHalfWidth * 2f;
            var quad = Prim(PrimitiveType.Quad, "Crossing", roadObject.parent, new Vector3(0f, 0.045f, 0f), new Vector3(size, size, 1f), mat,
                false, Quaternion.Euler(90f, 0f, 0f));
            quad.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>
        /// Mark everything that never moves as Static (the Inspector checkbox: batching, GI, occlusion,
        /// navigation...). Skipped: anything under a Rigidbody (cars, loose props, the portcullis), cameras,
        /// lights, UI, and objects that move or animate their own transforms.
        /// </summary>
        static void MarkStatic()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects()) MarkStatic(root.transform);
        }

        static void MarkStatic(Transform t)
        {
            if (Moves(t)) return; // and everything under it
            t.gameObject.isStatic = true;
            for (int i = 0; i < t.childCount; i++) MarkStatic(t.GetChild(i));
        }

        static bool Moves(Transform t) =>
            t.GetComponent<Rigidbody>() != null || t.GetComponent<Camera>() != null || t.GetComponent<Light>() != null ||
            t.GetComponent<Canvas>() != null || t.GetComponent<RectTransform>() != null ||
            t.GetComponent<Unity.Cinemachine.CinemachineVirtualCameraBase>() != null ||
            t.GetComponent<ArenaCrowd>() != null || t.GetComponent<TabletArm>() != null || t.GetComponent<GarageCamera>() != null ||
            t.GetComponent<ChaseCamera>() != null || t.GetComponent<IntroCinematic>() != null || t.GetComponent<EmperorGift>() != null ||
            t.GetComponent<CarController>() != null || t.GetComponent<ParticleSystem>() != null;

        static bool NearGate(Vector3 n, float x, float clear) =>
            System.Array.IndexOf(GateSides, n) >= 0 && Mathf.Abs(x) < clear;

        // ---------------------------------------------------------------- stands

        /// <summary>Striped sun awnings on masts along the top of every stand, sloping in over the upper rows.</summary>
        static void Velarium(Transform parent)
        {
            var root = Empty("Velarium", parent, Vector3.zero).transform;
            var red = Mat("VelariumRed", new Color(0.66f, 0.12f, 0.08f), 0.05f);
            var cream = Mat("VelariumCream", new Color(0.9f, 0.84f, 0.7f), 0.05f);
            var wood = Mat("VelariumMast", new Color(0.35f, 0.24f, 0.15f), 0.1f);
            float mastDist = StandBackDist, mastBase = StandBackTop, mastTop = StandBackTop + 7f;
            float innerDist = mastDist - 12f, innerY = mastTop - 4.5f;
            Vector3 a = new Vector3(0f, mastTop - 0.5f, mastDist), b = new Vector3(0f, innerY, innerDist);
            float len = Vector3.Distance(a, b);
            float pitch = Mathf.Atan2(a.y - b.y, a.z - b.z) * Mathf.Rad2Deg;
            float half = mastDist - 6f;
            foreach (var n in Sides)
            {
                var side = Empty("Side", root, Vector3.zero, Quaternion.LookRotation(n)).transform;
                int k = 0;
                for (float x = -half; x < half - 0.1f; x += 8f, k++)
                {
                    Prim(PrimitiveType.Cylinder, "Mast", side, new Vector3(x, (mastBase + mastTop) * 0.5f, mastDist), new Vector3(0.35f, (mastTop - mastBase) * 0.5f, 0.35f), wood);
                    var sail = Prim(PrimitiveType.Cube, "Sail", side, new Vector3(x + 4f, (a.y + b.y) * 0.5f, (a.z + b.z) * 0.5f),
                        new Vector3(7.7f, 0.06f, len), k % 2 == 0 ? red : cream, false, Quaternion.Euler(-pitch, 0f, 0f));
                    sail.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
                    // Rope from the sail's inner edge back to the mast top.
                    Prim(PrimitiveType.Cylinder, "Rope", side, new Vector3(x, (a.y + 1f + b.y) * 0.5f, (a.z + b.z) * 0.5f),
                        new Vector3(0.05f, len * 0.5f, 0.05f), wood, false, Quaternion.Euler(90f - pitch, 0f, 0f));
                }
                Prim(PrimitiveType.Cylinder, "Mast", side, new Vector3(half, (mastBase + mastTop) * 0.5f, mastDist), new Vector3(0.35f, (mastTop - mastBase) * 0.5f, 0.35f), wood);
            }
            MergeStatic(root, "Velarium");
        }

        /// <summary>Pale marble stairs up through the seating every ~30 m, kept clear of spectators.</summary>
        static void Aisles(Transform parent, List<Bounds> exclusions)
        {
            var root = Empty("Aisles", parent, Vector3.zero).transform;
            var marble = Mat("AisleMarble", new Color(0.9f, 0.87f, 0.8f), 0.3f);
            var edge = Mat("AisleEdge", new Color(0.55f, 0.5f, 0.42f), 0.1f);
            float first = ArenaHalf + 2f;
            float[] xs = { -52f, -22f, 22f, 52f };
            foreach (var n in Sides)
            {
                var side = Empty("Side", root, Vector3.zero, Quaternion.LookRotation(n)).transform;
                Vector3 along = Vector3.Cross(Vector3.up, n);
                foreach (float x in xs)
                {
                    for (int i = 0; i < StandRows; i++)
                    {
                        float dist = first + StandRowDepth * (i + 0.5f), top = StandFirstTop + StandRise * i;
                        // Marble tread on each row with a dark nosing at its front edge.
                        Prim(PrimitiveType.Cube, "Step", side, new Vector3(x, top + 0.03f, dist), new Vector3(1.8f, 0.1f, StandRowDepth), marble);
                        Prim(PrimitiveType.Cube, "Nosing", side, new Vector3(x, top + 0.05f, dist - StandRowDepth * 0.5f + 0.06f), new Vector3(1.82f, 0.12f, 0.12f), edge);
                    }
                    for (int s = -1; s <= 1; s += 2)
                        Prim(PrimitiveType.Cube, "Rail", side, new Vector3(x + s * 1f, (StandFirstTop + StandLastTop) * 0.5f + 0.5f, first + StandRowDepth * StandRows * 0.5f),
                            new Vector3(0.12f, 0.12f, Mathf.Sqrt(Mathf.Pow(StandRowDepth * StandRows, 2f) + Mathf.Pow(StandLastTop - StandFirstTop, 2f))),
                            edge, false, Quaternion.Euler(-Mathf.Atan2(StandLastTop - StandFirstTop, StandRowDepth * StandRows) * Mathf.Rad2Deg, 0f, 0f));
                    Vector3 centre = n * (first + StandRowDepth * StandRows * 0.5f) + along * x + Vector3.up * 12f;
                    Vector3 size = Abs(along) * 2.6f + Abs(n) * (StandRowDepth * StandRows + 2f) + Vector3.up * 30f;
                    exclusions.Add(new Bounds(centre, size));
                }
            }
            MergeStatic(root, "Aisles");
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>Square towers over the four corners of the stands, with crenellations and a flag each.</summary>
        static void CornerTowers(Transform parent, List<Bounds> exclusions)
        {
            var root = Empty("Towers", parent, Vector3.zero).transform;
            var flagRoot = Empty("Flags", parent, Vector3.zero).transform;
            float c = StandBackDist - 7f, size = 15f, h = StandBackTop + 9f;
            var trim = Mat("TowerTrim", new Color(0.6f, 0.54f, 0.45f), 0.05f);
            var pole = Mat("FlagPole", new Color(0.25f, 0.22f, 0.2f), 0.4f, 0.5f);
            int k = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var t = Empty("Tower", root, new Vector3(sx * c, 0f, sz * c)).transform;
                    Prim(PrimitiveType.Cube, "Body", t, new Vector3(0f, h * 0.5f, 0f), new Vector3(size, h, size), mStone2);
                    Prim(PrimitiveType.Cube, "Band", t, new Vector3(0f, h - 3f, 0f), new Vector3(size + 0.6f, 0.8f, size + 0.6f), trim);
                    Prim(PrimitiveType.Cube, "Band", t, new Vector3(0f, StandFirstTop, 0f), new Vector3(size + 0.6f, 0.8f, size + 0.6f), trim);
                    // Arched window slots on the two faces that look into the arena.
                    for (int w = -1; w <= 1; w++)
                    {
                        Prim(PrimitiveType.Cube, "Window", t, new Vector3(w * 4f, h - 8f, -sz * (size * 0.5f + 0.02f)), new Vector3(1.6f, 3.2f, 0.1f), mGate);
                        Prim(PrimitiveType.Cube, "Window", t, new Vector3(-sx * (size * 0.5f + 0.02f), h - 8f, w * 4f), new Vector3(0.1f, 3.2f, 1.6f), mGate);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        float off = size * 0.5f - 0.9f;
                        for (int j = -2; j <= 2; j++)
                        {
                            Vector3 along = i % 2 == 0 ? Vector3.right : Vector3.forward;
                            Vector3 outw = i % 2 == 0 ? Vector3.forward * (i == 0 ? off : -off) : Vector3.right * (i == 1 ? off : -off);
                            Prim(PrimitiveType.Cube, "Merlon", t, outw + along * (j * 3f) + Vector3.up * (h + 0.8f), new Vector3(1.6f, 1.6f, 1.6f), mStone);
                        }
                    }
                    var f = Empty("Flag", flagRoot, new Vector3(sx * c, h, sz * c)).transform;
                    Prim(PrimitiveType.Cylinder, "Pole", f, new Vector3(0f, 5f, 0f), new Vector3(0.25f, 5f, 0.25f), pole);
                    Prim(PrimitiveType.Sphere, "Finial", f, new Vector3(0f, 10.2f, 0f), Vector3.one * 0.6f, mNumberGold);
                    var cloth = Prim(PrimitiveType.Cube, "Cloth", f, new Vector3(2.4f, 8.6f, 0f), new Vector3(4.6f, 2.8f, 0.05f), k++ % 2 == 0 ? mClothRed : mClothPurple);
                    cloth.transform.localRotation = Quaternion.Euler(0f, sx * sz * 30f, 0f);
                    exclusions.Add(new Bounds(new Vector3(sx * c, 15f, sz * c), new Vector3(size + 1f, 40f, size + 1f)));
                }
            MergeStatic(root, "Towers");
        }

        /// <summary>Bronze fire bowls along the parapet (emissive flames, no lights: it's daytime).</summary>
        static void Braziers(Transform parent)
        {
            var root = Empty("Braziers", parent, Vector3.zero).transform;
            var bronze = Mat("Bronze", new Color(0.55f, 0.36f, 0.16f), 0.55f, 0.85f);
            var flame = GlowMat("BrazierFlame", new Color(1f, 0.55f, 0.15f), new Color(4f, 1.6f, 0.3f), false);
            var core = GlowMat("BrazierCore", new Color(1f, 0.85f, 0.4f), new Color(5f, 3.6f, 1.2f), false);
            float d = ArenaHalf + 0.3f, y = WallHeight + 0.8f;
            foreach (var n in Sides)
            {
                Vector3 along = Vector3.Cross(Vector3.up, n);
                for (float x = -66f; x <= 66f; x += 22f)
                {
                    if (NearGate(n, x, 8f) || (n == Vector3.forward && Mathf.Abs(x) < 10f)) continue;
                    Vector3 p = n * d + along * x + Vector3.up * y;
                    var b = Empty("Brazier", root, p).transform;
                    Prim(PrimitiveType.Cylinder, "Stem", b, new Vector3(0f, 0.35f, 0f), new Vector3(0.25f, 0.35f, 0.25f), bronze);
                    Prim(PrimitiveType.Cylinder, "Bowl", b, new Vector3(0f, 0.85f, 0f), new Vector3(1.1f, 0.18f, 1.1f), bronze);
                    var f1 = Prim(PrimitiveType.Sphere, "Flame", b, new Vector3(0f, 1.45f, 0f), new Vector3(0.8f, 1.2f, 0.8f), flame);
                    var f2 = Prim(PrimitiveType.Sphere, "Core", b, new Vector3(0f, 1.3f, 0f), new Vector3(0.45f, 0.7f, 0.45f), core);
                    f1.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                    f2.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            MergeStatic(root, "Braziers");
        }

        /// <summary>Pompeian fresco panels with dark frames along the arena wall, round shields between them.</summary>
        static void WallArt(Transform parent)
        {
            var root = Empty("WallArt", parent, Vector3.zero).transform;
            var frame = Mat("FrescoFrame", new Color(0.18f, 0.12f, 0.1f), 0.1f);
            Material[] panels =
            {
                Mat("FrescoRed", new Color(0.6f, 0.14f, 0.09f), 0.1f),
                Mat("FrescoOchre", new Color(0.78f, 0.56f, 0.22f), 0.1f),
            };
            var shieldRed = Mat("ShieldRed", new Color(0.55f, 0.1f, 0.07f), 0.35f, 0.1f);
            var shieldBlue = Mat("ShieldBlue", new Color(0.12f, 0.2f, 0.45f), 0.35f, 0.1f);
            float face = ArenaHalf - 0.02f;
            int k = 0;
            foreach (var n in Sides)
            {
                var side = Empty("Side", root, Vector3.zero, Quaternion.LookRotation(n)).transform;
                for (float x = -66f; x <= 66f; x += 11f, k++)
                {
                    if (NearGate(n, x, 8f)) continue;
                    Prim(PrimitiveType.Cube, "Frame", side, new Vector3(x, 2.6f, face), new Vector3(7.4f, 3.4f, 0.06f), frame);
                    Prim(PrimitiveType.Cube, "Panel", side, new Vector3(x, 2.6f, face - 0.04f), new Vector3(6.8f, 2.8f, 0.06f), panels[k % 2]);
                    Prim(PrimitiveType.Cube, "Stripe", side, new Vector3(x, 1.65f, face - 0.08f), new Vector3(6.8f, 0.18f, 0.04f), mNumberGold);
                    float sx = x + 5.5f;
                    if (sx < 66f && !NearGate(n, sx, 8f))
                    {
                        Prim(PrimitiveType.Cylinder, "Shield", side, new Vector3(sx, 3f, face - 0.08f), new Vector3(1.5f, 0.06f, 1.5f), k % 2 == 0 ? shieldBlue : shieldRed,
                            false, Quaternion.Euler(90f, 0f, 0f));
                        Prim(PrimitiveType.Sphere, "Boss", side, new Vector3(sx, 3f, face - 0.16f), new Vector3(0.4f, 0.4f, 0.25f), mNumberGold);
                    }
                }
            }
            MergeStatic(root, "WallArt");
        }

        // ---------------------------------------------------------------- arena floor

        /// <summary>Bronze gladiator on a marble plinth in each corner of the sand (plinth is solid).</summary>
        static void Statues(Transform parent)
        {
            var root = Empty("Statues", parent, Vector3.zero).transform;
            var bronze = Mat("StatueBronze", new Color(0.42f, 0.3f, 0.16f), 0.6f, 0.9f);
            var marble = Mat("AisleMarble", new Color(0.9f, 0.87f, 0.8f), 0.3f);
            float c = ArenaHalf - 8f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                {
                    var pos = new Vector3(sx * c, 0f, sz * c);
                    var s = Empty("Gladiator", root, pos, Quaternion.LookRotation(-new Vector3(sx, 0f, sz))).transform;
                    Prim(PrimitiveType.Cube, "Plinth", s, new Vector3(0f, 1.2f, 0f), new Vector3(4f, 2.4f, 4f), marble, true);
                    Prim(PrimitiveType.Cube, "PlinthCap", s, new Vector3(0f, 2.5f, 0f), new Vector3(4.4f, 0.25f, 4.4f), marble);
                    Prim(PrimitiveType.Cube, "Plaque", s, new Vector3(0f, 1.3f, 2.01f), new Vector3(2.4f, 0.8f, 0.04f), mNumberGold);
                    // Figure ~6 m tall: legs, kilt, torso, arms, helmeted head with crest, round shield, raised sword.
                    float b = 2.6f;
                    Prim(PrimitiveType.Cube, "LegL", s, new Vector3(-0.45f, b + 1.1f, 0f), new Vector3(0.45f, 2.2f, 0.5f), bronze);
                    Prim(PrimitiveType.Cube, "LegR", s, new Vector3(0.5f, b + 1.1f, 0.35f), new Vector3(0.45f, 2.2f, 0.5f), bronze, false, Quaternion.Euler(-12f, 0f, 0f));
                    Prim(PrimitiveType.Cylinder, "Kilt", s, new Vector3(0f, b + 2.4f, 0f), new Vector3(1.5f, 0.35f, 1.1f), bronze);
                    Prim(PrimitiveType.Cube, "Torso", s, new Vector3(0f, b + 3.5f, 0f), new Vector3(1.4f, 1.8f, 0.8f), bronze);
                    Prim(PrimitiveType.Cube, "ArmL", s, new Vector3(-0.95f, b + 3.4f, 0.35f), new Vector3(0.35f, 1.5f, 0.35f), bronze, false, Quaternion.Euler(-50f, 0f, 0f));
                    Prim(PrimitiveType.Cube, "ArmR", s, new Vector3(0.95f, b + 4.6f, 0f), new Vector3(0.35f, 1.6f, 0.35f), bronze, false, Quaternion.Euler(0f, 0f, -25f));
                    Prim(PrimitiveType.Sphere, "Head", s, new Vector3(0f, b + 4.85f, 0f), new Vector3(0.75f, 0.85f, 0.75f), bronze);
                    Prim(PrimitiveType.Cube, "Crest", s, new Vector3(0f, b + 5.4f, -0.05f), new Vector3(0.15f, 0.5f, 1f), mClothRed);
                    Prim(PrimitiveType.Cylinder, "Shield", s, new Vector3(-1.05f, b + 3.3f, 0.75f), new Vector3(1.6f, 0.08f, 1.6f), bronze, false, Quaternion.Euler(90f, 0f, 0f));
                    Prim(PrimitiveType.Sphere, "Boss", s, new Vector3(-1.05f, b + 3.3f, 0.85f), Vector3.one * 0.35f, mNumberGold);
                    Prim(PrimitiveType.Cube, "Sword", s, new Vector3(1.35f, b + 5.9f, 0f), new Vector3(0.12f, 1.8f, 0.05f), mNumberGold, false, Quaternion.Euler(0f, 0f, -25f));
                }
        }

        /// <summary>Loose barrel stacks by the walls, well clear of the road (cars can knock them over).</summary>
        static void Barrels(Transform parent)
        {
            var root = Empty("Barrels", parent, Vector3.zero).transform;
            var wood = Mat("BarrelWood", new Color(0.45f, 0.3f, 0.16f), 0.15f);
            Vector3[] spots = { new Vector3(-40f, 0f, 69f), new Vector3(69f, 0f, -40f), new Vector3(-69f, 0f, -50f), new Vector3(20f, 0f, -69f), new Vector3(69f, 0f, 62f) };
            int i = 0;
            foreach (var spot in spots)
            {
                for (int k = 0; k < 5; k++)
                {
                    Vector3 p = spot + new Vector3((k % 3 - 1) * 1.1f, 0.6f + (k >= 3 ? 1.2f : 0f), 0f);
                    if (k >= 3) p.x = spot.x + (k - 3.5f) * 1.1f;
                    var barrel = Prim(PrimitiveType.Cylinder, "Barrel" + i++, root, p, new Vector3(1f, 0.6f, 1f), wood, true);
                    Prop(barrel, 25f);
                }
            }
        }

        // ---------------------------------------------------------------- outside

        /// <summary>Dry grassland around the arena with cypresses, a few villas and hills in the haze.</summary>
        static void Countryside(Transform parent)
        {
            var root = Empty("Countryside", parent, Vector3.zero).transform;
            var grass = Mat("DryGrass", new Color(0.62f, 0.66f, 0.42f), 0f);
            ApplySand(grass, sandTex, sandGrain, new Vector2(10f, 10f), new Vector2(1200f, 1200f), new Color(0.6f, 0.62f, 0.4f));
            var ground = Prim(PrimitiveType.Cube, "Ground", root, new Vector3(0f, -0.12f, 0f), new Vector3(5000f, 0.2f, 5000f), grass);
            ground.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var rng = new System.Random(21);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var trees = Empty("Trees", root, Vector3.zero).transform;
            var bark = Mat("Bark", new Color(0.3f, 0.22f, 0.15f), 0.05f);
            var cypress = Mat("Cypress", new Color(0.16f, 0.26f, 0.13f), 0.05f);
            var pine = Mat("Pine", new Color(0.22f, 0.32f, 0.15f), 0.05f);
            for (int i = 0; i < 90; i++)
            {
                float ang = R(0f, Mathf.PI * 2f), r = R(118f, 300f);
                var p = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                if (p.z < -100f && Mathf.Abs(p.x) < 30f) continue; // keep the view out of the garage clear
                float h = R(8f, 14f);
                Prim(PrimitiveType.Cylinder, "Trunk", trees, p + Vector3.up * 1.2f, new Vector3(0.5f, 1.2f, 0.5f), bark);
                if (i % 5 == 0)
                    Prim(PrimitiveType.Sphere, "Canopy", trees, p + Vector3.up * (h * 0.6f + 2f), new Vector3(h * 0.7f, h * 0.25f, h * 0.7f), pine);
                else
                    Prim(PrimitiveType.Sphere, "Canopy", trees, p + Vector3.up * (h * 0.5f + 1.5f), new Vector3(h * 0.22f, h, h * 0.22f), cypress);
            }
            MergeStatic(trees, "Trees");

            var villas = Empty("Villas", root, Vector3.zero).transform;
            var plaster = Mat("Plaster", new Color(0.88f, 0.82f, 0.7f), 0.05f);
            var tile = Mat("RoofTile", new Color(0.62f, 0.27f, 0.16f), 0.1f);
            for (int i = 0; i < 12; i++)
            {
                float ang = R(0f, Mathf.PI * 2f), r = R(190f, 320f);
                var p = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                var v = Empty("Villa", villas, p, Quaternion.Euler(0f, R(0f, 360f), 0f)).transform;
                float w = R(10f, 18f), d = R(8f, 12f), h = R(5f, 7f);
                Prim(PrimitiveType.Cube, "Walls", v, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), plaster);
                Prim(PrimitiveType.Cube, "Roof", v, new Vector3(0f, h + 1f, -d * 0.25f), new Vector3(w + 1f, 0.4f, d * 0.6f), tile, false, Quaternion.Euler(20f, 0f, 0f));
                Prim(PrimitiveType.Cube, "Roof", v, new Vector3(0f, h + 1f, d * 0.25f), new Vector3(w + 1f, 0.4f, d * 0.6f), tile, false, Quaternion.Euler(-20f, 0f, 0f));
            }
            MergeStatic(villas, "Villas");

            var hills = Empty("Hills", root, Vector3.zero).transform;
            var hillMat = Mat("Hill", new Color(0.45f, 0.5f, 0.32f), 0f);
            for (int i = 0; i < 14; i++)
            {
                float ang = i / 14f * Mathf.PI * 2f + R(-0.15f, 0.15f), r = R(480f, 620f);
                var hill = Prim(PrimitiveType.Sphere, "Hill", hills, new Vector3(Mathf.Cos(ang) * r, -10f, Mathf.Sin(ang) * r),
                    new Vector3(R(220f, 360f), R(70f, 130f), R(160f, 240f)), hillMat, false, Quaternion.Euler(0f, R(0f, 360f), 0f));
                hill.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
            MergeStatic(hills, "Hills");
        }

        // ---------------------------------------------------------------- merge

        /// <summary>
        /// Combine every mesh under <paramref name="root"/> into one mesh per material (saved as assets) and remove
        /// the originals: hundreds of decoration boxes become a handful of draw calls. Visual only (no colliders).
        /// </summary>
        static void MergeStatic(Transform root, string name)
        {
            var byMat = new Dictionary<Material, List<CombineInstance>>();
            var shadows = new Dictionary<Material, ShadowCastingMode>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                var mat = mr.sharedMaterial;
                if (!byMat.TryGetValue(mat, out var list)) byMat[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mf.sharedMesh, transform = toRoot * mf.transform.localToWorldMatrix });
                shadows[mat] = mr.shadowCastingMode;
            }
            for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);
            int k = 0;
            foreach (var kv in byMat)
            {
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                string meshName = $"Dress_{name}_{k++}";
                var go = MeshObj(meshName, root, Vector3.zero, SaveMesh(mesh, meshName), kv.Key);
                go.GetComponent<MeshRenderer>().shadowCastingMode = shadows[kv.Key];
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }
    }
}
