using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using FruitCatch.Preparation;

[Serializable]
public sealed class ScoreMessageTier
{
    [SerializeField] private int minimumScore;
    [SerializeField, TextArea] private string message;

    public int MinimumScore => minimumScore;
    public string Message => message;

    public ScoreMessageTier(int minimumScore, string message)
    {
        this.minimumScore = minimumScore;
        this.message = message;
    }
}

public sealed class FruitCatchGameSession : MonoBehaviour
{
    [Header("Game References")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private FallingItemSpawner itemSpawner;
    [SerializeField] private EaterCatchController eaterController;
    [SerializeField] private CharacterPairController characterMovement;
    [SerializeField] private ItemEffectController itemEffects;

    public bool IsPlaying { get; private set; }
    public float ElapsedTime { get; private set; }
    public float RemainingTime => Mathf.Max(0f, gameDuration - ElapsedTime);

    [Header("Time")]
    [SerializeField, Min(1f)] private float gameDuration = 50f; // 本編の制限時間（秒）。
    [SerializeField, Min(0.1f)] private float startDisplayDuration = 1f;
    [SerializeField, Min(0.1f)] private float finishDisplayDuration = 1f;
    [SerializeField, Min(0.1f)] private float resultDisplayDuration = 10f; // 結果を表示して準備画面へ戻るまでの秒数。
    [SerializeField, Min(0f)] private float warningStartSeconds = 10f;
    [SerializeField, Min(0.1f)] private float warningBlinkInterval = 0.5f;

    [Header("Scene Flow")]
    [SerializeField] private string preparationSceneName = "Preparation";

    [Header("HUD")]
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text announcementText;
    [SerializeField] private GameObject resultsRoot;
    [SerializeField] private FuriganaText resultTitle;
    [SerializeField] private FuriganaText scoreResult;
    [SerializeField] private FuriganaText fruitResult;
    [SerializeField] private FuriganaText bugResult;
    [SerializeField] private FuriganaText resultMessage;

    [Header("Colors")]
    [SerializeField] private Color timerNormalColor = Color.white;
    [SerializeField] private Color timerWarningColor = new Color(1f, 0.2f, 0.15f);
    [SerializeField] private Color startColor = new Color(1f, 0.5f, 0.12f);
    [SerializeField] private Color finishColor = new Color(0.45f, 0.85f, 1f);

    [Header("Temporary score messages (editable)")]
    [SerializeField] private ScoreMessageTier[] scoreMessageTiers =
    {
        new ScoreMessageTier(int.MinValue, "つぎはもっとくだものを[食|た]べてみよう！"),
        new ScoreMessageTier(1, "いいちょうし！"),
        new ScoreMessageTier(100, "とてもすごい！"),
        new ScoreMessageTier(200, "くだものマスター！")
    };

    private void Awake()
    {
        if (itemSpawner != null)
            itemSpawner.SetSpawningEnabled(false);
        if (eaterController != null)
            eaterController.enabled = false;
        if (resultsRoot != null)
            resultsRoot.SetActive(false);
        if (announcementText != null)
            announcementText.gameObject.SetActive(false);

        if (characterMovement != null)
            characterMovement.SetMovementEnabled(false);
        UpdateTimer(gameDuration, false);
    }

    private IEnumerator Start()
    {
        if (gameManager != null)
            gameManager.ResetGameResults();

        yield return ShowAnnouncement("スタート！", startColor, startDisplayDuration);

        ElapsedTime = 0f;
        IsPlaying = true;
        if (characterMovement != null)
            characterMovement.SetMovementEnabled(true);
        if (itemEffects != null)
            itemEffects.BeginRun();
        if (itemSpawner != null)
            itemSpawner.BeginRun(gameDuration);
        if (eaterController != null)
            eaterController.enabled = true;

        float remaining = gameDuration;
        while (remaining > 0f)
        {
            ElapsedTime = Mathf.Min(gameDuration, ElapsedTime + Time.deltaTime);
            remaining = RemainingTime;
            if (itemSpawner != null)
                itemSpawner.AdvanceTo(ElapsedTime);
            bool warning = remaining <= warningStartSeconds;
            UpdateTimer(remaining, warning);
            if (remaining <= 0f)
                break;
            yield return null;
        }

        EndGameplay();
        RemoveFallingItems();
        UpdateTimer(0f, true);
        yield return ShowAnnouncement("おわり！", finishColor, finishDisplayDuration);
        ShowResults();
        yield return new WaitForSecondsRealtime(resultDisplayDuration);

        if (!string.IsNullOrWhiteSpace(preparationSceneName))
            SceneManager.LoadScene(preparationSceneName);
    }

    private void UpdateTimer(float seconds, bool warning)
    {
        if (timerText == null)
            return;

        int displayedSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        timerText.text = $"のこり {displayedSeconds:00}びょう";

        if (!warning)
        {
            timerText.color = timerNormalColor;
            return;
        }

        bool visible = Mathf.FloorToInt(Time.unscaledTime / Mathf.Max(0.1f, warningBlinkInterval)) % 2 == 0;
        Color color = timerWarningColor;
        color.a = visible ? 1f : 0.2f;
        timerText.color = color;
    }

    private IEnumerator ShowAnnouncement(string message, Color color, float duration)
    {
        if (announcementText == null)
        {
            yield return new WaitForSecondsRealtime(duration);
            yield break;
        }

        announcementText.gameObject.SetActive(true);
        announcementText.text = message;
        announcementText.color = color;
        RectTransform rect = announcementText.rectTransform;
        float elapsed = 0f;
        float actualDuration = Mathf.Max(0.1f, duration);

        while (elapsed < actualDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / actualDuration);
            float scale = t < 0.65f
                ? Mathf.Lerp(0.35f, 1.12f, Mathf.SmoothStep(0f, 1f, t / 0.65f))
                : Mathf.Lerp(1.12f, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.65f) / 0.35f));
            rect.localScale = Vector3.one * scale;

            Color current = color;
            if (t > 0.78f)
                current.a *= 1f - Mathf.InverseLerp(0.78f, 1f, t);
            announcementText.color = current;
            yield return null;
        }

        rect.localScale = Vector3.one;
        announcementText.gameObject.SetActive(false);
    }

    private void EndGameplay()
    {
        IsPlaying = false;
        if (itemSpawner != null)
            itemSpawner.SetSpawningEnabled(false);
        if (eaterController != null)
            eaterController.enabled = false;
        if (itemEffects != null)
            itemEffects.StopEffects();
        if (characterMovement != null)
            characterMovement.SetMovementEnabled(false);
    }

    private void OnDisable()
    {
        EndGameplay();
    }

    private void RemoveFallingItems()
    {
        foreach (FallingObject item in FindObjectsByType<FallingObject>())
            Destroy(item.gameObject);
    }

    private void ShowResults()
    {
        if (resultsRoot != null)
            resultsRoot.SetActive(true);

        int score = gameManager != null ? gameManager.Score : 0;
        int fruits = gameManager != null ? gameManager.FruitCount : 0;
        int bugs = gameManager != null ? gameManager.BugCount : 0;

        if (resultTitle != null)
            resultTitle.SetAnnotatedText("けっか[発表|はっぴょう]");
        if (scoreResult != null)
            scoreResult.SetAnnotatedText($"とく[点|てん]　{score}");
        if (fruitResult != null)
            fruitResult.SetAnnotatedText($"[食|た]べたくだもの　{fruits}こ");
        if (bugResult != null)
            bugResult.SetAnnotatedText($"[食|た]べたむし　{bugs}ひき");
        if (resultMessage != null)
            resultMessage.SetAnnotatedText(GetScoreMessage(score));
    }

    private string GetScoreMessage(int score)
    {
        string selected = string.Empty;
        int selectedMinimum = int.MinValue;
        foreach (ScoreMessageTier tier in scoreMessageTiers)
        {
            if (tier != null && score >= tier.MinimumScore && tier.MinimumScore >= selectedMinimum)
            {
                selectedMinimum = tier.MinimumScore;
                selected = tier.Message;
            }
        }
        return selected;
    }
}
