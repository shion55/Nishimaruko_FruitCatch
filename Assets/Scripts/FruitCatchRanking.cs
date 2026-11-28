using System;
using System.Collections.Generic;
using FruitCatch.Preparation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class FruitCatchRanking
{
    private const string SaveKey = "FruitCatch.LocalRanking.v1";
    private const int VisibleScores = 5;
    private static readonly Color DividerColor = new Color32(149, 163, 126, 255);
    private static readonly Color CurrentRowColor = new Color32(211, 229, 188, 255);

    [Serializable]
    private sealed class ScoreHistory
    {
        public List<int> scores = new();
    }

    private readonly ScoreHistory history;
    private readonly TMP_Text currentRank;
    private readonly (Image Background, TMP_Text Rank, TMP_Text Score)[] rows =
        new (Image, TMP_Text, TMP_Text)[VisibleScores];
    private int currentIndex = -1;
    internal RectTransform Card { get; }

    public FruitCatchRanking(RectTransform results, RectTransform resultsCard, TMP_FontAsset font)
    {
        history = Load();
        if (resultsCard != null && resultsCard.parent == results)
            FruitCatchUi.SetBounds(resultsCard, new Vector2(0.04f, 0.08f), new Vector2(0.55f, 0.92f));

        RectTransform card = FruitCatchUi.Rect("LocalRanking", results,
            new Vector2(0.57f, 0.08f), new Vector2(0.96f, 0.92f));
        Card = card;
        var panel = card.gameObject.AddComponent<RoundedDashedPanelGraphic>();
        panel.FillColor = FruitCatchUi.Cream;
        panel.BorderColor = DividerColor;
        panel.CornerRadius = 24f;
        panel.BorderThickness = 2f;
        panel.GapLength = 0f;

        var title = FruitCatchUi.Label("Title", card, new Vector2(0.08f, 0.84f), new Vector2(0.92f, 0.95f),
            font, FruitCatchUi.DeepGreen, 60f, "ランキング");
        title.fontStyle |= FontStyles.Bold;
        currentRank = FruitCatchUi.Label("CurrentRank", card, new Vector2(0.08f, 0.735f),
            new Vector2(0.92f, 0.83f), font, FruitCatchUi.DeepGreen, 42f, "");
        currentRank.fontStyle |= FontStyles.Bold;

        var rankHeading = FruitCatchUi.Label("RankHeading", card, new Vector2(0.118f, 0.670f),
            new Vector2(0.38f, 0.723f), font, FruitCatchUi.DeepGreen, 27f, "順位");
        rankHeading.alignment = TextAlignmentOptions.MidlineLeft;
        var scoreHeading = FruitCatchUi.Label("ScoreHeading", card, new Vector2(0.415f, 0.670f),
            new Vector2(0.882f, 0.723f), font, FruitCatchUi.DeepGreen, 27f, "得点");
        scoreHeading.alignment = TextAlignmentOptions.MidlineRight;
        AddDivider(card, 0.663f);

        for (int i = 0; i < rows.Length; i++)
        {
            float top = 0.65f - i * 0.115f;
            Image background = FruitCatchUi.Fill($"RankRow{i + 1}", card,
                new Vector2(0.075f, top - 0.105f), new Vector2(0.925f, top), Color.clear);
            RectTransform row = background.rectTransform;
            TMP_Text rank = FruitCatchUi.Label("Rank", row, new Vector2(0.05f, 0.04f),
                new Vector2(0.36f, 0.96f), font, FruitCatchUi.DeepGreen, 44f, "");
            rank.alignment = TextAlignmentOptions.MidlineLeft;
            TMP_Text score = FruitCatchUi.Label("Score", row, new Vector2(0.40f, 0.04f),
                new Vector2(0.95f, 0.96f), font, FruitCatchUi.DeepGreen, 48f, "");
            score.alignment = TextAlignmentOptions.MidlineRight;
            score.fontStyle |= FontStyles.Bold;
            rows[i] = (background, rank, score);
            if (i < rows.Length - 1)
                AddDivider(card, top - 0.11f);
        }
    }

    private static void AddDivider(RectTransform card, float y)
    {
        Image divider = FruitCatchUi.Fill("Divider", card, new Vector2(0.075f, y),
            new Vector2(0.925f, y), DividerColor);
        divider.rectTransform.sizeDelta = new Vector2(0f, 1.5f);
    }

    public bool Record(int score)
    {
        bool isHighScore = history.scores.Count == 0 || score > history.scores[0];
        // Insert after existing ties. Tied scores share a rank.
        currentIndex = history.scores.FindIndex(existing => existing < score);
        if (currentIndex < 0)
            currentIndex = history.scores.Count;
        history.scores.Insert(currentIndex, score);
        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(history));
        PlayerPrefs.Save();
        Refresh();
        return isHighScore;
    }

    private static ScoreHistory Load()
    {
        try
        {
            var saved = JsonUtility.FromJson<ScoreHistory>(PlayerPrefs.GetString(SaveKey));
            if (saved?.scores != null)
            {
                saved.scores.Sort((a, b) => b.CompareTo(a));
                return saved;
            }
        }
        catch (ArgumentException)
        {
            Debug.LogWarning("FruitCatch: 保存済みランキングを読み込めませんでした。");
        }
        return new ScoreHistory();
    }

    private void Refresh()
    {
        List<int> scores = history.scores;
        currentRank.text = currentIndex >= 0
            ? $"こんかいの順位　{scores.IndexOf(scores[currentIndex]) + 1} 位"
            : "きろくなし";
        int rank = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (i < scores.Count)
            {
                if (i == 0 || scores[i] != scores[i - 1])
                    rank = i + 1;
                row.Rank.text = $"{rank} 位";
                row.Score.text = $"{scores[i]} 点";
            }
            else
            {
                row.Rank.text = "";
                row.Score.text = "—";
            }
            row.Background.color = i == currentIndex ? CurrentRowColor : Color.clear;
        }
    }

    internal void Clear()
    {
        ClearSavedScores();
        history.scores.Clear();
        currentIndex = -1;
        Refresh();
    }

    internal static void ClearSavedScores()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
    }
}
