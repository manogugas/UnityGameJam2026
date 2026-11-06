using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HauntedCasino.UI
{
    /// <summary>Keeps a backing pill sized to its text (and hidden when the text is empty).</summary>
    public class PromptBackSync : MonoBehaviour
    {
        [SerializeField] float padding = 40f;

        TMP_Text text;
        Image image;
        RectTransform rt;

        public void Bind(TMP_Text target)
        {
            text = target;
            image = GetComponent<Image>();
            rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(0.5f, rt.anchorMin.y);
            rt.anchorMax = new Vector2(0.5f, rt.anchorMax.y);
        }

        void LateUpdate()
        {
            if (text == null) return;
            bool show = !string.IsNullOrEmpty(text.text);
            image.enabled = show;
            if (!show) return;
            rt.sizeDelta = new Vector2(text.preferredWidth + padding, rt.sizeDelta.y);
        }
    }
}
