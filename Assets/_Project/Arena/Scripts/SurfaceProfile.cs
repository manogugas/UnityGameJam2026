using UnityEngine;

namespace CarArena
{
    /// <summary>Tyre/ground behaviour of one driving surface.</summary>
    [CreateAssetMenu(menuName = "Car Arena/Surface Profile", fileName = "Surface")]
    public class SurfaceProfile : ScriptableObject
    {
        public string displayName = "Asphalt";
        [Tooltip("Peak tyre friction coefficient (mu).")]
        public float grip = 1.05f;
        [Tooltip("Fraction of peak grip left when the tyre is fully sliding.")]
        [Range(0.3f, 1f)] public float slideGrip = 0.72f;
        [Tooltip("Longitudinal slip ratio where grip peaks. Higher = more progressive/loose.")]
        public float peakSlipRatio = 0.1f;
        [Tooltip("Slip angle in degrees where cornering grip peaks.")]
        public float peakSlipAngle = 8f;
        [Tooltip("Rolling resistance force per newton of wheel load.")]
        public float rollingResistance = 0.015f;
        [Tooltip("Bump height in metres fed into the suspension.")]
        public float roughness = 0.003f;
        [Tooltip("Loose surface: rumble instead of tyre squeal.")]
        public bool loose;
    }
}
