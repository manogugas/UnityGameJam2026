using UnityEngine;

namespace CarArena
{
    /// <summary>Tags a collider with a surface profile. Colliders without one use the car's default surface.</summary>
    public class DrivingSurface : MonoBehaviour
    {
        public SurfaceProfile profile;
    }
}
