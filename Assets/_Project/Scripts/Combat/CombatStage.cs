using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedCasino.Combat
{
    /// <summary>
    /// Builds the placeholder 3D casino table, gamblers and ghost, and shows who is fighting whom:
    /// an attack marker over the attacker, a shield/target disc over the defender, an attack beam,
    /// an attacker lunge and a camera shot framing the exchange. Also plays hit feedback:
    /// flashes, shakes, hit-stop, camera punch and coin bursts.
    /// </summary>
    public class CombatStage : MonoBehaviour
    {
        class Actor
        {
            public Combatant C;
            public Transform T;
            public Renderer Body;
            public Vector3 Home;
            public Color BaseColor;
            public float MarkerHeight;
            public float Shake;
            public float Lunge;
        }

        static readonly Vector3 OverviewPos = new Vector3(0f, 4.6f, -7.2f);
        static readonly Vector3 OverviewLook = new Vector3(0f, 1.6f, 1.2f);

        [Tooltip("Shifts framed action to the right so the game panel on the left doesn't cover it.")]
        [SerializeField] float shotScreenShift = 1.1f;

        public Camera Cam { get; private set; }

        readonly Dictionary<Combatant, Actor> actors = new Dictionary<Combatant, Actor>();
        Combatant ghost, attacker, defender;
        Transform attackMarker, defendMarker;
        Material defendMarkerMat;
        LineRenderer beam;
        ParticleSystem coins;
        CombatCamera director;
        StageMaterials mats;
        bool hitStopping;

        public void BuildEnvironment(StageMaterials materials)
        {
            mats = materials ?? new StageMaterials();

            Cam = Camera.main;
            if (Cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                Cam = camGo.GetComponent<Camera>();
            }
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.03f, 0.015f, 0.04f);
            director = Cam.GetComponent<CombatCamera>();
            if (director == null) director = Cam.gameObject.AddComponent<CombatCamera>();
            director.Init(Cam, OverviewPos, OverviewLook);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.12f, 0.2f);

            var moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.65f, 1f);
            moon.intensity = 0.35f;
            moon.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            var lamp = new GameObject("TableLamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.75f, 0.45f);
            lamp.range = 14f;
            lamp.intensity = 4f;
            lamp.transform.position = new Vector3(0f, 4.5f, 0f);

            var floor = Prim(PrimitiveType.Plane, "Carpet", new Color(0.28f, 0.05f, 0.08f));
            floor.localScale = new Vector3(3f, 1f, 3f);

            var table = Prim(PrimitiveType.Cylinder, "CardTable", new Color(0.05f, 0.35f, 0.15f));
            table.position = new Vector3(0f, 0.85f, 0.8f);
            table.localScale = new Vector3(5.5f, 0.08f, 3.6f);
            var rim = Prim(PrimitiveType.Cylinder, "TableRim", new Color(0.3f, 0.15f, 0.05f));
            rim.position = new Vector3(0f, 0.8f, 0.8f);
            rim.localScale = new Vector3(5.9f, 0.08f, 4f);
            var leg = Prim(PrimitiveType.Cylinder, "TableLeg", new Color(0.2f, 0.1f, 0.04f));
            leg.position = new Vector3(0f, 0.4f, 0.8f);
            leg.localScale = new Vector3(0.6f, 0.4f, 0.6f);

            attackMarker = Prim(PrimitiveType.Cube, "AttackMarker", CombatUI.AttackColor, 2.5f);
            attackMarker.localScale = Vector3.one * 0.35f;
            attackMarker.gameObject.SetActive(false);

            defendMarker = Prim(PrimitiveType.Cylinder, "DefendMarker", CombatUI.DefendColor, 2.5f);
            defendMarker.localScale = new Vector3(0.6f, 0.04f, 0.6f);
            defendMarkerMat = defendMarker.GetComponent<Renderer>().material;
            defendMarker.gameObject.SetActive(false);

            beam = new GameObject("AttackBeam").AddComponent<LineRenderer>();
            beam.material = mats.MakeBeam();
            beam.positionCount = 2;
            beam.numCapVertices = 4;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.enabled = false;

            coins = BuildCoinBurst();
        }

        ParticleSystem BuildCoinBurst()
        {
            var go = new GameObject("CoinBurst");
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
            main.gravityModifier = 1.3f;
            main.startColor = new ParticleSystem.MinMaxGradient(GambleGame.Gold, new Color(1f, 0.95f, 0.6f));
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.2f;
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = mats.MakeParticles();
            return ps;
        }

        public void BuildActors(List<Combatant> players, Combatant ghostCombatant)
        {
            int n = players.Count;
            for (int i = 0; i < n; i++)
            {
                float x = n == 1 ? 0f : Mathf.Lerp(-2.6f, 2.6f, (float)i / (n - 1));
                var body = Prim(PrimitiveType.Capsule, players[i].Name, players[i].Color);
                body.position = new Vector3(x, 1f, -1.7f);
                body.localScale = new Vector3(0.8f, 1f, 0.8f);
                Register(players[i], body, body.GetComponent<Renderer>(), 1.5f);
            }

            ghost = ghostCombatant;
            var root = new GameObject("Ghost").transform;
            root.position = new Vector3(0f, 2.4f, 3.4f);
            var ghostBody = Prim(PrimitiveType.Capsule, "GhostBody", CombatUI.GhostColor, 1.2f);
            ghostBody.SetParent(root, false);
            ghostBody.localScale = new Vector3(1.6f, 1.5f, 1.6f);
            var hat = Prim(PrimitiveType.Cylinder, "TopHat", new Color(0.05f, 0.05f, 0.07f));
            hat.SetParent(root, false);
            hat.localPosition = new Vector3(0f, 1.75f, 0f);
            hat.localScale = new Vector3(0.9f, 0.35f, 0.9f);
            var brim = Prim(PrimitiveType.Cylinder, "HatBrim", new Color(0.05f, 0.05f, 0.07f));
            brim.SetParent(root, false);
            brim.localPosition = new Vector3(0f, 1.42f, 0f);
            brim.localScale = new Vector3(1.4f, 0.03f, 1.4f);
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = Prim(PrimitiveType.Sphere, "Eye", new Color(1f, 0.85f, 0.2f), 3f);
                eye.SetParent(root, false);
                eye.localPosition = new Vector3(0.3f * s, 0.75f, -0.72f);
                eye.localScale = Vector3.one * 0.22f;
            }
            var glow = new GameObject("GhostGlow").AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = CombatUI.GhostColor;
            glow.range = 6f;
            glow.intensity = 3f;
            glow.transform.SetParent(root, false);
            glow.transform.localPosition = new Vector3(0f, 0f, -1.5f);
            Register(ghost, root, ghostBody.GetComponent<Renderer>(), 2.35f);
        }

        void Register(Combatant c, Transform t, Renderer r, float markerHeight)
        {
            actors[c] = new Actor
            {
                C = c, T = t, Body = r, Home = t.position, BaseColor = r.material.GetColor("_BaseColor"), MarkerHeight = markerHeight
            };
        }

        public Vector3 PopupPoint(Combatant c) =>
            actors.TryGetValue(c, out var a) ? a.T.position + Vector3.up * (c == ghost ? 0.1f : 1.2f) : Vector3.zero;

        /// <summary>Show who attacks whom and frame the exchange. Pass nulls to clear (overview shot).</summary>
        public void SetRoles(Combatant attackerCombatant, Combatant defenderCombatant, bool defending = true)
        {
            bool newAttacker = attackerCombatant != null && attackerCombatant != attacker;
            attacker = attackerCombatant;
            defender = defenderCombatant;
            var markerColor = defending ? CombatUI.DefendColor : CombatUI.TargetColor;
            defendMarkerMat.SetColor("_BaseColor", markerColor);
            defendMarkerMat.SetColor("_EmissionColor", markerColor * 2.5f);
            attackMarker.gameObject.SetActive(attacker != null);
            defendMarker.gameObject.SetActive(defender != null);
            beam.enabled = attacker != null && defender != null;
            if (newAttacker) CasinoSfx.Play(Sfx.Whoosh, Random.Range(0.9f, 1.1f));
            UpdateShot();
        }

        void UpdateShot()
        {
            if (attacker == null || defender == null
                || !actors.TryGetValue(attacker, out var a) || !actors.TryGetValue(defender, out var d))
            {
                director.SetShot(OverviewPos, OverviewLook);
                return;
            }

            Vector3 from = a.Home, to = d.Home;
            Vector3 away = from - to;
            away.y = 0f;
            away.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, away).normalized;
            Vector3 pos, look;
            // Framing aims low so both fighters sit above the bottom UI panels.
            if (attacker == ghost)
            {
                // Wide side-on shot of the table: ghost on one side, its victim on the other.
                Vector3 mid = Vector3.Lerp(from, to, 0.5f);
                pos = mid + side * 8f + Vector3.up * 4.2f - away * 0.6f;
                look = mid - Vector3.up * 0.6f;
            }
            else
            {
                // Behind the gambler, looking past them at the ghost.
                pos = from + away * 4.2f + Vector3.up * 2.2f - side * 1.2f;
                look = Vector3.Lerp(from, to, 0.55f) - Vector3.up * 0.3f;
            }
            Vector3 fwd = (look - pos).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            director.SetShot(pos, look - right * shotScreenShift);
        }

        /// <summary>Hit feedback. Strength 0..1 scales shake, camera punch and hit-stop.</summary>
        public void Hit(Combatant c, float strength = 0.5f)
        {
            strength = Mathf.Clamp01(strength);
            if (actors.TryGetValue(c, out var a)) a.Shake = 0.25f + 0.25f * strength;
            director.Punch(0.3f + 0.7f * strength);
            CasinoSfx.Play(Sfx.Hit, Random.Range(0.85f, 1.1f) - strength * 0.2f);
            if (strength >= 0.5f) StartCoroutine(HitStop(0.05f + 0.1f * strength));
        }

        public void CoinBurst(Combatant c, int count)
        {
            if (!actors.TryGetValue(c, out var a)) return;
            coins.transform.position = a.T.position + Vector3.up * 0.6f;
            coins.Emit(count);
            CasinoSfx.Play(Sfx.Coin, Random.Range(0.95f, 1.15f));
        }

        IEnumerator HitStop(float duration)
        {
            if (hitStopping) yield break;
            hitStopping = true;
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(duration);
            Time.timeScale = 1f;
            hitStopping = false;
        }

        void OnDisable() => Time.timeScale = 1f;

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var a in actors.Values) UpdateActor(a, dt);

            if (attacker != null && actors.TryGetValue(attacker, out var atk))
            {
                attackMarker.position = atk.T.position + Vector3.up * (atk.MarkerHeight + Mathf.Sin(Time.time * 5f) * 0.1f);
                attackMarker.rotation = Quaternion.Euler(45f, Time.time * 180f, 45f);
            }
            if (defender != null && actors.TryGetValue(defender, out var def))
            {
                defendMarker.position = def.T.position + Vector3.up * (def.MarkerHeight + 0.1f);
                defendMarker.rotation = Quaternion.LookRotation(Cam.transform.forward) * Quaternion.Euler(90f, 0f, 0f);
            }
            if (beam.enabled && actors.TryGetValue(attacker, out var from) && actors.TryGetValue(defender, out var to))
            {
                beam.SetPosition(0, from.T.position + Vector3.up * (attacker == ghost ? 0.5f : 0.4f));
                beam.SetPosition(1, to.T.position + Vector3.up * (defender == ghost ? 0.5f : 0.4f));
                float pulse = 0.05f + Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.05f;
                beam.startWidth = pulse * 1.6f;
                beam.endWidth = pulse * 0.5f;
                var c0 = CombatUI.AttackColor;
                var c1 = CombatUI.AttackColor;
                c0.a = 0.9f;
                c1.a = 0.35f;
                beam.startColor = c0;
                beam.endColor = c1;
            }
        }

        void UpdateActor(Actor a, float dt)
        {
            bool isGhost = a.C == ghost;
            bool down = a.C.IsDown && !isGhost;
            Vector3 pos = a.Home;

            if (isGhost) pos += Vector3.up * Mathf.Sin(Time.time * 1.6f) * 0.18f;
            if (down) pos += Vector3.down * 0.6f;

            float lungeTarget = a.C == attacker && defender != null ? 1f : 0f;
            a.Lunge = Mathf.MoveTowards(a.Lunge, lungeTarget, dt * 3f);
            if (a.Lunge > 0f && defender != null && actors.TryGetValue(defender, out var target))
            {
                Vector3 dir = target.Home - a.Home;
                dir.y = 0f;
                pos += dir.normalized * (0.6f * a.Lunge);
            }

            Color color = down ? a.BaseColor * 0.3f : a.BaseColor;
            if (a.Shake > 0f)
            {
                a.Shake -= dt;
                pos += (Vector3)Random.insideUnitCircle * 0.15f;
                color = Mathf.Repeat(a.Shake, 0.1f) < 0.05f ? Color.white : Color.red;
            }
            a.T.position = pos;

            if (isGhost)
                a.T.rotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.7f) * 12f, Mathf.Sin(Time.time * 1.1f) * 4f);
            else
                a.T.rotation = down ? Quaternion.Euler(0f, 0f, 80f) : Quaternion.Euler(a.Lunge * 15f, 0f, 0f);

            SetColor(a.Body, color);
        }

        Transform Prim(PrimitiveType type, string name, Color color, float emission = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mat = mats.MakeLit(emission > 0f);
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.material = mat;
            SetColor(r, color);
            if (emission > 0f && mat != null)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            return go.transform;
        }

        static void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            r.material.SetColor("_BaseColor", c);
            r.material.SetColor("_Color", c);
        }
    }
}

namespace HauntedCasino.Combat
{
    /// <summary>
    /// Material templates for the generated stage. They must be real assets referenced from the scene:
    /// a build strips shaders nothing references, so Shader.Find alone returns null in players.
    /// </summary>
    [System.Serializable]
    public class StageMaterials
    {
        [Tooltip("URP/Lit, used for the table, gamblers and props.")]
        public Material lit;
        [Tooltip("URP/Lit with Emission enabled (keeps the emissive shader variant in builds).")]
        public Material litEmissive;
        [Tooltip("Sprites/Default (vertex colours), used for the attack beam.")]
        public Material beam;
        [Tooltip("URP Particles/Unlit, used for coin bursts.")]
        public Material particles;

        public Material MakeLit(bool emissive) => Make(emissive && litEmissive != null ? litEmissive : lit, "Universal Render Pipeline/Lit");
        public Material MakeBeam() => Make(beam, "Sprites/Default");
        public Material MakeParticles() => Make(particles, "Universal Render Pipeline/Particles/Unlit");

        static Material Make(Material template, string fallbackShader)
        {
            if (template != null) return new Material(template);
            var shader = Shader.Find(fallbackShader);
            if (shader == null)
            {
                Debug.LogError($"[CombatStage] No material assigned and shader '{fallbackShader}' is not in this build. Assign Stage Materials on the CombatManager.");
                shader = Shader.Find("Sprites/Default");
            }
            return shader != null ? new Material(shader) : null;
        }
    }
}
