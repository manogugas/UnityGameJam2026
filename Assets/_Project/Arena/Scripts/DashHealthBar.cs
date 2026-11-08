using TMPro;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Physical health gauge on the dashboard: a glowing bar that shrinks and shifts green to red,
    /// flickers when hit and blinks when critical, plus a number readout.
    /// </summary>
    public class DashHealthBar : MonoBehaviour
    {
        public CarHealth health;
        [Tooltip("Pivot at the bar's left end; its X scale is set to the health fraction.")]
        public Transform fillPivot;
        public Renderer fillRenderer;
        public TextMeshPro readout;
        public Color fullColor = new Color(0.25f, 1f, 0.3f);
        public Color emptyColor = new Color(1f, 0.15f, 0.05f);
        public float glow = 1.6f;
        [Range(0f, 1f)] public float criticalBelow = 0.25f;

        MaterialPropertyBlock block;
        float shown = 1f;
        int lastShownNumber = -1;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        void Update()
        {
            if (health == null || fillPivot == null) return;
            float target = health.Health01;
            shown = Mathf.MoveTowards(shown, target, Time.deltaTime * (shown > target ? 1.5f : 0.6f));
            var s = fillPivot.localScale;
            s.x = Mathf.Max(0.001f, shown);
            fillPivot.localScale = s;

            Color c = Color.Lerp(emptyColor, fullColor, Mathf.InverseLerp(0.15f, 0.8f, shown));
            float sinceHit = Time.time - health.LastDamageTime;
            float flicker = sinceHit < 0.35f ? (Mathf.Sin(Time.time * 70f) > 0f ? 1.8f : 0.4f) : 1f;
            float blink = !health.IsDead && shown < criticalBelow ? (Mathf.Sin(Time.time * 9f) > 0f ? 1f : 0.35f) : 1f;
            if (health.IsDead) { c = emptyColor; blink = 0.25f; }

            if (fillRenderer != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                fillRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColor, c);
                block.SetColor(EmissionColor, c * (glow * flicker * blink));
                fillRenderer.SetPropertyBlock(block);
            }

            if (readout != null)
            {
                int n = Mathf.CeilToInt(health.Health);
                if (n != lastShownNumber)
                {
                    lastShownNumber = n;
                    readout.text = n.ToString();
                }
                readout.color = c;
            }
        }
    }
}
