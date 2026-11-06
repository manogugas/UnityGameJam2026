using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HauntedCasino.Combat
{
    /// <summary>Builds the placeholder 3D casino table, gamblers and ghost, and plays hit feedback.</summary>
    public class CombatStage : MonoBehaviour
    {
        public Camera Cam { get; private set; }

        readonly Dictionary<Combatant, Transform> actors = new Dictionary<Combatant, Transform>();
        readonly Dictionary<Combatant, Renderer> bodies = new Dictionary<Combatant, Renderer>();
        readonly Dictionary<Combatant, Vector3> homes = new Dictionary<Combatant, Vector3>();
        Combatant ghost;
        Combatant active;
        Transform marker;
        Shader litShader;

        public void BuildEnvironment()
        {
            litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");

            Cam = Camera.main;
            if (Cam == null)
            {
                var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                camGo.tag = "MainCamera";
                Cam = camGo.GetComponent<Camera>();
            }
            Cam.transform.position = new Vector3(0f, 4.6f, -7.2f);
            Cam.transform.LookAt(new Vector3(0f, 1.6f, 1.2f));
            Cam.fieldOfView = 55f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.03f, 0.015f, 0.04f);

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

            var markerT = Prim(PrimitiveType.Sphere, "TurnMarker", GambleGame.Gold, 2f);
            markerT.localScale = Vector3.one * 0.3f;
            marker = markerT;
            marker.gameObject.SetActive(false);
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
                Register(players[i], body);
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
            Register(ghost, root, ghostBody.GetComponent<Renderer>());
        }

        void Register(Combatant c, Transform t, Renderer r = null)
        {
            actors[c] = t;
            bodies[c] = r != null ? r : t.GetComponent<Renderer>();
            homes[c] = t.position;
        }

        public Vector3 PopupPoint(Combatant c) =>
            actors.TryGetValue(c, out var t) ? t.position + Vector3.up * (c == ghost ? 0.9f : 1.5f) : Vector3.zero;

        public void SetActive(Combatant c)
        {
            active = c;
            marker.gameObject.SetActive(c != null);
        }

        public void Hit(Combatant c) => StartCoroutine(HitRoutine(c));

        public void SetDown(Combatant c, bool down)
        {
            if (!actors.TryGetValue(c, out var t) || c == ghost) return;
            t.rotation = down ? Quaternion.Euler(0f, 0f, 80f) : Quaternion.identity;
            t.position = homes[c] + (down ? Vector3.down * 0.6f : Vector3.zero);
            SetColor(bodies[c], down ? c.Color * 0.3f : c.Color);
        }

        IEnumerator HitRoutine(Combatant c)
        {
            if (!actors.TryGetValue(c, out var t)) yield break;
            var r = bodies[c];
            for (float time = 0; time < 0.35f; time += Time.deltaTime)
            {
                SetColor(r, Mathf.Repeat(time, 0.1f) < 0.05f ? Color.white : Color.red);
                t.position = homes[c] + (Vector3)Random.insideUnitCircle * 0.15f + GhostBob(c);
                yield return null;
            }
            t.position = homes[c] + GhostBob(c);
            SetColor(r, c.IsDown && c != ghost ? c.Color * 0.3f : c == ghost ? CombatUI.GhostColor : c.Color);
            if (c.IsDown && c != ghost) SetDown(c, true);
        }

        Vector3 GhostBob(Combatant c) => c == ghost ? Vector3.up * Mathf.Sin(Time.time * 1.6f) * 0.18f : Vector3.zero;

        void Update()
        {
            if (ghost != null && actors.TryGetValue(ghost, out var g))
            {
                g.position = homes[ghost] + GhostBob(ghost);
                g.rotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.7f) * 12f, Mathf.Sin(Time.time * 1.1f) * 4f);
            }
            if (active != null && marker != null && actors.TryGetValue(active, out var a))
            {
                float height = active == ghost ? 3.3f : 2.2f;
                marker.position = a.position + Vector3.up * (height + Mathf.Sin(Time.time * 4f) * 0.12f);
            }
        }

        Transform Prim(PrimitiveType type, string name, Color color, float emission = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var mat = new Material(litShader);
            var r = go.GetComponent<Renderer>();
            r.material = mat;
            SetColor(r, color);
            if (emission > 0f)
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
