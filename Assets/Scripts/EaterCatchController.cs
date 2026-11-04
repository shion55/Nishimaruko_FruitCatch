using UnityEngine;

public class EaterCatchController : MonoBehaviour
{
    [SerializeField] private MicrophoneAnalysis microphone;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private FallingItemSpawner fallingItemSpawner;
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
        if (microphone == null) return;

        float gain = microphone.Gain;

        // 音量の急激な変化を滑らかにする
        float smoothTime = gain > smoothedGain ? 0.05f : 0.15f;

        smoothedGain = Mathf.Lerp(
            smoothedGain,
            gain,
            1f - Mathf.Exp(-Time.deltaTime / smoothTime)
        );
        if (microphone.Gain < 0.1f)
        {
           myspriterenderer.sprite = ClosingImage;
        }
        else
        {

            myspriterenderer.sprite = EatingImage;
        }
            BounceByGain(smoothedGain, ref scaleVelocity);
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
        if (microphone == null || gameManager == null)
            return;

        // 声を出していないと取れない
        if (microphone.Gain <= 0.1f)
            return;

        CatchableItem item =
            other.GetComponent<CatchableItem>();

        if (item == null)
            return;

        bool isFeverItem =
            other.GetComponent<FeverItem>() != null;

        int score = item.ScoreDelta;

        if (!item.TryConsume())
            return;

        gameManager.AddScore(score);

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
