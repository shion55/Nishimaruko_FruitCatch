
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private ScoreHud scoreHud;

    private int score = 0;
    private int fruitCount;
    private int bugCount;
    private readonly Dictionary<string, int> normalFruitCounts = new(System.StringComparer.Ordinal);

    public int Score => score;
    public int FruitCount => fruitCount;
    public int BugCount => bugCount;
    public int NormalFruitCount { get; private set; }

    public int GetFruitCount(ItemDefinition fruit) =>
        fruit != null && normalFruitCounts.TryGetValue(fruit.ItemId, out int count) ? count : 0;

    private void Start()
    {
        UpdateScoreText();
    }

    public void AddScore(int amount)
    {
        score += amount;
        UpdateScoreText();
        scoreHud?.PlayScoreChange(amount);
    }

    public void RegisterCatch(ItemCategory category, int scoreDelta)
    {
        if (category == ItemCategory.Fruit)
            fruitCount++;
        else if (category == ItemCategory.Bug)
            bugCount++;

        AddScore(scoreDelta);
    }

    public void RegisterCatch(ItemConsumption consumption)
    {
        if (consumption.Kind == ItemKind.Fruit && consumption.Definition != null)
        {
            ItemDefinition fruit = consumption.Definition;
            normalFruitCounts[fruit.ItemId] = GetFruitCount(fruit) + 1;
            NormalFruitCount++;
        }
        RegisterCatch(consumption.Category, consumption.ScoreDelta);
    }

    public void ResetGameResults()
    {
        score = 0;
        fruitCount = 0;
        bugCount = 0;
        NormalFruitCount = 0;
        normalFruitCounts.Clear();
        UpdateScoreText();
        ClearScoreAnimation();
    }

    internal void ClearScoreAnimation() => scoreHud?.ResetAnimation();

    private void UpdateScoreText()
    {
        if (scoreText != null)
            scoreText.text = scoreHud != null ? score.ToString() : $"スコア: {score}";
    }
}
