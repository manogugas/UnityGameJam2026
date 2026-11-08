using Unity.Cinemachine;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Sends a Cinemachine impulse when this car crashes. Every Cinemachine camera with an impulse
    /// listener (the driver's view, the intro shots) shakes, fading with distance from the crash.
    /// </summary>
    [RequireComponent(typeof(CinemachineImpulseSource))]
    public class CrashImpulse : MonoBehaviour
    {
        public CarDamage damage;
        [Tooltip("Impulse strength at severity 1.")]
        public float strength = 0.9f;
        public float minSeverity = 0.08f;

        CinemachineImpulseSource source;

        void Awake()
        {
            source = GetComponent<CinemachineImpulseSource>();
            if (damage == null) damage = GetComponent<CarDamage>();
        }

        void OnEnable() { if (damage != null) damage.Impact += OnImpact; }
        void OnDisable() { if (damage != null) damage.Impact -= OnImpact; }

        void OnImpact(ImpactInfo info)
        {
            if (info.severity < minSeverity || source == null) return;
            Vector3 dir = info.worldDir.sqrMagnitude > 0.01f ? info.worldDir.normalized : Vector3.up;
            // Mostly a jolt along the hit, with a bit of vertical bounce.
            source.GenerateImpulseAt(info.worldPoint, (dir + Vector3.up * 0.4f) * (info.severity * strength));
        }
    }
}
