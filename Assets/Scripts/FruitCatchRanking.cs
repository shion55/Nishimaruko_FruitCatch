using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

internal sealed class FruitCatchRanking
{
    private const string SaveKey = "FruitCatch.LocalRanking.v1";
    private const int VisibleScores = 5;

    [Serializable]
    private sealed class ScoreHistory
    {
        public List<int> scores = new();
    }

    private readonly ScoreHistory history;
    private readonly TMP_Text currentRank;
    private readonly TMP_Text[] rows = new TMP_Text[VisibleScores];
    private int currentIndex = -1;
    internal RectTransform Card { get; }

    public FruitCatchRanking(RectTransform results, RectTransform resultsCard, TMP_FontAsset font)
    {
        history = Load();
        if (resultsCard != null && resultsCard.parent == results)
            FruitCatchUi.SetBounds(resultsCard, new Vector2(0.04f, 0.08f), new Vector2(0.55f, 0.92f));

        RectTransform card = FruitCatchUi.Fill("LocalRanking", results,
            new Vector2(0.57f, 0.08f), new Vector2(0.96f, 0.92f), FruitCatchUi.Ink).rectTransform;
        Card = card;
        FruitCatchUi.Fill("Accent", card, new Vector2(0f, 0.978f), Vector2.one, FruitCatchUi.Gold);
        FruitCatchUi.Label("Title", card, new Vector2(0.06f, 0.82f), new Vector2(0.94f, 0.96f),
            font, FruitCatchUi.Cream, 66f, "ランキング");
        currentRank = FruitCatchUi.Label("CurrentRank", card, new Vector2(0.05f, 0.71f),
            new Vector2(0.95f, 0.82f), font, FruitCatchUi.Gold, 46f, "");

        for (int i = 0; i < rows.Length; i++)
        {
            float top = 0.69f - i * 0.105f;
            RectTransform row = FruitCatchUi.Fill($"RankRow{i + 1}", card,
                new Vector2(0.075f, top - 0.09f), new Vector2(0.925f, top),
                new Color32(59, 43, 86, 255)).rectTransform;
            rows[i] = FruitCatchUi.Label("Score", row, new Vector2(0.05f, 0.01f),
                new Vector2(0.95f, 0.99f), font, FruitCatchUi.Cream, 42f, "");
        }

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
        currentRank.text = currentIndex >= 0 ? $"こんかい {scores.IndexOf(scores[currentIndex]) + 1} 位" : "きろくなし";
        int rank = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            if (i < scores.Count)
            {
                if (i == 0 || scores[i] != scores[i - 1])
                    rank = i + 1;
                rows[i].text = $"{rank} 位　　{scores[i]} 点";
            }
            else
            {
                rows[i].text = "—";
            }
            rows[i].color = i == currentIndex ? FruitCatchUi.Gold : FruitCatchUi.Cream;
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
