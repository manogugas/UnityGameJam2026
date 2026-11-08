using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Instrument cluster warning lamps and the assists button, like a real car:
    ///  - TC lamp (amber) flashes while traction control intervenes or the driven wheels spin,
    ///  - TC OFF lamp (amber) stays lit while the assists are switched off,
    ///  - the physical TC OFF button on the dash side presses in when toggled and its LED shows the state.
    /// Both lamps light briefly at start-up as a bulb check.
    /// </summary>
    public class DashWarningLights : MonoBehaviour
    {
        public CarController car;
        public Renderer tcLamp;
        public Renderer tcOffLamp;
        public Transform button;
        public Renderer buttonLed;

        public Color lampOn = new Color(1f, 0.62f, 0.05f);
        public Color lampOff = new Color(0.12f, 0.1f, 0.08f);
        public float glow = 2.2f;
        public float flashRate = 7f;
        [Tooltip("Keep flashing this long after the spin stops, so short blips are visible.")]
        public float flashHold = 0.35f;
        public float bulbCheckTime = 1.5f;
        public float buttonTravel = 0.006f;

        MaterialPropertyBlock block;
        Vector3 buttonRest;
        bool lastAssists;
        float pressTime = -10f, lastSpinTime = -10f, startTime;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Start()
        {
            if (button != null) buttonRest = button.localPosition;
            if (car != null) lastAssists = car.tractionControl;
            startTime = Time.time;
        }

        void Update()
        {
            if (car == null) return;
            float now = Time.time;
            bool bulbCheck = now - startTime < bulbCheckTime;

            if (car.TractionControlActive || car.WheelSpin) lastSpinTime = now;
            bool flashing = now - lastSpinTime < flashHold && !car.EngineDead;
            bool tcLit = bulbCheck || (flashing && Mathf.Repeat(now * flashRate, 1f) < 0.55f);
            bool offLit = bulbCheck || !car.tractionControl;

            SetLamp(tcLamp, tcLit);
            SetLamp(tcOffLamp, offLit);
            SetLamp(buttonLed, !car.tractionControl);

            if (car.tractionControl != lastAssists)
            {
                lastAssists = car.tractionControl;
                pressTime = now;
            }
            if (button != null)
            {
                float press = Mathf.Clamp01(1f - (now - pressTime) / 0.25f);
                button.localPosition = buttonRest + Vector3.forward * (buttonTravel * press);
            }
        }

        void SetLamp(Renderer r, bool on)
        {
            if (r == null) return;
            if (block == null) block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, on ? lampOn : lampOff);
            block.SetColor(EmissionColor, on ? lampOn * glow : Color.black);
            r.SetPropertyBlock(block);
        }
    }
}
