using TMPro;
using UnityEngine;

namespace FruitCatch.Preparation
{
    internal sealed class PreparationCountdown
    {
        private readonly RectTransform root;
        private readonly TMP_Text number;
        private readonly PreparationOvalGraphic ring;
        private readonly RectTransform spark;
        private readonly float orbitRadius;

        public PreparationCountdown(Transform parent, TMP_FontAsset font, bool large, TMP_Text existingNumber = null)
        {
            float diameter = large ? 280f : 116f;
            float stroke = large ? 9f : 5f;
            Vector2 anchor = large ? new Vector2(0.5f, 0.32f) : Vector2.one;
            root = FruitCatchUi.Rect("CountdownDial", parent, anchor, anchor);
            root.sizeDelta = Vector2.one * diameter;
            root.anchoredPosition = large ? Vector2.zero : new Vector2(-90f, -86f);

            float ringInset = large ? 12f : 0f;
            Color numberColor = large ? FruitCatchUi.DeepGreen : FruitCatchUi.Cream;
            AddOval("Disc", large ? FruitCatchUi.Cream : FruitCatchUi.Ink, true, 0f);
            if (large)
                AddOval("Rim", new Color32(82, 131, 74, 255), false, 4f);
            AddOval("Track", large ? new Color32(219, 232, 202, 255) : new Color32(115, 92, 142, 255),
                false, stroke, ringInset);
            ring = AddOval("Sweep", large ? FruitCatchUi.Coral : FruitCatchUi.Gold, false, stroke, ringInset);
            orbitRadius = (diameter - stroke) * 0.5f - ringInset;
            var head = AddOval("Spark", large ? FruitCatchUi.Gold : FruitCatchUi.Cream, true, 0f);
            spark = head.rectTransform;
            FruitCatchUi.SetBounds(spark, Vector2.one * 0.5f, Vector2.one * 0.5f);
            spark.sizeDelta = Vector2.one * stroke * 1.7f;

            Vector2 min = new Vector2(0.15f, 0.12f);
            Vector2 max = new Vector2(0.85f, 0.88f);
            number = existingNumber != null ? existingNumber :
                FruitCatchUi.Label("Seconds", root, min, max, font, numberColor, 70f, "");
            if (existingNumber != null)
            {
                number.rectTransform.SetParent(root, false);
                FruitCatchUi.SetBounds(number.rectTransform, min, max);
                number.alignment = TextAlignmentOptions.Center;
                number.color = numberColor;
                number.enableAutoSizing = true;
                number.fontSizeMin = 18f;
                number.fontSizeMax = 174f;
            }
            if (large)
            {
                number.enableVertexGradient = false;
                number.fontStyle |= FontStyles.Bold;
            }
            Hide();
        }

        public void Show(string label, float elapsedFraction)
        {
            root.gameObject.SetActive(true);
            if (number.text != label)
                number.text = label;

            float t = Mathf.Clamp01(elapsedFraction);
            float sweep = Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), 0.65f);
            ring.progress = sweep;
            ring.SetVerticesDirty();
            float angle = sweep * 2f * Mathf.PI;
            spark.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * orbitRadius;
            spark.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(t * Mathf.PI));
            float pop = Mathf.Clamp01(t / 0.32f);
            number.rectTransform.localScale = Vector3.one *
                Mathf.Lerp(0.9f, 1f, pop) * (1f + 0.14f * Mathf.Sin(pop * Mathf.PI));
        }

        public void Hide() => root.gameObject.SetActive(false);

        private PreparationOvalGraphic AddOval(string name, Color color, bool filled, float thickness, float inset = 0f)
        {
            RectTransform rect = FruitCatchUi.Rect(name, root, Vector2.zero, Vector2.one);
            rect.offsetMin = Vector2.one * inset;
            rect.offsetMax = -Vector2.one * inset;
            var oval = rect.gameObject.AddComponent<PreparationOvalGraphic>();
            oval.color = color;
            oval.filled = filled;
            oval.thickness = thickness;
            oval.SmoothEdges = true;
            oval.raycastTarget = false;
            return oval;
        }
    }
}
