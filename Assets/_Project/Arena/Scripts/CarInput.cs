using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>Reads the Drive map of the CarControls actions asset.</summary>
    public class CarInput : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;

        [Header("Override (AI / automated tests)")]
        [Tooltip("When on, steer/throttle/brake/handbrake come from the fields below instead of the devices.")]
        public bool overrideDriving;
        [Range(-1f, 1f)] public float overrideSteer;
        [Range(0f, 1f)] public float overrideThrottle;
        [Range(0f, 1f)] public float overrideBrake;
        public bool overrideHandbrake;

        InputActionMap map;
        InputAction steer, throttle, brake, handbrake, lookLeft, lookRight, lookBack, resetCar, cycleDrive, repair, toggleAssists;

        void Awake()
        {
            map = actions.FindActionMap("Drive", true);
            steer = map.FindAction("Steer", true);
            throttle = map.FindAction("Throttle", true);
            brake = map.FindAction("Brake", true);
            handbrake = map.FindAction("Handbrake", true);
            lookLeft = map.FindAction("LookLeft", true);
            lookRight = map.FindAction("LookRight", true);
            lookBack = map.FindAction("LookBack", true);
            resetCar = map.FindAction("ResetCar", true);
            cycleDrive = map.FindAction("CycleDrive", true);
            repair = map.FindAction("Repair", true);
            toggleAssists = map.FindAction("ToggleAssists", true);
        }

        void OnEnable() => map?.Enable();
        void OnDisable() => map?.Disable();

        public float Steer => overrideDriving ? overrideSteer : Mathf.Clamp(steer.ReadValue<float>(), -1f, 1f);
        public float Throttle => overrideDriving ? overrideThrottle : Mathf.Clamp01(throttle.ReadValue<float>());
        public float Brake => overrideDriving ? overrideBrake : Mathf.Clamp01(brake.ReadValue<float>());
        public bool Handbrake => overrideDriving ? overrideHandbrake : handbrake.IsPressed();
        public bool LookLeft => lookLeft.IsPressed();
        public bool LookRight => lookRight.IsPressed();
        public bool LookBack => lookBack.IsPressed();
        public bool ResetPressed => resetCar.WasPressedThisFrame();
        public bool CycleDrivePressed => cycleDrive.WasPressedThisFrame();
        public bool RepairPressed => repair.WasPressedThisFrame();
        public bool ToggleAssistsPressed => toggleAssists.WasPressedThisFrame();
    }
}
