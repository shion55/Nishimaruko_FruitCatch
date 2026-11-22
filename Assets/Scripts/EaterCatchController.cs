using UnityEngine;

[RequireComponent(typeof(CapsuleCollider2D))]
public class EaterCatchController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float mouthOpenThreshold = 0.1f;

    [SerializeField] private MicrophoneAnalysis microphone;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private FallingItemSpawner fallingItemSpawner;
    [SerializeField] private CatchEffectPlayer catchEffectPlayer;
    [SerializeField] private ScorePopup scorePopup;
    [SerializeField] private float maxScale = 1.8f;
    [SerializeField] private float maxGain = 0.25f;
    [SerializeField] private float stiffness = 100f;
    [SerializeField] private float damping = 15f;

    [SerializeField] private Sprite EatingImage;
    [SerializeField] private Sprite ClosingImage;

    [Header("Size and Bounce")]
    [SerializeField, Range(0f, 80f)] private float maxBounceAngle = 45f;
    [SerializeField, Min(0.1f)] private float bounceSpeed = 5f;

    private SpriteRenderer myspriterenderer;
    private CapsuleCollider2D mouthCollider;
    private float smoothedGain;
    private Vector3 originalScale;
    private float scaleMultiplier = 1f;

    private float scaleVelocity = 0f;

    public event System.Action<ItemConsumption> ItemConsumed;
    public float CurrentMouthWidth => mouthCollider != null ? mouthCollider.bounds.size.x : 0f;

    private void Awake()
    {
        if (microphone == null)
            microphone = GetComponentInParent<MicrophoneAnalysis>();
        originalScale = transform.localScale;
        myspriterenderer = GetComponent<SpriteRenderer>();
        mouthCollider = GetComponent<CapsuleCollider2D>();
        mouthCollider.direction = CapsuleDirection2D.Horizontal;
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
        myspriterenderer.sprite = gain <= mouthOpenThreshold ? ClosingImage : EatingImage;
        BounceByGain(smoothedGain, ref scaleVelocity);
    }

    private float ReadInputGain()
    {
        if (MouseDebugInput.Enabled)
        {
            float minimumGain = mouthOpenThreshold + 0.001f;
            return MouseDebugInput.LeftButtonHeld
                ? Mathf.Lerp(minimumGain, Mathf.Max(maxGain, minimumGain), MouseDebugInput.MouthOpenAmount)
                : 0f;
        }

        return microphone != null ? microphone.Gain : 0f;
    }

    private void BounceByGain(float gain, ref float velocity)
    {
        // 音量を拡大率に変換
        float normalizedGain = Mathf.Clamp01(gain / Mathf.Max(0.001f, maxGain));
        float targetScale = Mathf.Lerp(1f, maxScale, normalizedGain);

        // バネ運動
        float dt = Mathf.Min(Time.deltaTime, 0.04f);

        float acceleration =
            (targetScale - scaleMultiplier) * stiffness
            - velocity * damping;

        velocity += acceleration * dt;
        scaleMultiplier = Mathf.Clamp(scaleMultiplier + velocity * dt, 1f, Mathf.Max(1f, maxScale));

        // 元の大きさを基準に拡大縮小
        transform.localScale = new Vector3(
            originalScale.x * scaleMultiplier,
            originalScale.y,
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

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other != null)
            other.GetComponentInParent<FallingObject>()?.ReleaseBounceContact(mouthCollider);
    }


    private void TryCatch(Collider2D other)
    {
        if (!isActiveAndEnabled || gameManager == null || other == null)
            return;

        // 通常は声、マウスデバッグ中は左ボタンを押している間だけ取れる。
        if (ReadInputGain() <= mouthOpenThreshold)
            return;

        CatchableItem item =
            other.GetComponentInParent<CatchableItem>();

        if (item == null || item.Definition == null || item.IsConsumed)
            return;

        FallingObject falling = item.GetComponent<FallingObject>();
        if (falling != null && falling.IsRising)
            return;

        if (item.Category == ItemCategory.Fruit && item.CatchWidth > CurrentMouthWidth + 0.0001f)
        {
            if (falling != null && mouthCollider != null)
            {
                Bounds mouth = mouthCollider.bounds;
                float hitOffset = Mathf.Clamp(
                    (other.bounds.center.x - mouth.center.x) / Mathf.Max(0.001f, mouth.extents.x), -1f, 1f);
                float angle = hitOffset * maxBounceAngle * Mathf.Deg2Rad;
                falling.TryBounce(new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * bounceSpeed, mouthCollider);
            }
            return;
        }

        // 同じアイテムを複数回数えない
        if (!item.TryConsume(out ItemConsumption consumption))
            return;

        if (consumption.Category == ItemCategory.Fruit)
        {
            if (catchEffectPlayer != null)
                catchEffectPlayer.PlayFruitEffect();
        }
        else if (consumption.Category == ItemCategory.Bug)
        {
            if (catchEffectPlayer != null)
                catchEffectPlayer.PlayBugEffect();
        }

        if (scorePopup != null)
            scorePopup.ShowScore(consumption.ScoreDelta);

        gameManager.RegisterCatch(consumption.Category, consumption.ScoreDelta);
        ApplyItemEffect(consumption.Effect);
        ItemConsumed?.Invoke(consumption);
    }

    private void ApplyItemEffect(ItemEffectType effect)
    {
        if (effect == ItemEffectType.Fever)
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
