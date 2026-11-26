using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.UI;
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
    private CanvasGroup announcementBanner;
    private FruitCatchRanking ranking;
    private readonly RectTransform[] announcementSparkles = new RectTransform[8];

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

    private static int lastTransitionFrame = -1;
    private bool leaving;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetTransitionFrame() => lastTransitionFrame = -1;

    public static bool HandleOperatorInput(string preparationScene, string replayScene, System.Action stop)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || lastTransitionFrame == Time.frameCount)
            return false;

#if !UNITY_EDITOR
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            lastTransitionFrame = Time.frameCount;
            stop();
            Application.Quit();
            return true;
        }
#endif
        if (keyboard.f1Key.wasPressedThisFrame && !string.IsNullOrWhiteSpace(preparationScene))
        {
            return ChangeScene(preparationScene, () =>
            {
                stop();
                PreparationPythonBridge.ClearRegistration();
            });
        }
        if (keyboard.f2Key.wasPressedThisFrame && !string.IsNullOrWhiteSpace(replayScene))
            return ChangeScene(replayScene, stop);

        return false;
    }

    public static bool ChangeScene(string scene, System.Action stop = null)
    {
        if (lastTransitionFrame == Time.frameCount || string.IsNullOrWhiteSpace(scene))
            return false;

        lastTransitionFrame = Time.frameCount;
        stop?.Invoke();
        SceneManager.LoadScene(scene);
        return true;
    }

    private void Update()
    {
        if (!leaving)
            HandleOperatorInput(preparationSceneName, gameObject.scene.name, StopForOperator);
    }

    private void StopForOperator()
    {
        leaving = true;
        StopAllCoroutines();
        EndGameplay();
    }

    private void Awake()
    {
        if (itemSpawner != null)
            itemSpawner.SetSpawningEnabled(false);
        if (eaterController != null)
            eaterController.enabled = false;
        if (resultsRoot != null)
            resultsRoot.SetActive(false);
        if (announcementText != null)
            BuildAnnouncementBanner();

        if (characterMovement != null)
            characterMovement.SetMovementEnabled(false);
        UpdateTimer(gameDuration, false);
    }

    private IEnumerator Start()
    {
        if (gameManager != null)
            gameManager.ResetGameResults();

        FruitCatchAudioManager.Instance.PlayStart();
        yield return ShowAnnouncement("スタート！", startColor, startDisplayDuration);

        ElapsedTime = 0f;
        IsPlaying = true;
        FruitCatchAudioManager.Instance.PlayGameBgm();
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
        FruitCatchAudioManager.Instance.FadeOutGameBgm();
        RemoveFallingItems();
        UpdateTimer(0f, true);
        FruitCatchAudioManager.Instance.PlayFinish();
        yield return ShowAnnouncement("おわり！", finishColor, finishDisplayDuration);
        ShowResults();
        yield return new WaitForSecondsRealtime(resultDisplayDuration);

        ChangeScene(preparationSceneName);
    }

    private void BuildAnnouncementBanner()
    {
        RectTransform textRect = announcementText.rectTransform;
        int sibling = textRect.GetSiblingIndex();
        RectTransform banner = FruitCatchUi.Rect("AnnouncementBanner", textRect.parent,
            new Vector2(0.12f, 0.35f), new Vector2(0.88f, 0.65f));
        banner.SetSiblingIndex(sibling);
        announcementBanner = banner.gameObject.AddComponent<CanvasGroup>();
        announcementBanner.blocksRaycasts = false;

        textRect.SetParent(banner, false);
        FruitCatchUi.SetBounds(textRect, new Vector2(0.045f, 0.08f), new Vector2(0.955f, 0.92f));
        announcementText.alignment = TextAlignmentOptions.Center;
        announcementText.enableAutoSizing = true;
        announcementText.fontSizeMin = 20f;
        announcementText.fontSizeMax = 230f;
        announcementText.margin = Vector4.zero;
        announcementText.textWrappingMode = TextWrappingModes.NoWrap;
        announcementText.outlineColor = new Color32(23, 15, 42, 255);
        announcementText.outlineWidth = 0.22f;
        announcementText.gameObject.SetActive(true);
        for (int i = 0; i < announcementSparkles.Length; i++)
        {
            announcementSparkles[i] = CreateSparkle("AnnouncementSparkle_" + i, banner, 24f);
            announcementSparkles[i].gameObject.SetActive(false);
        }
        banner.gameObject.SetActive(false);
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
        if (announcementBanner == null)
        {
            yield return new WaitForSecondsRealtime(duration);
            yield break;
        }

        announcementText.text = message;
        bool isStart = message == "スタート！";
        announcementText.color = Color.white;
        announcementText.enableVertexGradient = true;
        announcementText.colorGradient = isStart
            ? new VertexGradient(new Color32(255, 250, 145, 255), new Color32(255, 225, 75, 255),
                new Color32(255, 151, 35, 255), new Color32(255, 108, 35, 255))
            : new VertexGradient(new Color32(184, 246, 255, 255), new Color32(135, 215, 255, 255),
                new Color32(122, 145, 255, 255), new Color32(178, 116, 255, 255));
        Color sparkleColor = isStart ? new Color32(255, 217, 75, 255) : new Color32(142, 226, 255, 255);
        foreach (RectTransform sparkle in announcementSparkles)
        {
            foreach (Image image in sparkle.GetComponentsInChildren<Image>())
                image.color = sparkleColor;
        }
        announcementBanner.gameObject.SetActive(true);
        float elapsed = 0f;
        float actualDuration = Mathf.Max(0.1f, duration);

        while (elapsed < actualDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / actualDuration);
            float entrance = Mathf.Clamp01(elapsed / 0.34f);
            float scale = isStart
                ? Mathf.Lerp(0.86f, 1f, entrance) + 0.1f * Mathf.Sin(entrance * Mathf.PI)
                : Mathf.Lerp(1.18f, 1f, entrance) - 0.06f * Mathf.Sin(entrance * Mathf.PI);
            announcementBanner.transform.localScale = Vector3.one * scale;
            announcementBanner.alpha = 1f - Mathf.InverseLerp(0.88f, 1f, t);
            float sparkleAmount = Mathf.Sin(Mathf.Clamp01(t / 0.42f) * Mathf.PI);
            for (int i = 0; i < announcementSparkles.Length; i++)
            {
                RectTransform sparkle = announcementSparkles[i];
                float angle = i * Mathf.PI * 2f / announcementSparkles.Length;
                sparkle.gameObject.SetActive(sparkleAmount > 0.01f);
                sparkle.anchoredPosition = new Vector2(Mathf.Cos(angle) * 300f,
                    Mathf.Sin(angle) * 105f) * Mathf.Lerp(0.55f, 1f, entrance);
                sparkle.localRotation = Quaternion.Euler(0f, 0f, i * 45f + entrance * 90f);
                sparkle.localScale = Vector3.one * sparkleAmount * (i % 2 == 0 ? 1f : 0.7f);
            }
            yield return null;
        }

        foreach (RectTransform sparkle in announcementSparkles)
            sparkle.gameObject.SetActive(false);
        announcementBanner.gameObject.SetActive(false);
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
        FruitCatchAudioManager.StopGameAudioIfAvailable();
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
        RectTransform resultCard = resultTitle != null ? resultTitle.transform.parent as RectTransform : null;
        bool isHighScore = false;

        if (gameManager != null && resultsRoot != null && resultsRoot.transform is RectTransform results)
        {
            TMP_FontAsset font = announcementText != null ? announcementText.font : timerText?.font;
            ranking = new FruitCatchRanking(results, resultCard, font);
            isHighScore = ranking.Record(score);
        }

        if (resultTitle != null)
            resultTitle.SetAnnotatedText(isHighScore ? "ハイスコア！" : "けっか[発表|はっぴょう]");
        if (scoreResult != null)
            scoreResult.SetAnnotatedText($"とく[点|てん]　{score}");
        if (fruitResult != null)
            fruitResult.SetAnnotatedText($"くだもの\n× {fruits}");
        if (bugResult != null)
            bugResult.SetAnnotatedText($"むし\n× {bugs}");
        if (resultMessage != null)
            resultMessage.SetAnnotatedText(GetScoreMessage(score));

        DecorateResultCounts(resultCard);
        if (isHighScore && resultCard != null)
        {
            TMP_FontAsset font = announcementText != null ? announcementText.font : timerText?.font;
            StartCoroutine(PlayHighScoreDecoration(resultCard, font));
        }
    }

    private void DecorateResultCounts(RectTransform resultCard)
    {
        if (resultCard == null)
            return;

        if (resultCard.Find("FruitSummaryCard") == null)
        {
            Image fruitCard = FruitCatchUi.Fill("FruitSummaryCard", resultCard,
                new Vector2(0.07f, 0.325f), new Vector2(0.495f, 0.61f),
                new Color32(35, 112, 78, 235));
            fruitCard.rectTransform.SetAsFirstSibling();
            Image bugCard = FruitCatchUi.Fill("BugSummaryCard", resultCard,
                new Vector2(0.505f, 0.325f), new Vector2(0.93f, 0.61f),
                new Color32(103, 48, 118, 235));
            bugCard.rectTransform.SetAsFirstSibling();
        }

        ConfigureResultCount(fruitResult, new Color32(255, 246, 183, 255));
        ConfigureResultCount(bugResult, new Color32(255, 221, 245, 255));
    }

    private static void ConfigureResultCount(FuriganaText result, Color color)
    {
        if (result == null || !result.TryGetComponent(out TMP_Text text))
            return;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 22f;
        text.fontSizeMax = 54f;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        text.lineSpacing = -10f;
    }

    private IEnumerator PlayHighScoreDecoration(RectTransform resultCard, TMP_FontAsset font)
    {
        TMP_Text titleText = resultTitle != null ? resultTitle.GetComponent<TMP_Text>() : null;
        if (titleText != null)
        {
            titleText.color = FruitCatchUi.Gold;
            titleText.outlineColor = new Color32(84, 42, 18, 255);
            titleText.outlineWidth = 0.18f;
        }

        RectTransform decoration = FruitCatchUi.Rect("HighScoreStars", resultCard,
            new Vector2(0f, 0.7f), new Vector2(1f, 1f));
        decoration.SetAsLastSibling();
        const int starCount = 10;
        var stars = new RectTransform[starCount];
        for (int i = 0; i < stars.Length; i++)
            stars[i] = CreateSparkle("Star_" + i, decoration, i % 2 == 0 ? 28f : 20f);

        float duration = 1.1f;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float burst = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.72f));
            float fade = 1f - Mathf.InverseLerp(0.72f, 1f, t);
            for (int i = 0; i < stars.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / stars.Length;
                stars[i].anchoredPosition = new Vector2(Mathf.Cos(angle) * 235f,
                    Mathf.Sin(angle) * 90f) * Mathf.Lerp(0.18f, 1f, burst);
                stars[i].localRotation = Quaternion.Euler(0f, 0f, i * 36f + burst * 110f);
                stars[i].localScale = Vector3.one * Mathf.Sin(burst * Mathf.PI) * fade;
            }
            if (titleText != null)
                titleText.rectTransform.localScale = Vector3.one *
                    (1f + Mathf.Sin(Mathf.Clamp01(t / 0.45f) * Mathf.PI) * 0.14f);
            yield return null;
        }

        if (titleText != null)
            titleText.rectTransform.localScale = Vector3.one;
        Destroy(decoration.gameObject);
    }

    private static RectTransform CreateSparkle(string name, Transform parent, float size)
    {
        RectTransform sparkle = FruitCatchUi.Rect(name, parent, Vector2.one * 0.5f, Vector2.one * 0.5f);
        sparkle.sizeDelta = Vector2.one * size;
        Image horizontal = FruitCatchUi.Fill("Horizontal", sparkle,
            new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), FruitCatchUi.Gold);
        Image vertical = FruitCatchUi.Fill("Vertical", sparkle,
            new Vector2(0.4f, 0f), new Vector2(0.6f, 1f), FruitCatchUi.Gold);
        horizontal.raycastTarget = false;
        vertical.raycastTarget = false;
        return sparkle;
    }

    [ContextMenu("ランキングを全件リセット")]
    private void ResetLocalRanking()
    {
        if (ranking != null)
            ranking.Clear();
        else
            FruitCatchRanking.ClearSavedScores();
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
