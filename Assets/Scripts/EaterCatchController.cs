using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CapsuleCollider2D))]
public class EaterCatchController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float mouthOpenThreshold = 0.1f; // 開口と取得を有効にするGainの下限。

    [SerializeField] private MicrophoneAnalysis microphone;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private FallingItemSpawner fallingItemSpawner;
    [SerializeField] private CatchEffectPlayer catchEffectPlayer;
    [SerializeField] private ScorePopup scorePopup;
    // 実際の値はEaterのInspectorで調整。シーンの設定はコードの初期値より優先される。
    [SerializeField] private float maxScale = 1.5f; // 最大の横幅倍率。
    [SerializeField] private float maxGain = 0.25f; // 最大開口に達するGain。
    [SerializeField] private float stiffness = 100f;
    [SerializeField] private float damping = 15f;

    [Header("Fever")]
    [SerializeField, Min(1f)] private float feverScaleMultiplier = 1.2f; // 声量による横幅に重ねる、縦横の拡大倍率。

    [SerializeField] private Sprite EatingImage;
    [SerializeField] private Sprite ClosingImage;
    [SerializeField, Min(0f)] private float eatingCloseDuration = 0.12f; // 食べた瞬間だけ閉口表示にする秒数。取得判定は止めない。

    [Header("Bounce Sensors")]
    // 子オブジェクトの位置とBoxCollider2D.sizeで、口の外側の救済範囲を調整する。
    [SerializeField] private BoxCollider2D leftBounceSensor;
    [SerializeField] private BoxCollider2D rightBounceSensor;
    [SerializeField, Min(0.1f)] private float bounceSpeed = 5f; // 真上へ弾く初速（units/秒）。

    private SpriteRenderer myspriterenderer;
    private CapsuleCollider2D mouthCollider;
    private float smoothedGain;
    private Vector3 originalScale;
    private float scaleMultiplier = 1f;

    private float scaleVelocity = 0f;
    private readonly List<Collider2D> contacts = new List<Collider2D>();
    private readonly TemporaryEffectTimer eatingTimer = new TemporaryEffectTimer();

    public event System.Action<ItemConsumption> ItemConsumed;

    // マイク／マウス代替入力を含む、口の拡縮と共通の平滑化済み音量。
    public float NormalizedVoiceVolume => Mathf.Clamp01(smoothedGain / Mathf.Max(0.001f, maxGain));

    private void Awake()
    {
        if (microphone == null)
            microphone = GetComponentInParent<MicrophoneAnalysis>();
        originalScale = transform.localScale;
        myspriterenderer = GetComponent<SpriteRenderer>();
        // 基準の横幅と固定高さはEaterのCapsuleCollider2D.sizeで調整する。
        mouthCollider = GetComponent<CapsuleCollider2D>();
        mouthCollider.direction = CapsuleDirection2D.Horizontal;
    }

    private void Update()
    {
        eatingTimer.Tick(Time.deltaTime);
        float gain = ReadInputGain();

        // 音量の急激な変化を滑らかにする
        float smoothTime = gain > smoothedGain ? 0.05f : 0.15f;

        smoothedGain = Mathf.Lerp(
            smoothedGain,
            gain,
            1f - Mathf.Exp(-Time.deltaTime / smoothTime)
        );
        myspriterenderer.sprite = eatingTimer.IsActive || gain <= mouthOpenThreshold ? ClosingImage : EatingImage;
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

        // 声量による横幅は維持し、フィーバー中は縦横とも大きくする。
        float feverScale = fallingItemSpawner != null && fallingItemSpawner.IsFever
            ? Mathf.Max(1f, feverScaleMultiplier) : 1f;
        transform.localScale = new Vector3(
            originalScale.x * scaleMultiplier * feverScale,
            originalScale.y * feverScale,
            originalScale.z
        );
    }

    private void FixedUpdate()
    {
        if (gameManager == null || ReadInputGain() <= mouthOpenThreshold || !mouthCollider.enabled)
            return;

        // 同じ果物が両方に触れても、取得を先に確定する。
        CatchAt(mouthCollider);

        // フィーバー果物を口で取得したフレームから、外側の範囲も取得に切り替える。
        if (fallingItemSpawner != null && fallingItemSpawner.IsFever)
        {
            CatchAt(leftBounceSensor);
            CatchAt(rightBounceSensor);
        }
        else
        {
            BounceAt(leftBounceSensor);
            BounceAt(rightBounceSensor);
        }
    }

    private void CatchAt(Collider2D sensor)
    {
        if (sensor == null || !sensor.isActiveAndEnabled)
            return;

        sensor.Overlap(ContactFilter2D.noFilter, contacts);
        foreach (Collider2D other in contacts)
            TryCatch(other.GetComponentInParent<CatchableItem>());
    }

    private void TryCatch(CatchableItem item)
    {
        if (item == null || item.Definition == null || item.IsConsumed)
            return;

        // 同じアイテムを複数回数えない
        if (!item.TryConsume(out ItemConsumption consumption))
            return;

        if (consumption.Category == ItemCategory.Fruit)
        {
            eatingTimer.Refresh(eatingCloseDuration);
            if (eatingTimer.IsActive)
                myspriterenderer.sprite = ClosingImage;
            if (catchEffectPlayer != null)
            {
                Vector2 contact = mouthCollider.ClosestPoint(consumption.WorldPosition);
                catchEffectPlayer.PlayFruitEffect(new Vector3(contact.x, contact.y, transform.position.z));
            }
        }
        else if (consumption.Category == ItemCategory.Bug)
        {
            if (catchEffectPlayer != null)
                catchEffectPlayer.PlayBugEffect();
        }

        if (scorePopup != null)
            scorePopup.ShowScore(consumption.ScoreDelta,
                consumption.Effect == ItemEffectType.Fever ||
                (fallingItemSpawner != null && fallingItemSpawner.IsFever));

        gameManager.RegisterCatch(consumption.Category, consumption.ScoreDelta);
        ApplyItemEffect(consumption.Effect);
        ItemConsumed?.Invoke(consumption);
    }

    private void BounceAt(BoxCollider2D sensor)
    {
        if (sensor == null || !sensor.isActiveAndEnabled)
            return;

        sensor.Overlap(ContactFilter2D.noFilter, contacts);
        foreach (Collider2D other in contacts)
        {
            CatchableItem item = other.GetComponentInParent<CatchableItem>();
            if (item != null && item.Definition != null && !item.IsConsumed &&
                item.Category == ItemCategory.Fruit)
            {
                FallingObject falling = item.GetComponent<FallingObject>();
                if (falling != null && falling.TryBounce(bounceSpeed) && catchEffectPlayer != null)
                {
                    Vector2 contact = sensor.ClosestPoint(other.bounds.center);
                    catchEffectPlayer.PlayBounceEffect(
                        new Vector3(contact.x, contact.y, item.transform.position.z), sensor == leftBounceSensor);
                }
            }
        }
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

    private void OnDisable()
    {
        eatingTimer.Clear();
        if (myspriterenderer != null)
        {
            myspriterenderer.sprite = ClosingImage;
            transform.localScale = originalScale;
        }
        scaleMultiplier = 1f;
        scaleVelocity = 0f;
    }

}
