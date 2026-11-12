using TMPro;
using UnityEngine;

namespace CarArena
{
    /// <summary>
    /// Racing number painted on the roof and both doors (white roundel, black digits).
    /// A gold rim lights up when the emperor marks this car's number.
    /// </summary>
    public class CarNumberPlates : MonoBehaviour
    {
        public TextMeshPro[] texts;
        [Tooltip("Gold rims behind the roundels, shown while marked by the emperor.")]
        public GameObject[] favourRims;

        public int Number { get; private set; } = -1;

        /// <summary>Racing numbers are always two digits: 00-99.</summary>
        public static string Format(int number) => Mathf.Clamp(number, 0, 99).ToString("00");

        public void SetNumber(int number)
        {
            Number = number;
            string s = number >= 0 ? Format(number) : "";
            if (texts == null) return;
            foreach (var t in texts) if (t != null) t.text = s;
        }

        [Tooltip("Optional gold beacon over the roof, shown while marked.")]
        public GameObject markBeacon;

        public void SetMarked(bool marked)
        {
            if (favourRims != null)
                foreach (var r in favourRims) if (r != null) r.SetActive(marked);
            if (markBeacon != null) markBeacon.SetActive(marked);
        }
    }
}
