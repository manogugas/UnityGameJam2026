using UnityEngine;

namespace CarArena
{
    /// <summary>Mirrors this object's active state onto other objects (one upgrade level spread over several parents).</summary>
    public class ActiveFollower : MonoBehaviour
    {
        public GameObject[] followers;

        void OnEnable() => Set(true);
        void OnDisable() => Set(false);

        void Set(bool on)
        {
            if (followers == null) return;
            foreach (var f in followers) if (f != null) f.SetActive(on);
        }
    }
}
