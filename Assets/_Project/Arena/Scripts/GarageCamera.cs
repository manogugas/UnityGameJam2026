using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Slowly circles the player's car (an ellipse that fits the gate tunnel) and renders it into a
    /// texture shown on the tablet. Only active while the tablet is up.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class GarageCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Orbit half-width across the tunnel / half-length along it.")]
        public Vector2 radius = new Vector2(2.9f, 5.4f);
        public float height = 1.7f;
        public float degreesPerSecond = 14f;
        public float lookHeight = 0.5f;
        public Vector2Int resolution = new Vector2Int(768, 480);
        [Tooltip("Optional light that only shines while the garage view is active.")]
        public Light showroomLight;

        public RenderTexture Texture { get; private set; }

        Camera cam;
        float angle = 35f;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.enabled = false;
        }

        public void SetActive(bool on)
        {
            if (on && Texture == null)
            {
                Texture = new RenderTexture(resolution.x, resolution.y, 24) { name = "GarageView", antiAliasing = 2 };
                cam.targetTexture = Texture;
            }
            cam.enabled = on;
            if (showroomLight != null) showroomLight.enabled = on;
            enabled = on;
        }

        void LateUpdate()
        {
            if (target == null) return;
            angle += degreesPerSecond * Time.deltaTime;
            float a = angle * Mathf.Deg2Rad;
            Vector3 c = target.position;
            Vector3 pos = c + target.right * (Mathf.Cos(a) * radius.x) + target.forward * (Mathf.Sin(a) * radius.y) + Vector3.up * height;
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(c + Vector3.up * lookHeight - pos));
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
