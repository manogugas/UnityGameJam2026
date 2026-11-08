using UnityEngine;

namespace CarArena
{
    /// <summary>One damaging collision, in world and car-local space.</summary>
    public struct ImpactInfo
    {
        public Vector3 worldPoint, worldDir, localPoint, localDir;
        public float impulse, severity;
        public bool front, rear, left, right, top, bottom;
        /// <summary>Rigidbody of what we hit (null for static geometry).</summary>
        public Rigidbody otherBody;
    }
}
