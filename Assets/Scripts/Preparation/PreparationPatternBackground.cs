using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FruitCatch.Preparation
{
    [ExecuteAlways]
    public sealed class PreparationPatternBackground : MonoBehaviour
    {
        [SerializeField] private Sprite fruitSprite;
        [SerializeField] private Sprite roboHoldSprite;
        [SerializeField] private Sprite roboWaitSprite;
        [SerializeField] private Sprite mochiSprite;
        [SerializeField] private Color backgroundColor = new Color32(201, 239, 82, 255);
        [SerializeField] private Color silhouetteColor = new Color32(49, 126, 55, 210);
        [SerializeField] private Vector2 scrollSpeed = new Vector2(-22f, -16f);
        [SerializeField] private Vector2 spacing = new Vector2(320f, 260f);

        private const int Columns = 8;
        private const int Rows = 7;
        private readonly List<RectTransform> icons = new List<RectTransform>();
        private readonly List<Vector2> basePositions = new List<Vector2>();
        private RectTransform visualRoot;
        private GameObject startPanel;
        private Vector2 scrollOffset;

        private void OnEnable()
        {
            Rebuild();
        }

        private void Update()
        {
            UpdateVisibility();
            if (!Application.isPlaying || icons.Count == 0)
                return;

            scrollOffset += scrollSpeed * Time.unscaledDeltaTime;
            LayoutIcons();
        }

        private void UpdateVisibility()
        {
            if (visualRoot == null)
                return;

            if (startPanel == null && transform.parent != null)
            {
                Transform panel = transform.parent.Find("01_Start");
                if (panel != null)
                    startPanel = panel.gameObject;
            }

            bool shouldShow = startPanel == null || !startPanel.activeInHierarchy;
            if (visualRoot.gameObject.activeSelf != shouldShow)
                visualRoot.gameObject.SetActive(shouldShow);
        }

        private void OnDisable()
        {
            ClearVisuals();
        }

        public void RefreshPreview()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            ClearVisuals();
            scrollOffset = Vector2.zero;

            visualRoot = CreateRect("GeneratedPattern", transform);
            if (!Application.isPlaying)
                visualRoot.gameObject.hideFlags = HideFlags.DontSaveInEditor;

            Image background = CreateRect("YellowGreenBackground", visualRoot).gameObject.AddComponent<Image>();
            background.color = backgroundColor;
            background.raycastTarget = false;

            RectTransform patternRoot = CreateRect("ScrollingSilhouettes", visualRoot);
            Sprite[] sprites = { fruitSprite, roboHoldSprite, roboWaitSprite, mochiSprite };
            for (int row = 0; row < Rows; row++)
            {
                for (int column = 0; column < Columns; column++)
                {
                    int index = row * Columns + column;
                    Sprite sprite = sprites[index % sprites.Length];
                    if (sprite == null)
                        continue;

                    RectTransform icon = CreateSilhouette(patternRoot, sprite, index);
                    float scale = 0.86f + (index % 3) * 0.1f;
                    icon.sizeDelta = new Vector2(210f, 210f) * scale;
                    icon.localRotation = Quaternion.Euler(0f, 0f, -18f + (index % 5) * 9f);
                    icons.Add(icon);
                    basePositions.Add(new Vector2(
                        (column - (Columns - 1) * 0.5f) * spacing.x + (row % 2) * spacing.x * 0.5f,
                        (row - (Rows - 1) * 0.5f) * spacing.y));
                }
            }

            LayoutIcons();
            UpdateVisibility();
        }

        private RectTransform CreateSilhouette(Transform parent, Sprite sprite, int index)
        {
            RectTransform maskRect = CreateRect($"Silhouette_{index}_{sprite.name}", parent);
            maskRect.anchorMin = maskRect.anchorMax = new Vector2(0.5f, 0.5f);

            Image maskImage = maskRect.gameObject.AddComponent<Image>();
            maskImage.sprite = sprite;
            maskImage.color = Color.white;
            maskImage.preserveAspect = true;
            maskImage.raycastTarget = false;

            Mask mask = maskRect.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            Image fill = CreateRect("DarkGreenFill", maskRect).gameObject.AddComponent<Image>();
            fill.color = silhouetteColor;
            fill.raycastTarget = false;
            return maskRect;
        }

        private void LayoutIcons()
        {
            float rangeX = spacing.x * Columns;
            float rangeY = spacing.y * Rows;
            for (int i = 0; i < icons.Count; i++)
            {
                Vector2 position = basePositions[i] + scrollOffset;
                position.x = Mathf.Repeat(position.x + rangeX * 0.5f, rangeX) - rangeX * 0.5f;
                position.y = Mathf.Repeat(position.y + rangeY * 0.5f, rangeY) - rangeY * 0.5f;
                icons[i].anchoredPosition = position;
            }
        }

        private void ClearVisuals()
        {
            icons.Clear();
            basePositions.Clear();
            if (visualRoot == null)
                return;

            if (Application.isPlaying)
                Destroy(visualRoot.gameObject);
            else
                DestroyImmediate(visualRoot.gameObject);
            visualRoot = null;
        }

        private static RectTransform CreateRect(string objectName, Transform parent)
        {
            RectTransform rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            return rect;
        }
    }
}
