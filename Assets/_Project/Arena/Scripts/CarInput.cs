using UnityEngine;
using UnityEngine.InputSystem;

namespace CarArena
{
    /// <summary>
    /// Reads the Drive map of the CarControls actions asset. With no actions asset (AI cars), all
    /// driving comes from the override fields and the button presses are always false.
    /// </summary>
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
        public bool overrideLookLeft, overrideLookRight, overrideLookBack;

        InputActionMap map;
        InputAction steer, throttle, brake, handbrake, lookLeft, lookRight, lookBack, resetCar, cycleDrive, repair, toggleAssists;

        /// <summary>True when there are no device bindings (AI-driven car).</summary>
        public bool IsAI => map == null;

        void Awake()
        {
            if (actions == null)
            {
                overrideDriving = true;
                return;
            }
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

        bool UseOverride => overrideDriving || map == null;

        public float Steer => UseOverride ? overrideSteer : Mathf.Clamp(steer.ReadValue<float>(), -1f, 1f);
        public float Throttle => UseOverride ? overrideThrottle : Mathf.Clamp01(throttle.ReadValue<float>());
        public float Brake => UseOverride ? overrideBrake : Mathf.Clamp01(brake.ReadValue<float>());
        public bool Handbrake => UseOverride ? overrideHandbrake : handbrake.IsPressed();
        public bool LookLeft => UseOverride ? overrideLookLeft : lookLeft.IsPressed();
        public bool LookRight => UseOverride ? overrideLookRight : lookRight.IsPressed();
        public bool LookBack => UseOverride ? overrideLookBack : lookBack.IsPressed();
        public bool ResetPressed => map != null && resetCar.WasPressedThisFrame();
        public bool CycleDrivePressed => map != null && cycleDrive.WasPressedThisFrame();
        public bool RepairPressed => map != null && repair.WasPressedThisFrame();
        public bool ToggleAssistsPressed => map != null && toggleAssists.WasPressedThisFrame();
    }
}
