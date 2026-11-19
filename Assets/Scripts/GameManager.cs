
using UnityEngine;
using TMPro;

public class GameManager : MonoBehaviour
{
    [SerializeField] private TMP_Text scoreText;

    private int score = 0;
    private int fruitCount;
    private int bugCount;

    public int Score => score;
    public int FruitCount => fruitCount;
    public int BugCount => bugCount;

    private void Start()
    {
        UpdateScoreText();
    }

    public void AddScore(int amount)
    {
        score += amount;
        UpdateScoreText();
    }

    public void RegisterCatch(ItemCategory category, int scoreDelta)
    {
        if (category == ItemCategory.Fruit)
            fruitCount++;
        else if (category == ItemCategory.Bug)
            bugCount++;

        AddScore(scoreDelta);
    }

    public void ResetGameResults()
    {
        score = 0;
        fruitCount = 0;
        bugCount = 0;
        UpdateScoreText();
    }

    private void UpdateScoreText()
    {
        if (scoreText != null)
            scoreText.text = $"スコア: {score}";
    }
}
