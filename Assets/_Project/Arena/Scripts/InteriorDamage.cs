using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// What the driver sees break inside: cracked windscreen and dash, dash pushed in,
    /// glovebox flapping open, sun visor dropping.
    /// </summary>
    public class InteriorDamage : MonoBehaviour
    {
        public CarDamage damage;
        public Rigidbody carBody;
        public CrackDecals windscreen;
        public CrackDecals dashCracks;
        public Transform dashboard;
        public Transform gloveboxHinge;
        public Transform sunVisorHinge;

        [Header("Tuning")]
        public float windscreenCrackSeverity = 0.12f;
        public float dashPushPerSeverity = 0.06f;
        public float maxDashPush = 0.14f;
        public float gloveboxOpenAngle = -75f;
        public float visorDropAngle = -50f;

        Vector3 dashBasePos;
        Quaternion dashBaseRot, gloveBaseRot, visorBaseRot;
        float frontAccum, dashPush, dashTwist;
        bool gloveOpen, visorDown;
        float gloveAngle, gloveVel, visorAngle, visorVel;

        void Awake()
        {
            if (dashboard != null) { dashBasePos = dashboard.localPosition; dashBaseRot = dashboard.localRotation; }
            if (gloveboxHinge != null) gloveBaseRot = gloveboxHinge.localRotation;
            if (sunVisorHinge != null) visorBaseRot = sunVisorHinge.localRotation;
        }

        void OnEnable()
        {
            damage.Impact += OnImpact;
            damage.Repaired += OnRepaired;
        }

        void OnDisable()
        {
            damage.Impact -= OnImpact;
            damage.Repaired -= OnRepaired;
        }

        void OnImpact(ImpactInfo i)
        {
            float s = i.severity;
            if (i.front)
            {
                frontAccum += s;
                dashPush = Mathf.Min(maxDashPush, dashPush + s * dashPushPerSeverity);
                dashTwist = Mathf.Clamp(dashTwist + Random.Range(-1f, 1f) * s * 6f, -8f, 8f);
                if (s > 0.25f && dashCracks != null)
                    dashCracks.AddCrack(new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(-0.15f, 0.15f)), s);
            }

            if (windscreen != null && s > windscreenCrackSeverity && (i.front || i.top || s > 0.45f))
            {
                // Map the impact onto the glass: across = car x, height = higher for roof hits.
                float y = i.top ? Random.Range(0.1f, 0.28f) : Random.Range(-0.25f, 0.15f);
                windscreen.AddCrack(new Vector2(i.localPoint.x * 0.8f + Random.Range(-0.15f, 0.15f), y), s);
            }

            if (frontAccum > 0.5f || s > 0.55f) gloveOpen = true;
            if (s > 0.4f) visorDown = true;

            gloveVel += Random.Range(-1f, 1f) * s * 400f;
            visorVel += Random.Range(-1f, 1f) * s * 300f;
        }

        void OnRepaired()
        {
            frontAccum = dashPush = dashTwist = 0f;
            gloveOpen = visorDown = false;
            gloveAngle = gloveVel = visorAngle = visorVel = 0f;
            if (windscreen != null) windscreen.Clear();
            if (dashCracks != null) dashCracks.Clear();
            Apply();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float bumps = 0f;
            if (carBody != null) bumps = (Mathf.PerlinNoise(Time.time * 6f, 1.7f) - 0.5f) * Mathf.Clamp01(carBody.linearVelocity.magnitude / 15f);

            Spring(ref gloveAngle, ref gloveVel, gloveOpen ? gloveboxOpenAngle + bumps * 25f : 0f, gloveOpen ? 60f : 400f, 5f, dt);
            gloveAngle = Mathf.Clamp(gloveAngle, -100f, 0f);
            Spring(ref visorAngle, ref visorVel, visorDown ? visorDropAngle + bumps * 20f : 0f, visorDown ? 80f : 400f, 6f, dt);
            visorAngle = Mathf.Clamp(visorAngle, -100f, 0f);
            Apply();
        }

        static void Spring(ref float x, ref float v, float target, float k, float c, float dt)
        {
            v += ((target - x) * k - v * c) * dt;
            x += v * dt;
        }

        void Apply()
        {
            if (dashboard != null)
            {
                dashboard.localPosition = dashBasePos + new Vector3(0f, dashPush * 0.3f, -dashPush);
                dashboard.localRotation = dashBaseRot * Quaternion.Euler(-dashPush * 70f, dashTwist, dashTwist * 0.5f);
            }
            if (gloveboxHinge != null) gloveboxHinge.localRotation = gloveBaseRot * Quaternion.Euler(gloveAngle, 0f, 0f);
            if (sunVisorHinge != null) sunVisorHinge.localRotation = visorBaseRot * Quaternion.Euler(visorAngle, 0f, 0f);
        }
    }
}
