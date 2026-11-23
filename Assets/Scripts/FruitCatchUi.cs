using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class FruitCatchUi
{
    public static readonly Color Ink = new Color32(35, 24, 62, 255);
    public static readonly Color Cream = new Color32(255, 248, 225, 255);
    public static readonly Color Gold = new Color32(255, 186, 69, 255);

    public static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetBounds(rect, min, max);
        return rect;
    }

    public static void SetBounds(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public static Image Fill(string name, Transform parent, Vector2 min, Vector2 max, Color color)
    {
        var image = Rect(name, parent, min, max).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TMP_Text Label(string name, Transform parent, Vector2 min, Vector2 max,
        TMP_FontAsset font, Color color, float maxSize, string value)
    {
        var label = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            label.font = font;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.enableAutoSizing = true;
        label.fontSizeMin = 8f;
        label.fontSizeMax = maxSize;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        label.text = value;
        return label;
    }
}
