using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FruitCatch.Preparation;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class FruitResultPresentation : MonoBehaviour
{
    [Header("Results Animation")]
    [SerializeField] private bool animationEnabled = true;
    [SerializeField, Min(0f)] private float fruitAppearanceInterval = 0.11f;
    [SerializeField, Range(1f, 1.5f)] private float fruitPopScale = 1.18f;
    [SerializeField, Min(0.01f)] private float fruitAppearanceDuration = 0.23f;
    [SerializeField, Min(0.01f)] private float countUpDuration = 0.55f;
    [SerializeField, Min(0f)] private float scoreRevealTime = 1.05f;
    [SerializeField, Min(0f)] private float rankingRevealTime = 1.5f;

    private const float ScorePopDuration = 0.25f;
    private static readonly Color[] TileColors =
    {
        new Color32(255, 237, 190, 255), new Color32(224, 242, 220, 255), new Color32(253, 225, 238, 255)
    };

    private sealed class Entry
    {
        public RectTransform icon;
        public TMP_Text counter;
        public int finalCount;
        public int shownCount;
    }

    internal bool AnimationEnabled => animationEnabled;

    internal IEnumerator Show(IReadOnlyList<CatchableItem> fruits, GameManager scores, TMP_FontAsset font,
        GameObject total, RectTransform score, GameObject ranking, float animationBudget)
    {
        List<Entry> entries = BuildEntries(fruits, scores, font);
        Vector3 scoreScale = score != null ? score.localScale : Vector3.one;
        if (animationEnabled && animationBudget >= 0.08f)
        {
            float iconDuration = Mathf.Max(0.01f, fruitAppearanceDuration);
            float counterDuration = Mathf.Max(0.01f, countUpDuration);
            float interval = Mathf.Max(0f, fruitAppearanceInterval);
            float iconsFinished = entries.Count > 0 ? (entries.Count - 1) * interval + iconDuration : 0f;
            float countersFinished = entries.Count > 0 ? iconsFinished + counterDuration : 0f;
            float showScoreAt = Mathf.Max(scoreRevealTime, countersFinished);
            float showRankingAt = Mathf.Max(rankingRevealTime, showScoreAt + ScorePopDuration);
            float playbackSpeed = Mathf.Max(1f, (showRankingAt + 0.05f) / Mathf.Max(0.01f, animationBudget));

            for (float elapsed = 0f; elapsed < showRankingAt && animationEnabled;
                elapsed += Time.unscaledDeltaTime * playbackSpeed)
            {
                float countProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - iconsFinished) / counterDuration));
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry entry = entries[i];
                    float iconProgress = Mathf.Clamp01((elapsed - i * interval) / iconDuration);
                    entry.icon.gameObject.SetActive(iconProgress > 0f);
                    if (iconProgress > 0f && iconProgress < 1f)
                    {
                        float scale = iconProgress < 0.6f
                            ? Mathf.SmoothStep(0.5f, fruitPopScale, iconProgress / 0.6f)
                            : Mathf.SmoothStep(fruitPopScale, 1f, (iconProgress - 0.6f) / 0.4f);
                        entry.icon.localScale = Vector3.one * scale;
                    }
                    else
                        entry.icon.localScale = Vector3.one;

                    bool counting = elapsed >= iconsFinished;
                    entry.counter.gameObject.SetActive(counting);
                    if (counting)
                        DisplayCount(entry, Mathf.RoundToInt(entry.finalCount * countProgress));
                }
                if (total != null) total.SetActive(elapsed >= countersFinished);
                if (score != null)
                {
                    bool showScore = elapsed >= showScoreAt;
                    score.gameObject.SetActive(showScore);
                    if (showScore)
                    {
                        float progress = Mathf.Clamp01((elapsed - showScoreAt) / ScorePopDuration);
                        score.localScale = scoreScale * (1f + 0.16f * Mathf.Sin(progress * Mathf.PI));
                    }
                }
                if (ranking != null) ranking.SetActive(false);
                yield return null;
            }
        }

        foreach (Entry entry in entries)
        {
            entry.icon.gameObject.SetActive(true);
            entry.icon.localScale = Vector3.one;
            entry.counter.gameObject.SetActive(true);
            DisplayCount(entry, entry.finalCount);
        }
        if (total != null) total.SetActive(true);
        if (score != null)
        {
            score.gameObject.SetActive(true);
            score.localScale = scoreScale;
        }
        if (ranking != null) ranking.SetActive(true);
    }

    private static void DisplayCount(Entry entry, int count)
    {
        if (entry.shownCount == count)
            return;
        entry.shownCount = count;
        entry.counter.SetText("×{0}", count);
    }

    private List<Entry> BuildEntries(IReadOnlyList<CatchableItem> fruits, GameManager scores, TMP_FontAsset font)
    {
        var entries = new List<Entry>();
        if (fruits == null || fruits.Count == 0)
            return entries;

        var uniqueFruits = new List<CatchableItem>();
        var added = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (CatchableItem fruit in fruits)
            if (fruit != null && fruit.Definition != null && added.Add(fruit.ItemId))
                uniqueFruits.Add(fruit);

        int columns = Mathf.Clamp(uniqueFruits.Count, 1, 3);
        int rows = Mathf.CeilToInt(uniqueFruits.Count / (float)columns);
        RectTransform viewport = FruitCatchUi.Fill("FruitBreakdown", transform,
            new Vector2(0.065f, 0.37f), new Vector2(0.935f, 0.80f), new Color32(190, 221, 237, 105)).rectTransform;
        RectTransform tiles = FruitCatchUi.Rect("FruitTiles", viewport,
            new Vector2(0f, 1f - Mathf.Max(1f, rows * 0.62f)), Vector2.one);
        tiles.pivot = new Vector2(0.5f, 1f);
        viewport.gameObject.AddComponent<RectMask2D>();
        if (rows > 1)
        {
            viewport.GetComponent<Image>().raycastTarget = true;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = tiles;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
        }

        for (int i = 0; i < uniqueFruits.Count; i++)
        {
            CatchableItem fruit = uniqueFruits[i];
            int column = i % columns;
            int row = i / columns;
            RectTransform tile = FruitCatchUi.Rect("Fruit_" + fruit.ItemId, tiles,
                new Vector2((column + 0.035f) / columns, 1f - (row + 0.96f) / rows),
                new Vector2((column + 0.965f) / columns, 1f - (row + 0.04f) / rows));
            var panel = tile.gameObject.AddComponent<RoundedDashedPanelGraphic>();
            panel.FillColor = TileColors[i % TileColors.Length];
            panel.CornerRadius = 19f;
            panel.BorderThickness = 0f;

            Image icon = FruitCatchUi.Fill("FruitIcon", tile,
                new Vector2(0.14f, 0.30f), new Vector2(0.86f, 0.76f), Color.white);
            icon.sprite = fruit.DisplaySprite;
            icon.preserveAspect = true;
            icon.enabled = icon.sprite != null;
            FruitCatchUi.Label("FruitName", tile, new Vector2(0.07f, 0.78f), new Vector2(0.93f, 0.96f),
                font, FruitCatchUi.Ink, 30f, fruit.Definition.DisplayName);
            TMP_Text counter = FruitCatchUi.Label("FruitCount", tile,
                new Vector2(0.09f, 0.04f), new Vector2(0.91f, 0.28f), font, FruitCatchUi.Ink, 57f, "×0");
            counter.fontStyle = FontStyles.Bold;
            entries.Add(new Entry
            {
                icon = icon.rectTransform,
                counter = counter,
                finalCount = scores != null ? scores.GetFruitCount(fruit.Definition) : 0,
                shownCount = 0
            });
        }
        return entries;
    }
}
