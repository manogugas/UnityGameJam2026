using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HauntedCasino.UI
{
    /// <summary>Hover grows and brightens the gold rim; press squashes. Runs on unscaled time.</summary>
    public class UIButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] float hoverScale = 1.06f;
        [SerializeField] float pressScale = 0.95f;

        Image rim;
        Color rimBase;
        bool hover, pressed;
        float scale = 1f;

        void Awake()
        {
            rim = UIFactory.BorderOf(this);
            if (rim != null) rimBase = rim.color;
        }

        void OnDisable()
        {
            hover = pressed = false;
            scale = 1f;
            transform.localScale = Vector3.one;
            if (rim != null) rim.color = rimBase;
        }

        public void OnPointerEnter(PointerEventData e) => hover = true;
        public void OnPointerExit(PointerEventData e) => hover = pressed = false;
        public void OnPointerDown(PointerEventData e) => pressed = true;
        public void OnPointerUp(PointerEventData e) => pressed = false;

        void Update()
        {
            float target = pressed ? pressScale : hover ? hoverScale : 1f;
            scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            transform.localScale = new Vector3(scale, scale, 1f);
            if (rim != null) rim.color = hover ? UITheme.Current.goldBright : rimBase;
        }
    }
}
