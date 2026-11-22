using UnityEngine;

public class EaterCatchController : MonoBehaviour
{
    private const float MouthOpenThreshold = 0.1f;

    [SerializeField] private MicrophoneAnalysis microphone;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private FallingItemSpawner fallingItemSpawner;
    [SerializeField] private CatchEffectPlayer catchEffectPlayer;
    [SerializeField] private ScorePopup scorePopup;
    [SerializeField] private float maxScale = 1.8f;
    [SerializeField] private float maxGain = 0.1f;
    [SerializeField] private float stiffness = 100f;
    [SerializeField] private float damping = 15f;

    [SerializeField] private Sprite EatingImage;
    [SerializeField] private Sprite ClosingImage;

    private SpriteRenderer myspriterenderer;
    private float smoothedGain;
    private Vector3 originalScale;
    private float scaleMultiplier = 1f;

    private float scaleVelocity = 0f;

    private void Awake()
    {
        if (microphone == null)
            microphone = GetComponentInParent<MicrophoneAnalysis>();
        originalScale = transform.localScale;
        myspriterenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        float gain = ReadInputGain();

        // 音量の急激な変化を滑らかにする
        float smoothTime = gain > smoothedGain ? 0.05f : 0.15f;

        smoothedGain = Mathf.Lerp(
            smoothedGain,
            gain,
            1f - Mathf.Exp(-Time.deltaTime / smoothTime)
        );
        if (gain <= MouthOpenThreshold)
        {
           myspriterenderer.sprite = ClosingImage;
        }
        else
        {

            myspriterenderer.sprite = EatingImage;
        }
            BounceByGain(smoothedGain, ref scaleVelocity);
    }

    private float ReadInputGain()
    {
        if (MouseDebugInput.Enabled)
        {
            return MouseDebugInput.LeftButtonHeld
                ? Mathf.Max(maxGain, MouthOpenThreshold + 0.01f)
                : 0f;
        }

        return microphone != null ? microphone.Gain : 0f;
    }

    private void BounceByGain(float gain, ref float velocity)
    {
        // 音量を拡大率に変換
        float normalizedGain = Mathf.Clamp01(gain / maxGain);
        float targetScale = Mathf.Lerp(1f, maxScale, normalizedGain);

        // バネ運動
        float dt = Mathf.Min(Time.deltaTime, 0.04f);

        float acceleration =
            (targetScale - scaleMultiplier) * stiffness
            - velocity * damping;

        velocity += acceleration * dt;
        scaleMultiplier = Mathf.Max(
            0.1f,
            scaleMultiplier + velocity * dt
        );

        // 元の大きさを基準に拡大縮小
        transform.localScale = new Vector3(
    originalScale.x * scaleMultiplier,
    originalScale.y * Mathf.Lerp(1f, scaleMultiplier, 0.1f),
    originalScale.z
);
    }

    
    private void OnTriggerEnter2D(Collider2D other)
    {
        TryCatch(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryCatch(other);
    }


    private void TryCatch(Collider2D other)
    {
        if (gameManager == null)
            return;

        // 通常は声、マウスデバッグ中は左ボタンを押している間だけ取れる。
        if (ReadInputGain() <= MouthOpenThreshold)
            return;

        CatchableItem item =
            other.GetComponent<CatchableItem>();

        if (item == null)
            return;

        bool isFeverItem =
            other.GetComponent<FeverItem>() != null;

        int score = item.ScoreDelta;

        // 同じアイテムを複数回数えない
        if (!item.TryConsume())
            return;

        if (item.Category == ItemCategory.Fruit)
        {
            if (catchEffectPlayer != null)
                catchEffectPlayer.PlayFruitEffect();
        }
        else if (item.Category == ItemCategory.Bug)
        {
            if (catchEffectPlayer != null)
                catchEffectPlayer.PlayBugEffect();
        }

        if (scorePopup != null)
            scorePopup.ShowScore(item.ScoreDelta);

        gameManager.RegisterCatch(item.Category, score);

        if (isFeverItem)
        {
            if (fallingItemSpawner != null)
                fallingItemSpawner.StartFever();
            else
                Debug.LogWarning(
                    "FallingItemSpawner が設定されていません"
                );
        }
    }

}
