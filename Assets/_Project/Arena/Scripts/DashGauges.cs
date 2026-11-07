using UnityEngine;

namespace CarArena
{
    /// <summary>Drives the speedometer and tachometer needles. Needles rotate about their local Z.</summary>
    public class DashGauges : MonoBehaviour
    {
        public CarController car;
        public Transform speedNeedle;
        public Transform rpmNeedle;
        public float maxSpeedKmh = 200f;
        public float maxRpm = 8000f;
        [Tooltip("Total needle sweep in degrees, centred on straight up.")]
        public float sweep = 270f;
        public float needleSharpness = 14f;

        float speedAngle, rpmAngle;

        public static float AngleFor(float value01, float sweep) => (value01 - 0.5f) * sweep;

        void LateUpdate()
        {
            if (car == null) return;
            float k = 1f - Mathf.Exp(-Time.deltaTime * needleSharpness);
            speedAngle = Mathf.Lerp(speedAngle, AngleFor(Mathf.Clamp01(car.SpeedKmh / maxSpeedKmh), sweep), k);
            float shake = Mathf.Sin(Time.time * 47f) * 0.6f * Mathf.InverseLerp(6000f, maxRpm, car.Rpm);
            rpmAngle = Mathf.Lerp(rpmAngle, AngleFor(Mathf.Clamp01(car.Rpm / maxRpm), sweep) + shake, k);
            // Angle is clockwise from straight up as the driver sees it, which is negative Z.
            if (speedNeedle != null) speedNeedle.localRotation = Quaternion.Euler(0f, 0f, -speedAngle);
            if (rpmNeedle != null) rpmNeedle.localRotation = Quaternion.Euler(0f, 0f, -rpmAngle);
        }
    }
}
