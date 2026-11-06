using UnityEngine;

namespace HauntedCasino.Combat
{
    /// <summary>Smoothly moves the camera between shots and adds punch (shake + FOV kick) on hits.</summary>
    public class CombatCamera : MonoBehaviour
    {
        [SerializeField] float followSharpness = 3.5f;
        [SerializeField] float baseFov = 55f;

        Camera cam;
        Vector3 basePos, targetPos, look, targetLook;
        float shake, fovKick;

        public void Init(Camera camera, Vector3 pos, Vector3 lookAt)
        {
            cam = camera;
            basePos = targetPos = pos;
            look = targetLook = lookAt;
            Apply(Vector3.zero);
        }

        public void SetShot(Vector3 pos, Vector3 lookAt)
        {
            targetPos = pos;
            targetLook = lookAt;
        }

        /// <summary>Strength 0..1: camera shake plus a quick zoom-in.</summary>
        public void Punch(float strength)
        {
            shake = Mathf.Max(shake, strength);
            fovKick = Mathf.Max(fovKick, strength * 8f);
        }

        void LateUpdate()
        {
            if (cam == null) return;
            float dt = Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-dt * followSharpness);
            basePos = Vector3.Lerp(basePos, targetPos, k);
            look = Vector3.Lerp(look, targetLook, k);
            Vector3 offset = shake > 0f ? Random.insideUnitSphere * (shake * 0.2f) : Vector3.zero;
            shake = Mathf.MoveTowards(shake, 0f, dt * 2.5f);
            fovKick = Mathf.MoveTowards(fovKick, 0f, dt * 25f);
            Apply(offset);
        }

        void Apply(Vector3 offset)
        {
            cam.transform.position = basePos + offset;
            cam.transform.rotation = Quaternion.LookRotation(look - basePos);
            cam.fieldOfView = baseFov - fovKick;
        }
    }
}
