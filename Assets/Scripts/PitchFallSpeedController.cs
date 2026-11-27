
using UnityEngine;

public class PitchFallSpeedController : MonoBehaviour
{
    [Header("Microphone")]
    [SerializeField] private MicrophoneAnalysis microphone;

    [SerializeField] private PitchFallSpeedSettings sharedSettings;
    internal PitchFallSpeedSettings OperatorPitchSettings => sharedSettings;

    [Header("Pitch Range")]
    [SerializeField] private float minPitchHz = 120f;
    [SerializeField] private float maxPitchHz = 600f;

    [Header("Fall Speed")]
    [SerializeField] private float maxFallSpeed = 4f;
    [SerializeField] private bool applyToFallingObjects = true;

    [Range(0f, 1f)]
    [SerializeField] private float minSpeedMultiplier = 0.25f;

    [Tooltip("ON: 高い声ほど速い / OFF: 高い声ほど遅い")]
    [SerializeField] private bool highPitchFaster = false;

    [Header("Smoothing")]
    [SerializeField] private float smoothTime = 0.15f;
    [SerializeField] private float pitchHoldTime = 0.2f;

    private float currentMultiplier = 1f;
    private float targetMultiplier = 1f;
    private float lastPitchTime = -Mathf.Infinity;
    private float targetPitch = 0.5f;
    private bool publishedFallSpeed;

    private float MinPitch => sharedSettings != null ? sharedSettings.minPitchHz : minPitchHz;
    private float MaxPitch => sharedSettings != null ? sharedSettings.maxPitchHz : maxPitchHz;
    private float MinMultiplier => sharedSettings != null ? sharedSettings.minSpeedMultiplier : minSpeedMultiplier;
    private bool HighPitchFaster => sharedSettings != null ? sharedSettings.highPitchFaster : highPitchFaster;
    private float SmoothTime => sharedSettings != null ? sharedSettings.smoothTime : smoothTime;
    private float PitchHoldTime => sharedSettings != null ? sharedSettings.pitchHoldTime : pitchHoldTime;

    // 演出も、落下速度に適用した平滑化済みの値を参照する。
    public float CurrentSpeedMultiplier => currentMultiplier;
    public float SlowdownStrength => Mathf.InverseLerp(1f, MinMultiplier, currentMultiplier);
    public bool HasPitchInput { get; private set; }
    public bool HasCurrentPitch { get; private set; }
    public float NormalizedPitch { get; private set; }

    void OnEnable()
    {
        sharedSettings = FruitCatchOperatorSettings.Pitch(sharedSettings);
        currentMultiplier = 1f;
        targetMultiplier = 1f;
        lastPitchTime = -Mathf.Infinity;
        HasPitchInput = false;
        HasCurrentPitch = false;
        NormalizedPitch = targetPitch = 0.5f;

        ApplySpeed();
    }

    void Update()
    {
        float pitch = microphone != null
            ? microphone.PitchHz
            : 0f;

        if (MouseDebugInput.Enabled)
        {
            HasCurrentPitch = MouseDebugInput.TryGetNormalizedPitch(out float normalizedPitch);
            HasPitchInput = HasCurrentPitch;
            if (HasCurrentPitch) targetPitch = normalizedPitch;
            targetMultiplier = HasPitchInput ? GetSpeedMultiplier(targetPitch) : 1f;
            lastPitchTime = -Mathf.Infinity;
        }
        else if (pitch > 0f)
        {
            HasPitchInput = HasCurrentPitch = true;
            lastPitchTime = Time.time;

            targetPitch = Mathf.InverseLerp(
                MinPitch,
                Mathf.Max(MinPitch + 1f, MaxPitch),
                pitch
            );

            targetMultiplier = GetSpeedMultiplier(targetPitch);
        }
        else
        {
            HasCurrentPitch = false;
            HasPitchInput = Time.time - lastPitchTime <= PitchHoldTime;
            if (!HasPitchInput)
            {
                // 声がなくなったら通常速度へ
                targetMultiplier = 1f;
            }
        }

        // 滑らかに速度を変化
        float t = 1f - Mathf.Exp(
            -Time.deltaTime /
            Mathf.Max(0.001f, SmoothTime)
        );

        currentMultiplier = Mathf.Lerp(
            currentMultiplier,
            targetMultiplier,
            t
        );
        NormalizedPitch = Mathf.Lerp(NormalizedPitch, targetPitch, t);

        ApplySpeed();
    }

    private float GetSpeedMultiplier(float normalizedPitch)
    {
        // マイクとマウスで、高低の向き・速度範囲を共通にする。
        return HighPitchFaster
            ? Mathf.Lerp(MinMultiplier, 1f, normalizedPitch)
            : Mathf.Lerp(1f, MinMultiplier, normalizedPitch);
    }

    private void ApplySpeed()
    {
        if (!applyToFallingObjects)
        {
            if (publishedFallSpeed) ResetFallSpeed();
            return;
        }
        publishedFallSpeed = true;
        float maxSpeed = Mathf.Max(0f, maxFallSpeed);

        // すべての落下物に同じ設定を適用
        FallingObject.FruitMaxSpeed = maxSpeed;
        FallingObject.BugMaxSpeed = maxSpeed;

        FallingObject.FruitMultiplier = currentMultiplier;
        FallingObject.BugMultiplier = currentMultiplier;
    }

    void OnDisable()
    {
        HasPitchInput = HasCurrentPitch = false;
        currentMultiplier = 1f;
        targetMultiplier = 1f;
        if (publishedFallSpeed) ResetFallSpeed();
    }

    private void ResetFallSpeed()
    {
        publishedFallSpeed = false;
        FallingObject.FruitMultiplier = 1f;
        FallingObject.BugMultiplier = 1f;

        FallingObject.FruitMaxSpeed = float.PositiveInfinity;
        FallingObject.BugMaxSpeed = float.PositiveInfinity;
    }
}
